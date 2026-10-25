using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using KirbySuperStar.Rom;

namespace KirbySuperStar.Viewer;

/// <summary>
/// Browses Kirby's animation frames straight from the ROM.
/// Left/Right change page, F toggles facing direction.
/// Run with <c>-- --export-sheet=PATH</c> to save every frame as one PNG and quit,
/// or <c>-- --screenshot=PATH</c> to save the first page as shown and quit.
/// </summary>
public partial class SpriteViewer : Node2D
{
    private const string RomPath = "res://rom/kss.sfc";
    private const int Columns = 9;
    private const int Rows = 4;
    private const int Zoom = 2;
    private const int HeaderHeight = 72;

    private KirbySprites? _sprites;
    private Node2D _grid = null!;
    private Label _status = null!;
    private int _page;
    private bool _facingLeft;

    private int PageSize => Columns * Rows;
    private int PageCount => (KirbySprites.CommonFrameCount / 2 + PageSize - 1) / PageSize;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.12f, 0.1f, 0.16f));
        TextureFilter = TextureFilterEnum.Nearest;
        _status = new Label { Position = new Vector2(16, 12) };
        AddChild(_status);
        _grid = new Node2D { Position = new Vector2(0, HeaderHeight) };
        AddChild(_grid);

        try
        {
            _sprites = new KirbySprites(RomImage.Load(ProjectSettings.GlobalizePath(RomPath)));
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _status.Text = $"Could not load the ROM: {e.Message}\nPlace your dump at rom/kss.sfc (see rom/README.md).";
            return;
        }

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--export-sheet="))
            {
                ExportSheet(arg["--export-sheet=".Length..]);
                GetTree().Quit();
                return;
            }
        }

        ShowPage();

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot="))
            {
                SaveScreenshot(arg["--screenshot=".Length..]);
            }
        }
    }

    private async void SaveScreenshot(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        GetViewport().GetTexture().GetImage().SavePng(path);
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_sprites is null || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Right:
                _page = (_page + 1) % PageCount;
                break;
            case Key.Left:
                _page = (_page + PageCount - 1) % PageCount;
                break;
            case Key.F:
                _facingLeft = !_facingLeft;
                break;
            default:
                return;
        }
        ShowPage();
    }

    private void ShowPage()
    {
        foreach (var child in _grid.GetChildren())
        {
            child.QueueFree();
        }

        var cell = KirbySprites.CanvasSize * Zoom;
        for (var i = 0; i < PageSize; i++)
        {
            var frame = (_page * PageSize + i) * 2 + (_facingLeft ? 1 : 0);
            if (frame >= KirbySprites.CommonFrameCount)
            {
                break;
            }
            var origin = new Vector2(i % Columns * cell, i / Columns * cell);
            _grid.AddChild(new Sprite2D
            {
                Texture = ImageTexture.CreateFromImage(_sprites!.RenderFrame(frame)),
                Centered = false,
                Position = origin,
                Scale = new Vector2(Zoom, Zoom),
            });
            _grid.AddChild(new Label
            {
                Text = $"{frame:X3}",
                Position = origin + new Vector2(4, 0),
                Modulate = new Color(1, 1, 1, 0.6f),
            });
        }

        _status.Text = $"Kirby frames, page {_page + 1}/{PageCount}, facing {(_facingLeft ? "left" : "right")}\n"
            + "Left/Right: page    F: flip facing";
    }

    private void ExportSheet(string path)
    {
        var frames = new List<Image>();
        for (var frame = 0; frame < KirbySprites.CommonFrameCount; frame += 2)
        {
            frames.Add(_sprites!.RenderFrame(frame));
        }

        const int sheetColumns = 16;
        var size = KirbySprites.CanvasSize;
        var sheet = Image.CreateEmpty(sheetColumns * size, (frames.Count + sheetColumns - 1) / sheetColumns * size, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.12f, 0.1f, 0.16f));
        for (var i = 0; i < frames.Count; i++)
        {
            sheet.BlendRect(frames[i], new Rect2I(0, 0, size, size), new Vector2I(i % sheetColumns * size, i / sheetColumns * size));
        }
        var error = sheet.SavePng(path);
        GD.Print(error == Error.Ok ? $"Saved {frames.Count} frames to {path}" : $"Failed to save {path}: {error}");
    }
}
