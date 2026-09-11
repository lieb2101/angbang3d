using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// 2D layer over the 3D world: the classic map, the raw terminal, and the HUD.
/// </summary>
public partial class Overlay : Control
{
    private Font _font;
    private int _fontSize = 16;
    private Vector2 _cell = new(10, 18);
    private Texture2D _logoTexture;
    private bool _logoLoadAttempted;

    public JsonElement? Frame { get; set; }
    public ViewMode Mode { get; set; } = ViewMode.World;
    public string Status { get; set; } = "starting...";
    public double Waiting { get; set; }
    public string FacingName { get; set; } = "N";
    public string PromptLine { get; set; }
    public string StairsHint { get; set; }
    public string MenuTitle { get; set; } = "A N G B A N D 3 D";
    public string MenuSubtitle { get; set; } = "first person Angband 4.2.6";
    public string[] MenuItems { get; set; } = System.Array.Empty<string>();
    public int MenuIndex { get; set; }

    // Guide & Primer state
    public int GuideSection { get; set; } = 0;
    public int GuideScroll { get; set; } = 0;

    // Discovery Pulses & Fog Smoothing (Step 13)
    private struct DiscoveryPulse
    {
        public int X;
        public int Y;
        public double StartTime;
        public double Duration;
        public Color Color;
    }

    private readonly System.Collections.Generic.Dictionary<long, double> _knownFeatures = new();
    private readonly System.Collections.Generic.List<DiscoveryPulse> _activePulses = new();
    private int _lastKnownDepth = -1;

    // Minimap properties
    public float MinimapScale { get; set; } = 1.0f;
    public float MinimapZoom { get; set; } = 1.0f;
    private const float MinMinimapScale = 0.5f;
    private const float MaxMinimapScale = 3.0f;
    private const float MinMinimapZoom = 0.5f;
    private const float MaxMinimapZoom = 3.0f;
    private const float Margin = 12f;

    // Mouse Interaction Events
    public event System.Action<int> MenuItemClicked;
    public event System.Action<int> GuideTabClicked;
    public event System.Action GuideCloseRequested;
    public event System.Action SplashContinueRequested;
    public event System.Action SplashGuideRequested;
    public event System.Action SplashWikiRequested;
    public event System.Action SplashQuitRequested;

    // Interactive clickable bounding rects
    private readonly System.Collections.Generic.List<Rect2> _menuItemRects = new();
    private readonly System.Collections.Generic.List<Rect2> _guideTabRects = new();
    private Rect2 _splashBtnRect;
    private Rect2 _splashGuideRect;
    private Rect2 _splashWikiRect;
    private Rect2 _splashQuitRect;
    private Rect2 _guideCloseRect;
    private Rect2 _guideWikiRect;

    public void ChangeMinimapScale(float delta)
    {
        MinimapScale = Mathf.Clamp(MinimapScale + delta, MinMinimapScale, MaxMinimapScale);
        QueueRedraw();
    }

    public void ChangeMinimapZoom(float delta)
    {
        MinimapZoom = Mathf.Clamp(MinimapZoom + delta, MinMinimapZoom, MaxMinimapZoom);
        QueueRedraw();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _font = ThemeDB.FallbackFont;
        _cell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, _fontSize).X,
            _font.GetHeight(_fontSize) + 2);
        MouseFilter = MouseFilterEnum.Pass;
    }

    private Rect2 GetMinimapRect()
    {
        var top = _cell.Y + 12f;
        var baseW = Mathf.Clamp(View.X * 0.16f, 240f, 420f);
        var baseH = Mathf.Clamp(View.Y * 0.20f, 180f, 320f);
        var maxW = Mathf.Max(140f, View.X - Margin * 2);
        var maxH = Mathf.Max(100f, View.Y - top - (_cell.Y * 3 + 30f));
        var w = Mathf.Clamp(baseW * MinimapScale, 140f, maxW);
        var h = Mathf.Clamp(baseH * MinimapScale, 100f, maxH);
        var left = View.X - w - Margin;
        return new Rect2(left, top, w, h);
    }

    // A Control inside a CanvasLayer has no meaningful size of its own, so lay
    // out against the viewport instead.
    private Vector2 View => GetViewportRect().Size;

    public override void _Draw()
    {
        if (Mode == ViewMode.Splash)
        {
            DrawSplash();
            return;
        }

        if (Mode == ViewMode.Guide)
        {
            DrawGuide();
            return;
        }

        if (Mode == ViewMode.Menu)
        {
            DrawMenu();
            return;
        }

        if (Status != null)
        {
            DrawRect(new Rect2(Vector2.Zero, View), new Color(0.04f, 0.04f, 0.05f));
            DrawString(_font, new Vector2(20, 40), Status,
                HorizontalAlignment.Left, -1, _fontSize, Colors.Orange);
            return;
        }

        if (Frame is not { } frame)
        {
            return;
        }

        switch (Mode)
        {
            case ViewMode.Terminal:
                DrawRect(new Rect2(Vector2.Zero, View), new Color(0.02f, 0.02f, 0.03f));
                DrawTerminal(frame);
                break;
            case ViewMode.Map:
                DrawRect(new Rect2(Vector2.Zero, View), new Color(0.02f, 0.02f, 0.03f));
                DrawMap(frame);
                DrawHud(frame);
                break;
            default:
                DrawHud(frame);
                DrawMinimap(frame);
                break;
        }

        if (Waiting > 2.0)
        {
            DrawString(_font, new Vector2(4, View.Y - _cell.Y * 3),
                $"waiting for the engine ({Waiting:F0}s)...",
                HorizontalAlignment.Left, -1, _fontSize, Colors.Orange);
        }
    }

    private void EnsureLogoLoaded()
    {
        if (_logoLoadAttempted)
        {
            return;
        }
        _logoLoadAttempted = true;
        try
        {
            var resPath = "res://assets/thunderbear_logo.png";
            if (ResourceLoader.Exists(resPath))
            {
                _logoTexture = GD.Load<Texture2D>(resPath);
            }
            if (_logoTexture == null)
            {
                var globalPath = ProjectSettings.GlobalizePath(resPath);
                if (System.IO.File.Exists(globalPath))
                {
                    var img = Image.LoadFromFile(globalPath);
                    if (img != null)
                    {
                        _logoTexture = ImageTexture.CreateFromImage(img);
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.Print($"[Overlay] Logo load: {ex.Message}");
        }
    }

    private void DrawCenteredString(string text, float y, int fontSize, Color color)
    {
        var size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        var x = (View.X - size.X) / 2f;
        DrawString(_font, new Vector2(x, y), text, HorizontalAlignment.Left, -1, fontSize, color);
    }

    private void DrawSplash()
    {
        EnsureLogoLoaded();

        // Atmospheric dark fantasy background
        DrawRect(new Rect2(Vector2.Zero, View), new Color(0.02f, 0.025f, 0.038f));

        // Ornate framing border
        DrawRect(new Rect2(20, 20, View.X - 40, View.Y - 40), new Color(0.32f, 0.26f, 0.14f, 0.55f), false, 2f);
        DrawRect(new Rect2(24, 24, View.X - 48, View.Y - 48), new Color(0.18f, 0.14f, 0.08f, 0.4f), false, 1f);

        // Corner embellishments
        var cornerSize = 14f;
        DrawLine(new Vector2(20, 20 + cornerSize), new Vector2(20 + cornerSize, 20), new Color(0.9f, 0.75f, 0.35f, 0.8f), 2f);
        DrawLine(new Vector2(View.X - 20, 20 + cornerSize), new Vector2(View.X - 20 - cornerSize, 20), new Color(0.9f, 0.75f, 0.35f, 0.8f), 2f);
        DrawLine(new Vector2(20, View.Y - 20 - cornerSize), new Vector2(20 + cornerSize, View.Y - 20), new Color(0.9f, 0.75f, 0.35f, 0.8f), 2f);
        DrawLine(new Vector2(View.X - 20, View.Y - 20 - cornerSize), new Vector2(View.X - 20 - cornerSize, View.Y - 20), new Color(0.9f, 0.75f, 0.35f, 0.8f), 2f);

        var topY = Mathf.Max(38f, View.Y * 0.06f);

        // Thunderbear Studios Presentation Banner
        DrawCenteredString("★   T H U N D E R B E A R   S T U D I O S   ★", topY, 24, new Color(1.0f, 0.84f, 0.45f));
        DrawCenteredString("—  P R E S E N T S  —", topY + 28f, 13, new Color(0.65f, 0.72f, 0.85f));

        // Logo geometry & positioning - clean with NO background behind the logo
        var cx = View.X / 2f;
        var logoY = topY + 48f;
        var availableH = View.Y - logoY - 180f;
        var logoH = Mathf.Clamp(availableH, 140f, 260f);
        var logoW = logoH * (512f / 670f);
        var logoX = cx - logoW / 2f;

        if (_logoTexture != null)
        {
            DrawTextureRect(_logoTexture, new Rect2(logoX, logoY, logoW, logoH), false, Colors.White);
        }
        else
        {
            // Clean ASCII fallback art if texture is deferred
            DrawAsciiSplashLogo(cx, logoY + 10f);
        }

        // Game Title & Subtitle
        var titleY = logoY + logoH + 28f;
        DrawCenteredString("A N G B A N D   3 D", titleY, 32, new Color(1.0f, 0.92f, 0.62f));
        DrawCenteredString("The Classic Roguelike Dungeon Crawler • Reborn in First-Person 3D", titleY + 28f, 14, new Color(0.82f, 0.76f, 0.62f));
        DrawCenteredString("Angband 4.2.6 C Engine  •  Godot 4 Architecture  •  Procedural Dungeon Depths", titleY + 48f, 12, new Color(0.55f, 0.62f, 0.75f));

        // Primary Action Prompt Button
        var btnW = 420f;
        var btnH = 36f;
        var btnY = View.Y - 102f;
        _splashBtnRect = new Rect2(cx - btnW / 2f, btnY, btnW, btnH);
        DrawRect(_splashBtnRect, new Color(0.24f, 0.17f, 0.07f, 0.9f));
        DrawRect(_splashBtnRect, new Color(0.85f, 0.68f, 0.3f, 0.95f), false, 1.5f);
        DrawCenteredString("[ PRESS SPACE / ENTER TO ENTER MAIN MENU ]", btnY + 24f, 14, new Color(1.0f, 0.96f, 0.8f));

        // Quick Navigation Shortcuts (Interactive clickable buttons)
        var footerY = View.Y - 42f;
        var s1 = "[1] Main Menu";
        var s2 = "[2] Game Guide & Primer";
        var s3 = "[W] Angband Wiki";
        var s4 = "[Esc] Quit";

        var sz1 = _font.GetStringSize(s1, HorizontalAlignment.Left, -1, 13);
        var sz2 = _font.GetStringSize(s2, HorizontalAlignment.Left, -1, 13);
        var sz3 = _font.GetStringSize(s3, HorizontalAlignment.Left, -1, 13);
        var sz4 = _font.GetStringSize(s4, HorizontalAlignment.Left, -1, 13);

        var spacing = 28f;
        var totalW = sz1.X + sz2.X + sz3.X + sz4.X + spacing * 3;
        var startX = (View.X - totalW) / 2f;

        var x1 = startX;
        var x2 = x1 + sz1.X + spacing;
        var x3 = x2 + sz2.X + spacing;
        var x4 = x3 + sz3.X + spacing;

        _splashBtnRect = new Rect2(cx - btnW / 2f, btnY, btnW, btnH);
        _splashGuideRect = new Rect2(x2 - 4, footerY - 16, sz2.X + 8, 22);
        _splashWikiRect = new Rect2(x3 - 4, footerY - 16, sz3.X + 8, 22);
        _splashQuitRect = new Rect2(x4 - 4, footerY - 16, sz4.X + 8, 22);

        DrawString(_font, new Vector2(x1, footerY), s1, HorizontalAlignment.Left, -1, 13, new Color(0.85f, 0.78f, 0.55f));
        DrawString(_font, new Vector2(x2, footerY), s2, HorizontalAlignment.Left, -1, 13, new Color(0.62f, 0.78f, 1.0f));
        DrawString(_font, new Vector2(x3, footerY), s3, HorizontalAlignment.Left, -1, 13, new Color(0.55f, 0.88f, 0.72f));
        DrawString(_font, new Vector2(x4, footerY), s4, HorizontalAlignment.Left, -1, 13, new Color(0.85f, 0.55f, 0.55f));
    }

    private void DrawAsciiSplashLogo(float cx, float topY)
    {
        var ascii = new[]
        {
            "        )  (  )        ",
            "       (   ) (         ",
            "      /\\ /\\ /\\ /\\      ",
            "     |  |  |  |  |     ",
            "     |  |  |  |  |     ",
            "      \\( \\/ \\/ )/      ",
            "       (      )        ",
            "        \\____/         ",
            "        /\\  /\\         ",
            "       /  \\/  \\        ",
            "      (________)       ",
            "     __/__/\\__\\__      ",
            "    (____/  \\____)     "
        };
        for (var i = 0; i < ascii.Length; i++)
        {
            var str = ascii[i];
            var size = _font.GetStringSize(str, HorizontalAlignment.Left, -1, 14);
            DrawString(_font, new Vector2(cx - size.X / 2f, topY + i * 16), str,
                HorizontalAlignment.Left, -1, 14, new Color(1.0f, 0.75f, 0.35f));
        }
    }

    private void DrawGuide()
    {
        // Dark scroll background
        DrawRect(new Rect2(Vector2.Zero, View), new Color(0.025f, 0.03f, 0.042f));

        // Header Bar
        DrawRect(new Rect2(0, 0, View.X, 46), new Color(0.06f, 0.08f, 0.12f, 0.96f));
        DrawLine(new Vector2(0, 46), new Vector2(View.X, 46), new Color(0.38f, 0.45f, 0.6f), 1.5f);
        DrawCenteredString("ANGBAND 3D — ADVENTURER'S SURVIVAL GUIDE & PRIMER", 30, 18, new Color(1.0f, 0.86f, 0.45f));

        // Tab Bar
        var tabs = new[]
        {
            "1. Overview",
            "2. 3D Controls",
            "3. Commands",
            "4. Survival",
            "5. Stats & Magic",
            "6. Wiki & Links"
        };

        var tabMargin = 20f;
        var tabW = (View.X - tabMargin * 2) / tabs.Length;
        var tabH = 30f;
        var tabY = 54f;
        _guideTabRects.Clear();

        for (var i = 0; i < tabs.Length; i++)
        {
            var isSel = i == GuideSection;
            var tRect = new Rect2(tabMargin + i * tabW, tabY, tabW - 4, tabH);
            _guideTabRects.Add(tRect);
            DrawRect(tRect, isSel ? new Color(0.24f, 0.18f, 0.08f, 0.95f) : new Color(0.05f, 0.06f, 0.09f, 0.85f));
            DrawRect(tRect, isSel ? new Color(0.9f, 0.75f, 0.35f) : new Color(0.25f, 0.3f, 0.4f), false, isSel ? 1.5f : 1f);

            var labelSize = _font.GetStringSize(tabs[i], HorizontalAlignment.Left, -1, 13);
            DrawString(_font, new Vector2(tRect.Position.X + (tRect.Size.X - labelSize.X) / 2f, tRect.Position.Y + 20),
                tabs[i], HorizontalAlignment.Left, -1, 13,
                isSel ? new Color(1.0f, 0.92f, 0.6f) : new Color(0.6f, 0.65f, 0.75f));
        }

        // Main Content Panel
        var panelRect = new Rect2(tabMargin, 92f, View.X - tabMargin * 2, View.Y - 142f);
        DrawRect(panelRect, new Color(0.032f, 0.038f, 0.052f, 0.95f));
        DrawRect(panelRect, new Color(0.28f, 0.35f, 0.48f, 0.75f), false, 1.2f);

        var lines = GetGuideLines(GuideSection);
        var lineHeight = 21f;
        var startY = panelRect.Position.Y + 26f;
        var visibleLines = Mathf.FloorToInt((panelRect.Size.Y - 36f) / lineHeight);
        var maxScroll = Mathf.Max(0, lines.Length - visibleLines);
        GuideScroll = Mathf.Clamp(GuideScroll, 0, maxScroll);

        if (GuideScroll > 0)
        {
            DrawString(_font, new Vector2(panelRect.Position.X + 20, panelRect.Position.Y + 16),
                "▲ (more lines above - use Up Arrow / PgUp / Wheel)", HorizontalAlignment.Left, -1, 11, new Color(0.7f, 0.75f, 0.85f));
        }

        var endIdx = Mathf.Min(lines.Length, GuideScroll + visibleLines);
        for (var i = GuideScroll; i < endIdx; i++)
        {
            var line = lines[i];
            var y = startY + (i - GuideScroll) * lineHeight;
            var isHeader = line.StartsWith("[");
            var isBullet = line.StartsWith("•") || line.StartsWith("  -") || line.StartsWith("    -");
            var isUrl = line.Contains("http://") || line.Contains("https://");

            var color = isHeader ? new Color(1.0f, 0.82f, 0.42f)
                : isUrl ? new Color(0.45f, 0.85f, 1.0f)
                : isBullet ? new Color(0.85f, 0.88f, 0.95f)
                : new Color(0.7f, 0.74f, 0.82f);

            var fontSize = isHeader ? 14 : 13;
            DrawString(_font, new Vector2(panelRect.Position.X + 24, y), line,
                HorizontalAlignment.Left, -1, fontSize, color);
        }

        if (endIdx < lines.Length)
        {
            DrawString(_font, new Vector2(panelRect.Position.X + 20, panelRect.Position.Y + panelRect.Size.Y - 8),
                "▼ (more lines below - use Down Arrow / PgDn / Wheel)", HorizontalAlignment.Left, -1, 11, new Color(0.7f, 0.75f, 0.85f));
        }

        // Footer Bar
        var footerY = View.Y - 42f;
        DrawRect(new Rect2(0, footerY, View.X, 42), new Color(0.06f, 0.08f, 0.12f, 0.96f));
        DrawLine(new Vector2(0, footerY), new Vector2(View.X, footerY), new Color(0.38f, 0.45f, 0.6f), 1f);

        var hintMsg = "[1-6] Tabs   |   [Tab / Left / Right] Cycle Tabs   |   [Up/Down/Wheel] Scroll   |   [W] Open Angband Wiki";
        var hintSize = _font.GetStringSize(hintMsg, HorizontalAlignment.Left, -1, 13);
        _guideWikiRect = new Rect2(20, footerY + 6, hintSize.X + 10, 30);
        DrawString(_font, new Vector2(24, footerY + 26),
            hintMsg, HorizontalAlignment.Left, -1, 13, new Color(0.65f, 0.72f, 0.85f));

        var backMsg = "[Esc / Enter / Space] Return to Menu";
        var backSize = _font.GetStringSize(backMsg, HorizontalAlignment.Left, -1, 13);
        _guideCloseRect = new Rect2(View.X - backSize.X - 30, footerY + 6, backSize.X + 16, 30);
        DrawString(_font, new Vector2(View.X - backSize.X - 24, footerY + 26),
            backMsg, HorizontalAlignment.Left, -1, 13, new Color(1.0f, 0.88f, 0.55f));
    }

    private static string[] GetGuideLines(int section)
    {
        return section switch
        {
            0 => new[]
            {
                "[ OVERVIEW & OBJECTIVE ]",
                "• Welcome to ANGBAND 3D — first-person dungeon crawler powered by the authentic Angband 4.2.6 engine.",
                "• Your Quest: Descend 100 levels into the dark fortress of Angband to defeat Morgoth, Lord of Darkness (5000ft).",
                "• Turn-Based Engine: Time advances only when you act. Monsters move, attack, and cast when you take a turn.",
                "• Permadeath: Death is permanent! No save reloads upon falling in combat. Preparation and prudence are essential.",
                "",
                "[ THE TOWN (DEPTH 0) ]",
                "• The Town is your safe haven above the dungeon. Rest, store surplus equipment at Home, and visit shops:",
                "    - General Store: Torches, rations of food, flasks of oil, shovels, spikes",
                "    - Armoury: Body armours, shields, cloaks, boots, gauntlets, helms",
                "    - Weaponsmith: Swords, axes, polearms, bows, crossbows, missiles",
                "    - Temple & Magic Shop: Sacred prayers, arcane spellbooks, identify scrolls",
                "    - Alchemy Shop: Potions of cure critical wounds, speed, restore stats, healing"
            },
            1 => new[]
            {
                "[ 3D FIRST-PERSON CONTROLS ]",
                "• Step Forward / Backward : Up / Down Arrow   or   Numpad 8 / 2",
                "• Strafe Left / Right     : Numpad 4 / 6   or   Top-row 4 / 6",
                "• Diagonal Steps          : Numpad 7 / 9 / 1 / 3",
                "• Stay / Rest 1 Turn      : Numpad 5   or   Period (.)",
                "• Instant Camera Turning  : Left / Right Arrow (Turns camera 90° with 0 turn cost)",
                "",
                "[ PERSPECTIVE & MAP VIEWS ]",
                "• F11 / Alt-Enter : Toggle Fullscreen / Maximized window mode",
                "• Shift + M       : Toggle full 2D classic tactical map overlay",
                "• Tab             : Toggle raw ASCII terminal view (classic Angband display)",
                "• [ ]             : Resize Minimap window size larger / smaller on screen (or Ctrl + PgUp/Dn)",
                "• PgUp / PgDn    : Scale / Zoom Minimap grid view (or + / - / Mouse Wheel)",
                "",
                "[ GAME MANAGEMENT ]",
                "• Ctrl + S  : Quick Save game immediately",
                "• Ctrl + X  : Save game and exit to Main Menu",
                "• Escape    : Open In-Game Pause Menu / Primer / Save Management"
            },
            2 => new[]
            {
                "[ ESSENTIAL ANGBAND COMMANDS ]",
                "• i : Open Inventory (view carried items, inspect weight and encumbrance)",
                "• e : Open Equipment sheet (inspect wielded weapon, armor, rings, light source)",
                "• w : Wield / Wear an item, weapon, or armor piece",
                "• t : Take off / unequip a currently worn item",
                "• d : Drop an item onto the floor",
                "• k : Destroy / junk an unwanted item from pack",
                "• q : Quaff a potion (Cure Critical, Speed, Healing, Restore Mana)",
                "• r : Read a magical scroll (Phase Door, Teleport, Word of Recall, Identify)",
                "• a : Aim a wand in a chosen direction",
                "• z : Zap a rechargeable magic rod",
                "• u : Use a staff (Perception, Speed, Healing, Destruction)",
                "• m : Cast a spell (Mages/Rangers) or recite a prayer (Priests/Paladins)",
                "• o / c : Open / Close adjacent dungeon doors",
                "• s : Search adjacent walls and floor for secret doors and traps",
                "• > / < : Descend down / Ascend up staircases",
                "• * : Enter Look / Target mode to inspect monsters and cast ranged spells"
            },
            3 => new[]
            {
                "[ PERMADEATH & SURVIVAL TACTICS ]",
                "• NEVER EXPLORE WITHOUT ESCAPES:",
                "    - Always carry 10+ Scrolls of Phase Door for short-range emergency hops out of melee.",
                "    - Always carry 3+ Scrolls of Teleportation for escaping deadly monster packs or breath attacks.",
                "• LIGHT IS LIFE:",
                "    - Never wander in pitch black darkness. Torches burn fuel; carry spare oil flasks or lanterns.",
                "• SPEED RULES COMBAT:",
                "    - Monsters with (+10) speed get 2 actions for every 1 of yours. Quaff Potions of Speed in tough fights!",
                "• THE LIFESAVING WORD OF RECALL:",
                "    - A Scroll of Word of Recall safely teleports you from deep dungeon depths back to Town,",
                "      and later teleports you straight back down to your deepest explored level.",
                "• DISCRETION OVER VALOR:",
                "    - If your HP falls below 50%, do not trade blows. Phase door or teleport away to heal.",
                "• IDENTIFY YOUR GEAR:",
                "    - Use Scrolls of Identify or Staves of Perception before equipping unknown items to avoid curses."
            },
            4 => new[]
            {
                "[ CHARACTER ATTRIBUTES ]",
                "• STR (Strength)     : Governs melee weapon damage, blows per round, and inventory carry capacity.",
                "• INT (Intelligence) : Governs Mage and Ranger spell success rates, maximum mana, and spell tiers.",
                "• WIS (Wisdom)       : Governs Priest and Paladin prayer success rates, divine mana, and saving throws.",
                "• DEX (Dexterity)    : Governs Armor Class (AC), melee hit chance, stealth rating, and trap disarming.",
                "• CON (Constitution) : Dictates maximum Hit Points (HP) and recovery speed from poison/stuns.",
                "• CHR (Charisma)     : Lowers store prices and improves shopkeeper haggling in Town.",
                "",
                "[ STATUS EFFECTS & GAUGES ]",
                "• Blind / Confused / Poisoned / Paralyzed : Deadly debuffs! Cure immediately with potions/staffs.",
                "• Hunger : Carry rations of food or Lembas bread to prevent starving in deep dungeon depths.",
                "• Free Action & Resists : Collect items granting Free Action (paralysis immunity) and elemental resists."
            },
            5 => new[]
            {
                "[ OFFICIAL DOCUMENTATION & COMMUNITY LINKS ]",
                "• Official Angband Manual & Documentation:",
                "    https://angband.readthedocs.io/",
                "    (Comprehensive guides on monsters, artifacts, spells, dungeon mechanics, and commands)",
                "",
                "• Angband V-Wiki & Community Character Ladder:",
                "    http://angband.oook.cz/",
                "    (Spoilers, monster lore, item catalogs, character dumps, and active forums)",
                "",
                "• Angband Project Upstream Repository:",
                "    https://github.com/angband/angband",
                "",
                "[ ONE-KEY BROWSER LAUNCH ]",
                "• Press [ W ] at any time to open the Official Angband Wiki directly in your default browser!"
            },
            _ => System.Array.Empty<string>()
        };
    }

    private void DrawMenu()
    {
        DrawRect(new Rect2(Vector2.Zero, View), new Color(0.025f, 0.03f, 0.045f));

        var cx = View.X / 2f;
        var top = View.Y * 0.18f;

        // Card frame around menu
        var menuWidth = 640f;
        var menuLeft = cx - menuWidth / 2f;

        DrawString(_font, new Vector2(menuLeft, top), MenuTitle,
            HorizontalAlignment.Left, -1, 32, new Color(1.0f, 0.84f, 0.45f));
        DrawString(_font, new Vector2(menuLeft, top + 28), MenuSubtitle,
            HorizontalAlignment.Left, -1, 15, new Color(0.6f, 0.65f, 0.78f));

        DrawLine(new Vector2(menuLeft, top + 42), new Vector2(menuLeft + menuWidth, top + 42),
            new Color(0.32f, 0.38f, 0.52f, 0.8f), 1.5f);

        const int maxVisible = 10;
        var startIdx = 0;
        if (MenuItems.Length > maxVisible)
        {
            startIdx = Mathf.Clamp(MenuIndex - maxVisible / 2, 0, MenuItems.Length - maxVisible);
        }
        var endIdx = Mathf.Min(MenuItems.Length, startIdx + maxVisible);

        _menuItemRects.Clear();

        if (startIdx > 0)
        {
            DrawString(_font, new Vector2(menuLeft, top + 64),
                "▲ (more items above - scroll or use Up Arrow)", HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.68f, 0.8f));
        }

        var itemStartY = top + 80;
        for (var i = 0; i < MenuItems.Length; i++)
        {
            if (i >= startIdx && i < endIdx)
            {
                var visualIndex = i - startIdx;
                var y = itemStartY + visualIndex * 38;
                var selected = i == MenuIndex;
                var itemRect = new Rect2(menuLeft, y - 22, menuWidth, 32);
                _menuItemRects.Add(itemRect);

                if (selected)
                {
                    DrawRect(itemRect, new Color(0.24f, 0.18f, 0.08f, 0.95f));
                    DrawRect(itemRect, new Color(0.95f, 0.78f, 0.35f), false, 1.5f);
                }
                else
                {
                    DrawRect(itemRect, new Color(0.04f, 0.05f, 0.08f, 0.65f));
                    DrawRect(itemRect, new Color(0.18f, 0.22f, 0.32f, 0.5f), false, 1f);
                }

                var prefix = selected ? "► " : "  ";
                var numStr = $"[{i + 1}] ";
                var label = MenuItems[i];

                DrawString(_font, new Vector2(menuLeft + 12, y), prefix,
                    HorizontalAlignment.Left, -1, 16, selected ? new Color(1.0f, 0.85f, 0.35f) : Colors.Transparent);
                DrawString(_font, new Vector2(menuLeft + 36, y), numStr,
                    HorizontalAlignment.Left, -1, 15, selected ? new Color(1.0f, 0.92f, 0.65f) : new Color(0.65f, 0.72f, 0.85f));
                DrawString(_font, new Vector2(menuLeft + 72, y), label,
                    HorizontalAlignment.Left, -1, 16, selected ? new Color(1.0f, 0.96f, 0.85f) : new Color(0.82f, 0.85f, 0.92f));
            }
            else
            {
                _menuItemRects.Add(new Rect2(0, 0, 0, 0));
            }
        }

        var visibleCount = endIdx - startIdx;
        if (endIdx < MenuItems.Length)
        {
            DrawString(_font, new Vector2(menuLeft, itemStartY + visibleCount * 38 + 4),
                "▼ (more items below - scroll or use Down Arrow)", HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.68f, 0.8f));
        }

        var footerY = itemStartY + visibleCount * 38 + (endIdx < MenuItems.Length ? 24 : 12);
        DrawLine(new Vector2(menuLeft, footerY), new Vector2(menuLeft + menuWidth, footerY),
            new Color(0.32f, 0.38f, 0.52f, 0.6f), 1f);

        DrawString(_font, new Vector2(menuLeft, footerY + 22),
            "[↑/↓] Navigate   [1-9] Quick Select   [Enter/Space/Click] Choose   [Esc] Back",
            HorizontalAlignment.Left, -1, 13, new Color(0.6f, 0.68f, 0.82f));
    }

    #region Compass & Orientation Helpers

    private static Vector2 GetFacingVector(string facing) => facing switch
    {
        "E" => new Vector2(1, 0),
        "S" => new Vector2(0, 1),
        "W" => new Vector2(-1, 0),
        _ => new Vector2(0, -1) // "N"
    };

    private static float GetFacingAngle(string facing) => facing switch
    {
        "E" => Mathf.Pi / 2f,
        "S" => Mathf.Pi,
        "W" => -Mathf.Pi / 2f,
        _ => 0f // "N"
    };

    private static string GetFacingArrowChar(string facing) => facing switch
    {
        "E" => "▶",
        "S" => "▼",
        "W" => "◀",
        _ => "▲" // "N"
    };

    private void DrawDirectionalPointer(Vector2 center, float size, string facing, Color fillColor, Color outlineColor)
    {
        var dir = GetFacingVector(facing);
        var side = new Vector2(-dir.Y, dir.X);

        var tip = center + dir * size;
        var baseCenter = center - dir * (size * 0.55f);
        var leftCorner = baseCenter + side * (size * 0.65f);
        var rightCorner = baseCenter - side * (size * 0.65f);
        var notch = center - dir * (size * 0.20f);

        // Two-tone 3D arrow fill for depth
        DrawColoredPolygon(new[] { tip, leftCorner, notch }, fillColor);
        DrawColoredPolygon(new[] { tip, notch, rightCorner }, fillColor.Darkened(0.25f));

        // Perimeter outline
        DrawPolyline(new[] { leftCorner, tip, rightCorner, notch, leftCorner }, outlineColor, 1.2f);
    }

    private void UpdateDiscoveryPulses(JsonElement frame, double now)
    {
        if (frame.GetProperty("player").ValueKind != JsonValueKind.Object ||
            frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var player = frame.GetProperty("player");
        var depth = player.GetProperty("depth").GetInt32();

        // Level transition: reset discoveries
        if (_lastKnownDepth != depth)
        {
            _lastKnownDepth = depth;
            _knownFeatures.Clear();
            _activePulses.Clear();
        }

        // Clean expired pulses
        for (var i = _activePulses.Count - 1; i >= 0; i--)
        {
            if (now - _activePulses[i].StartTime >= _activePulses[i].Duration)
            {
                _activePulses.RemoveAt(i);
            }
        }

        var map = frame.GetProperty("map");
        var rows = map.GetProperty("rows");
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();

        for (var y = 0; y < h && y < rows.GetArrayLength(); y++)
        {
            var r = rows[y];
            var glyphs = r.GetProperty("g").GetString() ?? "";
            var flags = r.GetProperty("l").GetString() ?? "";

            for (var x = 0; x < w && x < glyphs.Length; x++)
            {
                var ch = glyphs[x];
                var flag = x < flags.Length ? AngbandColors.HexVal(flags[x]) : 0;
                var inView = (flag & 0x2) != 0;

                if (!inView)
                {
                    continue;
                }

                // Detect landmark features entering active view for the first time
                var isStairsUp = ch == '<';
                var isStairsDown = ch == '>';
                var isStore = ch >= '1' && ch <= '9';

                if (isStairsUp || isStairsDown || isStore)
                {
                    var key = ((long)y << 16) | (long)(x & 0xFFFF);
                    if (!_knownFeatures.ContainsKey(key))
                    {
                        _knownFeatures[key] = now;
                        var pulseColor = isStairsDown ? new Color(1.0f, 0.85f, 0.20f, 0.95f) :
                                         isStairsUp ? new Color(0.35f, 0.85f, 1.0f, 0.95f) :
                                         new Color(0.40f, 1.0f, 0.50f, 0.90f);

                        _activePulses.Add(new DiscoveryPulse
                        {
                            X = x,
                            Y = y,
                            StartTime = now,
                            Duration = 0.85,
                            Color = pulseColor
                        });
                    }
                }
            }
        }
    }

    private void DrawVisionCone(Vector2 playerCenter, float distance, string facing, Color coneColor, Color edgeColor)
    {
        var dir = GetFacingVector(facing);
        var side = new Vector2(-dir.Y, dir.X);
        var coneSpread = distance * 0.65f;

        var leftTip = playerCenter + dir * distance + side * coneSpread;
        var rightTip = playerCenter + dir * distance - side * coneSpread;

        // Translucent vision wedge
        DrawColoredPolygon(new[] { playerCenter, leftTip, rightTip }, coneColor);
        DrawLine(playerCenter, leftTip, edgeColor, 1.0f);
        DrawLine(playerCenter, rightTip, edgeColor, 1.0f);

        // Arc along front boundary
        var baseAngle = Mathf.Atan2(dir.Y, dir.X);
        DrawArc(playerCenter, distance, baseAngle - 0.58f, baseAngle + 0.58f, 12, edgeColor, 1.0f);
    }

    #endregion

    private void DrawMap(JsonElement frame)
    {
        if (frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            DrawTerminal(frame);
            return;
        }

        var map = frame.GetProperty("map");
        var rows = map.GetProperty("rows");
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();
        var player = frame.GetProperty("player");

        var px = player.GetProperty("x").GetInt32();
        var py = player.GetProperty("y").GetInt32();
        var depth = player.GetProperty("depth").GetInt32();
        var locStr = depth == 0 ? "Town" : $"{depth * 50}ft";
        var headingChar = GetFacingArrowChar(FacingName);

        var cols = Mathf.FloorToInt(View.X / _cell.X);
        var lines = Mathf.FloorToInt((View.Y - _cell.Y * 3) / _cell.Y);
        var ox = Mathf.Clamp(px - cols / 2, 0, Mathf.Max(0, w - cols));
        var oy = Mathf.Clamp(py - lines / 2, 0, Mathf.Max(0, h - lines));
        var startX = Mathf.Max(0f, (View.X - cols * _cell.X) / 2f);
        var top = _cell.Y * 2;

        // Top tactical header bar
        DrawRect(new Rect2(0, 0, View.X, top - 2), new Color(0.03f, 0.05f, 0.09f, 0.95f));
        DrawLine(new Vector2(0, top - 2), new Vector2(View.X, top - 2), new Color(0.35f, 0.45f, 0.62f, 0.85f), 1.2f);
        var titleText = $"TACTICAL MAP  ({px},{py}) {locStr}   Facing: {FacingName} {headingChar}   [M / Esc] Close Map";
        DrawString(_font, new Vector2(12, _font.GetAscent(_fontSize) + 2), titleText,
            HorizontalAlignment.Left, -1, _fontSize, new Color(1.0f, 0.88f, 0.48f));

        // Draw vision cone if player is in visible window
        if (px >= ox && px < ox + cols && py >= oy && py < oy + lines)
        {
            var playerPos = new Vector2(startX + (px - ox) * _cell.X + _cell.X / 2f, top + (py - oy) * _cell.Y + _cell.Y / 2f);
            var coneDist = _cell.Y * 4.0f;
            DrawVisionCone(playerPos, coneDist, FacingName, new Color(1.0f, 0.88f, 0.35f, 0.16f), new Color(1.0f, 0.90f, 0.50f, 0.40f));
        }

        var now = Time.GetTicksMsec() / 1000.0;
        UpdateDiscoveryPulses(frame, now);

        for (var row = 0; row < lines && oy + row < h; row++)
        {
            var r = rows[oy + row];
            var glyphs = r.GetProperty("g").GetString() ?? "";
            var attrs = r.GetProperty("a").GetString() ?? "";
            var flags = r.GetProperty("l").GetString() ?? "";

            for (var c = 0; c < cols && ox + c < w; c++)
            {
                var cell = ox + c;
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
                if ((flag & 0x3) == 0)
                {
                    continue;
                }

                var inView = (flag & 0x2) != 0;
                var cellRect = new Rect2(startX + c * _cell.X, top + row * _cell.Y, _cell.X, _cell.Y);

                // FoW Tile Underlay Smoothing
                if (inView)
                {
                    var isFloor = ch == '.' || ch == '+' || ch == '\'';
                    var underColor = isFloor ? new Color(0.14f, 0.16f, 0.22f, 0.45f) : new Color(0.10f, 0.12f, 0.17f, 0.35f);
                    DrawRect(cellRect, underColor);
                }
                else
                {
                    DrawRect(cellRect, new Color(0.03f, 0.04f, 0.06f, 0.65f));
                }

                var colour = AngbandColors.Get(AngbandColors.ParseAttr(attrs, cell));
                if (!inView)
                {
                    colour = colour.Darkened(0.55f);
                    colour.A = 0.65f;
                }

                var isPlayer = (cell == px && oy + row == py);
                if (isPlayer)
                {
                    var playerPos = new Vector2(startX + c * _cell.X + _cell.X / 2f, top + row * _cell.Y + _cell.Y / 2f);
                    DrawCircle(playerPos, _cell.Y * 0.60f, new Color(1.0f, 0.85f, 0.25f, 0.35f));
                    DrawDirectionalPointer(playerPos, _cell.Y * 0.65f, FacingName,
                        new Color(1.0f, 0.95f, 0.40f), new Color(0.15f, 0.10f, 0.02f, 0.95f));
                }
                else
                {
                    DrawString(_font, new Vector2(startX + c * _cell.X, top + row * _cell.Y + _font.GetAscent(_fontSize)),
                        ch.ToString(), HorizontalAlignment.Left, -1, _fontSize, colour);
                }
            }
        }

        // Render animated discovery pulses on tactical map
        for (var i = 0; i < _activePulses.Count; i++)
        {
            var p = _activePulses[i];
            if (p.X >= ox && p.X < ox + cols && p.Y >= oy && p.Y < oy + lines)
            {
                var pulseT = (float)((now - p.StartTime) / p.Duration);
                if (pulseT >= 0f && pulseT <= 1f)
                {
                    var centerPos = new Vector2(
                        startX + (p.X - ox) * _cell.X + _cell.X / 2f,
                        top + (p.Y - oy) * _cell.Y + _cell.Y / 2f);
                    var radius = Mathf.Lerp(_cell.Y * 0.4f, _cell.Y * 2.2f, pulseT);
                    var alpha = (1f - pulseT) * p.Color.A;
                    var c = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);
                    DrawArc(centerPos, radius, 0, Mathf.Tau, 24, c, 1.8f);
                    DrawCircle(centerPos, radius * 0.35f, new Color(c.R, c.G, c.B, alpha * 0.30f));
                }
            }
        }
    }

    private void DrawTerminal(JsonElement frame)
    {
        if (frame.GetProperty("term").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var term = frame.GetProperty("term");
        var rows = term.GetProperty("rows");
        var rowCount = rows.GetArrayLength();
        var w = term.GetProperty("w").GetInt32();
        if (w <= 0 || rowCount <= 0) return;

        // Auto-scale terminal font to fill available screen area while maintaining aspect
        var targetCellW = View.X / (w + 2f);
        var targetCellH = View.Y / (rowCount + 2f);
        var termFontSize = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(targetCellW * 1.55f, targetCellH * 0.85f)), 14, 28);

        var termCharW = _font.GetStringSize("#", HorizontalAlignment.Left, -1, termFontSize).X;
        var termCharH = _font.GetHeight(termFontSize) + 2f;
        var termAscent = _font.GetAscent(termFontSize);

        var totalW = w * termCharW;
        var totalH = rowCount * termCharH;
        var startX = Mathf.Max(0f, (View.X - totalW) / 2f);
        var startY = Mathf.Max(0f, (View.Y - totalH) / 2f);

        for (var y = 0; y < rowCount; y++)
        {
            var r = rows[y];
            var glyphs = r.GetProperty("g").GetString() ?? "";
            var attrs = r.GetProperty("a").GetString() ?? "";
            for (var x = 0; x < w && x < glyphs.Length; x++)
            {
                if (glyphs[x] == ' ')
                {
                    continue;
                }
                DrawString(_font, new Vector2(startX + x * termCharW, startY + y * termCharH + termAscent),
                    glyphs[x].ToString(), HorizontalAlignment.Left, -1, termFontSize,
                    AngbandColors.Get(AngbandColors.ParseAttr(attrs, x)));
            }
        }
    }

    private void DrawMinimap(JsonElement frame)
    {
        if (frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var map = frame.GetProperty("map");
        var rows = map.GetProperty("rows");
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();
        var player = frame.GetProperty("player");

        var px = player.GetProperty("x").GetInt32();
        var py = player.GetProperty("y").GetInt32();

        var mapRect = GetMinimapRect();

        // Header bar layout
        var headerH = 22f;
        var headerRect = new Rect2(mapRect.Position.X, mapRect.Position.Y, mapRect.Size.X, headerH);
        var contentRect = new Rect2(mapRect.Position.X + 2, mapRect.Position.Y + headerH, mapRect.Size.X - 4, mapRect.Size.Y - headerH - 2);

        // Semi-transparent background panel with shadow/border
        DrawRect(mapRect, new Color(0.02f, 0.03f, 0.04f, 0.90f));
        DrawRect(headerRect, new Color(0.06f, 0.08f, 0.13f, 0.96f));
        DrawRect(mapRect, new Color(0.28f, 0.38f, 0.52f, 0.85f), false, 1.5f);
        DrawLine(
            new Vector2(headerRect.Position.X, headerRect.Position.Y + headerH),
            new Vector2(headerRect.Position.X + headerRect.Size.X, headerRect.Position.Y + headerH),
            new Color(0.35f, 0.45f, 0.6f, 0.85f), 1.2f);

        // Header Instructions: Right-aligned responsive labels for screen window size ([ ]) and grid scaling (PgUp/Dn)
        var hintFont = 9;
        var totalW = headerRect.Size.X;
        var hintStr = totalW switch
        {
            >= 220 => "[ ] Size   PgUp/Dn Scale",
            >= 180 => "[ ] Size  PgUp/Dn Scale",
            >= 145 => "[ ] Size  PgUp/Dn",
            _ => "[ ]  PgUp/Dn"
        };

        var hintSize = _font.GetStringSize(hintStr, HorizontalAlignment.Left, -1, hintFont);
        var hintX = headerRect.Position.X + headerRect.Size.X - hintSize.X - 6;
        DrawString(_font, new Vector2(hintX, headerRect.Position.Y + _font.GetAscent(hintFont) + 4),
            hintStr, HorizontalAlignment.Left, -1, hintFont, new Color(0.65f, 0.78f, 0.95f));

        // Header Title & Orientation Heading: Left-aligned with automatic compacting based on available width
        var titleFont = 10;
        var maxLeftW = hintX - headerRect.Position.X - 8;
        var depth = player.GetProperty("depth").GetInt32();
        var locStr = depth == 0 ? "Town" : $"{depth * 50}ft";
        var arrowChar = GetFacingArrowChar(FacingName);
        var fullTitle = $"MINIMAP ({px},{py}) {locStr}  [{arrowChar} {FacingName}]";
        var fullTitleSize = _font.GetStringSize(fullTitle, HorizontalAlignment.Left, -1, titleFont);

        if (maxLeftW >= fullTitleSize.X)
        {
            DrawString(_font, new Vector2(headerRect.Position.X + 6, headerRect.Position.Y + _font.GetAscent(titleFont) + 3),
                fullTitle, HorizontalAlignment.Left, -1, titleFont, new Color(1.0f, 0.84f, 0.45f));
        }
        else
        {
            var medTitle = $"MAP ({px},{py}) [{arrowChar}{FacingName}]";
            var medSize = _font.GetStringSize(medTitle, HorizontalAlignment.Left, -1, titleFont);
            if (maxLeftW >= medSize.X)
            {
                DrawString(_font, new Vector2(headerRect.Position.X + 6, headerRect.Position.Y + _font.GetAscent(titleFont) + 3),
                    medTitle, HorizontalAlignment.Left, -1, titleFont, new Color(1.0f, 0.84f, 0.45f));
            }
            else
            {
                var shortTitle = $"[{arrowChar}{FacingName}]";
                var shortSize = _font.GetStringSize(shortTitle, HorizontalAlignment.Left, -1, titleFont);
                if (maxLeftW >= shortSize.X)
                {
                    DrawString(_font, new Vector2(headerRect.Position.X + 6, headerRect.Position.Y + _font.GetAscent(titleFont) + 3),
                        shortTitle, HorizontalAlignment.Left, -1, titleFont, new Color(1.0f, 0.84f, 0.45f));
                }
            }
        }

        // Dynamic font size scaling with MinimapZoom
        var miniFontSize = Mathf.Clamp(Mathf.RoundToInt((contentRect.Size.Y / 16f) * MinimapZoom), 6, 28);
        var miniCell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, miniFontSize).X,
            _font.GetHeight(miniFontSize));

        var now = Time.GetTicksMsec() / 1000.0;
        UpdateDiscoveryPulses(frame, now);

        if (miniCell.X > 0 && miniCell.Y > 0)
        {
            var cols = Mathf.FloorToInt(contentRect.Size.X / miniCell.X);
            var lines = Mathf.FloorToInt(contentRect.Size.Y / miniCell.Y);
            var ox = Mathf.Clamp(px - cols / 2, 0, Mathf.Max(0, w - cols));
            var oy = Mathf.Clamp(py - lines / 2, 0, Mathf.Max(0, h - lines));

            // Draw player directional vision cone on minimap
            if (px >= ox && px < ox + cols && py >= oy && py < oy + lines)
            {
                var playerMiniPos = new Vector2(
                    contentRect.Position.X + (px - ox) * miniCell.X + miniCell.X / 2f,
                    contentRect.Position.Y + (py - oy) * miniCell.Y + miniCell.Y / 2f);
                var coneDist = Mathf.Clamp(miniCell.Y * 3.2f, 16f, 65f);
                DrawVisionCone(playerMiniPos, coneDist, FacingName,
                    new Color(1.0f, 0.85f, 0.25f, 0.16f),
                    new Color(1.0f, 0.88f, 0.45f, 0.38f));
            }

            for (var row = 0; row < lines && oy + row < h; row++)
            {
                var r = rows[oy + row];
                var glyphs = r.GetProperty("g").GetString() ?? "";
                var attrs = r.GetProperty("a").GetString() ?? "";
                var flags = r.GetProperty("l").GetString() ?? "";

                for (var c = 0; c < cols && ox + c < w; c++)
                {
                    var cell = ox + c;
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
                    if ((flag & 0x3) == 0)
                    {
                        continue;
                    }

                    var inView = (flag & 0x2) != 0;
                    var cellPos = new Vector2(contentRect.Position.X + c * miniCell.X, contentRect.Position.Y + row * miniCell.Y);
                    var cellRect = new Rect2(cellPos, miniCell);

                    if (cellPos.Y + miniCell.Y <= mapRect.Position.Y + mapRect.Size.Y)
                    {
                        // FoW Underlay Smoothing
                        if (inView)
                        {
                            var isFloor = ch == '.' || ch == '+' || ch == '\'';
                            var underColor = isFloor ? new Color(0.14f, 0.16f, 0.22f, 0.45f) : new Color(0.10f, 0.12f, 0.17f, 0.35f);
                            DrawRect(cellRect, underColor);
                        }
                        else
                        {
                            DrawRect(cellRect, new Color(0.03f, 0.04f, 0.06f, 0.65f));
                        }
                    }

                    var colour = AngbandColors.Get(AngbandColors.ParseAttr(attrs, cell));
                    if (!inView)
                    {
                        colour = colour.Darkened(0.55f);
                        colour.A = 0.65f;
                    }

                    var isPlayer = (cell == px && oy + row == py);
                    var drawPos = new Vector2(
                        contentRect.Position.X + c * miniCell.X,
                        contentRect.Position.Y + row * miniCell.Y + _font.GetAscent(miniFontSize));

                    if (drawPos.Y <= mapRect.Position.Y + mapRect.Size.Y)
                    {
                        if (isPlayer)
                        {
                            var centerPos = new Vector2(
                                contentRect.Position.X + c * miniCell.X + miniCell.X / 2f,
                                contentRect.Position.Y + row * miniCell.Y + miniCell.Y / 2f);
                            DrawCircle(centerPos, Mathf.Max(miniCell.Y * 0.55f, 4.5f), new Color(1.0f, 0.85f, 0.2f, 0.30f));
                            DrawDirectionalPointer(centerPos, Mathf.Max(miniCell.Y * 0.60f, 5.5f), FacingName,
                                new Color(1.0f, 0.95f, 0.40f), new Color(0.15f, 0.10f, 0.02f, 0.95f));
                        }
                        else
                        {
                            DrawString(_font, drawPos, ch.ToString(), HorizontalAlignment.Left, -1, miniFontSize, colour);
                        }
                    }
                }
            }

            // Render animated discovery pulses within minimap bounds
            for (var i = 0; i < _activePulses.Count; i++)
            {
                var p = _activePulses[i];
                if (p.X >= ox && p.X < ox + cols && p.Y >= oy && p.Y < oy + lines)
                {
                    var pulseT = (float)((now - p.StartTime) / p.Duration);
                    if (pulseT >= 0f && pulseT <= 1f)
                    {
                        var centerPos = new Vector2(
                            contentRect.Position.X + (p.X - ox) * miniCell.X + miniCell.X / 2f,
                            contentRect.Position.Y + (p.Y - oy) * miniCell.Y + miniCell.Y / 2f);

                        if (centerPos.Y >= contentRect.Position.Y && centerPos.Y <= mapRect.Position.Y + mapRect.Size.Y)
                        {
                            var radius = Mathf.Lerp(miniCell.Y * 0.4f, miniCell.Y * 2.0f, pulseT);
                            var alpha = (1f - pulseT) * p.Color.A;
                            var c = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);
                            DrawArc(centerPos, radius, 0, Mathf.Tau, 20, c, 1.5f);
                            DrawCircle(centerPos, radius * 0.35f, new Color(c.R, c.G, c.B, alpha * 0.25f));
                        }
                    }
                }
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

        var msgs = frame.GetProperty("messages");
        var msg = msgs.GetArrayLength() > 0
            ? msgs[msgs.GetArrayLength() - 1].GetProperty("text").GetString()
            : "";

        // A pending prompt replaces the message line and is highlighted, so a
        // -more- during a fight is obvious without leaving the world view.
        var prompt = PromptLine;
        var text = prompt ?? msg;
        var bg = prompt != null ? new Color(0.20f, 0.13f, 0.02f, 0.92f) : new Color(0, 0, 0, 0.65f);
        var fg = prompt != null ? new Color(1.0f, 0.88f, 0.55f) : Colors.White;

        DrawRect(new Rect2(0, 0, View.X, _cell.Y + 6), bg);
        DrawString(_font, new Vector2(6, _font.GetAscent(_fontSize) + 3), text,
            HorizontalAlignment.Left, -1, _fontSize, fg);

        var wizard = p.GetProperty("wizard").GetBoolean() ? " [WIZARD]" : "";
        var depth = p.GetProperty("depth").GetInt32();
        var place = depth == 0 ? "Town" : $"Depth {depth} ({depth * 50}ft)";

        var hp = p.GetProperty("hp").GetInt32();
        var hpMax = p.GetProperty("hp_max").GetInt32();
        var hpRatio = hpMax > 0 ? (float)hp / hpMax : 1f;

        var hasSp = p.TryGetProperty("sp_max", out var spmProp) && spmProp.GetInt32() > 0;
        var sp = p.TryGetProperty("sp", out var spProp) ? spProp.GetInt32() : 0;
        var spMax = hasSp ? spmProp.GetInt32() : 0;

        var ac = p.TryGetProperty("ac", out var acProp) ? acProp.GetInt32() : 0;
        var acToA = p.TryGetProperty("ac_to_a", out var actProp) ? actProp.GetInt32() : 0;
        var acStr = acToA != 0 ? $"AC {ac}(+{acToA})" : $"AC {ac}";

        var gold = p.GetProperty("gold").GetInt32();
        var exp = p.TryGetProperty("exp", out var expProp) ? expProp.GetInt32() : 0;
        var expNext = p.TryGetProperty("exp_next", out var expnProp) ? expnProp.GetInt32() : 0;
        var level = p.GetProperty("level").GetInt32();
        var maxLev = p.TryGetProperty("max_lev", out var mlProp) ? mlProp.GetInt32() : level;
        var levStr = maxLev > level ? $"L{level}({maxLev})" : $"L{level}";

        var speed = p.TryGetProperty("speed", out var spdProp) ? spdProp.GetInt32() : 0;
        var spdStr = speed > 0 ? $"+{speed}" : (speed < 0 ? $"{speed}" : "Norm");

        var lightRadius = p.GetProperty("light").GetInt32();
        var hasLightItem = p.TryGetProperty("light_item", out var liProp) && !string.IsNullOrEmpty(liProp.GetString());
        var lightItemName = hasLightItem ? liProp.GetString() : "None";
        var lightFuel = p.TryGetProperty("light_fuel", out var lfProp) ? lfProp.GetInt32() : 0;
        var lightStr = depth == 0
            ? (hasLightItem ? $"{lightItemName} (day)" : "None")
            : (hasLightItem ? $"{lightItemName} (R:{lightRadius}{(lightFuel > 0 ? $", {lightFuel}t" : "")})" : (lightRadius > 0 ? $"Light {lightRadius}" : "None"));

        // Height of the detailed 3-row footer
        var barH = _cell.Y * 3 + 12f;
        DrawRect(new Rect2(0, View.Y - barH, View.X, barH), new Color(0.02f, 0.03f, 0.05f, 0.88f));
        DrawLine(new Vector2(0, View.Y - barH), new Vector2(View.X, View.Y - barH), new Color(0.28f, 0.35f, 0.48f, 0.85f), 1.2f);

        // Row 1: Keybindings & contextual stairs hint
        var row1Y = View.Y - barH + _font.GetAscent(_fontSize) + 2;
        var hintPrefix = StairsHint != null ? StairsHint + "   |   " : "";
        var keyHints = "arrows/numpad: move/turn   Shift-M: map   Tab: term   F11: fullscreen   [ ]: size   PgUp/Dn: scale   i: inv   e: equip   m: cast   q: quaff   r: read   ^S: save   Esc: menu";
        if (StairsHint != null)
        {
            DrawString(_font, new Vector2(6, row1Y), hintPrefix, HorizontalAlignment.Left, -1, _fontSize, new Color(1.0f, 0.88f, 0.45f));
            var prefixSize = _font.GetStringSize(hintPrefix, HorizontalAlignment.Left, -1, _fontSize);
            DrawString(_font, new Vector2(6 + prefixSize.X, row1Y), keyHints, HorizontalAlignment.Left, -1, _fontSize, new Color(0.48f, 0.52f, 0.62f));
        }
        else
        {
            DrawString(_font, new Vector2(6, row1Y), keyHints, HorizontalAlignment.Left, -1, _fontSize, new Color(0.48f, 0.52f, 0.62f));
        }

        // Row 2: Comprehensive dynamic character statistics
        var row2Y = View.Y - barH + _cell.Y + _font.GetAscent(_fontSize) + 4;
        var name = p.TryGetProperty("name", out var nProp) && !string.IsNullOrEmpty(nProp.GetString()) ? nProp.GetString() : "Hero";
        var charStr = $"{name} the {p.GetProperty("race").GetString()} {p.GetProperty("class").GetString()}  {levStr}  ";
        DrawString(_font, new Vector2(6, row2Y), charStr, HorizontalAlignment.Left, -1, _fontSize, new Color(1.0f, 0.92f, 0.65f));
        var curX = 6 + _font.GetStringSize(charStr, HorizontalAlignment.Left, -1, _fontSize).X;

        // HP with color alert
        var hpColor = hpRatio > 0.6f ? new Color(0.45f, 0.95f, 0.45f) : (hpRatio > 0.25f ? new Color(1.0f, 0.85f, 0.2f) : new Color(1.0f, 0.3f, 0.3f));
        var hpStr = $"HP {hp}/{hpMax}  ";
        DrawString(_font, new Vector2(curX, row2Y), hpStr, HorizontalAlignment.Left, -1, _fontSize, hpColor);
        curX += _font.GetStringSize(hpStr, HorizontalAlignment.Left, -1, _fontSize).X;

        // SP if available
        if (hasSp)
        {
            var spStr = $"SP {sp}/{spMax}  ";
            DrawString(_font, new Vector2(curX, row2Y), spStr, HorizontalAlignment.Left, -1, _fontSize, new Color(0.4f, 0.82f, 1.0f));
            curX += _font.GetStringSize(spStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // AC, Gold, EXP, Place, Speed, Light
        var statTail = $"{acStr}  AU {gold}  EXP {exp}{(expNext > 0 && exp < expNext ? $"/{expNext}" : "")}  {place}  Spd {spdStr}  Light: {lightStr}  Facing {FacingName}{wizard}";
        DrawString(_font, new Vector2(curX, row2Y), statTail, HorizontalAlignment.Left, -1, _fontSize, new Color(0.75f, 0.84f, 0.95f));

        // Row 3: Target tracking, status condition badges, and secondary attributes
        var row3Y = View.Y - barH + _cell.Y * 2 + _font.GetAscent(_fontSize) + 6;
        var curBadgeX = 6f;

        // Target display
        if (p.TryGetProperty("target", out var targetProp) && targetProp.ValueKind == JsonValueKind.Object
            && targetProp.TryGetProperty("name", out var tNameProp) && !string.IsNullOrEmpty(tNameProp.GetString()))
        {
            var tName = tNameProp.GetString();
            var tPct = targetProp.TryGetProperty("pct", out var tpProp) ? tpProp.GetInt32() : 100;
            var targetStr = $"[ TARGET: {tName} ({tPct}% HP) ]  ";
            DrawString(_font, new Vector2(curBadgeX, row3Y), targetStr, HorizontalAlignment.Left, -1, _fontSize, new Color(1.0f, 0.55f, 0.2f));
            curBadgeX += _font.GetStringSize(targetStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // Active Status Badges
        if (p.TryGetProperty("statuses", out var stProp) && stProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var st in stProp.EnumerateArray())
            {
                if (st.TryGetProperty("name", out var stName))
                {
                    var nameStr = stName.GetString();
                    var attr = st.TryGetProperty("attr", out var aProp) ? aProp.GetInt32() : 1;
                    var badgeStr = $"[{nameStr}] ";
                    var badgeColor = AngbandColors.Get(attr);
                    DrawString(_font, new Vector2(curBadgeX, row3Y), badgeStr, HorizontalAlignment.Left, -1, _fontSize, badgeColor);
                    curBadgeX += _font.GetStringSize(badgeStr, HorizontalAlignment.Left, -1, _fontSize).X;
                }
            }
        }

        // Study spell badge
        if (p.TryGetProperty("study", out var studyProp) && studyProp.GetInt32() > 0)
        {
            var sStr = $"[STUDY: {studyProp.GetInt32()}] ";
            DrawString(_font, new Vector2(curBadgeX, row3Y), sStr, HorizontalAlignment.Left, -1, _fontSize, new Color(1.0f, 0.85f, 0.3f));
            curBadgeX += _font.GetStringSize(sStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // Resting badge
        if (p.TryGetProperty("resting", out var restProp) && restProp.GetInt32() != 0)
        {
            var rVal = restProp.GetInt32();
            var rStr = $"[RESTING{(rVal > 0 ? $": {rVal}" : "")}] ";
            DrawString(_font, new Vector2(curBadgeX, row3Y), rStr, HorizontalAlignment.Left, -1, _fontSize, new Color(0.4f, 0.85f, 0.95f));
            curBadgeX += _font.GetStringSize(rStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // Word of Recall badge
        if (p.TryGetProperty("word_recall", out var recProp) && recProp.GetInt32() > 0)
        {
            var rcStr = $"[RECALL: {recProp.GetInt32()}t] ";
            DrawString(_font, new Vector2(curBadgeX, row3Y), rcStr, HorizontalAlignment.Left, -1, _fontSize, new Color(0.85f, 0.5f, 1.0f));
            curBadgeX += _font.GetStringSize(rcStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // Deep Descent badge
        if (p.TryGetProperty("deep_descent", out var descProp) && descProp.GetInt32() > 0)
        {
            var ddStr = $"[DESCENT: {descProp.GetInt32()}t] ";
            DrawString(_font, new Vector2(curBadgeX, row3Y), ddStr, HorizontalAlignment.Left, -1, _fontSize, new Color(0.9f, 0.4f, 0.7f));
            curBadgeX += _font.GetStringSize(ddStr, HorizontalAlignment.Left, -1, _fontSize).X;
        }

        // If no badges/target active on row 3, display equipped gear summary
        if (curBadgeX <= 6f)
        {
            var weap = p.TryGetProperty("weapon_item", out var wProp) && !string.IsNullOrEmpty(wProp.GetString()) ? wProp.GetString() : "Bare Hands";
            var shld = p.TryGetProperty("shield_item", out var sProp) && !string.IsNullOrEmpty(sProp.GetString()) ? sProp.GetString() : "None";
            var bow = p.TryGetProperty("bow_item", out var bProp) && !string.IsNullOrEmpty(bProp.GetString()) ? bProp.GetString() : "None";
            var gearStr = $"Wielding: {weap}   Shield: {shld}   Ranged: {bow}";
            DrawString(_font, new Vector2(6, row3Y), gearStr, HorizontalAlignment.Left, -1, _fontSize, new Color(0.55f, 0.6f, 0.7f));
        }

        // Compass widget in bottom-right
        DrawCompass(new Vector2(View.X - 70, View.Y - barH / 2f));
    }

    private void DrawCompass(Vector2 center)
    {
        var radius = 16f;
        // Compass background circle
        DrawCircle(center, radius, new Color(0.08f, 0.08f, 0.12f, 0.8f));
        DrawArc(center, radius, 0, Mathf.Tau, 24, new Color(0.4f, 0.45f, 0.55f, 0.9f), 1.5f);

        // Compute angle based on FacingName ("N", "E", "S", "W")
        var angle = FacingName switch
        {
            "E" => Mathf.Pi / 2f,
            "S" => Mathf.Pi,
            "W" => -Mathf.Pi / 2f,
            _ => 0f // "N"
        };

        // Pointer vector pointing in facing direction (up is -Y in 2D canvas coordinates)
        var dirVec = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
        var sideVec = new Vector2(-dirVec.Y, dirVec.X) * 4f;

        var tip = center + dirVec * (radius - 2f);
        var baseCenter = center - dirVec * (radius - 4f);
        var leftCorner = center + sideVec;
        var rightCorner = center - sideVec;

        // North / Forward pointer (red/orange)
        DrawColoredPolygon(new[] { tip, leftCorner, center }, new Color(0.95f, 0.25f, 0.25f, 0.95f));
        DrawColoredPolygon(new[] { tip, center, rightCorner }, new Color(0.75f, 0.15f, 0.15f, 0.95f));

        // South / Backward pointer (white/gray)
        DrawColoredPolygon(new[] { baseCenter, leftCorner, center }, new Color(0.6f, 0.65f, 0.75f, 0.85f));
        DrawColoredPolygon(new[] { baseCenter, center, rightCorner }, new Color(0.45f, 0.5f, 0.6f, 0.85f));

        // Label facing
        var label = FacingName;
        var labelSize = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 10);
        DrawString(_font, new Vector2(center.X - labelSize.X / 2f, center.Y + radius + 10),
            label, HorizontalAlignment.Left, -1, 10, new Color(1.0f, 0.85f, 0.4f));
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            if (Mode == ViewMode.Menu)
            {
                for (var i = 0; i < _menuItemRects.Count; i++)
                {
                    if (_menuItemRects[i].Size.X > 0 && _menuItemRects[i].HasPoint(motion.Position))
                    {
                        if (MenuIndex != i)
                        {
                            MenuIndex = i;
                            QueueRedraw();
                        }
                        break;
                    }
                }
            }
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (Mode == ViewMode.Splash)
                {
                    if (_splashGuideRect.HasPoint(mb.Position))
                    {
                        SplashGuideRequested?.Invoke();
                    }
                    else if (_splashWikiRect.HasPoint(mb.Position))
                    {
                        SplashWikiRequested?.Invoke();
                    }
                    else if (_splashQuitRect.HasPoint(mb.Position))
                    {
                        SplashQuitRequested?.Invoke();
                    }
                    else
                    {
                        SplashContinueRequested?.Invoke();
                    }
                }
                else if (Mode == ViewMode.Guide)
                {
                    for (var i = 0; i < _guideTabRects.Count; i++)
                    {
                        if (_guideTabRects[i].HasPoint(mb.Position))
                        {
                            GuideTabClicked?.Invoke(i);
                            return;
                        }
                    }
                    if (_guideWikiRect.HasPoint(mb.Position))
                    {
                        SplashWikiRequested?.Invoke();
                    }
                    else if (_guideCloseRect.HasPoint(mb.Position))
                    {
                        GuideCloseRequested?.Invoke();
                    }
                }
                else if (Mode == ViewMode.Menu)
                {
                    for (var i = 0; i < _menuItemRects.Count; i++)
                    {
                        if (_menuItemRects[i].Size.X > 0 && _menuItemRects[i].HasPoint(mb.Position))
                        {
                            MenuItemClicked?.Invoke(i);
                            return;
                        }
                    }
                }
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                if (Mode == ViewMode.Guide)
                {
                    GuideScroll = Mathf.Max(0, GuideScroll - 3);
                    QueueRedraw();
                }
                else if (Mode == ViewMode.World)
                {
                    ChangeMinimapZoom(0.15f);
                }
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (Mode == ViewMode.Guide)
                {
                    GuideScroll += 3;
                    QueueRedraw();
                }
                else if (Mode == ViewMode.World)
                {
                    ChangeMinimapZoom(-0.15f);
                }
            }
        }
    }
}
