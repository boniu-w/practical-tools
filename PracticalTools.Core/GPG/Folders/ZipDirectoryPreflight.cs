using System.Buffers.Binary;

namespace PracticalTools.GPG.Folders;

/// <summary>Bound central-directory entry count before ZipArchive allocates its entry collection.</summary>
internal static class ZipDirectoryPreflight
{
    public static void Validate(Stream stream, int maxEntries, CancellationToken token)
    {
        if (stream.Length < 22) throw new InvalidDataException("不是完整的 ZIP 归档。");
        var tail = new byte[(int)Math.Min(stream.Length, 65557)];
        stream.Position = stream.Length - tail.Length;
        stream.ReadExactly(tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length) { end = i; break; }
        }
        if (end < 0 || U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0)
            throw new InvalidDataException("ZIP 目录无效或使用了不支持的分卷格式。");
        ulong count = U16(tail, end + 10), size = U32(tail, end + 12), offset = U32(tail, end + 16);
        var endPosition = stream.Length - tail.Length + end;
        if (count == 65535 || size == uint.MaxValue || offset == uint.MaxValue)
        {
            if (endPosition < 20) throw new InvalidDataException("ZIP64 定位信息缺失。");
            var locator = ReadAt(stream, endPosition - 20, 20);
            if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
                throw new InvalidDataException("ZIP64 定位信息无效。");
            var recordPosition = U64(locator, 8);
            if (recordPosition > (ulong)(stream.Length - 56)) throw new InvalidDataException("ZIP64 目录越界。");
            var record = ReadAt(stream, (long)recordPosition, 56);
            if (U32(record, 0) != 0x06064b50 || U64(record, 4) < 44 || U32(record, 16) != 0 || U32(record, 20) != 0 || U64(record, 24) != U64(record, 32))
                throw new InvalidDataException("ZIP64 目录无效。");
            count = U64(record, 32); size = U64(record, 40); offset = U64(record, 48);
        }
        else if (U16(tail, end + 8) != count) throw new InvalidDataException("ZIP 分卷目录不受支持。");
        if (count > (ulong)maxEntries || offset > (ulong)endPosition || size > (ulong)endPosition - offset)
            throw new InvalidDataException("ZIP 条目数或目录范围超过限制。");
        stream.Position = (long)offset;
        var limit = (long)(offset + size);
        var header = new byte[46];
        ulong actual = 0;
        while (stream.Position < limit)
        {
            token.ThrowIfCancellationRequested();
            if (++actual > (ulong)maxEntries || limit - stream.Position < header.Length) throw new InvalidDataException("ZIP 目录条目数无效。");
            stream.ReadExactly(header);
            if (U32(header, 0) != 0x02014b50 || U16(header, 34) != 0 || (U16(header, 8) & 1) != 0)
                throw new InvalidDataException("ZIP 目录异常，或包含额外加密/分卷条目。");
            var nameLength = U16(header, 28);
            var skip = (long)nameLength + U16(header, 30) + U16(header, 32);
            if (nameLength == 0 || nameLength > 4096 || skip > limit - stream.Position) throw new InvalidDataException("ZIP 条目路径或目录长度无效。");
            stream.Position += skip;
        }
        if (actual != count) throw new InvalidDataException("ZIP 条目数与目录声明不一致。");
        stream.Position = 0;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    { stream.Position = offset; var bytes = new byte[count]; stream.ReadExactly(bytes); return bytes; }
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
}
