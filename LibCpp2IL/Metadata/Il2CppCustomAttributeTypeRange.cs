namespace LibCpp2IL.Metadata;

public class Il2CppCustomAttributeTypeRange : ReadableClass, IIl2CppTokenProvider
{
    [Version(Min = 24.1f)] public uint token;
    public int start;
    [Version(Max = 27.9f)] public int count; //Removed in v29

    public uint Token => token;

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtLeast(24.1f))
            token = reader.ReadUInt32();

        start = reader.ReadInt32();

        if (IsLessThan(29f))
            count = reader.ReadInt32();

        base.Read(reader);
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        if (IsAtLeast(24.1f))
            writer.Write(token);

        writer.Write(start);

        if (IsLessThan(29f))
            writer.Write(count);
    }
}
