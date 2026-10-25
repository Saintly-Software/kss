using System;
using System.IO;
using System.Security.Cryptography;

namespace KirbySuperStar.Rom;

/// <summary>
/// A verified Kirby Super Star (USA) ROM image held in memory.
/// </summary>
public sealed class RomImage
{
    /// <summary>SHA-1 of the only supported dump.</summary>
    public const string ExpectedSha1 = "cca1e67fa230bf4f60154f5ea55d06568229dc5d";

    private readonly byte[] _data;

    private RomImage(byte[] data)
    {
        _data = data;
    }

    /// <summary>Size of the ROM in bytes.</summary>
    public int Length => _data.Length;

    /// <summary>
    /// Loads the ROM at <paramref name="path"/>.
    /// Throws <see cref="FileNotFoundException"/> if it is missing and
    /// <see cref="InvalidDataException"/> if it is not the supported dump.
    /// </summary>
    public static RomImage Load(string path)
    {
        var data = File.ReadAllBytes(path);
        var sha1 = Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();
        if (sha1 != ExpectedSha1)
        {
            throw new InvalidDataException($"{path} is not Kirby Super Star (USA). SHA-1 {sha1}, expected {ExpectedSha1}.");
        }
        return new RomImage(data);
    }

    /// <summary>
    /// Converts an SA-1 CPU address such as <c>0xC70000</c> to a file offset.
    /// Banks $C0-$FF map 64 KiB per bank; banks $00-$3F and $80-$BF map 32 KiB per bank at $8000-$FFFF.
    /// </summary>
    public static int ToFileOffset(int snesAddress)
    {
        var bank = (snesAddress >> 16) & 0xFF;
        var offset = snesAddress & 0xFFFF;
        if (bank >= 0xC0)
        {
            return ((bank - 0xC0) << 16) | offset;
        }
        return ((bank & 0x3F) << 15) | (offset & 0x7FFF);
    }

    /// <summary>Returns <paramref name="length"/> bytes starting at a file offset.</summary>
    public ReadOnlySpan<byte> Slice(int fileOffset, int length) => _data.AsSpan(fileOffset, length);

    /// <summary>Reads an unsigned byte at a file offset.</summary>
    public byte U8(int fileOffset) => _data[fileOffset];

    /// <summary>Reads a signed byte at a file offset.</summary>
    public sbyte S8(int fileOffset) => unchecked((sbyte)_data[fileOffset]);

    /// <summary>Reads a little-endian 16-bit word at a file offset.</summary>
    public ushort U16(int fileOffset) => (ushort)(_data[fileOffset] | (_data[fileOffset + 1] << 8));

    /// <summary>Reads a little-endian 24-bit CPU address at a file offset.</summary>
    public int U24(int fileOffset) => _data[fileOffset] | (_data[fileOffset + 1] << 8) | (_data[fileOffset + 2] << 16);
}
