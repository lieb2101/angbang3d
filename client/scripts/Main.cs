using System;
using System.Text.Json;
using Godot;

namespace Angband3D;

public enum ViewMode { World, Map, Terminal, Menu, Splash, Guide, Death }
/// <summary>
/// Owns the bridge, decides which view is showing, and routes input.
/// </summary>
public partial class Main : Node
{
    private static readonly string[] Facings = { "N", "E", "S", "W" };
    private IGameEngineBridge _bridge;
    private BridgeClient _localBridge;
    private WebSocketBridgeClient _cloudBridge;
    private bool _cloudMode;
    private string _cloudUrl = "ws://localhost:8080/ws";
    private string _downloadUrl = "http://localhost:8080/download/angband3d-standalone.zip";
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
    private string _saveName;
    private string _characterName;
    private bool _manualRequested;
    private bool _randomRequested;
    private bool _inSplash;
    private bool _inGuide;
    private bool _inMenu;
    private bool _inPauseMenu;
    private bool _inDeath;
    private int _menuIndex;
    private int _autoMenuChoice;
    private readonly System.Collections.Generic.List<(string Label, Action Execute)> _menuItems = new();

    public override void _Ready()
    {
        try
        {
            if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
            }
        }
        catch { }

        var allArgs = new System.Collections.Generic.List<string>(OS.GetCmdlineUserArgs());
        foreach (var a in OS.GetCmdlineArgs())
        {
            if (!allArgs.Contains(a))
            {
                allArgs.Add(a);
            }
        }

        foreach (var arg in allArgs)
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
            else if (arg == "--cloud")
            {
                _cloudMode = true;
            }
            else if (arg.StartsWith("--server="))
            {
                _cloudMode = true;
                _cloudUrl = arg["--server=".Length..];
            }
            else if (arg.StartsWith("--download-url="))
            {
                _downloadUrl = arg["--download-url=".Length..];
            }
        }

        if (OS.HasFeature("web"))
        {
            _cloudMode = true;
        }

        AddChild(new AudioManager());

        _world = new DungeonWorld();
        AddChild(_world);

        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Overlay();
        layer.AddChild(_overlay);

        _overlay.MenuItemClicked += idx =>
        {
            if (_inMenu && idx >= 0 && idx < _menuItems.Count)
            {
                AudioManager.Play(SoundEffect.MenuSelect);
                _menuIndex = idx;
                _menuItems[_menuIndex].Execute();
            }
        };

        _overlay.GuideTabClicked += section =>
        {
            if (_inGuide)
            {
                AudioManager.Play(SoundEffect.MenuNav);
                _overlay.GuideSection = Mathf.Clamp(section, 0, 5);
                _overlay.GuideScroll = 0;
                _overlay.QueueRedraw();
            }
        };

        _overlay.GuideCloseRequested += () =>
        {
            if (_inGuide)
            {
                _inGuide = false;
                if (_inPauseMenu)
                {
                    OpenPauseMenu();
                }
                else
                {
                    OpenMenu();
                }
            }
        };

        _overlay.SplashContinueRequested += () =>
        {
            if (_inSplash)
            {
                OpenMenu();
            }
        };

        _overlay.SplashGuideRequested += () =>
        {
            if (_inSplash)
            {
                OpenGuide();
            }
        };

        _overlay.SplashWikiRequested += () =>
        {
            OS.ShellOpen("https://angband.readthedocs.io/");
        };

        _overlay.SplashQuitRequested += () =>
        {
            GetTree().Quit();
        };

        _overlay.DeathReloadRequested += OnDeathReload;
        _overlay.DeathRerollRequested += OnDeathReroll;
        _overlay.DeathIdentifyRequested += OnDeathIdentify;
        _overlay.DeathMainMenuRequested += OnDeathMainMenu;
        _overlay.DeathTabClicked += section =>
        {
            if (_inDeath)
            {
                AudioManager.Play(SoundEffect.MenuNav);
                _overlay.DeathSection = Mathf.Clamp(section, 0, 4);
                _overlay.DeathScroll = 0;
                _overlay.QueueRedraw();
            }
        };

        _localBridge = new BridgeClient();
        AddChild(_localBridge);
        _localBridge.FrameReceived += OnFrame;
        _localBridge.Disconnected += OnBridgeDisconnected;

        _cloudBridge = new WebSocketBridgeClient();
        AddChild(_cloudBridge);
        _cloudBridge.FrameReceived += OnFrame;
        _cloudBridge.Disconnected += OnBridgeDisconnected;

        var exe = FindEngine();
        if (exe == null && !_cloudMode)
        {
            _cloudMode = true;
            GD.Print("Local engine not found; automatically defaulting to Cloud Mode.");
        }

        _bridge = _cloudMode ? _cloudBridge : _localBridge;

        // A flag or test script means the caller already decided; otherwise show splash.
        if (_autoBirth || _manualRequested)
        {
            StartGame();
        }
        else if (_script != null || _autoMenuChoice > 0)
        {
            OpenMenu();
        }
        else
        {
            OpenSplash();
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

    public record SaveFileInfo(System.IO.FileInfo File, string CharacterName, string Description)
    {
        public string DisplayName => !string.IsNullOrEmpty(CharacterName) ? CharacterName : File.Name;

        public string DisplaySummary
        {
            get
            {
                if (!string.IsNullOrEmpty(Description))
                {
                    return Description;
                }
                return DisplayName;
            }
        }
    }

    private static SaveFileInfo GetSaveInfo(System.IO.FileInfo file)
    {
        var desc = ReadSaveDescription(file.FullName);
        string charName = null;
        if (!string.IsNullOrEmpty(desc))
        {
            var commaIdx = desc.IndexOf(',');
            if (commaIdx > 0)
            {
                charName = desc.Substring(0, commaIdx).Trim();
            }
        }
        return new SaveFileInfo(file, charName, desc);
    }

    private static string ReadSaveDescription(string filePath)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(filePath);
            if (stream.Length < 36)
            {
                return null;
            }

            var header = new byte[36];
            var read = stream.Read(header, 0, 36);
            if (read < 36)
            {
                return null;
            }

            // Check magic "SaveVNLA"
            if (header[0] != 0x53 || header[1] != 0x61 || header[2] != 0x76 || header[3] != 0x65 ||
                header[4] != 0x56 || header[5] != 0x4E || header[6] != 0x4C || header[7] != 0x41)
            {
                return null;
            }

            // Check block name "description"
            var blockName = System.Text.Encoding.ASCII.GetString(header, 8, 16).TrimEnd('\0');
            if (blockName != "description")
            {
                return null;
            }

            var size = System.BitConverter.ToUInt32(header, 28);
            if (size == 0 || size > 512 || stream.Length < 36 + size)
            {
                return null;
            }

            var buf = new byte[size];
            var bufRead = stream.Read(buf, 0, (int)size);
            if (bufRead < size)
            {
                return null;
            }

            var nullIdx = System.Array.IndexOf(buf, (byte)0);
            var strLen = nullIdx >= 0 ? nullIdx : (int)size;
            return System.Text.Encoding.UTF8.GetString(buf, 0, strLen);
        }
        catch
        {
            return null;
        }
    }

    private static System.IO.FileInfo[] GetSaveFiles()
    {
        var dir = SaveDir();
        if (!System.IO.Directory.Exists(dir))
        {
            return Array.Empty<System.IO.FileInfo>();
        }
        var di = new System.IO.DirectoryInfo(dir);
        var list = new System.Collections.Generic.List<System.IO.FileInfo>();
        foreach (var f in di.GetFiles())
        {
            if (!f.Name.StartsWith("smoke", StringComparison.OrdinalIgnoreCase) &&
                !f.Name.StartsWith("test", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(f);
            }
        }
        var files = list.ToArray();
        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
        return files;
    }

    private void OpenSplash()
    {
        _inSplash = true;
        _inGuide = false;
        _inMenu = false;
        _inPauseMenu = false;
        _overlay.Status = null;
        _overlay.Mode = ViewMode.Splash;
        _overlay.QueueRedraw();
    }

    private void OpenGuide()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inGuide = true;
        _inSplash = false;
        _inMenu = false;
        _overlay.Status = null;
        _overlay.Mode = ViewMode.Guide;
        _overlay.GuideSection = 0;
        _overlay.GuideScroll = 0;
        _overlay.QueueRedraw();
    }

    private void OpenMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inSplash = false;
        _inGuide = false;
        _inMenu = true;
        _inPauseMenu = false;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        if (saves.Length > 0)
        {
            var topSave = GetSaveInfo(saves[0]);
            _menuItems.Add(($"Continue Last Played ({topSave.DisplayName})", () =>
            {
                _saveName = saves[0].Name;
                _characterName = topSave.CharacterName;
                _inMenu = false;
                StartGame();
            }));
            _menuItems.Add(("Load Saved Game...", OpenLoadMenu));
            _menuItems.Add(("Save Game Manager (Backup / Export / Import)", OpenSaveManagerMenu));
            _menuItems.Add(("Delete Saved Game...", OpenDeleteMenu));
        }
        else
        {
            _menuItems.Add(("Save Game Manager (Import / Cloud Sync)", OpenSaveManagerMenu));
        }

        _menuItems.Add(("New Character (Custom - Race/Class/Stats)", () =>
        {
            _saveName = null;
            _characterName = null;
            _inMenu = false;
            StartGame();
        }));
        _menuItems.Add(("New Character (Random - Review & Re-roll)", () =>
        {
            _saveName = null;
            _characterName = null;
            _randomRequested = true;
            _inMenu = false;
            StartGame();
        }));

        var modeLabel = _cloudMode
            ? "Engine Mode: [Cloud Realm] (Click to switch to Local Engine)"
            : "Engine Mode: [Local Engine] (Click to switch to Cloud Realm)";
        _menuItems.Add((modeLabel, () =>
        {
            AudioManager.Play(SoundEffect.MenuSelect);
            _cloudMode = !_cloudMode;
            _bridge = _cloudMode ? _cloudBridge : _localBridge;
            OpenMenu();
        }));

        _menuItems.Add(("Download Standalone Game Package (.zip)", () =>
        {
            AudioManager.Play(SoundEffect.MenuSelect);
            OS.ShellOpen(_downloadUrl);
        }));

        _menuItems.Add(("Game Guide & Primer (Controls, Survival, Wiki)", OpenGuide));
        _menuItems.Add(("Toggle Fullscreen / Windowed (F11)", () =>
        {
            ToggleFullscreen();
            RefreshMenuDisplay("A N G B A N D 3 D", "first person Angband 4.2.6");
        }));
        _menuItems.Add(("Angband Wiki (Open in Browser)", () =>
        {
            OS.ShellOpen("https://angband.readthedocs.io/");
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
            var info = GetSaveInfo(saveFile);
            var label = $"{info.DisplaySummary}  ({saveFile.LastWriteTime:yyyy-MM-dd HH:mm})";
            _menuItems.Add((label, () =>
            {
                _saveName = saveFile.Name;
                _characterName = info.CharacterName;
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
            var info = GetSaveInfo(saveFile);
            var label = $"{info.DisplaySummary}  ({saveFile.LastWriteTime:yyyy-MM-dd HH:mm})";
            _menuItems.Add((label, () => ConfirmDeleteMenu(saveFile, info)));
        }

        _menuItems.Add(("Back", _inPauseMenu ? OpenPauseMenu : OpenMenu));
        RefreshMenuDisplay("DELETE SAVE", "Select a saved game to delete");
    }

    private void ConfirmDeleteMenu(System.IO.FileInfo file, SaveFileInfo info)
    {
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        _menuItems.Add(($"Yes, Delete '{info.DisplayName}' permanently", () =>
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

        RefreshMenuDisplay("CONFIRM DELETE", $"Are you sure you want to permanently delete '{info.DisplaySummary}'?");
    }

    private static string BackupDir()
    {
        var saveDir = SaveDir();
        var bDir = System.IO.Path.Combine(saveDir, "backups");
        if (!System.IO.Directory.Exists(bDir))
        {
            try { System.IO.Directory.CreateDirectory(bDir); } catch { }
        }
        return bDir;
    }

    private void OpenSaveManagerMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        if (saves.Length > 0)
        {
            _menuItems.Add(("Backup All Saves to Local Archive", () =>
            {
                var bDir = BackupDir();
                int count = 0;
                foreach (var s in saves)
                {
                    try
                    {
                        var dest = System.IO.Path.Combine(bDir, $"{System.IO.Path.GetFileNameWithoutExtension(s.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.sav");
                        System.IO.File.Copy(s.FullName, dest, true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        GD.PushWarning($"Save backup error: {ex.Message}");
                    }
                }
                AudioManager.Play(SoundEffect.MenuSelect);
                RefreshMenuDisplay("SAVES ARCHIVED", $"Successfully archived {count} save file(s) to:\n{bDir}");
            }));

            _menuItems.Add(("Export Save to Downloads Folder...", OpenExportMenu));
        }

        _menuItems.Add(("Import Save from Backup Archive...", OpenImportMenu));

        _menuItems.Add(("Upload Local Save to Cloud Realm...", OpenCloudUploadMenu));

        _menuItems.Add(("Download Saves from Cloud Realm", DownloadCloudSaves));

        _menuItems.Add(("Back to Main Menu", OpenMenu));

        RefreshMenuDisplay("SAVE GAME MANAGER", "Backup, export, and move save games between local & cloud");
    }

    private void OpenExportMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        foreach (var file in saves)
        {
            var saveFile = file;
            var info = GetSaveInfo(saveFile);
            _menuItems.Add(($"Export {info.DisplayName}", () =>
            {
                try
                {
                    string targetDir = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                        "Downloads"
                    );
                    if (!System.IO.Directory.Exists(targetDir))
                    {
                        targetDir = BackupDir();
                    }
                    var dest = System.IO.Path.Combine(targetDir, $"{saveFile.Name}.sav");
                    System.IO.File.Copy(saveFile.FullName, dest, true);
                    AudioManager.Play(SoundEffect.MenuSelect);
                    RefreshMenuDisplay("SAVE EXPORTED", $"Exported '{saveFile.Name}' successfully to:\n{dest}");
                }
                catch (Exception ex)
                {
                    RefreshMenuDisplay("EXPORT FAILED", ex.Message);
                }
            }));
        }

        _menuItems.Add(("Back to Save Manager", OpenSaveManagerMenu));
        RefreshMenuDisplay("EXPORT SAVE FILE", "Select a save file to export to your Downloads folder");
    }

    private void OpenImportMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var bDir = BackupDir();
        var di = new System.IO.DirectoryInfo(bDir);
        var files = di.Exists ? di.GetFiles("*.sav") : Array.Empty<System.IO.FileInfo>();
        Array.Sort(files, (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

        if (files.Length == 0)
        {
            _menuItems.Add(("No backup files found in backups folder", OpenSaveManagerMenu));
        }
        else
        {
            foreach (var file in files)
            {
                var bFile = file;
                _menuItems.Add(($"Restore {bFile.Name} ({bFile.LastWriteTime:yyyy-MM-dd HH:mm})", () =>
                {
                    try
                    {
                        var destDir = SaveDir();
                        var targetName = bFile.Name;
                        if (targetName.EndsWith(".sav"))
                        {
                            targetName = targetName[..^4];
                        }
                        var destPath = System.IO.Path.Combine(destDir, targetName);
                        System.IO.File.Copy(bFile.FullName, destPath, true);
                        AudioManager.Play(SoundEffect.MenuSelect);
                        RefreshMenuDisplay("SAVE RESTORED", $"Restored '{targetName}' to active saves.\nReady to load.");
                    }
                    catch (Exception ex)
                    {
                        RefreshMenuDisplay("RESTORE FAILED", ex.Message);
                    }
                }));
            }
        }

        _menuItems.Add(("Back to Save Manager", OpenSaveManagerMenu));
        RefreshMenuDisplay("IMPORT SAVE FILE", $"Select a backup file to restore into {SaveDir()}");
    }

    private void OpenCloudUploadMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inMenu = true;
        _menuIndex = 0;
        _menuItems.Clear();

        var saves = GetSaveFiles();
        if (saves.Length == 0)
        {
            _menuItems.Add(("No local saves found to upload", OpenSaveManagerMenu));
        }
        else
        {
            foreach (var file in saves)
            {
                var saveFile = file;
                var info = GetSaveInfo(saveFile);
                _menuItems.Add(($"Upload {info.DisplayName} to Cloud", () =>
                {
                    UploadSaveToCloud(info);
                }));
            }
        }

        _menuItems.Add(("Back to Save Manager", OpenSaveManagerMenu));
        RefreshMenuDisplay("UPLOAD TO CLOUD", "Select a local character to send to the cloud server");
    }

    private async void UploadSaveToCloud(SaveFileInfo save)
    {
        try
        {
            _overlay.Status = $"uploading {save.DisplayName} to cloud...";
            _overlay.QueueRedraw();
            using var client = new System.Net.Http.HttpClient();
            var httpBase = _cloudUrl.Replace("ws://", "http://").Replace("wss://", "https://");
            var uri = new Uri(httpBase);
            var uploadUrl = $"{uri.Scheme}://{uri.Authority}/api/saves/upload";

            var fileBytes = await System.IO.File.ReadAllBytesAsync(save.File.FullName);
            using var content = new System.Net.Http.ByteArrayContent(fileBytes);
            content.Headers.Add("X-Character-Name", save.File.Name);
            var response = await client.PostAsync(uploadUrl, content);
            if (response.IsSuccessStatusCode)
            {
                RefreshMenuDisplay("UPLOAD SUCCESSFUL", $"Save '{save.DisplayName}' uploaded to cloud realm.");
            }
            else
            {
                RefreshMenuDisplay("UPLOAD FAILED", $"Server returned HTTP {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            RefreshMenuDisplay("UPLOAD ERROR", ex.Message);
        }
    }

    private async void DownloadCloudSaves()
    {
        try
        {
            _overlay.Status = "fetching cloud saves...";
            _overlay.QueueRedraw();
            using var client = new System.Net.Http.HttpClient();
            var httpBase = _cloudUrl.Replace("ws://", "http://").Replace("wss://", "https://");
            var uri = new Uri(httpBase);
            var listUrl = $"{uri.Scheme}://{uri.Authority}/api/saves";

            var response = await client.GetStringAsync(listUrl);
            using var doc = JsonDocument.Parse(response);
            var savesArray = doc.RootElement.GetProperty("saves");
            int downloaded = 0;
            var saveDir = SaveDir();

            foreach (var s in savesArray.EnumerateArray())
            {
                var fname = s.GetProperty("filename").GetString();
                if (!string.IsNullOrEmpty(fname))
                {
                    var fileUrl = $"{uri.Scheme}://{uri.Authority}/api/saves/{Uri.EscapeDataString(fname)}";
                    var data = await client.GetByteArrayAsync(fileUrl);
                    var dest = System.IO.Path.Combine(saveDir, fname);
                    await System.IO.File.WriteAllBytesAsync(dest, data);
                    downloaded++;
                }
            }

            RefreshMenuDisplay("CLOUD SYNC COMPLETE", $"Downloaded {downloaded} save file(s) from cloud realm.");
        }
        catch (Exception ex)
        {
            RefreshMenuDisplay("CLOUD SYNC ERROR", ex.Message);
        }
    }

    private void OpenPauseMenu()
    {
        AudioManager.Play(SoundEffect.MenuOpen);
        _inMenu = true;
        _inPauseMenu = true;
        _inSplash = false;
        _inGuide = false;
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

        _menuItems.Add(("Backup Active Character Save (.sav)", () =>
        {
            var saves = GetSaveFiles();
            if (saves.Length > 0)
            {
                var bDir = BackupDir();
                var s = saves[0];
                var dest = System.IO.Path.Combine(bDir, $"{s.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.sav");
                try
                {
                    System.IO.File.Copy(s.FullName, dest, true);
                    AudioManager.Play(SoundEffect.MenuSelect);
                    RefreshMenuDisplay("BACKUP CREATED", $"Active save archived to:\n{dest}");
                }
                catch (Exception ex)
                {
                    RefreshMenuDisplay("BACKUP FAILED", ex.Message);
                }
            }
        }));

        _menuItems.Add(("Download Standalone Game Package (.zip)", () =>
        {
            AudioManager.Play(SoundEffect.MenuSelect);
            OS.ShellOpen(_downloadUrl);
        }));

        _menuItems.Add(("Game Guide & Primer", OpenGuide));
        _menuItems.Add(("Toggle Fullscreen / Windowed (F11)", () =>
        {
            ToggleFullscreen();
            var charDisplay = !string.IsNullOrEmpty(_characterName)
                ? _characterName
                : (!string.IsNullOrEmpty(_saveName) ? _saveName : "Adventurer");
            RefreshMenuDisplay("GAME MENU", $"Current Character: {charDisplay}");
        }));
        _menuItems.Add(("Angband Wiki (Open in Browser)", () =>
        {
            OS.ShellOpen("https://angband.readthedocs.io/");
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

        var charDisplay = !string.IsNullOrEmpty(_characterName)
            ? _characterName
            : (!string.IsNullOrEmpty(_saveName) ? _saveName : "Adventurer");
        RefreshMenuDisplay("GAME MENU", $"Current Character: {charDisplay}");
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

    private void OnDeathReload()
    {
        AudioManager.Play(SoundEffect.MenuSelect);
        _inDeath = false;
        if (_bridge.Connected)
        {
            _bridge.Stop();
        }

        var saves = GetSaveFiles();
        if (saves.Length > 0)
        {
            var topSave = GetSaveInfo(saves[0]);
            _saveName = saves[0].Name;
            _characterName = topSave.CharacterName;
        }
        StartGame();
    }

    private void OnDeathReroll()
    {
        AudioManager.Play(SoundEffect.MenuSelect);
        _inDeath = false;
        if (_bridge.Connected)
        {
            _bridge.Stop();
        }

        _saveName = null;
        _characterName = null;
        _randomRequested = true;
        StartGame();
    }

    private void OnDeathIdentify()
    {
        AudioManager.Play(SoundEffect.MenuSelect);
        _overlay.DeathItemsIdentified = true;
        if (_bridge.Connected && !_bridge.Busy)
        {
            _bridge.SendKey("u");
        }
        _overlay.QueueRedraw();
    }

    private void OnDeathMainMenu()
    {
        AudioManager.Play(SoundEffect.MenuSelect);
        _inDeath = false;
        if (_bridge.Connected)
        {
            _bridge.Stop();
        }
        OpenMenu();
    }

    private void OnBridgeDisconnected(string reason)
    {
        _overlay.Status = $"disconnected: {reason}";
        _overlay.QueueRedraw();
    }

    private void StartGame(bool newCharacter = false)
    {
        _overlay.Mode = ViewMode.Terminal;
        _overlay.Status = _cloudMode ? "connecting to cloud realm..." : "starting...";
        _overlay.QueueRedraw();

        if (_cloudMode)
        {
            if (newCharacter && string.IsNullOrEmpty(_saveName))
            {
                _saveName = "Adventurer_" + Guid.NewGuid().ToString("N")[..4];
            }
            _bridge = _cloudBridge;
            _cloudBridge.ConnectToServer(_cloudUrl, _saveName, newCharacter);
        }
        else
        {
            var exe = FindEngine();
            if (exe == null)
            {
                _overlay.Status = "angband.exe not found locally - switch to Cloud mode or build engine";
                _overlay.QueueRedraw();
                return;
            }
            _bridge = _localBridge;
            _localBridge.Start(exe, _saveName, newCharacter: false);
        }
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

        if (f.TryGetProperty("player", out var pl) && pl.ValueKind == JsonValueKind.Object)
        {
            if (pl.TryGetProperty("name", out var pName))
            {
                var charName = pName.GetString();
                if (!string.IsNullOrEmpty(charName))
                {
                    _characterName = charName;
                    if (string.IsNullOrEmpty(_saveName))
                    {
                        _saveName = charName;
                    }
                }
            }
        }

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
        if (frame.TryGetProperty("player", out var pl) && pl.ValueKind == JsonValueKind.Object
            && pl.TryGetProperty("dead", out var dProp) && dProp.GetBoolean())
        {
            if (!_inDeath)
            {
                _inDeath = true;
                _inMenu = false;
                _inPauseMenu = false;
                _inGuide = false;
                _inSplash = false;
                _overlay.DeathSection = 0;
                _overlay.DeathScroll = 0;
                _overlay.DeathItemsIdentified = false;
                AudioManager.Play(SoundEffect.PlayerDeath);
            }
            return ViewMode.Death;
        }

        _inDeath = false;
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

        if (_cloudMode && _cloudBridge != null && _cloudBridge.Connected)
        {
            _overlay.NetworkBadge = $"Cloud [{_cloudBridge.PingMs}ms]";
        }
        else if (_cloudMode)
        {
            _overlay.NetworkBadge = "Cloud [Connecting...]";
        }
        else
        {
            _overlay.NetworkBadge = null;
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

    public void ToggleFullscreen()
    {
        try
        {
            var currentMode = DisplayServer.WindowGetMode();
            if (currentMode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
            }
            else
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"client: ToggleFullscreen failed: {ex.Message}");
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || _bridge == null)
        {
            return;
        }

        // Global display toggle: F11 or Alt+Enter toggles Fullscreen / Maximized
        if (key.Keycode == Key.F11 || (key.Keycode == Key.Enter && key.AltPressed))
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_inSplash)
        {
            if (key.Keycode == Key.Escape)
            {
                GetTree().Quit();
            }
            else if (key.Keycode is Key.Key2 or Key.Kp2 or Key.G or Key.P)
            {
                OpenGuide();
            }
            else if (key.Keycode is Key.Key3 or Key.Kp3 or Key.W)
            {
                OS.ShellOpen("https://angband.readthedocs.io/");
            }
            else
            {
                OpenMenu();
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_inGuide)
        {
            if (key.Keycode is Key.Escape or Key.Enter or Key.KpEnter or Key.Space)
            {
                _inGuide = false;
                if (_inPauseMenu)
                {
                    OpenPauseMenu();
                }
                else
                {
                    OpenMenu();
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.W)
            {
                OS.ShellOpen("https://angband.readthedocs.io/");
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode is Key.Tab or Key.Right)
            {
                _overlay.GuideSection = (_overlay.GuideSection + 1) % 6;
                _overlay.GuideScroll = 0;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Left)
            {
                _overlay.GuideSection = (_overlay.GuideSection + 5) % 6;
                _overlay.GuideScroll = 0;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode is >= Key.Key1 and <= Key.Key6)
            {
                _overlay.GuideSection = (int)(key.Keycode - Key.Key1);
                _overlay.GuideScroll = 0;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode is >= Key.Kp1 and <= Key.Kp6)
            {
                _overlay.GuideSection = (int)(key.Keycode - Key.Kp1);
                _overlay.GuideScroll = 0;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Up)
            {
                _overlay.GuideScroll = Mathf.Max(0, _overlay.GuideScroll - 1);
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Down)
            {
                _overlay.GuideScroll++;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Pageup || key.PhysicalKeycode == Key.Pageup || key.KeyLabel == Key.Pageup)
            {
                _overlay.GuideScroll = Mathf.Max(0, _overlay.GuideScroll - 6);
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (key.Keycode == Key.Pagedown || key.PhysicalKeycode == Key.Pagedown || key.KeyLabel == Key.Pagedown)
            {
                _overlay.GuideScroll += 6;
                _overlay.QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            GetViewport().SetInputAsHandled();
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
                case Key.Up or Key.Kp8:
                    _menuIndex = Mathf.PosMod(_menuIndex - 1, _menuItems.Count);
                    AudioManager.Play(SoundEffect.MenuNav);
                    break;
                case Key.Down or Key.Kp2:
                    _menuIndex = Mathf.PosMod(_menuIndex + 1, _menuItems.Count);
                    AudioManager.Play(SoundEffect.MenuNav);
                    break;
                case Key.Home:
                    _menuIndex = 0;
                    AudioManager.Play(SoundEffect.MenuNav);
                    break;
                case Key.End:
                    _menuIndex = _menuItems.Count - 1;
                    AudioManager.Play(SoundEffect.MenuNav);
                    break;
                case Key.Enter or Key.KpEnter or Key.Space:
                    if (_menuIndex >= 0 && _menuIndex < _menuItems.Count)
                    {
                        AudioManager.Play(SoundEffect.MenuSelect);
                        _menuItems[_menuIndex].Execute();
                    }
                    return;
                case Key.Escape:
                    AudioManager.Play(SoundEffect.MenuNav);
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

        if (_inDeath)
        {
            switch (key.Keycode)
            {
                case Key.U:
                    OnDeathIdentify();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.R:
                    OnDeathReload();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.N:
                    OnDeathReroll();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.M or Key.Escape:
                    OnDeathMainMenu();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Tab or Key.Right:
                    AudioManager.Play(SoundEffect.MenuNav);
                    _overlay.DeathSection = (_overlay.DeathSection + 1) % 5;
                    _overlay.DeathScroll = 0;
                    _overlay.QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Left:
                    AudioManager.Play(SoundEffect.MenuNav);
                    _overlay.DeathSection = (_overlay.DeathSection + 4) % 5;
                    _overlay.DeathScroll = 0;
                    _overlay.QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                case >= Key.Key1 and <= Key.Key5:
                    AudioManager.Play(SoundEffect.MenuNav);
                    _overlay.DeathSection = (int)(key.Keycode - Key.Key1);
                    _overlay.DeathScroll = 0;
                    _overlay.QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                case >= Key.Kp1 and <= Key.Kp5:
                    AudioManager.Play(SoundEffect.MenuNav);
                    _overlay.DeathSection = (int)(key.Keycode - Key.Kp1);
                    _overlay.DeathScroll = 0;
                    _overlay.QueueRedraw();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Up:
                    if (_overlay.DeathSection == 0)
                    {
                        if (!_bridge.Busy) _bridge.SendKey("up");
                    }
                    else
                    {
                        _overlay.DeathScroll = Mathf.Max(0, _overlay.DeathScroll - 1);
                        _overlay.QueueRedraw();
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Down:
                    if (_overlay.DeathSection == 0)
                    {
                        if (!_bridge.Busy) _bridge.SendKey("down");
                    }
                    else
                    {
                        _overlay.DeathScroll++;
                        _overlay.QueueRedraw();
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Pageup or Key.Home:
                    if (_overlay.DeathSection == 0)
                    {
                        if (!_bridge.Busy) _bridge.SendKey("pageup");
                    }
                    else
                    {
                        _overlay.DeathScroll = Mathf.Max(0, _overlay.DeathScroll - 6);
                        _overlay.QueueRedraw();
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Pagedown or Key.End:
                    if (_overlay.DeathSection == 0)
                    {
                        if (!_bridge.Busy) _bridge.SendKey("pagedown");
                    }
                    else
                    {
                        _overlay.DeathScroll += 6;
                        _overlay.QueueRedraw();
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                default:
                    // In Tab 0 (Tombstone & Original Menus), pass keyboard events directly to Angband's death menu
                    if (_overlay.DeathSection == 0 && !_bridge.Busy)
                    {
                        var deathSpec = ToKeySpec(key);
                        if (deathSpec != null)
                        {
                            _bridge.SendKey(deathSpec);
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    }
                    break;
            }
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

        // Minimap controls: Screen size ([ / ]) and Grid scale zoom (PgUp / PgDn / +/-)
        // Must be checked BEFORE movement direction keys so PgUp/PgDn are never captured as keypad diagonal moves.
        if (inWorld && IsBracketLeft(key))
        {
            _overlay.ChangeMinimapScale(-0.15f);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inWorld && IsBracketRight(key))
        {
            _overlay.ChangeMinimapScale(0.15f);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inWorld && IsMinimapZoomIn(key))
        {
            if (key.CtrlPressed)
            {
                _overlay.ChangeMinimapScale(0.15f);
            }
            else
            {
                _overlay.ChangeMinimapZoom(0.15f);
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (inWorld && IsMinimapZoomOut(key))
        {
            if (key.CtrlPressed)
            {
                _overlay.ChangeMinimapScale(-0.15f);
            }
            else
            {
                _overlay.ChangeMinimapZoom(-0.15f);
            }
            GetViewport().SetInputAsHandled();
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
        // Explicitly guard against minimap and navigation keys being treated as directions
        if (key.Keycode is Key.Pageup or Key.Pagedown or Key.Home or Key.End or Key.Insert or Key.Delete
            || key.Keycode is Key.Bracketleft or Key.Bracketright or Key.Braceleft or Key.Braceright
            || key.Keycode is Key.Equal or Key.Plus or Key.Minus or Key.KpAdd or Key.KpSubtract)
        {
            return 0;
        }

        if (key.Keycode is >= Key.Kp1 and <= Key.Kp9)
        {
            return (int)(key.Keycode - Key.Kp1) + 1;
        }

        if (key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            return (int)(key.Keycode - Key.Key1) + 1;
        }

        if (key.PhysicalKeycode is >= Key.Kp1 and <= Key.Kp9 && !key.CtrlPressed && !key.AltPressed)
        {
            return (int)(key.PhysicalKeycode - Key.Kp1) + 1;
        }

        return 0;
    }

    private static bool IsBracketLeft(InputEventKey key)
    {
        return key.Keycode is Key.Bracketleft or Key.Braceleft
            || key.PhysicalKeycode is Key.Bracketleft or Key.Braceleft
            || key.KeyLabel is Key.Bracketleft or Key.Braceleft
            || key.Unicode is '[' or '{'
            || (char)key.Keycode is '[' or '{'
            || key.AsTextKeycode() is "[" or "{";
    }

    private static bool IsBracketRight(InputEventKey key)
    {
        return key.Keycode is Key.Bracketright or Key.Braceright
            || key.PhysicalKeycode is Key.Bracketright or Key.Braceright
            || key.KeyLabel is Key.Bracketright or Key.Braceright
            || key.Unicode is ']' or '}'
            || (char)key.Keycode is ']' or '}'
            || key.AsTextKeycode() is "]" or "}";
    }

    private static bool IsMinimapZoomIn(InputEventKey key)
    {
        return key.Keycode is Key.Pageup or Key.Equal or Key.Plus or Key.KpAdd
            || key.PhysicalKeycode is Key.Pageup
            || key.KeyLabel is Key.Pageup or Key.Equal or Key.Plus or Key.KpAdd
            || key.Unicode is '+' or '='
            || key.AsTextKeycode() is "PageUp" or "Page Up" or "PgUp" or "+" or "=";
    }

    private static bool IsMinimapZoomOut(InputEventKey key)
    {
        return key.Keycode is Key.Pagedown or Key.Minus or Key.KpSubtract
            || key.PhysicalKeycode is Key.Pagedown
            || key.KeyLabel is Key.Pagedown or Key.Minus or Key.KpSubtract
            || key.Unicode is '-' or '_'
            || key.AsTextKeycode() is "PageDown" or "Page Down" or "PgDn" or "-" or "_";
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
            case Key.Pageup: return "pageup";
            case Key.Pagedown: return "pagedown";
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
