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
    private bool _randomRequested;
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
        var exe = FindEngine();
        if (!string.IsNullOrEmpty(exe))
        {
            var dir = System.IO.Path.GetDirectoryName(exe) ?? "";
            return System.IO.Path.Combine(dir, "lib", "save");
        }
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
            _menuItems.Add(("Delete Saved Game...", OpenDeleteMenu));
        }

        _menuItems.Add(("New Character (Custom - Race/Class/Stats)", () =>
        {
            _saveName = FreeSlot(_saveName);
            _inMenu = false;
            StartGame();
        }));
        _menuItems.Add(("New Character (Random - Review & Re-roll)", () =>
        {
            _saveName = FreeSlot(_saveName);
            _randomRequested = true;
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

        if (saves.Length > 0)
        {
            _menuItems.Add(("Delete a Saved Game...", OpenDeleteMenu));
        }
        _menuItems.Add(("Back", _inPauseMenu ? OpenPauseMenu : OpenMenu));
        RefreshMenuDisplay("LOAD GAME", "Select a character to load");
    }

    private void OpenDeleteMenu()
    {
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        if (saves.Length == 0)
        {
            _menuItems.Add(("Back", _inPauseMenu ? OpenPauseMenu : OpenMenu));
            RefreshMenuDisplay("DELETE SAVE", "No saved games found");
            return;
        }

        foreach (var file in saves)
        {
            var saveFile = file;
            var label = $"{saveFile.Name}  ({saveFile.LastWriteTime:yyyy-MM-dd HH:mm})";
            _menuItems.Add((label, () => ConfirmDeleteMenu(saveFile)));
        }

        _menuItems.Add(("Back", _inPauseMenu ? OpenPauseMenu : OpenMenu));
        RefreshMenuDisplay("DELETE SAVE", "Select a saved game to delete");
    }

    private void ConfirmDeleteMenu(System.IO.FileInfo file)
    {
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        _menuItems.Add(($"Yes, Delete '{file.Name}' permanently", () =>
        {
            try
            {
                if (System.IO.File.Exists(file.FullName))
                {
                    System.IO.File.Delete(file.FullName);
                    GD.Print($"client: deleted save {file.FullName}");
                }
            }
            catch (Exception ex)
            {
                GD.PushError($"client: failed to delete save {file.FullName}: {ex.Message}");
            }

            var remaining = GetSaveFiles();
            if (remaining.Length > 0)
            {
                OpenDeleteMenu();
            }
            else if (_inPauseMenu)
            {
                OpenPauseMenu();
            }
            else
            {
                OpenMenu();
            }
        }));

        _menuItems.Add(("Cancel (Keep Save)", OpenDeleteMenu));

        RefreshMenuDisplay("CONFIRM DELETE", $"Are you sure you want to permanently delete '{file.Name}'?");
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

        var saves = GetSaveFiles();
        if (saves.Length > 0)
        {
            _menuItems.Add(("Delete Saved Game...", OpenDeleteMenu));
        }

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
        string execDir = null;
        try
        {
            var execPath = OS.GetExecutablePath();
            if (!string.IsNullOrEmpty(execPath))
            {
                execDir = System.IO.Path.GetDirectoryName(execPath);
            }
        }
        catch { }

        var bases = new System.Collections.Generic.List<string> { repo, root };
        if (!string.IsNullOrEmpty(execDir))
        {
            bases.Add(execDir);
            var parent = System.IO.Path.GetDirectoryName(execDir);
            if (!string.IsNullOrEmpty(parent))
            {
                bases.Add(parent);
            }
        }

        foreach (var name in new[] { "angband.exe", "angband" })
        {
            foreach (var b in bases)
            {
                if (string.IsNullOrEmpty(b)) continue;
                var candidates = new[]
                {
                    System.IO.Path.Combine(b, "engine", "build", "game", name),
                    System.IO.Path.Combine(b, "engine", name),
                    System.IO.Path.Combine(b, "game", name),
                    System.IO.Path.Combine(b, name),
                };
                foreach (var p in candidates)
                {
                    if (System.IO.File.Exists(p))
                    {
                        return p;
                    }
                }
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
        HandleRandomBirth(f);
        if (!_autoBirth && !_randomRequested)
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

    private static bool IsInContextMenu(JsonElement frame)
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
               || ui.GetProperty("more").GetBoolean();
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

    private void HandleRandomBirth(JsonElement frame)
    {
        if (!_randomRequested)
        {
            return;
        }

        if (frame.GetProperty("phase").GetString() == "play"
            && frame.GetProperty("map").ValueKind == JsonValueKind.Object
            && frame.GetProperty("ui").GetProperty("awaiting_command").GetBoolean())
        {
            _randomRequested = false;
            _world.OnFrame(frame);
            return;
        }

        var screen = Screen(frame);
        if (screen.Contains("to start over", StringComparison.OrdinalIgnoreCase))
        {
            // Character summary review screen reached: stop auto-advancing so player can review / re-roll
            _randomRequested = false;
            return;
        }

        if (screen.Contains("press any key to continue", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("[press any key", StringComparison.OrdinalIgnoreCase))
        {
            _bridge.SendKey("enter");
        }
        else if (screen.Contains("select your character traits", StringComparison.OrdinalIgnoreCase))
        {
            _bridge.SendKey("@");
        }
        else if (screen.Contains("-more-", StringComparison.OrdinalIgnoreCase))
        {
            _bridge.SendKey("enter");
        }
        else if (screen.Contains("[y/n]", StringComparison.OrdinalIgnoreCase)
            || screen.Contains("are you sure", StringComparison.OrdinalIgnoreCase))
        {
            _bridge.SendKey("y");
        }
        else
        {
            _bridge.SendKey("enter");
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
                case "strafeleft": _bridge.SendKey(_world.RelativeMoveKey(4)); return;
                case "straferight": _bridge.SendKey(_world.RelativeMoveKey(6)); return;
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
            if (_menuItems.Count == 0)
            {
                return;
            }

            switch (key.Keycode)
            {
                case Key.Up or Key.Kp8: _menuIndex = Mathf.PosMod(_menuIndex - 1, _menuItems.Count); break;
                case Key.Down or Key.Kp2: _menuIndex = Mathf.PosMod(_menuIndex + 1, _menuItems.Count); break;
                case Key.Enter or Key.KpEnter or Key.Space:
                    if (_menuIndex >= 0 && _menuIndex < _menuItems.Count)
                    {
                        _menuItems[_menuIndex].Execute();
                    }
                    return;
                case Key.Escape:
                    if (_overlay.MenuTitle == "CONFIRM DELETE")
                    {
                        OpenDeleteMenu();
                    }
                    else if (_overlay.MenuTitle is "DELETE SAVE" or "LOAD GAME")
                    {
                        if (_inPauseMenu)
                        {
                            OpenPauseMenu();
                        }
                        else
                        {
                            OpenMenu();
                        }
                    }
                    else if (_inPauseMenu)
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
                    var n = (key.Keycode >= Key.Kp1 && key.Keycode <= Key.Kp9)
                        ? (int)(key.Keycode - Key.Kp1)
                        : (int)(key.Keycode - Key.Key1);
                    if (n >= 0 && n < _menuItems.Count)
                    {
                        _menuIndex = n;
                        _menuItems[_menuIndex].Execute();
                    }
                    return;
            }
            _overlay.MenuIndex = _menuIndex;
            _overlay.QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

        var frame = _bridge.Frame;

        if (frame.HasValue && frame.Value.GetProperty("phase").GetString() == "setup")
        {
            var screen = Screen(frame.Value);
            if (screen.Contains("to start over", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Keycode == Key.S)
                {
                    _randomRequested = true;
                    _bridge.SendKey("s");
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        if (key.Keycode == Key.Escape)
        {
            if (frame.HasValue && IsInContextMenu(frame.Value))
            {
                if (!_bridge.Busy)
                {
                    _bridge.SendKey("escape");
                    GetViewport().SetInputAsHandled();
                }
                return;
            }

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

        var inWorld = frame.HasValue && EffectiveMode(frame.Value) == ViewMode.World;

        // Capital M only: lowercase m is Angband's cast command.
        if (key.Keycode == Key.M && key.ShiftPressed && (inWorld || _mapMode))
        {
            _mapMode = !_mapMode;
            Refresh();
            return;
        }

        // Relative directional movement via number pad (or top-row numbers in world view).
        // 8 = forward, 2 = back, 4 = strafe left, 6 = strafe right, diagonals 7,9,1,3, 5 = stay.
        var dir = GetDirectionKey(key);
        if (inWorld && dir >= 1 && dir <= 9)
        {
            if (!_bridge.Busy)
            {
                var moveKey = _world.RelativeMoveKey(dir);
                if (moveKey != null)
                {
                    _bridge.SendKey(moveKey);
                }
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inWorld && key.Keycode == Key.Pageup)
        {
            _overlay.ChangeMinimapScale(0.15f);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inWorld && key.Keycode == Key.Pagedown)
        {
            _overlay.ChangeMinimapScale(-0.15f);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Turning is a camera change, not a game action: it must always be
        // instant, and must never wait on the engine or cost a game turn.
        // Arrow Left / Right turn orientation; number pad 4 / 6 strafes instead (handled above).
        if (inWorld && key.Keycode is Key.Left or Key.Right)
        {
            _world.Turn(key.Keycode == Key.Left ? -1 : 1);
            Refresh();
            return;
        }

        if (inWorld && key.Keycode is Key.Up or Key.Down)
        {
            if (!_bridge.Busy)
            {
                _bridge.SendKey(_world.MoveKey(key.Keycode == Key.Up));
            }
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

    private static int GetDirectionKey(InputEventKey key)
    {
        if (key.Keycode is >= Key.Kp1 and <= Key.Kp9)
        {
            return (int)(key.Keycode - Key.Kp1) + 1;
        }

        if (key.PhysicalKeycode is >= Key.Kp1 and <= Key.Kp9)
        {
            return (int)(key.PhysicalKeycode - Key.Kp1) + 1;
        }

        if (key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            return (int)(key.Keycode - Key.Key1) + 1;
        }

        return 0;
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
            case Key.Tab: return "tab";
            case Key.Delete: return "delete";
            case Key.Home: return "home";
            case Key.End: return "end";
        }

        if (key.Keycode is >= Key.Kp0 and <= Key.Kp9)
        {
            return ((char)('0' + (key.Keycode - Key.Kp0))).ToString();
        }

        if (key.Keycode is Key.KpPeriod) return ".";
        if (key.Keycode is Key.KpAdd) return "+";
        if (key.Keycode is Key.KpSubtract) return "-";
        if (key.Keycode is Key.KpMultiply) return "*";
        if (key.Keycode is Key.KpDivide) return "/";

        if (key.CtrlPressed && key.Keycode is >= Key.A and <= Key.Z)
        {
            return "C-" + (char)('a' + (key.Keycode - Key.A));
        }

        var ch = (char)key.Unicode;
        return ch >= ' ' && ch < 127 ? ch.ToString() : null;
    }
}
