// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Immutable;
using CAstFfi.Data;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using static bottlenoselabs.clang;

namespace CAstFfi.Extract.Domain.Explore.Handlers;

[UsedImplicitly]
public sealed class UnionExplorer : RecordExplorer
{
    public UnionExplorer(ILogger<UnionExplorer> logger)
        : base(logger, false)
    {
    }

    protected override ExploreKindCursors ExpectedCursors { get; } =
        ExploreKindCursors.Is(CXCursorKind.CXCursor_UnionDecl);

    protected override CRecord Explore(ExploreContext context, ExploreInfoNode info)
    {
        var union = Union(context, info);
        return union;
    }

    private CRecord Union(ExploreContext context, ExploreInfoNode info)
    {
        var fields = UnionFields(context, info.Type, info);
        var comment = context.Comment(info.Cursor);

        var result = new CRecord
        {
            RecordKind = CRecordKind.Union,
            Location = info.Location,
            Name = info.TypeName,
            Fields = fields,
            SizeOf = info.SizeOf,
            AlignOf = info.AlignOf!.Value,
            Comment = comment
        };

        return result;
    }

    private ImmutableArray<CRecordField> UnionFields(
        ExploreContext context,
        CXType type,
        ExploreInfoNode parentInfo)
    {
        var builder = ImmutableArray.CreateBuilder<CRecordField>();
        var fieldCursors = FieldCursorsFromType(type);

        for (var i = 0; i < fieldCursors.Length; i++)
        {
            var fieldCursor = fieldCursors[i];
            var nextRecordField = UnionField(context, fieldCursor, parentInfo, i);
            builder.Add(nextRecordField);
        }

        var result = builder.ToImmutable();
        return result;
    }

    private CRecordField UnionField(
        ExploreContext context,
        CXCursor cursor,
        ExploreInfoNode parentInfo,
        int fieldIndex)
    {
        var name = context.CursorName(cursor);
        var type = clang_getCursorType(cursor);
        var location = context.Location(cursor, type);
        var typeInfo = context.VisitType(type, parentInfo, fieldIndex)!;
        var comment = context.Comment(cursor);

        var result = new CRecordField
        {
            Name = name,
            Location = location,
            TypeInfo = typeInfo,
            Comment = comment
        };

        return result;
    }
}
