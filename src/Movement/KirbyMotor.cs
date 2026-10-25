using System;
using System.Collections.Generic;
using Godot;
using KirbySuperStar.Rom;

namespace KirbySuperStar.Movement;

/// <summary>What Kirby is doing.</summary>
public enum KirbyState
{
    Idle,
    Crouch,
    Walk,
    Run,
    Brake,
    Slide,
    Jump,
    Fall,
    Float,
    Exhale,
}

/// <summary>
/// Kirby's movement as a fixed-step simulation. Call <see cref="Step"/> once per tick at 60 ticks per second.
/// Collision is a box against axis-aligned solid rectangles.
/// </summary>
public sealed class KirbyMotor
{
    private const int LandPoseTicks = 2;
    private const float WalkPoseMinSpeed = 0.25f;

    private readonly IReadOnlyList<Rect2> _solids;
    private readonly int _jumpAnimationTicks;
    private readonly int _floatStartTicks;
    private KirbyInput _previous;
    private int _tick;
    private int _lastHeldLeft = int.MinValue / 2;
    private int _lastHeldRight = int.MinValue / 2;
    private int _holdTicks;
    private int _jumpBoostLeft;
    private bool _runJump;
    private int _flapTimer;
    private bool _brakeLocked;
    private bool _runByButton;
    private bool _skidPose;
    private int _landPose;
    private KirbyPose _pose;

    /// <summary>Creates a motor standing at <paramref name="position"/>.</summary>
    /// <param name="profile">Tuning values. MAY be replaced at any time through <see cref="Profile"/>.</param>
    /// <param name="solids">Solid rectangles in pixels.</param>
    /// <param name="animations">Used only for the lengths of the jump and float-start animations.</param>
    public KirbyMotor(MovementProfile profile, IReadOnlyList<Rect2> solids, Vector2 position, IReadOnlyDictionary<KirbyPose, KirbyAnimation> animations)
    {
        Profile = profile;
        _solids = solids;
        Position = position;
        _jumpAnimationTicks = animations[KirbyPose.JumpRise].Length;
        _floatStartTicks = animations[KirbyPose.FloatStart].Length;
        Grounded = Probe(0, 1);
    }

    /// <summary>Tuning values in use.</summary>
    public MovementProfile Profile { get; set; }

    /// <summary>Kirby's origin in pixels: the center of his body.</summary>
    public Vector2 Position { get; private set; }

    /// <summary>Velocity in pixels per tick. Positive Y is down.</summary>
    public Vector2 Velocity { get; private set; }

    /// <summary>1 when facing right, -1 when facing left.</summary>
    public int Facing { get; private set; } = 1;

    /// <summary>Current state.</summary>
    public KirbyState State { get; private set; }

    /// <summary>Ticks spent in <see cref="State"/>.</summary>
    public int StateTicks { get; private set; }

    /// <summary>True while standing on something solid.</summary>
    public bool Grounded { get; private set; }

    /// <summary>Animation to show.</summary>
    public KirbyPose Pose => _pose;

    /// <summary>Ticks since <see cref="Pose"/> started.</summary>
    public int PoseTicks { get; private set; }

    /// <summary>Collision box in pixels.</summary>
    public Rect2 Hitbox => BoxAt(Position);

    /// <summary>Moves Kirby to <paramref name="position"/> and stops him.</summary>
    public void Teleport(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        Grounded = Probe(0, 1);
        Enter(Grounded ? KirbyState.Idle : KirbyState.Fall);
    }

    /// <summary>Advances the simulation by one tick.</summary>
    public void Step(KirbyInput input)
    {
        var jumpPressed = input.Jump && !_previous.Jump;
        var upPressed = input.Up && !_previous.Up;
        var attackPressed = input.Attack && !_previous.Attack;
        var direction = input.Direction;
        var doubleTap = IsDoubleTap(input);
        TrackHeld(input);

        switch (State)
        {
            case KirbyState.Idle:
            case KirbyState.Walk:
            case KirbyState.Run:
            case KirbyState.Crouch:
                UpdateGround(input, direction, jumpPressed, doubleTap);
                break;
            case KirbyState.Brake:
                UpdateBrake(input, direction, jumpPressed, doubleTap);
                break;
            case KirbyState.Slide:
                UpdateSlide(input);
                break;
            case KirbyState.Jump:
            case KirbyState.Fall:
                UpdateAirborne(input, direction, jumpPressed || upPressed);
                break;
            case KirbyState.Float:
                UpdateFloat(input, direction, jumpPressed || upPressed, attackPressed);
                break;
            case KirbyState.Exhale:
                UpdateExhale(direction);
                break;
        }

        MoveAndCollide();
        AfterMove(input, direction);
        UpdatePose();

        _previous = input;
        _tick++;
        StateTicks++;
        PoseTicks++;
    }

    private void UpdateGround(KirbyInput input, int direction, bool jumpPressed, bool doubleTap)
    {
        var p = Profile;
        if (jumpPressed)
        {
            if (input.Down && State != KirbyState.Run)
            {
                Enter(KirbyState.Slide);
                UpdateSlide(input);
            }
            else
            {
                StartJump(input, direction, State == KirbyState.Run && direction != 0);
            }
            return;
        }

        if (input.Down)
        {
            if (State != KirbyState.Crouch)
            {
                Enter(KirbyState.Crouch);
            }
            ApplyFriction(p.GroundFriction);
            return;
        }
        if (State == KirbyState.Crouch)
        {
            Enter(KirbyState.Idle);
        }

        if (direction == 0)
        {
            _holdTicks = 0;
            _skidPose = false;
            var moving = State is KirbyState.Walk or KirbyState.Run;
            if (moving && p.BrakeOnRelease)
            {
                EnterBrake(State == KirbyState.Run);
                UpdateBrake(input, direction, false, false);
                return;
            }
            if (moving)
            {
                Enter(KirbyState.Idle);
            }
            ApplyFriction(p.GroundFriction);
            return;
        }

        var byButton = p.RunButton && input.Run;
        var wantsRun = doubleTap || p.AlwaysRun || byButton;
        if (State == KirbyState.Idle)
        {
            Facing = direction;
            Enter(wantsRun ? KirbyState.Run : KirbyState.Walk);
            _runByButton = byButton && !doubleTap && !p.AlwaysRun;
        }
        else if (State == KirbyState.Walk)
        {
            _holdTicks++;
            if (wantsRun || (p.RunHoldTicks > 0 && _holdTicks >= p.RunHoldTicks))
            {
                Enter(KirbyState.Run);
                _runByButton = byButton && !doubleTap && !p.AlwaysRun;
            }
        }
        else if (State == KirbyState.Run && _runByButton && !byButton && !p.AlwaysRun)
        {
            Enter(KirbyState.Walk);
        }

        var opposing = Velocity.X != 0 && Math.Sign(Velocity.X) != direction;
        if (State == KirbyState.Run && opposing)
        {
            if (p.RunTurnSkids)
            {
                EnterBrake(true);
                UpdateBrake(input, direction, false, false);
                return;
            }
            if (!_skidPose && Math.Abs(Velocity.X) >= p.SkidPoseMinSpeed)
            {
                _skidPose = true;
            }
        }
        if (!opposing)
        {
            _skidPose = false;
        }

        Facing = direction;
        var running = State == KirbyState.Run;
        GroundMove(direction, running ? p.RunSpeed : p.WalkSpeed, running ? p.RunAccel : p.WalkAccel);
    }

    private void UpdateBrake(KirbyInput input, int direction, bool jumpPressed, bool doubleTap)
    {
        if (jumpPressed)
        {
            StartJump(input, direction, false);
            return;
        }
        if (direction != 0)
        {
            if (!_brakeLocked)
            {
                Facing = direction;
                Enter(doubleTap ? KirbyState.Run : KirbyState.Walk);
                GroundMove(direction, doubleTap ? Profile.RunSpeed : Profile.WalkSpeed, doubleTap ? Profile.RunAccel : Profile.WalkAccel);
                return;
            }
            if (direction == Facing)
            {
                Enter(KirbyState.Run);
                GroundMove(direction, Profile.RunSpeed, Profile.RunAccel);
                return;
            }
            if (doubleTap)
            {
                Facing = direction;
                Velocity = new Vector2(0, Velocity.Y);
                Enter(KirbyState.Run);
                return;
            }
        }
        ApplyFriction(Profile.GroundFriction);
        if (Velocity.X == 0)
        {
            Enter(direction != 0 ? KirbyState.Walk : KirbyState.Idle);
            if (direction != 0)
            {
                Facing = direction;
            }
        }
    }

    private void UpdateSlide(KirbyInput input)
    {
        var p = Profile;
        if (StateTicks < p.SlideHoldTicks)
        {
            Velocity = new Vector2(Facing * p.SlideSpeed, Velocity.Y);
        }
        else if (StateTicks < p.SlideHoldTicks + p.SlideDecelTicks)
        {
            var speed = Math.Max(0, Math.Abs(Velocity.X) - p.SlideDecel);
            Velocity = new Vector2(Facing * speed, Velocity.Y);
        }
        else
        {
            Velocity = new Vector2(0, Velocity.Y);
            Enter(input.Down ? KirbyState.Crouch : KirbyState.Idle);
        }
    }

    private void UpdateAirborne(KirbyInput input, int direction, bool floatPressed)
    {
        var p = Profile;
        if (floatPressed)
        {
            StartFloat();
            UpdateFloat(input, direction, false, false);
            return;
        }

        if (State == KirbyState.Jump && StateTicks >= _jumpAnimationTicks)
        {
            if (!p.RunJumpKeepsSpeed)
            {
                _runJump = false;
            }
            Enter(KirbyState.Fall);
        }

        var vy = Velocity.Y;
        if (_jumpBoostLeft > 0)
        {
            vy = Approach(vy, p.JumpBoostTarget, p.JumpBoostStep);
            _jumpBoostLeft--;
        }
        else
        {
            if (State == KirbyState.Jump && !input.Jump && vy < p.JumpReleaseVelocity)
            {
                vy = p.JumpReleaseVelocity;
            }
            var gravity = p.Gravity;
            if (input.Jump && vy >= 0 && vy < p.ApexSpeedThreshold)
            {
                gravity *= p.ApexGravityScale;
            }
            vy = Approach(vy, p.MaxFallSpeed, gravity);
        }
        Velocity = new Vector2(Velocity.X, vy);

        if (direction != 0)
        {
            Facing = direction;
        }
        if (_runJump)
        {
            AirMove(direction, p.RunSpeed, p.RunJumpAccel, p.RunJumpTurnDecel, p.RunJumpFriction);
        }
        else
        {
            AirMove(direction, p.AirSpeed, p.AirAccel, p.AirTurnDecel, p.AirFriction);
        }
    }

    private void UpdateFloat(KirbyInput input, int direction, bool flapPressed, bool attackPressed)
    {
        var p = Profile;
        if (attackPressed)
        {
            Enter(KirbyState.Exhale);
            UpdateExhale(direction);
            return;
        }

        var vy = Velocity.Y;
        var holding = input.Jump || input.Up;
        _flapTimer--;
        if (flapPressed)
        {
            vy = Flap();
        }
        else if (_flapTimer <= 0)
        {
            _flapTimer = p.FlapRepeatTicks;
            if (holding)
            {
                vy = Flap();
            }
            else if (vy < 0)
            {
                vy = 0;
            }
        }
        else if (p.FlapCutImmediate && !holding && vy < 0)
        {
            vy = 0;
        }
        vy = Approach(vy, p.FloatMaxFallSpeed, p.FloatGravity);
        Velocity = new Vector2(Velocity.X, vy);

        if (direction != 0)
        {
            Facing = direction;
        }
        AirMove(direction, p.FloatSpeed, p.FloatAccel, p.FloatTurnDecel, p.FloatFriction);
    }

    private float Flap()
    {
        _flapTimer = Profile.FlapRepeatTicks;
        if (StateTicks >= _floatStartTicks)
        {
            PoseTicks = 0;
        }
        return Profile.FlapVelocity;
    }

    private void UpdateExhale(int direction)
    {
        var p = Profile;
        if (StateTicks >= p.ExhaleTicks)
        {
            Enter(Grounded ? KirbyState.Idle : KirbyState.Fall);
        }
        Velocity = new Vector2(Velocity.X, Approach(Velocity.Y, p.MaxFallSpeed, p.Gravity));
        if (direction != 0)
        {
            Facing = direction;
        }
        if (Grounded)
        {
            ApplyFriction(p.GroundFriction);
        }
        else
        {
            AirMove(direction, p.AirSpeed, p.AirAccel, p.AirTurnDecel, p.AirFriction);
        }
    }

    private void StartJump(KirbyInput input, int direction, bool running)
    {
        _runJump = running;
        Velocity = new Vector2(Velocity.X, Profile.JumpVelocity);
        _jumpBoostLeft = Profile.JumpBoostTicks;
        Grounded = false;
        Enter(KirbyState.Jump);
        UpdateAirborne(input, direction, false);
    }

    private void StartFloat()
    {
        _jumpBoostLeft = 0;
        if (!Profile.RunJumpKeepsSpeed)
        {
            _runJump = false;
        }
        Enter(KirbyState.Float);
        Velocity = new Vector2(Velocity.X, Flap());
    }

    private void EnterBrake(bool fromRun)
    {
        _brakeLocked = fromRun;
        Enter(KirbyState.Brake);
    }

    private void Enter(KirbyState state)
    {
        if (state != KirbyState.Run)
        {
            _runByButton = false;
        }
        if (state != KirbyState.Walk)
        {
            _holdTicks = 0;
        }
        State = state;
        StateTicks = 0;
    }

    private void GroundMove(int direction, float max, float accel)
    {
        var vx = Velocity.X;
        if (vx != 0 && Math.Sign(vx) != direction)
        {
            vx = Profile.TurnDecel <= 0 ? -vx : vx + direction * Profile.TurnDecel;
            if (Profile.TurnDecel > 0 && Math.Sign(vx) == direction)
            {
                vx = direction * Math.Min(Math.Abs(vx), max);
            }
        }
        else
        {
            vx = Accelerate(vx, direction, max, accel);
        }
        Velocity = new Vector2(vx, Velocity.Y);
    }

    private void AirMove(int direction, float max, float accel, float turn, float friction)
    {
        var vx = Velocity.X;
        if (direction == 0)
        {
            vx = Approach(vx, 0, friction);
        }
        else if (vx != 0 && Math.Sign(vx) != direction)
        {
            vx += direction * turn;
            if (Math.Sign(vx) == direction)
            {
                vx = direction * Math.Min(Math.Abs(vx), max);
            }
        }
        else
        {
            vx = Accelerate(vx, direction, max, accel);
        }
        Velocity = new Vector2(vx, Velocity.Y);
    }

    private static float Accelerate(float vx, int direction, float max, float accel)
    {
        var speed = Math.Abs(vx);
        speed = speed < max ? Math.Min(max, speed + accel) : Math.Max(max, speed - accel);
        return direction * speed;
    }

    private void ApplyFriction(float friction)
    {
        Velocity = new Vector2(Approach(Velocity.X, 0, friction), Velocity.Y);
    }

    private static float Approach(float value, float target, float step)
    {
        return value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
    }

    private bool IsDoubleTap(KirbyInput input)
    {
        var window = Profile.DoubleTapTicks;
        if (input.Right && !_previous.Right && _tick - _lastHeldRight <= window)
        {
            return true;
        }
        return input.Left && !_previous.Left && _tick - _lastHeldLeft <= window;
    }

    private void TrackHeld(KirbyInput input)
    {
        if (_previous.Right)
        {
            _lastHeldRight = _tick - 1;
        }
        if (_previous.Left)
        {
            _lastHeldLeft = _tick - 1;
        }
    }

    private void MoveAndCollide()
    {
        var position = Position;
        var velocity = Velocity;

        position.X += velocity.X;
        foreach (var solid in _solids)
        {
            var box = BoxAt(position);
            if (!box.Intersects(solid))
            {
                continue;
            }
            var half = Profile.HitboxSize.X / 2;
            position.X = velocity.X > 0 ? solid.Position.X - half : solid.End.X + half;
            velocity.X = 0;
        }

        var wasGrounded = Grounded;
        Grounded = false;
        position.Y += velocity.Y;
        foreach (var solid in _solids)
        {
            var box = BoxAt(position);
            if (!box.Intersects(solid))
            {
                continue;
            }
            if (velocity.Y >= 0)
            {
                position.Y = solid.Position.Y - Profile.HitboxBottom;
                Grounded = true;
            }
            else
            {
                position.Y = solid.End.Y + Profile.HitboxSize.Y - Profile.HitboxBottom;
                _jumpBoostLeft = 0;
            }
            velocity.Y = 0;
        }

        Position = position;
        Velocity = velocity;
        if (!Grounded && velocity.Y >= 0 && wasGrounded)
        {
            Grounded = Probe(0, 1);
        }
    }

    private void AfterMove(KirbyInput input, int direction)
    {
        var onGroundState = State is KirbyState.Idle or KirbyState.Walk or KirbyState.Run or KirbyState.Crouch or KirbyState.Brake or KirbyState.Slide;
        if (onGroundState && !Grounded)
        {
            _runJump = State == KirbyState.Run && Profile.RunJumpKeepsSpeed;
            Enter(KirbyState.Fall);
            return;
        }
        if (!Grounded || onGroundState)
        {
            return;
        }

        Velocity = new Vector2(Velocity.X, 0);
        if (State == KirbyState.Exhale)
        {
            return;
        }
        if (State == KirbyState.Float)
        {
            Enter(KirbyState.Exhale);
            return;
        }

        _landPose = LandPoseTicks;
        _runJump = false;
        var keepRun = !Profile.BrakeOnRelease && direction != 0 && Math.Sign(Velocity.X) == direction && Math.Abs(Velocity.X) > Profile.WalkSpeed;
        if (input.Down)
        {
            Enter(KirbyState.Crouch);
        }
        else if (direction == 0)
        {
            Enter(KirbyState.Idle);
            if (Profile.BrakeOnRelease)
            {
                Velocity = new Vector2(0, 0);
            }
        }
        else
        {
            Enter(keepRun ? KirbyState.Run : KirbyState.Walk);
        }
    }

    private void UpdatePose()
    {
        KirbyPose pose;
        switch (State)
        {
            case KirbyState.Idle:
                pose = Math.Abs(Velocity.X) > WalkPoseMinSpeed ? KirbyPose.Walk : KirbyPose.Idle;
                break;
            case KirbyState.Crouch:
                pose = KirbyPose.Crouch;
                break;
            case KirbyState.Walk:
                pose = KirbyPose.Walk;
                break;
            case KirbyState.Run:
                pose = _skidPose ? KirbyPose.Brake : KirbyPose.Run;
                break;
            case KirbyState.Brake:
                pose = KirbyPose.Brake;
                break;
            case KirbyState.Slide:
                pose = KirbyPose.Slide;
                break;
            case KirbyState.Jump:
                pose = KirbyPose.JumpRise;
                break;
            case KirbyState.Float:
                pose = StateTicks < _floatStartTicks ? KirbyPose.FloatStart : KirbyPose.Float;
                break;
            case KirbyState.Exhale:
                pose = KirbyPose.Exhale;
                break;
            default:
                pose = KirbyPose.Fall;
                break;
        }
        if (_landPose > 0)
        {
            _landPose--;
            if (State is KirbyState.Idle or KirbyState.Walk or KirbyState.Run)
            {
                pose = KirbyPose.Land;
            }
        }
        if (pose != _pose)
        {
            _pose = pose;
            PoseTicks = 0;
        }
    }

    private Rect2 BoxAt(Vector2 position)
    {
        var size = Profile.HitboxSize;
        return new Rect2(position.X - size.X / 2, position.Y + Profile.HitboxBottom - size.Y, size.X, size.Y);
    }

    private bool Probe(float dx, float dy)
    {
        var box = BoxAt(Position + new Vector2(dx, dy));
        foreach (var solid in _solids)
        {
            if (box.Intersects(solid))
            {
                return true;
            }
        }
        return false;
    }
}
