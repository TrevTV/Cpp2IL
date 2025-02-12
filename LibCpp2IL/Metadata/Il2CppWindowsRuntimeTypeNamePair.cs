namespace LibCpp2IL.Metadata;

public class Il2CppWindowsRuntimeTypeNamePair : ReadableClass
{
    public int nameIndex;
    public int typeIndex;

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();
        typeIndex = reader.ReadInt32();
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(nameIndex);
        writer.Write(typeIndex);
    }
}
