using System;
using System.Text.Json;
using Godot;

namespace Angband3D;

public enum ViewMode { World, Map, Terminal, Menu }
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
    private bool _autoBirth;
    private int _birthSteps;
    private double _waiting;

    private string[] _script;
    private int _scriptStep;
    private string _shotPath;
    private int _shotAfter = 120;
    private int _frames;
    private bool _shotTaken;
    private string _saveName = "angband3d";
    private bool _manualRequested;
    private bool _inMenu;
    private bool _inPauseMenu;
    private int _menuIndex;
    private int _autoMenuChoice;
    private readonly System.Collections.Generic.List<(string Label, Action Execute)> _menuItems = new();

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
            // Tests only: skip Angband's character creation screens. A player
            // should get to choose their own race and class.
            else if (arg == "--autobirth")
            {
                _autoBirth = true;
            }
            else if (arg == "--manual")
            {
                _manualRequested = true;
            }
            else if (arg.StartsWith("--menu="))
            {
                int.TryParse(arg["--menu=".Length..], out _autoMenuChoice);
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

        // A flag means the caller already decided; otherwise offer the choice.
        if (_autoBirth || _manualRequested)
        {
            StartGame();
        }
        else
        {
            OpenMenu();
        }
    }

    private static string SaveDir()
    {
        var root = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        var repo = System.IO.Path.GetDirectoryName(root) ?? "";
        return System.IO.Path.Combine(repo, "engine", "build", "game", "lib", "save");
    }

    private static System.IO.FileInfo[] GetSaveFiles()
    {
        var dir = SaveDir();
        if (!System.IO.Directory.Exists(dir))
        {
            return Array.Empty<System.IO.FileInfo>();
        }
        var di = new System.IO.DirectoryInfo(dir);
        var files = di.GetFiles();
        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return files;
    }

    private static bool SaveExists(string name) =>
        System.IO.File.Exists(System.IO.Path.Combine(SaveDir(), name));

    /// <summary>A slot name that is not in use, so nothing is ever overwritten.</summary>
    private static string FreeSlot(string baseName)
    {
        if (!SaveExists(baseName))
        {
            return baseName;
        }
        for (var i = 2; i < 100; i++)
        {
            if (!SaveExists($"{baseName}-{i}"))
            {
                return $"{baseName}-{i}";
            }
        }
        return baseName + "-new";
    }

    private void OpenMenu()
    {
        _inMenu = true;
        _inPauseMenu = false;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        if (saves.Length > 0)
        {
            _menuItems.Add(($"Continue Last Played ({saves[0].Name})", () =>
            {
                _saveName = saves[0].Name;
                _inMenu = false;
                StartGame();
            }));
            _menuItems.Add(("Load Saved Game...", OpenLoadMenu));
        }

        _menuItems.Add(("New Character (Custom - Race/Class/Stats)", () =>
        {
            _saveName = FreeSlot(_saveName);
            _inMenu = false;
            StartGame();
        }));
        _menuItems.Add(("New Character (Random)", () =>
        {
            _saveName = FreeSlot(_saveName);
            _autoBirth = true;
            _inMenu = false;
            StartGame();
        }));
        _menuItems.Add(("Quit", () => GetTree().Quit()));

        RefreshMenuDisplay("A N G B A N D 3 D", "first person Angband 4.2.6");

        if (_autoMenuChoice > 0 && _autoMenuChoice <= _menuItems.Count)
        {
            _menuIndex = _autoMenuChoice - 1;
            GD.Print($"menu: auto-selecting {_menuItems[_menuIndex].Label}");
            _menuItems[_menuIndex].Execute();
        }
    }

    private void OpenLoadMenu()
    {
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        foreach (var file in saves)
        {
            var saveFile = file;
            var label = $"{saveFile.Name}  ({saveFile.LastWriteTime:yyyy-MM-dd HH:mm})";
            _menuItems.Add((label, () =>
            {
                _saveName = saveFile.Name;
                _inMenu = false;
                _inPauseMenu = false;
                if (_bridge.Connected)
                {
                    _bridge.Stop();
                }
                StartGame();
            }));
        }

        _menuItems.Add(("Back", _inPauseMenu ? OpenPauseMenu : OpenMenu));
        RefreshMenuDisplay("LOAD GAME", "Select a character to load");
    }

    private void OpenPauseMenu()
    {
        _inMenu = true;
        _inPauseMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        _menuItems.Add(("Resume Game", () =>
        {
            _inMenu = false;
            _inPauseMenu = false;
            if (_bridge.Frame is { } f)
            {
                _overlay.Mode = EffectiveMode(f);
            }
            _overlay.QueueRedraw();
        }));

        _menuItems.Add(("Save Game Now (Ctrl-S)", () =>
        {
            _bridge.SendKey("C-s");
            _inMenu = false;
            _inPauseMenu = false;
            if (_bridge.Frame is { } f)
            {
                _overlay.Mode = EffectiveMode(f);
            }
            _overlay.QueueRedraw();
        }));

        _menuItems.Add(("Load Other Character...", OpenLoadMenu));

        _menuItems.Add(("Save and Quit (Ctrl-X)", () =>
        {
            _bridge.SendKey("C-x");
            _inMenu = false;
            _inPauseMenu = false;
            OpenMenu();
        }));

        _menuItems.Add(("Quit to Main Menu", () =>
        {
            _bridge.Stop();
            _inMenu = false;
            _inPauseMenu = false;
            OpenMenu();
        }));

        RefreshMenuDisplay("GAME MENU", $"Current Character: {_saveName}");
    }

    private void RefreshMenuDisplay(string title, string subtitle)
    {
        _overlay.Status = null;
        _overlay.Mode = ViewMode.Menu;
        _overlay.MenuTitle = title;
        _overlay.MenuSubtitle = subtitle;
        _overlay.MenuItems = _menuItems.ConvertAll(m => m.Label).ToArray();
        _overlay.MenuIndex = _menuIndex;
        _overlay.QueueRedraw();
    }

    private void StartGame()
    {
        var exe = FindEngine();
        _overlay.Mode = ViewMode.Terminal;
        _overlay.Status = "starting...";
        _overlay.QueueRedraw();
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
        _waiting = 0.0;
        _overlay.Waiting = 0.0;

        AutoBirth(f);
        if (!_autoBirth)
        {
            _world.Features = _bridge.Features;
            _world.OnFrame(f);
            // Only while a test script still has steps left: this auto-answers
            // prompts, which would slam a human player's menus shut on open.
            if (_script != null && _scriptStep < _script.Length)
            {
                ScriptTick(f);
            }
        }

        _overlay.Mode = EffectiveMode(f);
        _overlay.FacingName = Facings[_world.Facing];
        _overlay.PromptLine = PromptOf(f);
        _overlay.StairsHint = _world.StairsHint;
        _overlay.QueueRedraw();
    }

    /// <summary>
    /// Which states need Angband's own screen.
    ///
    /// A pushed screen (inventory, store) obviously does. So does any real
    /// question - direction, target, which item - because answering those needs
    /// the map and cursor that only exist on the terminal.
    ///
    /// A bare -more- does not: throwing the player out of the world view on
    /// every combat message is what made fights unreadable.
    /// </summary>
    private static bool NeedsTerminal(JsonElement frame)
    {
        if (frame.GetProperty("phase").GetString() != "play")
        {
            return true;
        }
        if (!frame.TryGetProperty("ui", out var ui))
        {
            return false;
        }
        if (ui.GetProperty("overlay").GetInt32() > 0)
        {
            return true;
        }
        return !ui.GetProperty("awaiting_command").GetBoolean()
               && !ui.GetProperty("more").GetBoolean();
    }

    /// <summary>The pending prompt, if the game is waiting on something small.</summary>
    private static string PromptOf(JsonElement frame)
    {
        if (!frame.TryGetProperty("ui", out var ui))
        {
            return null;
        }
        var waiting = ui.GetProperty("more").GetBoolean()
                      || !ui.GetProperty("awaiting_command").GetBoolean();
        if (!waiting || frame.GetProperty("term").ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var rows = frame.GetProperty("term").GetProperty("rows");
        var line = rows.GetArrayLength() > 0 ? rows[0].GetProperty("g").GetString() ?? "" : "";
        line = line.TrimEnd();
        return line.Length > 0 ? line : null;
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
        // Only a real stall counts: time since the last frame, not merely
        // having a key in flight.
        var wasStalled = _waiting > 2.0;
        _waiting = _bridge is { Busy: true } ? _waiting + delta : 0.0;
        _overlay.Waiting = _waiting;
        if (wasStalled != _waiting > 2.0)
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

        if (_inMenu)
        {
            switch (key.Keycode)
            {
                case Key.Up: _menuIndex = Mathf.PosMod(_menuIndex - 1, _menuItems.Count); break;
                case Key.Down: _menuIndex = Mathf.PosMod(_menuIndex + 1, _menuItems.Count); break;
                case Key.Enter or Key.KpEnter or Key.Space:
                    if (_menuIndex >= 0 && _menuIndex < _menuItems.Count)
                    {
                        _menuItems[_menuIndex].Execute();
                    }
                    return;
                case Key.Escape:
                    if (_inPauseMenu)
                    {
                        _inMenu = false;
                        _inPauseMenu = false;
                        if (_bridge.Frame is { } f)
                        {
                            _overlay.Mode = EffectiveMode(f);
                        }
                        _overlay.QueueRedraw();
                    }
                    else
                    {
                        GetTree().Quit();
                    }
                    return;
                default:
                    var n = key.Keycode - Key.Key1;
                    if (n >= 0 && n < _menuItems.Count)
                    {
                        _menuIndex = (int)n;
                        _menuItems[_menuIndex].Execute();
                    }
                    return;
            }
            _overlay.MenuIndex = _menuIndex;
            _overlay.QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            OpenPauseMenu();
            GetViewport().SetInputAsHandled();
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

        // Capital M only: lowercase m is Angband's cast command.
        if (key.Keycode == Key.M && key.ShiftPressed && (inWorld || _mapMode))
        {
            _mapMode = !_mapMode;
            Refresh();
            return;
        }

        // Turning is a camera change, not a game action: it must always be
        // instant, and must never wait on the engine or cost a game turn.
        if (inWorld && key.Keycode is Key.Left or Key.Right)
        {
            _world.Turn(key.Keycode == Key.Left ? -1 : 1);
            Refresh();
            return;
        }

        if (inWorld && !_bridge.Busy)
        {
            switch (key.Keycode)
            {
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
