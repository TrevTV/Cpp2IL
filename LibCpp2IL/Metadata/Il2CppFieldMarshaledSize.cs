namespace LibCpp2IL.Metadata;

public class Il2CppFieldMarshaledSize : ReadableClass
{
    public int fieldIndex;
    public int typeIndex;
    public int size;

    public override void Read(ClassReadingBinaryReader reader)
    {
        fieldIndex = reader.ReadInt32();
        typeIndex = reader.ReadInt32();
        size = reader.ReadInt32();

        base.Read(reader);
    }

    public override void Write(ClassWritingBinaryWriter writer)
    {
        writer.Write(fieldIndex);
        writer.Write(typeIndex);
        writer.Write(size);
    }
}
