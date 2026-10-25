using System.Collections.Generic;
using Godot;

namespace KirbySuperStar.Rom;

/// <summary>
/// One hardware sprite of a Kirby animation frame, relative to Kirby's position.
/// </summary>
/// <param name="Large">True for a 16x16 sprite, false for 8x8.</param>
/// <param name="X">Horizontal offset in pixels.</param>
/// <param name="Y">Vertical offset in pixels.</param>
/// <param name="Tile">Tile index within Kirby's 16-tile-wide, 2-row graphics slot.</param>
/// <param name="FlipH">Mirrored horizontally.</param>
/// <param name="FlipV">Mirrored vertically.</param>
public readonly record struct SpritePiece(bool Large, int X, int Y, int Tile, bool FlipH, bool FlipV);

/// <summary>
/// Reads Kirby's animation frames: which tiles each frame uploads and how its sprites are arranged.
/// Frame numbers are those the game stores per object; odd frames are the left-facing twin of the even frame before them.
/// </summary>
public sealed class KirbySprites
{
    /// <summary>Number of frames shared by every copy ability.</summary>
    public const int CommonFrameCount = 0x14E;

    /// <summary>Canvas size used by <see cref="RenderFrame"/>.</summary>
    public const int CanvasSize = 64;

    /// <summary>Position of Kirby's origin on the canvas used by <see cref="RenderFrame"/>.</summary>
    public static readonly Vector2I CanvasOrigin = new(32, 40);

    private const int FrameGraphicsTable = 0xC70000;
    private const int LayoutPointerTable = 0xC4DECC;
    private const int LayoutBankTable = 0xC4DEF3;
    private const int KirbyPaletteAddress = 0xC471F6;
    private const int NormalKirbyType = 2;
    private const int SlotVramBase = 0x6000;
    private const int SlotTileMask = 0x1F;
    private const int SharedLayoutLimit = 0x22;
    private const int SharedLayoutFrame = 0x12;
    private const int PuffedFirstFrame = 0xCA;
    private const int PuffedLastFrame = 0xD3;

    private readonly RomImage _rom;

    /// <summary>Creates a reader over <paramref name="rom"/>.</summary>
    public KirbySprites(RomImage rom)
    {
        _rom = rom;
        Palette = SnesGraphics.ReadPalette(rom, RomImage.ToFileOffset(KirbyPaletteAddress));
    }

    /// <summary>Kirby's default sprite palette.</summary>
    public Color[] Palette { get; }

    /// <summary>
    /// Returns the file offset of each tile a frame uploads, keyed by tile index within Kirby's slot.
    /// </summary>
    public Dictionary<int, int> GetTileSources(int frame)
    {
        var sources = new Dictionary<int, int>();
        var tableOffset = RomImage.ToFileOffset(FrameGraphicsTable);
        var record = tableOffset + _rom.U16(tableOffset + (frame & ~1));
        while (true)
        {
            int length = _rom.U16(record);
            if (length == 0)
            {
                return sources;
            }
            var firstTile = (_rom.U16(record + 2) - SlotVramBase) / 16;
            var source = RomImage.ToFileOffset(_rom.U24(record + 4));
            for (var i = 0; i < length / SnesGraphics.Tile4BppSize; i++)
            {
                sources[firstTile + i] = source + i * SnesGraphics.Tile4BppSize;
            }
            record += 7;
        }
    }

    /// <summary>
    /// Returns the sprites of a frame, front-most first.
    /// Frames below <c>$22</c> use the shared 24x24 layout of frame <c>$12</c>, and the puffed frames
    /// <c>$CA</c>-<c>$D3</c> use a 2x2 grid of 16x16 sprites. Both are empirical substitutes for a lookup
    /// that is not yet understood; see <c>docs/rom-notes.md</c>.
    /// </summary>
    public List<SpritePiece> GetLayout(int frame)
    {
        if (frame < SharedLayoutLimit)
        {
            return ReadLayout(SharedLayoutFrame | (frame & 1));
        }
        if (frame >= PuffedFirstFrame && frame <= PuffedLastFrame)
        {
            return PuffedLayout((frame & 1) != 0);
        }
        return ReadLayout(frame);
    }

    private static List<SpritePiece> PuffedLayout(bool facingLeft)
    {
        var pieces = new List<SpritePiece>();
        for (var i = 0; i < 4; i++)
        {
            var column = i % 2;
            var x = facingLeft ? -column * 16 : (column - 1) * 16;
            pieces.Add(new SpritePiece(true, x, i / 2 * 16 - 20, i * 2, facingLeft, false));
        }
        return pieces;
    }

    private List<SpritePiece> ReadLayout(int frame)
    {
        var tableBank = _rom.U8(RomImage.ToFileOffset(LayoutBankTable) + NormalKirbyType);
        var tableAddress = (tableBank << 16) | _rom.U16(RomImage.ToFileOffset(LayoutPointerTable) + NormalKirbyType * 2);
        var index = frame * 2;
        var dataBank = index < 0x44 ? 0xC5 : 0xC6;
        var cursor = RomImage.ToFileOffset((dataBank << 16) | _rom.U16(RomImage.ToFileOffset(tableAddress) + index));

        var pieces = new List<SpritePiece>();
        while (true)
        {
            var flags = _rom.U8(cursor);
            var attributes = _rom.U16(cursor + 3);
            pieces.Add(new SpritePiece(
                Large: (flags & 1) != 0,
                X: _rom.S8(cursor + 1),
                Y: _rom.S8(cursor + 2),
                Tile: attributes & SlotTileMask,
                FlipH: (attributes & 0x4000) != 0,
                FlipV: (attributes & 0x8000) != 0));
            cursor += 5;
            if ((flags & 2) != 0 || pieces.Count >= 32)
            {
                return pieces;
            }
        }
    }

    /// <summary>
    /// Draws a frame onto a <see cref="CanvasSize"/>-square image with Kirby's origin at <see cref="CanvasOrigin"/>.
    /// Sprites whose tiles the frame does not upload are skipped.
    /// </summary>
    public Image RenderFrame(int frame, Color[]? palette = null)
    {
        palette ??= Palette;
        var image = Image.CreateEmpty(CanvasSize, CanvasSize, false, Image.Format.Rgba8);
        var sources = GetTileSources(frame);
        var layout = GetLayout(frame);
        for (var p = layout.Count - 1; p >= 0; p--)
        {
            DrawPiece(image, layout[p], sources, palette);
        }
        return image;
    }

    private void DrawPiece(Image image, SpritePiece piece, Dictionary<int, int> sources, Color[] palette)
    {
        var size = piece.Large ? 2 : 1;
        for (var ty = 0; ty < size; ty++)
        {
            for (var tx = 0; tx < size; tx++)
            {
                var sx = piece.FlipH ? size - 1 - tx : tx;
                var sy = piece.FlipV ? size - 1 - ty : ty;
                if (!sources.TryGetValue(piece.Tile + sy * 16 + sx, out var source))
                {
                    continue;
                }
                var pixels = SnesGraphics.Decode4BppTile(_rom.Slice(source, SnesGraphics.Tile4BppSize));
                for (var py = 0; py < 8; py++)
                {
                    for (var px = 0; px < 8; px++)
                    {
                        var color = pixels[py * 8 + px];
                        if (color == 0)
                        {
                            continue;
                        }
                        var x = CanvasOrigin.X + piece.X + tx * 8 + (piece.FlipH ? 7 - px : px);
                        var y = CanvasOrigin.Y + piece.Y + ty * 8 + (piece.FlipV ? 7 - py : py);
                        if (x >= 0 && y >= 0 && x < CanvasSize && y < CanvasSize)
                        {
                            image.SetPixel(x, y, palette[color]);
                        }
                    }
                }
            }
        }
    }
}
