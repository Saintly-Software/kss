using Godot;

namespace KirbySuperStar.Movement;

/// <summary>
/// Every tuning value of Kirby's movement. Speeds are pixels per tick and accelerations pixels per tick
/// squared, at 60 ticks per second; durations are ticks.
/// Values in the "Level-critical" group decide which gaps and ledges Kirby can reach, so presets
/// SHOULD keep them equal to the original game.
/// </summary>
[GlobalClass]
public partial class MovementProfile : Resource
{
    /// <summary>Name shown on screen.</summary>
    [Export] public string DisplayName { get; set; } = "Custom";

    /// <summary>Top walking speed.</summary>
    [ExportGroup("Level-critical")]
    [Export] public float WalkSpeed { get; set; } = 1.3125f;

    /// <summary>Top running speed.</summary>
    [Export] public float RunSpeed { get; set; } = 2f;

    /// <summary>Top horizontal speed in the air after a standing jump or a fall.</summary>
    [Export] public float AirSpeed { get; set; } = 1.4375f;

    /// <summary>Vertical speed set when a jump starts. Negative is up.</summary>
    [Export] public float JumpVelocity { get; set; } = -13f;

    /// <summary>Ticks after take-off during which vertical speed is pulled hard toward <see cref="JumpBoostTarget"/>.</summary>
    [Export] public int JumpBoostTicks { get; set; } = 4;

    /// <summary>Vertical speed the boost pulls toward.</summary>
    [Export] public float JumpBoostTarget { get; set; } = 3.5f;

    /// <summary>How much the boost changes vertical speed per tick.</summary>
    [Export] public float JumpBoostStep { get; set; } = 2.5234375f;

    /// <summary>Downward acceleration in normal air movement.</summary>
    [Export] public float Gravity { get; set; } = 0.171875f;

    /// <summary>Fastest normal fall.</summary>
    [Export] public float MaxFallSpeed { get; set; } = 3.5f;

    /// <summary>Downward acceleration while floating.</summary>
    [Export] public float FloatGravity { get; set; } = 0.09375f;

    /// <summary>Fastest fall while floating.</summary>
    [Export] public float FloatMaxFallSpeed { get; set; } = 1.375f;

    /// <summary>Vertical speed set by each flap. Negative is up.</summary>
    [Export] public float FlapVelocity { get; set; } = -3f;

    /// <summary>Ticks between automatic flaps while jump is held.</summary>
    [Export] public int FlapRepeatTicks { get; set; } = 16;

    /// <summary>Top horizontal speed while floating.</summary>
    [Export] public float FloatSpeed { get; set; } = 1.0625f;

    /// <summary>Speed at the start of a slide.</summary>
    [Export] public float SlideSpeed { get; set; } = 2.75f;

    /// <summary>Ticks the slide keeps full speed.</summary>
    [Export] public int SlideHoldTicks { get; set; } = 16;

    /// <summary>Slow-down per tick after the hold.</summary>
    [Export] public float SlideDecel { get; set; } = 0.125f;

    /// <summary>Ticks of slowing down before the slide stops.</summary>
    [Export] public int SlideDecelTicks { get; set; } = 16;

    /// <summary>Collision box size. Kirby's origin is the box's horizontal center.</summary>
    [Export] public Vector2 HitboxSize { get; set; } = new(16, 20);

    /// <summary>Distance from Kirby's origin down to the bottom of the collision box.</summary>
    [Export] public float HitboxBottom { get; set; } = 12;

    /// <summary>Speed gained per tick while walking.</summary>
    [ExportGroup("Ground")]
    [Export] public float WalkAccel { get; set; } = 0.3125f;

    /// <summary>Speed gained per tick while running.</summary>
    [Export] public float RunAccel { get; set; } = 0.1875f;

    /// <summary>Speed lost per tick when no direction is held.</summary>
    [Export] public float GroundFriction { get; set; } = 0.1875f;

    /// <summary>When true, releasing the direction enters a brake state with its own pose until Kirby stops.</summary>
    [Export] public bool BrakeOnRelease { get; set; } = true;

    /// <summary>
    /// Speed change per tick when the held direction opposes motion.
    /// Zero reverses instantly and keeps the speed.
    /// </summary>
    [Export] public float TurnDecel { get; set; }

    /// <summary>When true, reversing out of a run skids without control until Kirby stops.</summary>
    [Export] public bool RunTurnSkids { get; set; } = true;

    /// <summary>Minimum speed at which reversing shows the skid pose when <see cref="RunTurnSkids"/> is false.</summary>
    [Export] public float SkidPoseMinSpeed { get; set; } = 1.9f;

    /// <summary>Window in ticks for a double tap to start a run.</summary>
    [Export] public int DoubleTapTicks { get; set; } = 20;

    /// <summary>Ticks of holding a direction before walking turns into running. Zero disables it.</summary>
    [Export] public int RunHoldTicks { get; set; }

    /// <summary>When true, the run button runs.</summary>
    [Export] public bool RunButton { get; set; }

    /// <summary>When true, every walk is a run.</summary>
    [Export] public bool AlwaysRun { get; set; }

    /// <summary>Speed gained per tick toward the held direction.</summary>
    [ExportGroup("Air")]
    [Export] public float AirAccel { get; set; } = 0.1875f;

    /// <summary>Speed change per tick when the held direction opposes motion.</summary>
    [Export] public float AirTurnDecel { get; set; } = 0.1875f;

    /// <summary>Speed lost per tick with no direction held.</summary>
    [Export] public float AirFriction { get; set; } = 0.03125f;

    /// <summary>Speed gained per tick during a running jump.</summary>
    [Export] public float RunJumpAccel { get; set; } = 0.09375f;

    /// <summary>Speed change per tick during a running jump when the held direction opposes motion.</summary>
    [Export] public float RunJumpTurnDecel { get; set; } = 0.09375f;

    /// <summary>Speed lost per tick during a running jump with no direction held.</summary>
    [Export] public float RunJumpFriction { get; set; } = 0.015625f;

    /// <summary>
    /// When true, a running jump keeps <see cref="RunSpeed"/> as its top speed until landing.
    /// When false, it drops to <see cref="AirSpeed"/> once the jump animation ends.
    /// </summary>
    [Export] public bool RunJumpKeepsSpeed { get; set; }

    /// <summary>Upward speed kept when jump is released while rising. Negative is up.</summary>
    [Export] public float JumpReleaseVelocity { get; set; } = -1f;

    /// <summary>Gravity multiplier just after the top of a jump while jump is held.</summary>
    [Export] public float ApexGravityScale { get; set; } = 1f;

    /// <summary>Fall speed below which <see cref="ApexGravityScale"/> applies.</summary>
    [Export] public float ApexSpeedThreshold { get; set; } = 1f;

    /// <summary>Speed gained per tick toward the held direction.</summary>
    [ExportGroup("Float")]
    [Export] public float FloatAccel { get; set; } = 0.09375f;

    /// <summary>Speed change per tick when the held direction opposes motion.</summary>
    [Export] public float FloatTurnDecel { get; set; } = 0.09375f;

    /// <summary>Speed lost per tick with no direction held.</summary>
    [Export] public float FloatFriction { get; set; } = 0.015625f;

    /// <summary>
    /// When true, releasing jump stops a flap's climb at once.
    /// When false, it is only checked every <see cref="FlapRepeatTicks"/>.
    /// </summary>
    [Export] public bool FlapCutImmediate { get; set; }

    /// <summary>Ticks spent exhaling before Kirby falls normally.</summary>
    [Export] public int ExhaleTicks { get; set; } = 18;
}
