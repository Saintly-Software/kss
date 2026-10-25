using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using KirbySuperStar.Movement;
using KirbySuperStar.Rom;

namespace KirbySuperStar.Game;

/// <summary>
/// Movement test scene: Kirby in <see cref="TestStage"/> with a following camera and an on-screen help panel.
/// Command-line options after <c>--</c>:
/// <list type="bullet">
/// <item><c>--script=PATH</c> plays an <see cref="InputScript"/> instead of reading the controller.</item>
/// <item><c>--trace=PATH</c> writes Kirby's state every tick as CSV.</item>
/// <item><c>--shots=DIR</c> sets where script screenshots go (default: the script's folder).</item>
/// <item><c>--screenshot=PATH</c> saves one screenshot after a few frames and quits.</item>
/// <item><c>--preset=classic|modern</c> picks the starting preset.</item>
/// </list>
/// </summary>
public partial class Playground : Node2D
{
    private const string RomPath = "res://rom/kss.sfc";
    private const float CameraZoom = 3;

    /// <summary>Preset faithful to the original game.</summary>
    [Export] public MovementProfile Classic { get; set; } = null!;

    /// <summary>Preset with the movement fixes.</summary>
    [Export] public MovementProfile Modern { get; set; } = null!;

    private TestStage _stage = null!;
    private KirbyView _view = null!;
    private Camera2D _camera = null!;
    private Label _hud = null!;
    private KirbyMotor? _motor;
    private List<ScriptCommand>? _script;
    private int _scriptIndex;
    private int _scriptTicksLeft;
    private string _shotDirectory = ".";
    private bool _paused;
    private string? _pressedAction;
    private StreamWriter? _trace;
    private long _tick;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.12f, 0.1f, 0.16f));
        _stage = new TestStage();
        AddChild(_stage);
        _view = new KirbyView();
        AddChild(_view);
        _camera = new Camera2D
        {
            Zoom = new Vector2(CameraZoom, CameraZoom),
            LimitLeft = 0,
            LimitTop = 0,
            LimitRight = (int)TestStage.Size.X,
            LimitBottom = (int)TestStage.Size.Y,
        };
        AddChild(_camera);
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(8, 8) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.6f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
        layer.AddChild(panel);
        _hud = new Label();
        _hud.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_hud);

        KirbySprites sprites;
        Dictionary<KirbyPose, KirbyAnimation> animations;
        try
        {
            var rom = RomImage.Load(ProjectSettings.GlobalizePath(RomPath));
            sprites = new KirbySprites(rom);
            animations = KirbyAnimations.Load(rom);
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _hud.Text = $"Could not load the ROM: {e.Message}\nPlace your dump at rom/kss.sfc (see rom/README.md).";
            return;
        }

        var profile = Classic;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--preset=modern")
            {
                profile = Modern;
            }
        }
        _view.Setup(sprites, animations);
        _motor = new KirbyMotor(profile, _stage.Solids, TestStage.Spawn, animations);
        ReadOptions();
        Present();
    }

    public override void _ExitTree()
    {
        _trace?.Dispose();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_motor is null || _paused)
        {
            return;
        }

        KirbyInput input;
        if (_script is not null)
        {
            if (_pressedAction is not null)
            {
                Input.ActionRelease(_pressedAction);
                _pressedAction = null;
            }
            if (!NextScriptInput(out input))
            {
                return;
            }
        }
        else
        {
            input = ReadController();
        }
        HandleMetaInput();

        _motor.Step(input);
        _trace?.WriteLine(string.Join(',',
            _tick.ToString(CultureInfo.InvariantCulture),
            _motor.Profile.DisplayName,
            _motor.State,
            _motor.Pose,
            $"{_view.GameFrame:X3}",
            F(_motor.Position.X), F(_motor.Position.Y), F(_motor.Velocity.X), F(_motor.Velocity.Y),
            _motor.Grounded ? 1 : 0,
            _motor.Facing,
            InputLetters(input)));
        _tick++;
        Present();
    }

    private void Present()
    {
        if (_motor is null)
        {
            return;
        }
        _view.Display(_motor);
        _camera.Position = _motor.Position.Round();
        var p = _motor.Profile;
        var runHelp = p.RunButton ? "Shift / C / RB: run" : "Run: double-tap a direction";
        _hud.Text =
            $"Preset: {p.DisplayName}   (Tab / Back to switch)      Always run: {(p.AlwaysRun ? "on" : "off")}   (R / LB)\n" +
            $"State: {_motor.State}   Frame: ${_view.GameFrame:X2}   Speed: {_motor.Velocity.X:0.00}, {_motor.Velocity.Y:0.00} px/tick\n" +
            $"Move: arrows / WASD / d-pad     Jump: Z / Space / A     Float: jump again in the air, Up flaps\n" +
            $"Exhale: X / J / X button     Slide: Down + jump     {runHelp}     Reset: Backspace / Start";
    }

    private void HandleMetaInput()
    {
        if (Input.IsActionJustPressed("switch_preset"))
        {
            _motor!.Profile = _motor.Profile == Classic ? Modern : Classic;
        }
        if (Input.IsActionJustPressed("toggle_always_run"))
        {
            _motor!.Profile.AlwaysRun = !_motor.Profile.AlwaysRun;
        }
        if (Input.IsActionJustPressed("reset"))
        {
            _motor!.Teleport(TestStage.Spawn);
        }
    }

    private static KirbyInput ReadController()
    {
        return new KirbyInput(
            Input.IsActionPressed("move_left"),
            Input.IsActionPressed("move_right"),
            Input.IsActionPressed("move_up"),
            Input.IsActionPressed("move_down"),
            Input.IsActionPressed("jump"),
            Input.IsActionPressed("attack"),
            Input.IsActionPressed("run"));
    }

    private bool NextScriptInput(out KirbyInput input)
    {
        input = default;
        while (_scriptTicksLeft == 0)
        {
            if (_scriptIndex >= _script!.Count)
            {
                GetTree().Quit();
                _paused = true;
                return false;
            }
            var command = _script[_scriptIndex++];
            switch (command.Verb)
            {
                case "hold":
                case "wait":
                    _scriptTicksLeft = command.Ticks;
                    break;
                case "press":
                    _pressedAction = command.Args[0];
                    Input.ActionPress(_pressedAction);
                    break;
                case "preset":
                    _motor!.Profile = command.Args[0].Equals("modern", StringComparison.OrdinalIgnoreCase) ? Modern : Classic;
                    break;
                case "teleport":
                    _motor!.Teleport(new Vector2(float.Parse(command.Args[0], CultureInfo.InvariantCulture), float.Parse(command.Args[1], CultureInfo.InvariantCulture)));
                    break;
                case "mark":
                    _trace?.WriteLine($"# {string.Join(' ', command.Args)}");
                    break;
                case "shot":
                    Present();
                    CaptureThenResume(Path.Combine(_shotDirectory, command.Args[0]));
                    return false;
                case "quit":
                    _paused = true;
                    GetTree().Quit();
                    return false;
            }
        }
        input = _script![_scriptIndex - 1].Input;
        _scriptTicksLeft--;
        return true;
    }

    private async void CaptureThenResume(string path)
    {
        _paused = true;
        for (var i = 0; i < 2; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved {path}" : $"Failed to save {path}: {error}");
        _paused = false;
    }

    private void ReadOptions()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--script="))
            {
                var path = arg["--script=".Length..];
                _script = InputScript.Parse(File.ReadAllText(path));
                _shotDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            }
            else if (arg.StartsWith("--shots="))
            {
                _shotDirectory = arg["--shots=".Length..];
            }
            else if (arg.StartsWith("--trace="))
            {
                _trace = new StreamWriter(arg["--trace=".Length..], false, new UTF8Encoding(false));
                _trace.WriteLine("tick,preset,state,pose,frame,x,y,vx,vy,grounded,facing,input");
            }
            else if (arg.StartsWith("--screenshot="))
            {
                _script = new List<ScriptCommand>
                {
                    new("wait", default, 3, Array.Empty<string>()),
                    new("shot", default, 0, new[] { Path.GetFullPath(arg["--screenshot=".Length..]) }),
                };
            }
        }
    }

    private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string InputLetters(KirbyInput input)
    {
        var letters = new StringBuilder();
        foreach (var (held, letter) in new[] { (input.Left, 'L'), (input.Right, 'R'), (input.Up, 'U'), (input.Down, 'D'), (input.Jump, 'J'), (input.Attack, 'A'), (input.Run, 'B') })
        {
            if (held)
            {
                letters.Append(letter);
            }
        }
        return letters.ToString();
    }
}
