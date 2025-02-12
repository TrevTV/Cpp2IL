namespace LibCpp2IL.Metadata;

public class Il2CppParameterDefaultValue : ReadableClass
{
    public int parameterIndex;
    public int typeIndex;
    public int dataIndex;

    public object? ContainedDefaultValue => LibCpp2ILUtils.GetDefaultValue(dataIndex, typeIndex);

    public override void Read(ClassReadingBinaryReader reader)
    {
        parameterIndex = reader.ReadInt32();
        typeIndex = reader.ReadInt32();
        dataIndex = reader.ReadInt32();
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(parameterIndex);
        writer.Write(typeIndex);
        writer.Write(dataIndex);
    }
}
