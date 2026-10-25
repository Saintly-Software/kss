using System.Collections.Generic;

namespace KirbySuperStar.Rom;

/// <summary>One frame of an animation and how many ticks it stays on screen.</summary>
/// <param name="Frame">Right-facing frame number as used by <see cref="KirbySprites"/>.</param>
/// <param name="Duration">Ticks at 60 per second. Zero means the frame holds until the animation changes.</param>
public readonly record struct AnimationStep(int Frame, int Duration);

/// <summary>A sequence of frames that either loops or holds its last frame.</summary>
/// <param name="Steps">Frames in order.</param>
/// <param name="Loops">True to repeat; false to hold the last frame.</param>
/// <param name="OffsetY">Pixels to draw the frames lower than Kirby's origin.</param>
public sealed record KirbyAnimation(IReadOnlyList<AnimationStep> Steps, bool Loops, int OffsetY = 0)
{
    /// <summary>Total ticks of one pass, or zero if a step holds forever.</summary>
    public int Length
    {
        get
        {
            var total = 0;
            foreach (var step in Steps)
            {
                if (step.Duration == 0)
                {
                    return 0;
                }
                total += step.Duration;
            }
            return total;
        }
    }

    /// <summary>Returns the frame shown <paramref name="tick"/> ticks after the animation started.</summary>
    public int FrameAt(int tick)
    {
        var length = Length;
        if (length > 0 && Loops)
        {
            tick %= length;
        }
        foreach (var step in Steps)
        {
            if (step.Duration == 0 || tick < step.Duration)
            {
                return step.Frame;
            }
            tick -= step.Duration;
        }
        return Steps[^1].Frame;
    }

    /// <summary>Creates an animation that shows one frame forever.</summary>
    public static KirbyAnimation Still(int frame, int offsetY = 0) => new(new[] { new AnimationStep(frame, 0) }, false, offsetY);
}

/// <summary>Poses the movement prototype can show.</summary>
public enum KirbyPose
{
    Idle,
    Crouch,
    Walk,
    Run,
    Brake,
    JumpRise,
    Fall,
    Land,
    Slide,
    FloatStart,
    Float,
    Exhale,
}

/// <summary>
/// Reads Kirby's animations from the ROM. Addresses and the frame substitutions are listed in <c>docs/rom-notes.md</c>.
/// </summary>
public static class KirbyAnimations
{
    private const int IdleScript = 0xC40CF3;
    private const int JumpScript = 0xC40B14;
    private const int WalkList = 0xC40634;
    private const int RunList = 0xC40642;
    private const int FloatStartList = 0xC40994;
    private const int FloatList = 0xC4099C;
    private const int SetFrameAndWait = 0x47;
    private const int SquashedOffset = 10;

    private static readonly Dictionary<int, int> BrokenFrameSubstitutes = new()
    {
        [0x1A] = 0x18,
    };

    /// <summary>Returns an animation for every <see cref="KirbyPose"/>.</summary>
    public static Dictionary<KirbyPose, KirbyAnimation> Load(RomImage rom)
    {
        return new Dictionary<KirbyPose, KirbyAnimation>
        {
            [KirbyPose.Idle] = new(ReadObjectScript(rom, IdleScript), true),
            [KirbyPose.Crouch] = KirbyAnimation.Still(0x4E, SquashedOffset),
            [KirbyPose.Walk] = new(ReadFrameList(rom, WalkList), true),
            [KirbyPose.Run] = new(ReadFrameList(rom, RunList), true),
            [KirbyPose.Brake] = KirbyAnimation.Still(0x6C),
            [KirbyPose.JumpRise] = new(ReadObjectScript(rom, JumpScript), false),
            [KirbyPose.Fall] = KirbyAnimation.Still(0x1E),
            [KirbyPose.Land] = KirbyAnimation.Still(0x4E, SquashedOffset),
            [KirbyPose.Slide] = KirbyAnimation.Still(0x52, SquashedOffset),
            [KirbyPose.FloatStart] = new(ReadFrameList(rom, FloatStartList), false),
            [KirbyPose.Float] = new(ReadFrameList(rom, FloatList), false),
            [KirbyPose.Exhale] = KirbyAnimation.Still(0xD2),
        };
    }

    /// <summary>
    /// Reads a frame list: 16-bit words of frame (bits 0-9) and duration (bits 10-15), ending with zero.
    /// </summary>
    public static List<AnimationStep> ReadFrameList(RomImage rom, int address)
    {
        var steps = new List<AnimationStep>();
        var offset = RomImage.ToFileOffset(address);
        while (true)
        {
            int word = rom.U16(offset);
            if (word == 0)
            {
                return steps;
            }
            steps.Add(new AnimationStep(Substitute(word & 0x3FF), word >> 10));
            offset += 2;
        }
    }

    /// <summary>
    /// Reads the leading "set frame and wait" commands of an object script, stopping at the first other command.
    /// </summary>
    public static List<AnimationStep> ReadObjectScript(RomImage rom, int address)
    {
        var steps = new List<AnimationStep>();
        var offset = RomImage.ToFileOffset(address);
        while (rom.U8(offset) == SetFrameAndWait)
        {
            steps.Add(new AnimationStep(Substitute(rom.U16(offset + 2)), rom.U8(offset + 1)));
            offset += 4;
        }
        return steps;
    }

    private static int Substitute(int frame) => BrokenFrameSubstitutes.GetValueOrDefault(frame, frame);
}
