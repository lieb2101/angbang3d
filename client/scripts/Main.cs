using System;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Phase 1 client: renders the bridge's structured map as a classic roguelike
/// view, with the raw terminal channel available as an overlay.
/// </summary>
public partial class Main : Node2D
{
    private const int TermWidth = 80;
    private const int TermHeight = 24;

    private BridgeClient _bridge;
    private Font _font;
    private int _fontSize = 16;
    private Vector2 _cell = new(10, 18);

    private bool _forceTerminal;
    private string _status = "starting...";
    private bool _autoBirth = true;
    private int _birthSteps;
    private double _waiting;
    private string[] _script;
    private int _scriptStep;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--keys="))
            {
                _script = arg["--keys=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        _font = ThemeDB.FallbackFont;
        _cell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, _fontSize).X,
            _font.GetHeight(_fontSize) + 2);

        _bridge = new BridgeClient();
        AddChild(_bridge);
        _bridge.FrameReceived += OnFrame;
        _bridge.Disconnected += reason =>
        {
            _status = $"disconnected: {reason}";
            QueueRedraw();
        };

        var exe = FindEngine();
        if (exe == null)
        {
            _status = "angband.exe not found - build the engine first (tools/build.ps1)";
            QueueRedraw();
            return;
        }

        _status = "connecting...";
        _bridge.Start(exe);
    }

    /// <summary>Locate the built engine relative to the project directory.</summary>
    private static string FindEngine()
    {
        var root = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        var repo = System.IO.Path.GetDirectoryName(root);
        string[] candidates =
        {
            System.IO.Path.Combine(repo ?? "", "engine", "build", "game", "angband.exe"),
            System.IO.Path.Combine(repo ?? "", "engine", "build", "game", "angband"),
        };
        foreach (var c in candidates)
        {
            if (System.IO.File.Exists(c))
            {
                return c;
            }
        }
        return null;
    }

    public override void _Process(double delta)
    {
        // A key was sent but no frame came back; say so rather than looking frozen.
        var wasStalled = _waiting > 1.0;
        _waiting = _bridge is { Busy: true } ? _waiting + delta : 0.0;
        if (wasStalled != _waiting > 1.0)
        {
            QueueRedraw();
        }
    }

    private void OnFrame()
    {
        var f = _bridge.Frame!.Value;

        if (_status != null)
        {
            GD.Print($"bridge: first frame, phase={f.GetProperty("phase").GetString()}, " +
                     $"term={f.GetProperty("term").GetProperty("w").GetInt32()}x" +
                     $"{f.GetProperty("term").GetProperty("h").GetInt32()}");
        }
        _status = null;

        AutoBirth(f);
        if (!_autoBirth)
        {
            RunScript();
        }
        QueueRedraw();
    }

    /// <summary>
    /// Walk the splash and character-creation screens automatically so the
    /// client starts in the dungeon. '@' completes birth with random choices.
    /// </summary>
    private void AutoBirth(JsonElement frame)
    {
        if (!_autoBirth)
        {
            return;
        }

        if (frame.GetProperty("phase").GetString() == "play"
            && frame.GetProperty("map").ValueKind == JsonValueKind.Object)
        {
            _autoBirth = false;
            var map = frame.GetProperty("map");
            GD.Print($"bridge: in play - map {map.GetProperty("w").GetInt32()}x" +
                     $"{map.GetProperty("h").GetInt32()}, " +
                     $"{frame.GetProperty("monsters").GetArrayLength()} monster(s) visible");
            return;
        }

        if (++_birthSteps > 24)
        {
            _autoBirth = false;
            GD.PushWarning("bridge: gave up auto-completing character creation");
            return;
        }

        _bridge.SendKey(_birthSteps == 1 ? "enter" : "@");
    }

    /// <summary>Replay a scripted key sequence, for automated client testing.</summary>
    private void RunScript()
    {
        if (_script == null || _scriptStep >= _script.Length)
        {
            return;
        }
        var frame = _bridge.Frame!.Value;
        GD.Print($"script: view={(NeedsTerminal(frame) || _forceTerminal ? "TERMINAL" : "map")} " +
                 $"ui={frame.GetProperty("ui")} row0=\"{TermRow(frame, 0).TrimEnd()}\"");
        _bridge.SendKey(_script[_scriptStep++]);
    }

    private static string TermRow(JsonElement frame, int y)
    {
        var rows = frame.GetProperty("term").GetProperty("rows");
        return y < rows.GetArrayLength() ? rows[y].GetProperty("g").GetString() ?? "" : "";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        // Tab forces the terminal on; it is never sent to the game.
        if (key.Keycode == Key.Tab)
        {
            _forceTerminal = !_forceTerminal;
            QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

        var spec = ToKeySpec(key);
        if (spec != null && !_bridge.Busy)
        {
            _bridge.SendKey(spec);
            GetViewport().SetInputAsHandled();
        }
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
            case Key.Home: return "home";
            case Key.End: return "end";
        }

        var ch = (char)key.Unicode;
        if (key.CtrlPressed && key.Keycode is >= Key.A and <= Key.Z)
        {
            return "C-" + char.ToLowerInvariant((char)('a' + (key.Keycode - Key.A)));
        }
        if (ch >= ' ' && ch < 127)
        {
            return ch.ToString();
        }
        return null;
    }

    public override void _Draw()
    {
        var bg = new Color(0.04f, 0.04f, 0.05f);
        DrawRect(new Rect2(Vector2.Zero, GetViewportRect().Size), bg);

        if (_status != null)
        {
            DrawString(_font, new Vector2(20, 40), _status,
                HorizontalAlignment.Left, -1, _fontSize, Colors.Orange);
            return;
        }

        if (_bridge.Frame is not { } frame)
        {
            return;
        }

        if (_forceTerminal || NeedsTerminal(frame))
        {
            DrawTerminal(frame, Vector2.Zero);
            DrawTerminalHint(frame);
        }
        else
        {
            DrawMap(frame);
            DrawHud(frame);
        }

        if (_waiting > 1.0)
        {
            DrawString(_font, new Vector2(4, GetViewportRect().Size.Y - _cell.Y * 3),
                $"waiting for the engine ({_waiting:F0}s)...",
                HorizontalAlignment.Left, -1, _fontSize, Colors.Orange);
        }
    }

    /// <summary>
    /// Whether Angband is showing something that only exists on the terminal
    /// channel: a menu, store, prompt or -more- pause. Without this the client
    /// keeps drawing the map while keystrokes vanish into an invisible menu,
    /// which looks exactly like a freeze.
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

    private void DrawTerminalHint(JsonElement frame)
    {
        var forced = _forceTerminal && !NeedsTerminal(frame);
        var text = forced
            ? "terminal view - Tab to return to the map"
            : "Angband is asking something - answer it, or press ESC to back out";
        DrawString(_font, new Vector2(4, GetViewportRect().Size.Y - 6), text,
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.55f, 0.55f, 0.62f));
    }

    private void DrawMap(JsonElement frame)
    {
        if (frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            // Birth, menus and level generation have no map; show the terminal.
            DrawTerminal(frame, Vector2.Zero);
            return;
        }

        var map = frame.GetProperty("map");
        var rows = map.GetProperty("rows");
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();

        // Centre the view on the player.
        var player = frame.GetProperty("player");
        var view = GetViewportRect().Size;
        var cols = Mathf.FloorToInt(view.X / _cell.X);
        var lines = Mathf.FloorToInt((view.Y - _cell.Y * 3) / _cell.Y);
        var originX = Mathf.Clamp(player.GetProperty("x").GetInt32() - cols / 2, 0, Mathf.Max(0, w - cols));
        var originY = Mathf.Clamp(player.GetProperty("y").GetInt32() - lines / 2, 0, Mathf.Max(0, h - lines));

        var top = _cell.Y * 2;

        for (var row = 0; row < lines && originY + row < h; row++)
        {
            var r = rows[originY + row];
            var glyphs = r.GetProperty("g").GetString() ?? "";
            var attrs = r.GetProperty("a").GetString() ?? "";
            var flags = r.GetProperty("l").GetString() ?? "";

            for (var col = 0; col < cols && originX + col < w; col++)
            {
                var cell = originX + col;
                if (cell >= glyphs.Length)
                {
                    break;
                }

                var ch = glyphs[cell];
                if (ch == ' ')
                {
                    continue;
                }

                var flag = cell < flags.Length ? AngbandColors.HexVal(flags[cell]) : 0;
                var known = (flag & 0x1) != 0;
                var inView = (flag & 0x2) != 0;
                if (!known && !inView)
                {
                    continue;
                }

                var colour = AngbandColors.Get(AngbandColors.ParseAttr(attrs, cell));
                // Remembered but unseen terrain is dimmed: the roguelike map
                // memory that becomes fog-of-memory rendering in 3D.
                if (!inView)
                {
                    colour = colour.Darkened(0.55f);
                }

                DrawString(_font,
                    new Vector2(col * _cell.X, top + row * _cell.Y + _font.GetAscent(_fontSize)),
                    ch.ToString(), HorizontalAlignment.Left, -1, _fontSize, colour);
            }
        }
    }

    private void DrawHud(JsonElement frame)
    {
        var p = frame.GetProperty("player");
        if (p.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string Msg()
        {
            var msgs = frame.GetProperty("messages");
            return msgs.GetArrayLength() > 0
                ? msgs[msgs.GetArrayLength() - 1].GetProperty("text").GetString()
                : "";
        }

        DrawString(_font, new Vector2(4, _font.GetAscent(_fontSize)), Msg(),
            HorizontalAlignment.Left, -1, _fontSize, Colors.White);

        var wizard = p.GetProperty("wizard").GetBoolean() ? "  [WIZARD]" : "";
        var line =
            $"{p.GetProperty("race").GetString()} {p.GetProperty("class").GetString()}  " +
            $"L{p.GetProperty("level").GetInt32()}  " +
            $"HP {p.GetProperty("hp").GetInt32()}/{p.GetProperty("hp_max").GetInt32()}  " +
            $"SP {p.GetProperty("sp").GetInt32()}/{p.GetProperty("sp_max").GetInt32()}  " +
            $"AU {p.GetProperty("gold").GetInt32()}  " +
            $"Depth {p.GetProperty("depth").GetInt32() * 50}ft  " +
            $"Light {p.GetProperty("light").GetInt32()}  " +
            $"Mon {frame.GetProperty("monsters").GetArrayLength()}{wizard}";

        var y = GetViewportRect().Size.Y - _cell.Y;
        DrawString(_font, new Vector2(4, y), line,
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.7f, 0.85f, 1.0f));
        DrawString(_font, new Vector2(4, y - _cell.Y), "Tab: terminal view    ?: help    >: descend",
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.45f, 0.45f, 0.5f));
    }

    private void DrawTerminal(JsonElement frame, Vector2 origin)
    {
        if (frame.GetProperty("term").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var term = frame.GetProperty("term");
        var rows = term.GetProperty("rows");
        var w = term.GetProperty("w").GetInt32();

        for (var y = 0; y < rows.GetArrayLength() && y < TermHeight; y++)
        {
            var r = rows[y];
            var glyphs = r.GetProperty("g").GetString() ?? "";
            var attrs = r.GetProperty("a").GetString() ?? "";

            for (var x = 0; x < w && x < glyphs.Length && x < TermWidth; x++)
            {
                var ch = glyphs[x];
                if (ch == ' ')
                {
                    continue;
                }
                DrawString(_font,
                    origin + new Vector2(x * _cell.X, y * _cell.Y + _font.GetAscent(_fontSize)),
                    ch.ToString(), HorizontalAlignment.Left, -1, _fontSize,
                    AngbandColors.Get(AngbandColors.ParseAttr(attrs, x)));
            }
        }
    }
}
