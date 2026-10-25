using System.Collections.Generic;
using Godot;

namespace KirbySuperStar.Game;

/// <summary>
/// A hand-built room for trying out movement: floor, platforms at several heights, a pit, a low ceiling and walls.
/// Sizes are in pixels on the 16-pixel tile grid.
/// </summary>
public partial class TestStage : Node2D
{
    /// <summary>Tile size in pixels.</summary>
    public const int Tile = 16;

    /// <summary>Room size in pixels.</summary>
    public static readonly Vector2 Size = new(80 * Tile, 30 * Tile);

    /// <summary>Where Kirby starts: his origin, standing on the floor.</summary>
    public static readonly Vector2 Spawn = new(5 * Tile, 25 * Tile - 12);

    private static readonly Color Sky = new(0.55f, 0.8f, 0.95f);
    private static readonly Color Ground = new(0.45f, 0.3f, 0.2f);
    private static readonly Color GroundTop = new(0.35f, 0.7f, 0.3f);
    private static readonly Color Grid = new(1, 1, 1, 0.08f);

    /// <summary>Solid rectangles Kirby collides with.</summary>
    public List<Rect2> Solids { get; } = new()
    {
        TileRect(0, 0, 80, 1),
        TileRect(0, 1, 1, 29),
        TileRect(79, 1, 1, 29),
        TileRect(1, 25, 34, 5),
        TileRect(35, 28, 4, 2),
        TileRect(39, 25, 40, 5),
        TileRect(10, 23, 6, 1),
        TileRect(18, 21, 4, 1),
        TileRect(25, 19, 4, 1),
        TileRect(45, 15, 8, 1),
        TileRect(56, 22, 5, 3),
        TileRect(64, 22, 12, 1),
    };

    /// <summary>Returns a rectangle in tile units converted to pixels.</summary>
    public static Rect2 TileRect(int x, int y, int width, int height)
    {
        return new Rect2(x * Tile, y * Tile, width * Tile, height * Tile);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Sky);
        for (var x = 0; x <= Size.X; x += Tile)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), Grid);
        }
        for (var y = 0; y <= Size.Y; y += Tile)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Grid);
        }
        foreach (var solid in Solids)
        {
            DrawRect(solid, Ground);
            DrawRect(new Rect2(solid.Position, new Vector2(solid.Size.X, 3)), GroundTop);
        }
    }
}
