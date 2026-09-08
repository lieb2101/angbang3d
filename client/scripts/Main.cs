using System;
using System.Text.Json;
using Godot;

namespace Angband3D;

public enum ViewMode { World, Map, Terminal }
/// <summary>
/// Owns the bridge, decides which view is showing, and routes input.
/// </summary>
public partial class Main : Node
{
    private static readonly string[] Facings = { "N", "E", "S", "W" };
    private BridgeClient _bridge;
    private DungeonWorld _world;
    private Overlay _overlay;

    private bool _forceTerminal;
    private bool _mapMode;
    private bool _autoBirth = true;
    private int _birthSteps;
    private double _waiting;

    private string[] _script;
    private int _scriptStep;
    private string _shotPath;
    private int _shotAfter = 120;
    private int _frames;
    private bool _shotTaken;
    private string _saveName = "angband3d";

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--keys="))
            {
                _script = arg["--keys=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (arg.StartsWith("--screenshot="))
            {
                _shotPath = arg["--screenshot=".Length..];
            }
            else if (arg.StartsWith("--shot-after="))
            {
                int.TryParse(arg["--shot-after=".Length..], out _shotAfter);
            }
            else if (arg.StartsWith("--save="))
            {
                _saveName = arg["--save=".Length..];
            }
        }

        _world = new DungeonWorld();
        AddChild(_world);

        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Overlay();
        layer.AddChild(_overlay);

        _bridge = new BridgeClient();
        AddChild(_bridge);
        _bridge.FrameReceived += OnFrame;
        _bridge.Disconnected += reason =>
        {
            _overlay.Status = $"disconnected: {reason}";
            _overlay.QueueRedraw();
        };

        var exe = FindEngine();
        if (exe == null)
        {
            _overlay.Status = "angband.exe not found - build the engine first (build.cmd)";
            return;
        }
        _overlay.Status = "connecting...";
        // Never pass -n: that would overwrite an existing character. Angband
        // loads the save if there is one and starts birth if there is not.
        _bridge.Start(exe, _saveName, newCharacter: false);
    }

    private static string FindEngine()
    {
        var root = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        var repo = System.IO.Path.GetDirectoryName(root) ?? "";
        foreach (var name in new[] { "angband.exe", "angband" })
        {
            var p = System.IO.Path.Combine(repo, "engine", "build", "game", name);
            if (System.IO.File.Exists(p))
            {
                return p;
            }
        }
        return null;
    }

    private void OnFrame()
    {
        var f = _bridge.Frame!.Value;
        _overlay.Status = null;
        _overlay.Frame = f;

        AutoBirth(f);
        if (!_autoBirth)
        {
            _world.OnFrame(f);
            ScriptTick(f);
        }

        _overlay.Mode = EffectiveMode(f);
        _overlay.FacingName = Facings[_world.Facing];
        _overlay.QueueRedraw();
    }

    /// <summary>
    /// Menus, stores and prompts exist only on the terminal channel. Rendering
    /// the world during them would swallow the player's keystrokes invisibly.
    /// </summary>
    private static bool NeedsTerminal(JsonElement frame)
    {
        if (!frame.TryGetProperty("ui", out var ui))
        {
            return false;
        }
        return ui.GetProperty("overlay").GetInt32() > 0
               || ui.GetProperty("more").GetBoolean()
               || !ui.GetProperty("awaiting_command").GetBoolean();
    }

    private ViewMode EffectiveMode(JsonElement frame)
    {
        if (_forceTerminal || NeedsTerminal(frame))
        {
            return ViewMode.Terminal;
        }
        return _mapMode ? ViewMode.Map : ViewMode.World;
    }

    /// <summary>
    /// Answer whatever the screen is asking. Returns true if it handled it, in
    /// which case the caller must not send anything else this frame.
    /// </summary>
    private bool TryAnswerPrompt(JsonElement frame)
    {
        var ui = frame.GetProperty("ui");
        if (ui.GetProperty("awaiting_command").GetBoolean() && !ui.GetProperty("more").GetBoolean())
        {
            return false;
        }

        var screen = Screen(frame);
        if (screen.Contains("-more-"))
        {
            _bridge.SendKey("enter");
        }
        else if (screen.Contains("'y': use as is"))
        {
            _bridge.SendKey("Y");
        }
        else if (screen.Contains("[y/n]") || screen.Contains("are you sure"))
        {
            _bridge.SendKey("y");
        }
        else if (screen.Contains("press any key"))
        {
            _bridge.SendKey("enter");
        }
        else
        {
            _bridge.SendKey("escape");
        }
        return true;
    }

    private void AutoBirth(JsonElement frame)
    {
        if (!_autoBirth)
        {
            return;
        }
        if (frame.GetProperty("phase").GetString() == "play"
            && frame.GetProperty("map").ValueKind == JsonValueKind.Object
            && frame.GetProperty("ui").GetProperty("awaiting_command").GetBoolean())
        {
            _autoBirth = false;
            _world.OnFrame(frame);
            var map = frame.GetProperty("map");
            GD.Print($"client: in play - map {map.GetProperty("w").GetInt32()}x" +
                     $"{map.GetProperty("h").GetInt32()}");
            return;
        }
        if (++_birthSteps > 40)
        {
            _autoBirth = false;
            GD.PushWarning("client: gave up auto-completing character creation");
            return;
        }

        // Answer whatever the screen is asking rather than assuming a fixed key
        // order: an existing savefile adds confirmation screens that a blind
        // sequence walks straight past.
        var screen = Screen(frame);
        if (screen.Contains("-more-"))
        {
            _bridge.SendKey("enter");
        }
        else if (screen.Contains("'y': use as is"))
        {
            _bridge.SendKey("Y");
        }
        else if (screen.Contains("[y/n]") || screen.Contains("are you sure"))
        {
            _bridge.SendKey("y");
        }
        else if (screen.Contains("press any key"))
        {
            _bridge.SendKey("enter");
        }
        else
        {
            _bridge.SendKey("@");
        }
    }

    private void ScriptTick(JsonElement frame)
    {
        if (TryAnswerPrompt(frame))
        {
            return;
        }
        RunScript(frame);
    }

    private static string Screen(JsonElement frame)
    {
        if (frame.GetProperty("term").ValueKind != JsonValueKind.Object)
        {
            return "";
        }
        var sb = new System.Text.StringBuilder();
        foreach (var row in frame.GetProperty("term").GetProperty("rows").EnumerateArray())
        {
            sb.Append(row.GetProperty("g").GetString()).Append('\n');
        }
        return sb.ToString().ToLowerInvariant();
    }

    private static string LastMessage(JsonElement frame)
    {
        var msgs = frame.GetProperty("messages");
        return msgs.GetArrayLength() > 0
            ? (msgs[msgs.GetArrayLength() - 1].GetProperty("text").GetString() ?? "").ToLowerInvariant()
            : "";
    }

    private void RunScript(JsonElement frame)
    {
        // Turning and view toggles never reach the engine, so they produce no
        // new frame; keep consuming steps until one actually sends a key.
        while (_script != null && _scriptStep < _script.Length)
        {
            var spec = _script[_scriptStep++];
            var pl = frame.GetProperty("player");
            var facing = Facings[_world.Facing];
            GD.Print($"script: {spec}  view={EffectiveMode(frame)}  facing={facing}  " +
                     $"pos=({pl.GetProperty("x").GetInt32()},{pl.GetProperty("y").GetInt32()})  " +
                     $"depth={pl.GetProperty("depth").GetInt32()}");

            switch (spec)
            {
                case "turnleft": _world.Turn(-1); continue;
                case "turnright": _world.Turn(1); continue;
                case "map": _mapMode = !_mapMode; continue;
                case "term": _forceTerminal = !_forceTerminal; continue;
                case "forward": _bridge.SendKey(_world.MoveKey(true)); return;
                case "back": _bridge.SendKey(_world.MoveKey(false)); return;
                case "walk":
                    // Scripted exploration: turn rather than butt into a wall.
                    if (LastMessage(frame).Contains("wall in the way"))
                    {
                        _world.Turn(1);
                    }
                    _bridge.SendKey(_world.MoveKey(true));
                    return;
                default: _bridge.SendKey(spec); return;
            }
        }
    }

    public override void _Process(double delta)
    {
        var wasStalled = _waiting > 1.0;
        _waiting = _bridge is { Busy: true } ? _waiting + delta : 0.0;
        _overlay.Waiting = _waiting;
        if (wasStalled != _waiting > 1.0)
        {
            _overlay.QueueRedraw();
        }

        if (_shotPath != null && !_shotTaken && ++_frames >= _shotAfter)
        {
            _shotTaken = true;
            CallDeferred(nameof(Capture));
        }
    }

    private void Capture()
    {
        if (_bridge.Frame is { } f && f.GetProperty("map").ValueKind == JsonValueKind.Object)
        {
            var pl = f.GetProperty("player");
            GD.Print("around: " + _world.DescribeAround(f.GetProperty("map"),
                pl.GetProperty("x").GetInt32(), pl.GetProperty("y").GetInt32()));
        }
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(_shotPath);
        GD.Print($"screenshot: {_shotPath} ({img.GetWidth()}x{img.GetHeight()}) err={err}");
        GetTree().Quit();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || _bridge == null)
        {
            return;
        }

        if (key.Keycode == Key.Tab)
        {
            _forceTerminal = !_forceTerminal;
            Refresh();
            return;
        }

        var frame = _bridge.Frame;
        var inWorld = frame.HasValue && EffectiveMode(frame.Value) == ViewMode.World;

        if (inWorld && key.Keycode == Key.M)
        {
            _mapMode = !_mapMode;
            Refresh();
            return;
        }
        if (_mapMode && key.Keycode == Key.M)
        {
            _mapMode = false;
            Refresh();
            return;
        }

        // Dungeon Master style controls, but only while walking around: turning
        // is free because Angband has no facing, so it costs no game turn.
        if (inWorld && !_bridge.Busy)
        {
            switch (key.Keycode)
            {
                case Key.Left: _world.Turn(-1); Refresh(); return;
                case Key.Right: _world.Turn(1); Refresh(); return;
                case Key.Up: _bridge.SendKey(_world.MoveKey(true)); return;
                case Key.Down: _bridge.SendKey(_world.MoveKey(false)); return;
            }
        }

        var spec = ToKeySpec(key);
        if (spec != null && !_bridge.Busy)
        {
            _bridge.SendKey(spec);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        if (_bridge.Frame is { } f)
        {
            _overlay.Mode = EffectiveMode(f);
            _overlay.FacingName = Facings[_world.Facing];
        }
        _overlay.QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private static string ToKeySpec(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Left: return "left";
            case Key.Right: return "right";
            case Key.Up: return "up";
            case Key.Down: return "down";
            case Key.Enter or Key.KpEnter: return "enter";
            case Key.Escape: return "escape";
            case Key.Space: return "space";
            case Key.Backspace: return "backspace";
        }

        if (key.CtrlPressed && key.Keycode is >= Key.A and <= Key.Z)
        {
            return "C-" + (char)('a' + (key.Keycode - Key.A));
        }

        var ch = (char)key.Unicode;
        return ch >= ' ' && ch < 127 ? ch.ToString() : null;
    }
}
