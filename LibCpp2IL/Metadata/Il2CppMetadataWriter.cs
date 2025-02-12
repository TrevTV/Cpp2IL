using System;
using System.IO;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata;

public static class Il2CppMetadataWriter
{
    private static Il2CppMetadata _metadata;

    public static void WriteTo(Il2CppMetadata m, string path)
    {
        _metadata = m;

        using var fileStream = File.Open(path, FileMode.Create);
        using var writer = new ClassWritingBinaryWriter(fileStream);

        // metadataHeader (includes magic + version)
        writer.WriteReadableClass(m.metadataHeader);

        LibLogger.Verbose("\tWriting image definitions...");
        var start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppImageDefinition>(m.metadataHeader.imagesOffset, m.imageDefinitions);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading assembly definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppAssemblyDefinition>(m.metadataHeader.assembliesOffset, m.AssemblyDefinitions);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting type definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppTypeDefinition>(m.metadataHeader.typeDefinitionsOffset, m.typeDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting interface offsets...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppInterfaceOffset>(m.metadataHeader.interfaceOffsetsOffset, m.interfaceOffsets);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting vtable indices...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<uint>(m.metadataHeader.vtableMethodsOffset, m.VTableMethodIndices);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting method definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppMethodDefinition>(m.metadataHeader.methodsOffset, m.methodDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting method parameter definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppParameterDefinition>(m.metadataHeader.parametersOffset, m.parameterDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting field definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppFieldDefinition>(m.metadataHeader.fieldsOffset, m.fieldDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting default field values...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppFieldDefaultValue>(m.metadataHeader.fieldDefaultValuesOffset, m.fieldDefaultValues);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting default parameter values...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppParameterDefaultValue>(m.metadataHeader.parameterDefaultValuesOffset, m.parameterDefaultValues);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting default field and parameter values...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<byte>(m.metadataHeader.fieldAndParameterDefaultValueDataOffset, GetFieldAndParameterDefaultValueData(m));
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting property definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppPropertyDefinition>(m.metadataHeader.propertiesOffset, m.propertyDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting interface definitions...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<int>(m.metadataHeader.interfacesOffset, m.interfaceIndices);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting nested type definitions...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<int>(m.metadataHeader.nestedTypesOffset, m.nestedTypeIndices);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting event definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppEventDefinition>(m.metadataHeader.eventsOffset, m.eventDefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting generic container definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppGenericContainer>(m.metadataHeader.genericContainersOffset, m.genericContainers);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting generic parameter definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppGenericParameter>(m.metadataHeader.genericParametersOffset, m.genericParameters);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting generic parameter constraint indices...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<int>(m.metadataHeader.genericParameterConstraintsOffset, m.constraintIndices);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tWriting referenced assemblies...");
        start = DateTime.Now;
        writer.WriteClassArrayAtRawAddr<int>(m.metadataHeader.referencedAssembliesOffset, m.referencedAssemblies);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        //v17+ fields
        LibLogger.Verbose("\tWriting string definitions...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppStringLiteral>(m.metadataHeader.stringLiteralOffset, m.stringLiterals);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        if (m.MetadataVersion < 24.2f)
        {
            LibLogger.Verbose("\tWriting RGCTX data...");
            start = DateTime.Now;

            writer.WriteMetadataClassArray<Il2CppRGCTXDefinition>(m.metadataHeader.rgctxEntriesOffset, m.RgctxDefinitions!);

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        //Removed in v27 (2020.2) and also 24.5 (2019.4.21)
        if (m.MetadataVersion < 27f)
        {
            LibLogger.Verbose("\tWriting usage data...");
            start = DateTime.Now;
            writer.WriteMetadataClassArray<Il2CppMetadataUsageList>(m.metadataHeader.metadataUsageListsOffset, m.metadataUsageLists!);
            writer.WriteMetadataClassArray<Il2CppMetadataUsagePair>(m.metadataHeader.metadataUsagePairsOffset, m.metadataUsagePairs!);

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        LibLogger.Verbose("\tWriting field references...");
        start = DateTime.Now;
        writer.WriteMetadataClassArray<Il2CppFieldRef>(m.metadataHeader.fieldRefsOffset, m.fieldRefs);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        //v21+ fields

        if (m.MetadataVersion < 29)
        {
            //Removed in v29
            LibLogger.Verbose("\tWriting attribute types...");
            start = DateTime.Now;
            writer.WriteMetadataClassArray<Il2CppCustomAttributeTypeRange>(m.metadataHeader.attributesInfoOffset, [.. m.attributeTypeRanges!]);
            writer.WriteClassArrayAtRawAddr<int>(m.metadataHeader.attributeTypesOffset, m.attributeTypes);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
        else
        {
            //Since v29
            LibLogger.Verbose("\tWriting Attribute data...");
            start = DateTime.Now;

            //Pointer array
            writer.WriteMetadataClassArrayAtRawAddr<Il2CppCustomAttributeDataRange>(m.metadataHeader.attributeDataRangeOffset, [.. m.AttributeDataRanges!]);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        // string literals
        for (uint i = 0; i < m.stringLiterals.Length; i++)
        {
            var str = m.GetStringLiteralFromIndex(i);
            //LibLogger.VerboseNewline(str);
            var addr = m.metadataHeader.stringLiteralDataOffset + m.stringLiterals[i].dataIndex;
            writer.WriteStringWithNullTerminatorAtRawAddress(addr, str);
        }

        // strings
        for (var i = 0; i < m.metadataHeader.stringCount; i++)
        {
            var str = m.GetStringFromIndex(i);
            //LibLogger.VerboseNewline(str);
            var addr = m.metadataHeader.stringOffset + i;
            writer.WriteStringWithNullTerminatorAtRawAddress(addr, str);
        }
    }

    private static byte[] GetFieldAndParameterDefaultValueData(Il2CppMetadata m)
    {
        var offset = m.metadataHeader.fieldAndParameterDefaultValueDataOffset;
        var count = m.metadataHeader.fieldAndParameterDefaultValueDataCount;

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

    public void WriteStringWithNullTerminator(string value)
    {
        Write(Encoding.UTF8.GetBytes(value));
        Write((byte)0);
    }

    public void WriteStringWithNullTerminatorAtRawAddress(int offset, string value)
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

    public void WriteMetadataClassArrayAtRawAddr<T>(int offset, T[] objects) where T : ReadableClass
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

    public void WriteClassArrayAtRawAddr<T>(int offset, T[] objects) where T : new()
    {
        Seek(offset, SeekOrigin.Begin);

        foreach (var obj in objects)
        {
            WritePrimitive(obj!);
        }
    }
}
