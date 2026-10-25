using System;
using Godot;

namespace KirbySuperStar.Rom;

/// <summary>
/// Decoders for SNES tile and palette formats.
/// </summary>
public static class SnesGraphics
{
    /// <summary>Bytes in one 8x8 4bpp tile.</summary>
    public const int Tile4BppSize = 32;

    /// <summary>
    /// Decodes one 8x8 4bpp planar tile into 64 palette indices, row-major.
    /// </summary>
    public static byte[] Decode4BppTile(ReadOnlySpan<byte> tile)
    {
        var pixels = new byte[64];
        for (var y = 0; y < 8; y++)
        {
            int p0 = tile[2 * y], p1 = tile[2 * y + 1], p2 = tile[16 + 2 * y], p3 = tile[16 + 2 * y + 1];
            for (var x = 0; x < 8; x++)
            {
                var bit = 7 - x;
                pixels[y * 8 + x] = (byte)(((p0 >> bit) & 1) | (((p1 >> bit) & 1) << 1) | (((p2 >> bit) & 1) << 2) | (((p3 >> bit) & 1) << 3));
            }
        }
        return pixels;
    }

    /// <summary>
    /// Converts a 15-bit BGR555 color to a Godot color.
    /// </summary>
    public static Color FromBgr555(ushort value)
    {
        return Color.Color8(Expand5(value & 0x1F), Expand5((value >> 5) & 0x1F), Expand5((value >> 10) & 0x1F));
    }

    /// <summary>
    /// Reads a 16-color palette at a file offset. Index 0 is transparent.
    /// </summary>
    public static Color[] ReadPalette(RomImage rom, int fileOffset)
    {
        var colors = new Color[16];
        for (var i = 0; i < 16; i++)
        {
            colors[i] = FromBgr555(rom.U16(fileOffset + i * 2));
        }
        colors[0] = Colors.Transparent;
        return colors;
    }

    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));
}
