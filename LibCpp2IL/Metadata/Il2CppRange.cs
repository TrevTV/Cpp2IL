namespace LibCpp2IL.Metadata;

public class Il2CppRange : ReadableClass
{
    public int start;
    public int length;

    public override void Read(ClassReadingBinaryReader reader)
    {
        start = reader.ReadInt32();
        length = reader.ReadInt32();

        base.Read(reader);
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(start);
        writer.Write(length);
    }
}
