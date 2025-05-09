// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Immutable;
using System.Text;
using CAstFfi.Common;
using CAstFfi.Data;
using CAstFfi.Extract.Domain.Explore.Handlers;
using CAstFfi.Extract.Domain.Parse;
using CAstFfi.Extract.Infrastructure;
using CAstFfi.Extract.Infrastructure.Clang;
using CAstFfi.Extract.Input.Sanitized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static bottlenoselabs.clang;

namespace CAstFfi.Extract.Domain.Explore;

public sealed partial class Explorer
{
    private readonly ILogger<Explorer> _logger;

    private readonly List<CArray> _arrays = new();
    private readonly List<CEnumConstant> _enumConstants = new();
    private readonly List<CEnum> _enums = new();
    private readonly ArrayDeque<ExploreInfoNode> _frontierFunctions = new();
    private readonly ArrayDeque<ExploreInfoNode> _frontierTypes = new();
    private readonly ArrayDeque<ExploreInfoNode> _frontierVariables = new();
    private readonly List<CFunctionPointer> _functionPointers = new();
    private readonly List<CFunction> _functions = new();
    private readonly ImmutableDictionary<CKind, ExploreHandler> _handlers;
    private readonly List<MacroObjectCandidate> _macroObjectCandidates = new();
    private readonly List<COpaqueType> _opaqueTypes = new();
    private readonly Parser _parser;
    private readonly List<CPointer> _pointers = new();
    private readonly List<CPrimitive> _primitives = new();
    private readonly List<CRecord> _records = new();
    private readonly List<CTypeAlias> _typeAliases = new();
    private readonly List<CVariable> _variables = new();

    private readonly HashSet<string> _visitedIncludeFilePaths = new();

    public Explorer(
        IServiceProvider services,
        ILogger<Explorer> logger,
        Parser parser)
    {
        _logger = logger;
        _handlers = CreateHandlers(services);
        _parser = parser;
    }

    public CAbstractSyntaxTreeTargetPlatform GetAbstractSyntaxTree(
        string headerFilePath,
        CTargetPlatform targetPlatform,
        ExtractParseOptions parseOptions,
        ExtractExploreOptions exploreOptions)
    {
        CAbstractSyntaxTreeTargetPlatform result;

        var translationUnit = _parser.TranslationUnit(
            headerFilePath, targetPlatform, parseOptions, out var linkedPaths);

        var context = new ExploreContext(
            _handlers,
            targetPlatform,
            translationUnit,
            exploreOptions,
            parseOptions,
            TryEnqueueVisitInfoNode,
            linkedPaths);

        var platformScope = _logger.BeginScope(targetPlatform)!;

        try
        {
            VisitTranslationUnit(context, translationUnit, headerFilePath);
            ExploreVariables(context);
            ExploreFunctions(context);
            ExploreTypes(context);
            result = CollectAbstractSyntaxTree(context);
        }
        catch (Exception e)
        {
            CleanUp(context);
            LogFailure(e);
            throw;
        }

        CleanUp(context);
        LogSuccess();

        platformScope.Dispose();
        return result;
    }

    private static ImmutableDictionary<CKind, ExploreHandler> CreateHandlers(IServiceProvider services)
    {
        var result = new Dictionary<CKind, ExploreHandler>
        {
            { CKind.EnumConstant, services.GetService<EnumConstantExplorer>()! },
            { CKind.Variable, services.GetService<VariableExplorer>()! },
            { CKind.Function, services.GetService<FunctionExplorer>()! },
            { CKind.Struct, services.GetService<StructExplorer>()! },
            { CKind.Union, services.GetService<UnionExplorer>()! },
            { CKind.Enum, services.GetService<EnumExplorer>()! },
            { CKind.TypeAlias, services.GetService<TypeAliasExplorer>()! },
            { CKind.OpaqueType, services.GetService<OpaqueTypeExplorer>()! },
            { CKind.FunctionPointer, services.GetService<FunctionPointerExplorer>()! },
            { CKind.Array, services.GetService<ArrayExplorer>()! },
            { CKind.Pointer, services.GetService<PointerExplorer>()! },
            { CKind.Primitive, services.GetService<PrimitiveExplorer>()! }
        };
        return result.ToImmutableDictionary();
    }

    private void CleanUp(ExploreContext context)
    {
        clang_disposeTranslationUnit(context.TranslationUnit);
        _parser.CleanUp();
    }

    private CAbstractSyntaxTreeTargetPlatform CollectAbstractSyntaxTree(ExploreContext context)
    {
        var macroObjects = CollectMacroObjects(context);
        var variables = CollectVariables();
        var functions = CollectFunctions();
        var records = CollectRecords();
        var enums = CollectEnums();
        var typeAliases = CollectTypeAliases();
        var opaqueTypes = CollectOpaqueTypes();
        var functionPointers = CollectFunctionPointers();
        var enumConstants = CollectEnumConstants();

        var result = new CAbstractSyntaxTreeTargetPlatform
        {
            FileName = context.FilePath,
            PlatformRequested = context.TargetPlatformRequested,
            PlatformActual = context.TargetPlatformActual,
            MacroObjects = macroObjects.ToImmutableDictionary(x => x.Name),
            Variables = variables,
            Functions = functions,
            Records = records,
            Enums = enums,
            TypeAliases = typeAliases,
            OpaqueTypes = opaqueTypes,
            FunctionPointers = functionPointers,
            EnumConstants = enumConstants
        };

        return result;
    }

    private ImmutableDictionary<string, CEnumConstant> CollectEnumConstants()
    {
        _enumConstants.Sort();
        var enumConstants = _enumConstants.ToImmutableDictionary(x => x.Name);
        return enumConstants;
    }

    private ImmutableDictionary<string, CFunctionPointer> CollectFunctionPointers()
    {
        _functionPointers.Sort();
        var functionPointers = _functionPointers.ToImmutableDictionary(x => x.Name);
        return functionPointers;
    }

    private ImmutableDictionary<string, COpaqueType> CollectOpaqueTypes()
    {
        _opaqueTypes.Sort();
        var opaqueTypes = _opaqueTypes.ToImmutableDictionary(x => x.Name);
        return opaqueTypes;
    }

    private ImmutableDictionary<string, CTypeAlias> CollectTypeAliases()
    {
        _typeAliases.Sort();
        var typeAliases = _typeAliases.ToImmutableDictionary(x => x.Name);
        return typeAliases;
    }

    private ImmutableDictionary<string, CEnum> CollectEnums()
    {
        _enums.Sort();
        var enums = _enums.ToImmutableDictionary(x => x.Name);
        return enums;
    }

    private ImmutableDictionary<string, CRecord> CollectRecords()
    {
        _records.Sort();
        var records = _records.ToImmutableDictionary(x => x.Name);
        return records;
    }

    private ImmutableDictionary<string, CFunction> CollectFunctions()
    {
        _functions.Sort();
        var functions = _functions.ToImmutableDictionary(x => x.Name);
        return functions;
    }

    private ImmutableDictionary<string, CVariable> CollectVariables()
    {
        _variables.Sort();
        var variables = _variables.ToImmutableDictionary(x => x.Name);
        return variables;
    }

    private ImmutableArray<CMacroObject> CollectMacroObjects(ExploreContext context)
    {
        LogExploringMacros();
        var macroObjectCandidates = _macroObjectCandidates.ToImmutableArray();
        var macroObjects = _parser.MacroObjects(
            macroObjectCandidates, NativeUtility.HostPlatform, context.ParseOptions);
        var macroNamesFound = macroObjects.Select(macroObject => macroObject.Name).ToArray();
        var macroNamesFoundString = string.Join(", ", macroNamesFound);
        LogFoundMacros(macroNamesFound.Length, macroNamesFoundString);
        return macroObjects;
    }

    private void ExploreVariables(ExploreContext context)
    {
        var totalCount = _frontierVariables.Count;
        var variableNamesToExplore = string.Join(", ", _frontierVariables.Select(x => x.Name));
        LogExploringVariables(totalCount, variableNamesToExplore);
        ExploreFrontier(context, _frontierVariables);
        var variableNamesFound = _variables.Select(x => x.Name).ToArray();
        var variableNamesFoundString = string.Join(", ", variableNamesFound);
        LogFoundVariables(variableNamesFound.Length, variableNamesFoundString);
    }

    private void ExploreFunctions(ExploreContext context)
    {
        var totalCount = _frontierFunctions.Count;
        var functionNamesToExplore = string.Join(", ", _frontierFunctions.Select(x => x.Name));
        LogExploringFunctions(totalCount, functionNamesToExplore);
        ExploreFrontier(context, _frontierFunctions);
        var functionNamesFound = _functions.Select(x => x.Name).ToArray();
        var functionNamesFoundString = string.Join(", ", functionNamesFound);
        LogFoundFunctions(functionNamesFound.Length, functionNamesFoundString);
    }

    private void ExploreTypes(ExploreContext context)
    {
        var totalCount = _frontierTypes.Count;
        var typeNamesToExplore = string.Join(", ", _frontierTypes.Select(x => x.Name));
        LogExploringTypes(totalCount, typeNamesToExplore);
        ExploreFrontier(context, _frontierTypes);

        var typeNamesFound = new List<string>();
        typeNamesFound.AddRange(_records.Select(x => x.Name));
        typeNamesFound.AddRange(_enums.Select(x => x.Name));
        typeNamesFound.AddRange(_typeAliases.Select(x => x.Name));
        typeNamesFound.AddRange(_opaqueTypes.Select(x => x.Name));
        typeNamesFound.AddRange(_functionPointers.Select(x => x.Name));
        typeNamesFound.AddRange(_pointers.Select(x => x.Name));
        typeNamesFound.AddRange(_arrays.Select(x => x.Name));
        typeNamesFound.AddRange(_enumConstants.Select(x => x.Name));
        typeNamesFound.AddRange(_primitives.Select(x => x.Name));
        var typeNamesFoundJoined = string.Join(", ", typeNamesFound);

        LogFoundTypes(typeNamesFound.Count, typeNamesFoundJoined);
    }

    private void ExploreFrontier(
        ExploreContext context, ArrayDeque<ExploreInfoNode> frontier)
    {
        while (frontier.Count > 0)
        {
            var node = frontier.PopFront()!;
            ExploreNode(context, node);
        }
    }

    private void ExploreNode(ExploreContext context, ExploreInfoNode info)
    {
        var node = context.Explore(info);
        if (node != null)
        {
            FoundNode(node);
        }
    }

    private void FoundNode(CNode node)
    {
        var location = node is CNodeWithLocation nodeWithLocation ? nodeWithLocation.Location : null;
        LogFoundNode(node.Kind, node.Name, location);

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (node.Kind)
        {
            case CKind.Variable:
                FoundVariable((CVariable)node);
                break;
            case CKind.Function:
                FoundFunction((CFunction)node);
                break;
            case CKind.Struct:
            case CKind.Union:
                FoundRecord((CRecord)node);
                break;
            case CKind.Enum:
                FoundEnum((CEnum)node);
                break;
            case CKind.TypeAlias:
                FoundTypeAlias((CTypeAlias)node);
                break;
            case CKind.OpaqueType:
                FoundOpaqueType((COpaqueType)node);
                break;
            case CKind.FunctionPointer:
                FoundFunctionPointer((CFunctionPointer)node);
                break;
            case CKind.Pointer:
                FoundPointer((CPointer)node);
                break;
            case CKind.Array:
                FoundArray((CArray)node);
                break;
            case CKind.EnumConstant:
                FoundEnumConstant((CEnumConstant)node);
                break;
            case CKind.Primitive:
                FoundPrimitive((CPrimitive)node);
                break;
            default:
                var up = new NotImplementedException(
                    $"Found a node of kind '{node.Kind}' but do not know how to add it.");
                throw up;
        }
    }

    private void FoundVariable(CVariable node)
    {
        _variables.Add(node);
    }

    private void FoundFunction(CFunction node)
    {
        _functions.Add(node);
    }

    private void FoundRecord(CRecord node)
    {
        _records.Add(node);
    }

    private void FoundEnum(CEnum node)
    {
        _enums.Add(node);
    }

    private void FoundTypeAlias(CTypeAlias node)
    {
        _typeAliases.Add(node);
    }

    private void FoundOpaqueType(COpaqueType node)
    {
        _opaqueTypes.Add(node);
    }

    private void FoundFunctionPointer(CFunctionPointer node)
    {
        _functionPointers.Add(node);
    }

    private void FoundPointer(CPointer node)
    {
        _pointers.Add(node);
    }

    private void FoundArray(CArray node)
    {
        _arrays.Add(node);
    }

    private void FoundEnumConstant(CEnumConstant node)
    {
        _enumConstants.Add(node);
    }

    private void FoundPrimitive(CPrimitive node)
    {
        _primitives.Add(node);
    }

    private void VisitTranslationUnit(ExploreContext context, CXTranslationUnit translationUnit, string filePath)
    {
        LogVisitingTranslationUnit(filePath);

        VisitMacroCandidates(context, translationUnit, filePath);

        var isEnabledSingleHeader = context.ParseOptions.IsEnabledSingleHeader;
        var translationUnitCursor = clang_getTranslationUnitCursor(translationUnit);

        ImmutableArray<CXCursor> cursors;

        if (context.ExploreOptions.IsEnabledOnlyExternalTopLevelCursors)
        {
            cursors = translationUnitCursor.GetDescendents(
                (child, _) => IsExternalTopLevelCursor(child), !isEnabledSingleHeader);
        }
        else
        {
            cursors = translationUnitCursor.GetDescendents();
        }

        foreach (var cursor in cursors)
        {
            VisitTopLevelCursor(context, cursor);
        }

        if (!isEnabledSingleHeader)
        {
            VisitIncludes(context, translationUnit);
        }

        LogVisitedTranslationUnit(filePath);
    }

    private void VisitMacroCandidates(ExploreContext context, CXTranslationUnit translationUnit, string filePath)
    {
        var macroObjectCandidates = _parser.MacroObjectCandidates(
            translationUnit, context.TargetPlatformRequested, context.ParseOptions);
        foreach (var macroObjectCandidate in macroObjectCandidates)
        {
            _macroObjectCandidates.Add(macroObjectCandidate);
        }
    }

    private void VisitIncludes(ExploreContext context, CXTranslationUnit translationUnit)
    {
        var translationUnitCursor = clang_getTranslationUnitCursor(translationUnit);
        var includeCursors = translationUnitCursor.GetDescendents(
            static (child, _) => child.kind == CXCursorKind.CXCursor_InclusionDirective);

        var stringBuilder = new StringBuilder();

        foreach (var includeCursor in includeCursors)
        {
            var code = includeCursor.GetCode(stringBuilder);
            var isSystem = code.Contains('<', StringComparison.InvariantCulture);
            if (isSystem && !context.ExploreOptions.IsEnabledSystemDeclarations)
            {
                continue;
            }

            var file = clang_getIncludedFile(includeCursor);
            var filePath = clang_getFileName(file).String();
            if (_visitedIncludeFilePaths.Contains(filePath))
            {
                continue;
            }

            _visitedIncludeFilePaths.Add(filePath);
            VisitInclude(context, filePath);
        }
    }

    private void VisitInclude(ExploreContext context, string headerFilePath)
    {
        var includeTranslationUnit = _parser.TranslationUnit(
            headerFilePath,
            context.TargetPlatformRequested,
            context.ParseOptions,
            out _,
            true,
            true);

        VisitTranslationUnit(context, includeTranslationUnit, headerFilePath);
    }

    private static bool IsExternalTopLevelCursor(CXCursor cursor)
    {
        var kind = clang_getCursorKind(cursor);
        if (kind != CXCursorKind.CXCursor_FunctionDecl &&
            kind != CXCursorKind.CXCursor_VarDecl &&
            kind != CXCursorKind.CXCursor_EnumDecl &&
            kind != CXCursorKind.CXCursor_TypedefDecl &&
            kind != CXCursorKind.CXCursor_StructDecl)
        {
            return false;
        }

        if (kind == CXCursorKind.CXCursor_EnumDecl)
        {
            return true;
        }

        var linkage = clang_getCursorLinkage(cursor);
        var visibility = clang_getCursorVisibility(cursor);
        var isExported = linkage == CXLinkageKind.CXLinkage_External &&
                         visibility == CXVisibilityKind.CXVisibility_Default;
        return isExported;
    }

    private void VisitTopLevelCursor(ExploreContext context, CXCursor cursor)
    {
        if (cursor.kind is CXCursorKind.CXCursor_MacroDefinition
            or CXCursorKind.CXCursor_MacroExpansion
            or CXCursorKind.CXCursor_MacroInstantiation)
        {
            // Macro are handled in the parser domain logic because Clang does not provide us with a good enough API to deal with them
            return;
        }

        var kind = cursor.kind switch
        {
            CXCursorKind.CXCursor_FunctionDecl => CKind.Function,
            CXCursorKind.CXCursor_VarDecl => CKind.Variable,
            CXCursorKind.CXCursor_EnumDecl => CKind.Enum,
            CXCursorKind.CXCursor_TypedefDecl => CKind.TypeAlias,
            CXCursorKind.CXCursor_StructDecl => CKind.Struct,
            _ => CKind.Unknown
        };

        if (kind == CKind.Unknown)
        {
            LogUnexpectedTopLevelCursor(cursor.kind);
            return;
        }

        var type = clang_getCursorType(cursor);
        if (type.kind == CXTypeKind.CXType_Unexposed)
        {
            // CXType_Unexposed: A type whose specific kind is not exposed via this interface (libclang).
            // When this happens, use the "canonical form" or the standard/normal form of the type
            type = clang_getCanonicalType(type);
        }

        if (type.kind == CXTypeKind.CXType_Attributed)
        {
            // CXTypeKind.CXType_Attributed: The type has a Clang attribute.
            type = clang_Type_getModifiedType(type);
        }

        var name = clang_getCursorSpelling(cursor).String();
        if (context.ExploreOptions.OpaqueTypeNames.Contains(name))
        {
            kind = CKind.OpaqueType;
        }

        var isAnonymous = clang_Cursor_isAnonymous(cursor) > 0;
        var info = context.CreateVisitInfoNode(kind, name, cursor, type, null);

        if (kind == CKind.Enum && isAnonymous)
        {
            var enumConstants = cursor.GetDescendents(
                static (cursor, _) => cursor.kind == CXCursorKind.CXCursor_EnumConstantDecl,
                false);
            var enumIntegerType = clang_getEnumDeclIntegerType(cursor);
            foreach (var enumConstant in enumConstants)
            {
                var enumConstantName = enumConstant.Name();
                var enumConstantVisitInfo = context.CreateVisitInfoNode(
                    CKind.EnumConstant,
                    enumConstantName,
                    enumConstant,
                    enumIntegerType,
                    info);
                TryEnqueueVisitInfoNode(context, CKind.EnumConstant, enumConstantVisitInfo);
            }
        }
        else if (kind == CKind.TypeAlias)
        {
            context.VisitType(type, null);
        }
        else if (kind == CKind.Struct)
        {
            if (info.SizeOf == -2)
            {
                // Incomplete top-level struct; likely used with a pointer to make an opaque type.
                return;
            }

            TryEnqueueVisitInfoNode(context, kind, info);
        }
        else
        {
            TryEnqueueVisitInfoNode(context, kind, info);
        }
    }

    private void TryEnqueueVisitInfoNode(ExploreContext context, CKind kind, ExploreInfoNode info)
    {
        var frontier = kind switch
        {
            CKind.Variable => _frontierVariables,
            CKind.Function => _frontierFunctions,
            _ => _frontierTypes
        };

        if (!context.CanVisit(kind, info))
        {
            return;
        }

        LogEnqueue(kind, info.Name, info.Location);
        frontier.PushBack(info);
    }

    [LoggerMessage(0, LogLevel.Error, "- Expected a top level translation unit declaration (function, variable, enum, typedef, struct, or macro) but found '{Kind}'")]
    private partial void LogUnexpectedTopLevelCursor(CXCursorKind kind);

    [LoggerMessage(1, LogLevel.Error, "- Failure")]
    private partial void LogFailure(Exception exception);

    [LoggerMessage(2, LogLevel.Debug, "- Success")]
    private partial void LogSuccess();

    [LoggerMessage(3, LogLevel.Debug, "- Visiting translation unit: {FilePath}")]
    private partial void LogVisitingTranslationUnit(string filePath);

    [LoggerMessage(4, LogLevel.Information, "- Visited translation unit: {FilePath}")]
    private partial void LogVisitedTranslationUnit(string filePath);

    [LoggerMessage(5, LogLevel.Information, "- Exploring macros")]
    private partial void LogExploringMacros();

    [LoggerMessage(6, LogLevel.Information, "- Found {FoundCount} macros: {Names}")]
    private partial void LogFoundMacros(int foundCount, string names);

    [LoggerMessage(7, LogLevel.Information, "- Exploring {Count} variables: {Names}")]
    private partial void LogExploringVariables(int count, string names);

    [LoggerMessage(8, LogLevel.Information, "- Found {FoundCount} variables: {Names}")]
    private partial void LogFoundVariables(int foundCount, string names);

    [LoggerMessage(9, LogLevel.Information, "- Exploring {Count} functions: {Names}")]
    private partial void LogExploringFunctions(int count, string names);

    [LoggerMessage(10, LogLevel.Information, "- Found {FoundCount} functions: {Names}")]
    private partial void LogFoundFunctions(int foundCount, string names);

    [LoggerMessage(11, LogLevel.Information, "- Exploring {Count} types: {Names}")]
    private partial void LogExploringTypes(int count, string names);

    [LoggerMessage(12, LogLevel.Information, "- Found {FoundCount} types: {Names}")]
    private partial void LogFoundTypes(int foundCount, string names);

    [LoggerMessage(13, LogLevel.Debug, "- Enqueued {Kind} '{Name}' ({Location})")]
    private partial void LogEnqueue(CKind kind, string name, CLocation? location);

    [LoggerMessage(14, LogLevel.Information, "- Found {Kind} '{Name}' ({Location})")]
    private partial void LogFoundNode(CKind kind, string name, CLocation? location);
}
