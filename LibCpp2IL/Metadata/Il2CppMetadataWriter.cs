using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata;

public static class Il2CppMetadataWriter
{
    private static Il2CppMetadata _metadata;
    private static ClassWritingBinaryWriter _writer;

    public static void WriteTo(Il2CppMetadata m, string path)
    {
        _metadata = m;
        var mh = m.metadataHeader;

        UpdateHeaderLengths(m);

        RecalculateOffsets(m);

        using var fileStream = File.Open(path, FileMode.Create);
        using var writer = new ClassWritingBinaryWriter(fileStream);

        _writer = writer;

        WriteMetadataClassArray("image definitions", mh.imagesOffset, m.imageDefinitions);
        WriteMetadataClassArray("assembly definitions", mh.assembliesOffset, m.AssemblyDefinitions);
        WriteMetadataClassArray("type definitions", mh.typeDefinitionsOffset, m.typeDefs);
        WriteMetadataClassArray("interface offsets", mh.interfaceOffsetsOffset, m.interfaceOffsets);
        WriteClassArray("vtable indices", mh.vtableMethodsOffset, m.VTableMethodIndices);
        WriteMetadataClassArray("method definitions", mh.methodsOffset, m.methodDefs);
        WriteMetadataClassArray("method parameter definitions", mh.parametersOffset, m.parameterDefs);
        WriteMetadataClassArray("field definitions", mh.fieldsOffset, m.fieldDefs);
        WriteMetadataClassArray("default field values", mh.fieldDefaultValuesOffset, m.fieldDefaultValues);
        WriteMetadataClassArray("field marshaled sizes", mh.fieldMarshaledSizesOffset, m.fieldMarshaledSizes);
        WriteMetadataClassArray("default parameter values", mh.parameterDefaultValuesOffset, m.parameterDefaultValues);
        WriteClassArray("field and parameter default values", mh.fieldAndParameterDefaultValueDataOffset, m.fieldAndParameterDefaultValueData);
        WriteMetadataClassArray("property definitions", mh.propertiesOffset, m.propertyDefs);
        WriteClassArray("interface definitions", mh.interfacesOffset, m.interfaceIndices);
        WriteClassArray("nested type definitions", mh.nestedTypesOffset, m.nestedTypeIndices);
        WriteMetadataClassArray("event definitions", mh.eventsOffset, m.eventDefs);
        WriteMetadataClassArray("generic container definitions", mh.genericContainersOffset, m.genericContainers);
        WriteMetadataClassArray("generic parameter definitions", mh.genericParametersOffset, m.genericParameters);
        WriteClassArray("generic parameter constraint indices", mh.genericParameterConstraintsOffset, m.constraintIndices);
        WriteClassArray("referenced assemblies", mh.referencedAssembliesOffset, m.referencedAssemblies);
        WriteMetadataClassArray("string definitions", mh.stringLiteralOffset, m.stringLiterals);

        if (m.MetadataVersion < 24.2f)
        {
            WriteMetadataClassArray("RGCTX data", mh.rgctxEntriesOffset, m.RgctxDefinitions!);
        }

        if (m.MetadataVersion < 27f)
        {
            WriteMetadataClassArray("usage data", mh.metadataUsageListsOffset, m.metadataUsageLists!);
            WriteMetadataClassArray("usage pairs", mh.metadataUsagePairsOffset, m.metadataUsagePairs!);
        }

        WriteMetadataClassArray("field references", mh.fieldRefsOffset, m.fieldRefs);
        WriteClassArray("unresolved virtual call parameter types", mh.unresolvedVirtualCallParameterTypesOffset, m.unresolvedVirtualCallParameterTypes);
        WriteMetadataClassArray("unresolved virtual call parameter ranges", mh.unresolvedVirtualCallParameterRangesOffset, m.unresolvedVirtualCallParameterRanges);
        WriteMetadataClassArray("Windows runtime type names", mh.windowsRuntimeTypeNamesOffset, m.windowsRuntimeTypeNames);

        if (m.MetadataVersion >= 27)
        {
            //WriteMetadataClassArray("Windows runtime strings", mh.windowsRuntimeTypeNamesOffset, ref mh.windowsRuntimeTypeNamesSize, m.windowsRuntimeTypeNames);
        }

        if (m.MetadataVersion >= 24)
        {
            WriteClassArray("exported type definitions", mh.exportedTypeDefinitionsOffset, m.exportedTypeDefinitions);
        }

        //v21+ fields
        if (m.MetadataVersion < 29)
        {
            //Removed in v29
            WriteMetadataClassArray("attribute infos", mh.attributesInfoOffset, [.. m.attributeTypeRanges!]);
            WriteClassArray("attribute types", mh.attributeTypesOffset, m.attributeTypes!);
        }
        else
        {
            //Pointer array
            WriteMetadataClassArray("attribute data", mh.attributeDataRangeOffset, [.. m.AttributeDataRanges!]);
        }

        // TODO: make stringLiterals and strings modifiable
        for (uint i = 0; i < m.stringLiterals.Length; i++)
        {
            var str = m.GetStringLiteralFromIndex(i);
            var addr = m.metadataHeader.stringLiteralDataOffset + m.stringLiterals[i].dataIndex;
            writer.WriteStringWithNullTerminator(addr, str);
        }

        writer.WriteClassArray<byte>(m.metadataHeader.stringOffset, GetStringDataBytes(m));

        // metadataHeader (includes magic + version)
        writer.WriteReadableClassAtAddr(0, mh);
    }

    private static void UpdateHeaderLengths(Il2CppMetadata m)
    {
        var mh = m.metadataHeader;

        UpdateMetadataClassArrayLength(ref mh.imagesCount, m.imageDefinitions);
        UpdateMetadataClassArrayLength(ref mh.assembliesCount, m.AssemblyDefinitions);
        UpdateMetadataClassArrayLength(ref mh.typeDefinitionsCount, m.typeDefs);
        UpdateMetadataClassArrayLength(ref mh.interfaceOffsetsCount, m.interfaceOffsets);
        UpdateClassArrayLength(ref mh.vtableMethodsCount, m.VTableMethodIndices);
        UpdateMetadataClassArrayLength(ref mh.methodsCount, m.methodDefs);
        UpdateMetadataClassArrayLength(ref mh.parametersCount, m.parameterDefs);
        UpdateMetadataClassArrayLength(ref mh.fieldsCount, m.fieldDefs);
        UpdateMetadataClassArrayLength(ref mh.fieldDefaultValuesCount, m.fieldDefaultValues);
        UpdateMetadataClassArrayLength(ref mh.fieldMarshaledSizesCount, m.fieldMarshaledSizes);
        UpdateMetadataClassArrayLength(ref mh.parameterDefaultValuesCount, m.parameterDefaultValues);
        UpdateClassArrayLength(ref mh.fieldAndParameterDefaultValueDataCount, m.fieldAndParameterDefaultValueData);
        UpdateMetadataClassArrayLength(ref mh.propertiesCount, m.propertyDefs);
        UpdateClassArrayLength(ref mh.interfacesCount, m.interfaceIndices);
        UpdateClassArrayLength(ref mh.nestedTypesCount, m.nestedTypeIndices);
        UpdateMetadataClassArrayLength(ref mh.eventsCount, m.eventDefs);
        UpdateMetadataClassArrayLength(ref mh.genericContainersCount, m.genericContainers);
        UpdateMetadataClassArrayLength(ref mh.genericParametersCount, m.genericParameters);
        UpdateClassArrayLength(ref mh.genericParameterConstraintsCount, m.constraintIndices);
        UpdateClassArrayLength(ref mh.referencedAssembliesCount, m.referencedAssemblies);
        UpdateMetadataClassArrayLength(ref mh.stringLiteralCount, m.stringLiterals);
        UpdateMetadataClassArrayLength(ref mh.rgctxEntriesCount, m.RgctxDefinitions!);
        UpdateMetadataClassArrayLength(ref mh.metadataUsageListsCount, m.metadataUsageLists!);
        UpdateMetadataClassArrayLength(ref mh.metadataUsagePairsCount, m.metadataUsagePairs!);
        UpdateMetadataClassArrayLength(ref mh.fieldRefsCount, m.fieldRefs);
        UpdateClassArrayLength(ref mh.unresolvedVirtualCallParameterTypesCount, m.unresolvedVirtualCallParameterTypes);
        UpdateMetadataClassArrayLength(ref mh.unresolvedVirtualCallParameterRangesCount, m.unresolvedVirtualCallParameterRanges);
        UpdateMetadataClassArrayLength(ref mh.windowsRuntimeTypeNamesSize, m.windowsRuntimeTypeNames);
        UpdateClassArrayLength(ref mh.exportedTypeDefinitionsCount, m.exportedTypeDefinitions);
        if (m.AttributeDataRanges != null)
            UpdateMetadataClassArrayLength(ref mh.attributeDataRangeCount, [.. m.AttributeDataRanges!]);
    }

    private static void WriteMetadataClassArray<T>(string name, int offset, T[] data) where T : ReadableClass
    {
        if (data.Length == 0)
            return;

        LibLogger.Verbose($"\tWriting {name}...");
        var start = DateTime.Now;
        _writer.WriteMetadataClassArray(offset, data);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
    }

    private static void WriteClassArray<T>(string name, int offset, T[] data) where T : struct
    {
        if (data.Length == 0)
            return;

        LibLogger.Verbose($"\tWriting {name}...");
        var start = DateTime.Now;
        _writer.WriteClassArray(offset, data);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
    }

    private static byte[] GetStringDataBytes(Il2CppMetadata m)
    {
        var offset = m.metadataHeader.stringOffset;
        var count = m.metadataHeader.stringCount;

        return m.ReadByteArrayAtRawAddress(offset, count);
    }

    private static void UpdateMetadataClassArrayLength<T>(ref int count, T[] data) where T : ReadableClass
    {
        if (data == null)
            return;

        count = data.Length == 0 ? 0 : data.Length * data.First().Size;
    }

    private static void UpdateClassArrayLength<T>(ref int count, T[] data) where T : struct
    {
        if (data == null)
            return;

        count = data.Length == 0 ? 0 : data.Length * Marshal.SizeOf<T>();
    }

    private static void RecalculateOffsets(Il2CppMetadata m)
    {
        var type = typeof(Il2CppGlobalMetadataHeader);
        var offsetFields = type.GetFields().Where(f => f.Name.EndsWith("Offset")).ToArray();

        var baseOffset = (int)offsetFields.First().GetValue(m.metadataHeader)!;

        var currentOffset = baseOffset;

        for (var i = 0; i < offsetFields.Length; i++)
        {
            var offsetField = offsetFields[i];
            var countField = type.GetField(offsetField.Name[..(offsetField.Name.Length - 6)] + "Count") ?? type.GetField(offsetField.Name[..(offsetField.Name.Length - 6)] + "Size")!;

            offsetField.SetValue(m.metadataHeader, currentOffset);
            currentOffset += (int)countField.GetValue(m.metadataHeader)!;
        }
    }
}

public class ClassWritingBinaryWriter : BinaryWriter
{
    public bool Is32Bit;
    public ulong PointerSize => Is32Bit ? 4ul : 8ul;

    public ClassWritingBinaryWriter(Stream output) : base(output)
    {
    }

    public void WritePrimitive(object value)
    {
        switch (value)
        {
            case bool v: Write(v); break;
            case char v: Write(v); break;
            case int v: Write(v); break;
            case uint v: Write(v); break;
            case short v: Write(v); break;
            case ushort v: Write(v); break;
            case sbyte v: Write(v); break;
            case byte v: Write(v); break;
            case long v: Write(v); break;
            case ulong v: Write(v); break;
            case float v: Write(v); break;
            case double v: Write(v); break;
            default:
                throw new ArgumentException($"Unsupported primitive type: {value.GetType()}");
        }
    }

    public void WriteReadableClass(ReadableClass readable)
    {
        readable.Write(this);
    }

    public void WriteReadableClassAtAddr(int offset, ReadableClass readable)
    {
        Seek(offset, SeekOrigin.Begin);
        readable.Write(this);
    }

    public void WriteStringWithNullTerminator(int offset, string value)
    {
        Seek(offset, SeekOrigin.Begin);
        Write(Encoding.UTF8.GetBytes(value));
        Write((byte)0);
    }

    public void WriteByteArray(byte[] data)
    {
        Write(data.Length);
        Write(data);
    }

    public void WriteByteArrayAtRawAddress(int offset, byte[] data)
    {
        Seek(offset, SeekOrigin.Begin);
        Write(data);
    }

    public void WriteMetadataClassArray<T>(int offset, T[] objects) where T : ReadableClass
    {
        Seek(offset, SeekOrigin.Begin);
        foreach (var obj in objects)
        {
            WriteReadableClass(obj);
        }
    }

    public void WriteClassArray<T>(int offset, T[] objects) where T : new()
    {
        Seek(offset, SeekOrigin.Begin);

        foreach (var obj in objects)
        {
            WritePrimitive(obj!);
        }
    }
}
