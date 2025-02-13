using System;
using System.IO;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public abstract class ReadableClass
{
    internal float MetadataVersion { get; set; }
    internal int Size { get; set; }

    public T Clone<T>() => (T)MemberwiseClone();

    protected bool IsAtLeast(float vers) => MetadataVersion >= vers;
    protected bool IsLessThan(float vers) => MetadataVersion < vers;
    protected bool IsAtMost(float vers) => MetadataVersion <= vers;
    protected bool IsNot(float vers) => Math.Abs(MetadataVersion - vers) > 0.001f;
    protected bool Is(float vers) => Math.Abs(MetadataVersion - vers) < 0.001f;

    // TODO: this is truly awful but i really didn't want to manually write a bunch of size calculations
    public virtual void Read(ClassReadingBinaryReader reader)
    {
        using MemoryStream dummyStream = new();
        using ClassWritingBinaryWriter writer = new(dummyStream);

        Write(writer);

        if (writer.BaseStream.Position > 0)
            Size = (int)writer.BaseStream.Position;
    }

    public virtual void Write(ClassWritingBinaryWriter writer) { }
}
