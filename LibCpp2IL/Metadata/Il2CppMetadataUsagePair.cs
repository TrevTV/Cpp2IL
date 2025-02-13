namespace LibCpp2IL.Metadata;

public class Il2CppMetadataUsagePair : ReadableClass
{
    public uint destinationIndex;
    public uint encodedSourceIndex;

    public override void Read(ClassReadingBinaryReader reader)
    {
        destinationIndex = reader.ReadUInt32();
        encodedSourceIndex = reader.ReadUInt32();

        base.Read(reader);
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(destinationIndex);
        writer.Write(encodedSourceIndex);
    }
}
