using System.Text;

namespace PracticalTools.TestFixtures;

internal static class MediaFixture
{
    public static byte[] Wave()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        const int samples = 8000 * 4;
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVEfmt "u8); w.Write(16);
        w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        for (int i = 0; i < samples; i++) w.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 8000) * 1000));
        return stream.ToArray();
    }
    public static byte[] Avi()
    {
        static byte[] Build(Action<BinaryWriter> write) { using var s = new MemoryStream(); using var w = new BinaryWriter(s); write(w); return s.ToArray(); }
        static void Chunk(BinaryWriter w, string name, byte[] bytes)
        { w.Write(Encoding.ASCII.GetBytes(name)); w.Write(bytes.Length); w.Write(bytes); if (bytes.Length % 2 != 0) w.Write((byte)0); }
        var avih = Build(w => { w.Write(100000); w.Write(160); w.Write(0); w.Write(0); w.Write(30); w.Write(0); w.Write(1); w.Write(16); w.Write(2); w.Write(2); w.Write(new byte[16]); });
        var strh = Build(w => { w.Write("vidsDIB "u8); w.Write(0); w.Write(0); w.Write(0); w.Write(1); w.Write(10); w.Write(0); w.Write(30); w.Write(16); w.Write(-1); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write((short)2); w.Write((short)2); });
        var strf = Build(w => { w.Write(40); w.Write(2); w.Write(2); w.Write((short)1); w.Write((short)24); w.Write(0); w.Write(16); w.Write(new byte[16]); });
        var strl = Build(w => { w.Write("strl"u8); Chunk(w, "strh", strh); Chunk(w, "strf", strf); });
        var hdrl = Build(w => { w.Write("hdrl"u8); Chunk(w, "avih", avih); Chunk(w, "LIST", strl); });
        var movi = Build(w => { w.Write("movi"u8); for (int i = 0; i < 30; i++) Chunk(w, "00db", new byte[] { 0, 0, 255, 0, 0, 255, 0, 0, 0, 0, 255, 0, 0, 255, 0, 0 }); });
        return Build(w => Chunk(w, "RIFF", Build(b => { b.Write("AVI "u8); Chunk(b, "LIST", hdrl); Chunk(b, "LIST", movi); })));
    }
}
