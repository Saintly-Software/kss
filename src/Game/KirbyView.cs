using System.Collections.Generic;
using Godot;
using KirbySuperStar.Movement;
using KirbySuperStar.Rom;

namespace KirbySuperStar.Game;

/// <summary>
/// Draws Kirby with frames rendered from the ROM. Each frame is rendered once and cached.
/// </summary>
public partial class KirbyView : Sprite2D
{
    private readonly Dictionary<int, ImageTexture> _cache = new();
    private KirbySprites _sprites = null!;
    private IReadOnlyDictionary<KirbyPose, KirbyAnimation> _animations = null!;

    /// <summary>Game frame number currently shown.</summary>
    public int GameFrame { get; private set; }

    /// <summary>Sets the sprite source. MUST be called before <see cref="Display"/>.</summary>
    public void Setup(KirbySprites sprites, IReadOnlyDictionary<KirbyPose, KirbyAnimation> animations)
    {
        _sprites = sprites;
        _animations = animations;
        Centered = false;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Shows the frame that matches the motor's pose, facing and position.</summary>
    public void Display(KirbyMotor motor)
    {
        var animation = _animations[motor.Pose];
        var frame = animation.FrameAt(motor.PoseTicks) & ~1;
        if (motor.Facing < 0)
        {
            frame |= 1;
        }
        GameFrame = frame;
        if (!_cache.TryGetValue(frame, out var texture))
        {
            texture = ImageTexture.CreateFromImage(_sprites.RenderFrame(frame));
            _cache[frame] = texture;
        }
        Texture = texture;
        Position = (motor.Position + new Vector2(0, animation.OffsetY) - KirbySprites.CanvasOrigin).Floor();
    }
}
