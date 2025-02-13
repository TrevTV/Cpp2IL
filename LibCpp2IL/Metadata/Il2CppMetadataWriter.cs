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

        using var fileStream = File.Open(path, FileMode.Create);
        using var writer = new ClassWritingBinaryWriter(fileStream);

        _writer = writer;

        WriteMetadataClassArray("image definitions", mh.imagesOffset, ref mh.imagesCount, m.imageDefinitions);
        WriteMetadataClassArray("assembly definitions", mh.assembliesOffset, ref mh.assembliesCount, m.AssemblyDefinitions);
        WriteMetadataClassArray("type definitions", mh.typeDefinitionsOffset, ref mh.typeDefinitionsCount, m.typeDefs);
        WriteMetadataClassArray("interface offsets", mh.interfaceOffsetsOffset, ref mh.interfaceOffsetsCount, m.interfaceOffsets);
        WriteClassArray("vtable indices", mh.vtableMethodsOffset, ref mh.vtableMethodsCount, m.VTableMethodIndices);
        WriteMetadataClassArray("method definitions", mh.methodsOffset, ref mh.methodsCount, m.methodDefs);
        WriteMetadataClassArray("method parameter definitions", mh.parametersOffset, ref mh.parametersCount, m.parameterDefs);
        WriteMetadataClassArray("field definitions", mh.fieldsOffset, ref mh.fieldsCount, m.fieldDefs);
        WriteMetadataClassArray("default field values", mh.fieldDefaultValuesOffset, ref mh.fieldDefaultValuesCount, m.fieldDefaultValues);
        WriteMetadataClassArray("field marshaled sizes", mh.fieldMarshaledSizesOffset, ref mh.fieldMarshaledSizesCount, m.fieldMarshaledSizes);
        WriteMetadataClassArray("default parameter values", mh.parameterDefaultValuesOffset, ref mh.parameterDefaultValuesCount, m.parameterDefaultValues);
        WriteClassArray("field and parameter default values", mh.fieldAndParameterDefaultValueDataOffset, ref mh.fieldAndParameterDefaultValueDataCount, m.fieldAndParameterDefaultValueData);
        WriteMetadataClassArray("property definitions", mh.propertiesOffset, ref mh.propertiesCount, m.propertyDefs);
        WriteClassArray("interface definitions", mh.interfacesOffset, ref mh.interfacesCount, m.interfaceIndices);
        WriteClassArray("nested type definitions", mh.nestedTypesOffset, ref mh.nestedTypesCount, m.nestedTypeIndices);
        WriteMetadataClassArray("event definitions", mh.eventsOffset, ref mh.eventsCount, m.eventDefs);
        WriteMetadataClassArray("generic container definitions", mh.genericContainersOffset, ref mh.genericContainersCount, m.genericContainers);
        WriteMetadataClassArray("generic parameter definitions", mh.genericParametersOffset, ref mh.genericParametersCount, m.genericParameters);
        WriteClassArray("generic parameter constraint indices", mh.genericParameterConstraintsOffset, ref mh.genericParameterConstraintsCount, m.constraintIndices);
        WriteClassArray("referenced assemblies", mh.referencedAssembliesOffset, ref mh.referencedAssembliesCount, m.referencedAssemblies);
        WriteMetadataClassArray("string definitions", mh.stringLiteralOffset, ref mh.stringLiteralCount, m.stringLiterals);

        if (m.MetadataVersion < 24.2f)
        {
            WriteMetadataClassArray("RGCTX data", mh.rgctxEntriesOffset, ref mh.rgctxEntriesCount, m.RgctxDefinitions!);
        }

        if (m.MetadataVersion < 27f)
        {
            WriteMetadataClassArray("usage data", mh.metadataUsageListsOffset, ref mh.metadataUsageListsCount, m.metadataUsageLists!);
            WriteMetadataClassArray("usage pairs", mh.metadataUsagePairsOffset, ref mh.metadataUsagePairsCount, m.metadataUsagePairs!);
        }

        WriteMetadataClassArray("field references", mh.fieldRefsOffset, ref mh.fieldRefsCount, m.fieldRefs);
        WriteClassArray("unresolved virtual call parameter types", mh.unresolvedVirtualCallParameterTypesOffset, ref mh.unresolvedVirtualCallParameterTypesCount, m.unresolvedVirtualCallParameterTypes);
        WriteMetadataClassArray("unresolved virtual call parameter ranges", mh.unresolvedVirtualCallParameterRangesOffset, ref mh.unresolvedVirtualCallParameterRangesCount, m.unresolvedVirtualCallParameterRanges);
        WriteMetadataClassArray("Windows runtime type names", mh.windowsRuntimeTypeNamesOffset, ref mh.windowsRuntimeTypeNamesSize, m.windowsRuntimeTypeNames);

        if (m.MetadataVersion >= 27)
        {
            //WriteMetadataClassArray("Windows runtime strings", mh.windowsRuntimeTypeNamesOffset, ref mh.windowsRuntimeTypeNamesSize, m.windowsRuntimeTypeNames);
        }

        if (m.MetadataVersion >= 24)
        {
            WriteClassArray("exported type definitions", mh.exportedTypeDefinitionsOffset, ref mh.exportedTypeDefinitionsCount, m.exportedTypeDefinitions);
        }

        //v21+ fields
        if (m.MetadataVersion < 29)
        {
            //Removed in v29
            WriteMetadataClassArray("attribute infos", mh.attributesInfoOffset, ref mh.attributesInfoCount, [.. m.attributeTypeRanges!]);
            WriteClassArray("attribute types", mh.attributeTypesOffset, ref mh.attributeTypesCount, m.attributeTypes!);
        }
        else
        {
            //Pointer array
            WriteMetadataClassArray("attribute data", mh.attributeDataRangeOffset, ref mh.attributeDataRangeCount, [.. m.AttributeDataRanges!]);
        }

        for (uint i = 0; i < m.stringLiterals.Length; i++)
        {
            var str = m.GetStringLiteralFromIndex(i);
            var addr = m.metadataHeader.stringLiteralDataOffset + m.stringLiterals[i].dataIndex;
            writer.WriteStringWithNullTerminator(addr, str);
        }

        writer.WriteClassArray<byte>(m.metadataHeader.stringOffset, GetStringDataBytes(m));

        // metadataHeader (includes magic + version)
        writer.WriteReadableClassAtAddr(0, mh);

        // TODO: determine offsets if modified
        // TODO: make stringLiterals and strings modifiable
    }

    private static void WriteMetadataClassArray<T>(string name, int offset, ref int count, T[] data) where T : ReadableClass
    {
        if (data.Length == 0)
            return;

        LibLogger.Verbose($"\tWriting {name}...");
        var start = DateTime.Now;
        _writer.WriteMetadataClassArray(offset, data);
        count = data.Length * data.First().Size;
        Console.WriteLine($"{name} : {data.First().Size}");
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
    }

    private static void WriteClassArray<T>(string name, int offset, ref int count, T[] data) where T : struct
    {
        if (data.Length == 0)
            return;

        LibLogger.Verbose($"\tWriting {name}...");
        var start = DateTime.Now;
        _writer.WriteClassArray(offset, data);
        count = data.Length * Marshal.SizeOf<T>();
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
    }

    private static byte[] GetStringDataBytes(Il2CppMetadata m)
    {
        var offset = m.metadataHeader.stringOffset;
        var count = m.metadataHeader.stringCount;

        return m.ReadByteArrayAtRawAddress(offset, count);
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
