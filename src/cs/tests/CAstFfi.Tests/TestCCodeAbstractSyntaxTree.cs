// Copyright (c) Bottlenose Labs Inc. (https://github.com/bottlenoselabs). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using CAstFfi.Data;
using CAstFfi.Tests.Data.Models;
using JetBrains.Annotations;
using Xunit;

namespace CAstFfi.Tests;

[PublicAPI]
public sealed class TestCCodeAbstractSyntaxTree
{
    private readonly ImmutableDictionary<string, CTestEnum> _enums;
    private readonly ImmutableDictionary<string, CTestFunction> _functions;
    private readonly ImmutableDictionary<string, CTestMacroObject> _macroObjects;
    private readonly ImmutableDictionary<string, CTestRecord> _records;
    private readonly ImmutableDictionary<string, CTestTypeAlias> _typeAliases;
    private readonly ImmutableDictionary<string, CTestFunctionPointer> _functionPointers;
    private readonly ImmutableDictionary<string, CTestOpaqueType> _opaqueTypes;

    private readonly ImmutableHashSet<string>.Builder _namesTested;

    public string TargetPlatformRequested { get; }

    public string TargetPlatformActual { get; }

    public TestCCodeAbstractSyntaxTree(
        CTargetPlatform targetPlatformRequested,
        CTargetPlatform targetPlatformActual,
        ImmutableDictionary<string, CTestFunction> functions,
        ImmutableDictionary<string, CTestEnum> enums,
        ImmutableDictionary<string, CTestRecord> records,
        ImmutableDictionary<string, CTestMacroObject> macroObjects,
        ImmutableDictionary<string, CTestTypeAlias> typeAliases,
        ImmutableDictionary<string, CTestFunctionPointer> functionPointers,
        ImmutableDictionary<string, CTestOpaqueType> opaqueTypes)
    {
        TargetPlatformRequested = targetPlatformRequested.ToString();
        TargetPlatformActual = targetPlatformActual.ToString();

        _functions = functions;
        _enums = enums;
        _records = records;
        _macroObjects = macroObjects;
        _typeAliases = typeAliases;
        _functionPointers = functionPointers;
        _opaqueTypes = opaqueTypes;
        _namesTested = ImmutableHashSet.CreateBuilder<string>();

        AssertPInvokePlatformNameFunction();
    }

    public void AssertNodesAreTested()
    {
        foreach (var value in _enums.Values)
        {
            Assert.True(_namesTested.Contains(value.Name), $"The C enum '{value.Name}' is not covered in a test!");
        }

        foreach (var value in _records.Values)
        {
            Assert.True(_namesTested.Contains(value.Name), $"The C record '{value.Name}' is not covered in a test!");
        }

        foreach (var value in _macroObjects.Values)
        {
            Assert.True(_namesTested.Contains(value.Name), $"The C macro object '{value.Name}' is not covered in a test!");
        }

        foreach (var value in _typeAliases.Values)
        {
            Assert.True(_namesTested.Contains(value.Name), $"The C type alias '{value.Name}' is not covered in a test!");
        }

        foreach (var value in _functionPointers.Values)
        {
            Assert.True(_namesTested.Contains(value.Name), $"The C function pointer '{value.Name}' is not covered in a test!");
        }
    }

    public CTestFunction GetFunction(string name)
    {
        var exists = _functions.TryGetValue(name, out var value);
        Assert.True(exists, $"The function '{name}' does not exist.");
        _namesTested.Add(name);
        return value!;
    }

    public CTestFunction? TryGetFunction(string name)
    {
        var exists = _functions.TryGetValue(name, out var value);
        if (!exists)
        {
            return null;
        }

        _namesTested.Add(name);
        return value;
    }

    public CTestEnum GetEnum(string name)
    {
        var exists = _enums.TryGetValue(name, out var value);
        Assert.True(exists, $"The enum '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        return value!;
    }

    public CTestEnum? TryGetEnum(string name)
    {
        var exists = _enums.TryGetValue(name, out var value);
        if (!exists)
        {
            return null;
        }

        _namesTested.Add(name);
        return value;
    }

    public CTestRecord GetRecord(string name)
    {
        var exists = _records.TryGetValue(name, out var value);
        Assert.True(exists, $"The record '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        AssertRecord(value!);
        return value!;
    }

    public CTestRecord? TryGetRecord(string name)
    {
        var exists = _records.TryGetValue(name, out var value);
        if (!exists)
        {
            return null;
        }

        _namesTested.Add(name);
        AssertRecord(value!);
        return value;
    }

    public CTestMacroObject GetMacroObject(string name)
    {
        var exists = _macroObjects.TryGetValue(name, out var value);
        Assert.True(exists, $"The macro object '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        return value!;
    }

    public CTestMacroObject? TryGetMacroObject(string name)
    {
        var exists = _macroObjects.TryGetValue(name, out var value);
        if (!exists)
        {
            return null;
        }

        _namesTested.Add(name);
        return value;
    }

    public CTestTypeAlias GetTypeAlias(string name)
    {
        var exists = _typeAliases.TryGetValue(name, out var value);
        Assert.True(exists, $"The type alias '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        return value!;
    }

    public CTestTypeAlias? TryGetTypeAlias(string name)
    {
        var exists = _typeAliases.TryGetValue(name, out var value);
        if (!exists)
        {
            return null;
        }

        _namesTested.Add(name);
        return value;
    }

    public CTestFunctionPointer GetFunctionPointer(string name)
    {
        var exists = _functionPointers.TryGetValue(name, out var value);
        Assert.True(exists, $"The function pointer '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        return value!;
    }

    public CTestFunctionPointer? TryGetFunctionPointer(string name)
    {
        var exists = _functionPointers.TryGetValue(name, out var value);
        return exists ? value : null;
    }

    public CTestOpaqueType GetOpaqueType(string name)
    {
        var exists = _opaqueTypes.TryGetValue(name, out var value);
        Assert.True(exists, $"The opaque type '{name}' does not exist: {TargetPlatformRequested}");
        _namesTested.Add(name);
        return value!;
    }

    public CTestOpaqueType? TryGetOpaqueType(string name)
    {
        var exists = _opaqueTypes.TryGetValue(name, out var value);
        return exists ? value : null;
    }

    private void AssertRecord(CTestRecord record)
    {
        var namesLookup = new List<string>();

        foreach (var field in record.Fields)
        {
            AssertRecordField(record, field, namesLookup);
        }

        Assert.True(
            record.AlignOf > 0,
            $"C record '{record.Name}' does not have an alignment of which is positive.");

        Assert.True(
            record.SizeOf >= 0,
            $"C record '{record.Name}' does not have an size of of which is positive or zero.");
    }

    private void AssertRecordField(CTestRecord record, CTestRecordField field, List<string> namesLookup)
    {
        var recordKindName = record.IsUnion ? "union" : "struct";

        Assert.False(
            namesLookup.Contains(field.Name),
            $"C {recordKindName} '{record.Name}' already has a field named `{field.Name}`.");
        namesLookup.Add(field.Name);

        Assert.True(
            field.OffsetOf >= 0,
            $"C {recordKindName} '{record.Name}' field '{field.Name}' does not have an offset of which is positive or zero.");
        Assert.True(
            field.SizeOf > 0,
            $"C {recordKindName} '{record.Name}' field '{field.Name}' does not have a size of which is positive.");

        if (record.IsUnion)
        {
            Assert.True(
                field.OffsetOf == 0,
                $"C union '{record.Name}' field '{field.Name}' does not have an offset of zero.");
            Assert.True(
                field.SizeOf == record.SizeOf,
                $"C union '{record.Name}' field '{field.Name}' does not have a size that matches the union.");
        }
    }

    private void AssertPInvokePlatformNameFunction()
    {
        var function = GetFunction("ffi_get_platform_name");
        Assert.Equal("cdecl", function.CallingConvention);
        Assert.Equal("const char*", function.ReturnTypeName);
        Assert.Equal("// Returns the current platform name.", function.Comment);
        Assert.True(function.Parameters.IsDefaultOrEmpty);
    }
}
