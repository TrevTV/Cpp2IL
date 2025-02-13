namespace LibCpp2IL.Metadata;

public class Il2CppStringLiteral : ReadableClass
{
    public uint length;
    public int dataIndex;
    public string? injectedString = null;
    public bool injected = false;

    public override void Read(ClassReadingBinaryReader reader)
    {
        length = reader.ReadUInt32();
        dataIndex = reader.ReadInt32();

        base.Read(reader);
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(length);
        writer.Write(dataIndex);
    }
}
