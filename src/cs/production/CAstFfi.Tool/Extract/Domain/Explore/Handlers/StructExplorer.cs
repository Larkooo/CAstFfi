// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Immutable;
using CAstFfi.Data;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using static bottlenoselabs.clang;

namespace CAstFfi.Extract.Domain.Explore.Handlers;

[UsedImplicitly]
public sealed class StructExplorer : RecordExplorer
{
    public StructExplorer(ILogger<StructExplorer> logger)
        : base(logger, false)
    {
    }

    protected override ExploreKindCursors ExpectedCursors { get; } =
        ExploreKindCursors.Is(CXCursorKind.CXCursor_StructDecl);

    protected override CRecord Explore(ExploreContext context, ExploreInfoNode info)
    {
        var @struct = Struct(context, info);
        return @struct;
    }

    private CRecord Struct(ExploreContext context, ExploreInfoNode info)
    {
        var fields = StructFields(context, info);
        var comment = context.Comment(info.Cursor);

        try
        {
            var record = new CRecord
            {
                RecordKind = CRecordKind.Struct,
                Location = info.Location,
                Name = info.Name,
                Fields = fields,
                SizeOf = info.SizeOf,
                AlignOf = info.AlignOf!.Value,
                Comment = comment
            };

            return record;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private ImmutableArray<CRecordField> StructFields(
        ExploreContext context,
        ExploreInfoNode structInfo)
    {
        var builder = ImmutableArray.CreateBuilder<CRecordField>();
        var fieldCursors = FieldCursorsFromType(structInfo.Type);
        var fieldCursorsLength = fieldCursors.Length;
        if (fieldCursorsLength > 0)
        {
            for (var i = 0; i < fieldCursors.Length; i++)
            {
                var fieldCursor = fieldCursors[i];
                var field = StructField(context, structInfo, fieldCursor, i);
                builder.Add(field);
            }
        }

        var result = builder.ToImmutable();
        return result;
    }

    private CRecordField StructField(
        ExploreContext context,
        ExploreInfoNode structInfo,
        CXCursor fieldCursor,
        int fieldIndex)
    {
        var fieldName = context.CursorName(fieldCursor);
        var type = clang_getCursorType(fieldCursor);
        var location = context.Location(fieldCursor, type);
        var typeInfo = context.VisitType(type, structInfo, fieldIndex)!;
        var offsetOf = (int)clang_Cursor_getOffsetOfField(fieldCursor) / 8;
        var comment = context.Comment(fieldCursor);

        return new CRecordField
        {
            Name = fieldName,
            Location = location,
            TypeInfo = typeInfo,
            OffsetOf = offsetOf,
            Comment = comment
        };
    }
}
