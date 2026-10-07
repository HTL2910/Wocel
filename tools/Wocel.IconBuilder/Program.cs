if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Wocel.IconBuilder input.png output.ico");
    return 2;
}

var png = File.ReadAllBytes(args[0]);
await using var output = File.Create(args[1]);
using var writer = new BinaryWriter(output);
writer.Write((ushort)0); // reserved
writer.Write((ushort)1); // icon
writer.Write((ushort)1); // image count
writer.Write((byte)0);   // 256 px
writer.Write((byte)0);   // 256 px
writer.Write((byte)0);   // palette
writer.Write((byte)0);   // reserved
writer.Write((ushort)1); // planes
writer.Write((ushort)32);
writer.Write(png.Length);
writer.Write(22);        // header + directory entry
writer.Write(png);
return 0;
