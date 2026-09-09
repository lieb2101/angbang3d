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

    // Minimap properties
    public float MinimapScale { get; set; } = 1.0f;
    private const float MinMinimapScale = 0.5f;
    private const float MaxMinimapScale = 3.0f;
    private const float Margin = 12f;

    public void ChangeMinimapScale(float delta)
    {
        MinimapScale = Mathf.Clamp(MinimapScale + delta, MinMinimapScale, MaxMinimapScale);
        QueueRedraw();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _font = ThemeDB.FallbackFont;
        _cell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, _fontSize).X,
            _font.GetHeight(_fontSize) + 2);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private Rect2 GetMinimapRect()
    {
        var top = _cell.Y + 12f;
        var baseW = 240f;
        var baseH = 180f;
        var maxW = Mathf.Max(140f, View.X - Margin * 2);
        var maxH = Mathf.Max(100f, View.Y - top - (_cell.Y * 2 + 20f));
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

    private void DrawMenu()
    {
        DrawRect(new Rect2(Vector2.Zero, View), new Color(0.03f, 0.03f, 0.045f));

        var cx = View.X / 2f;
        var top = View.Y * 0.22f;

        DrawString(_font, new Vector2(cx - 180, top), MenuTitle,
            HorizontalAlignment.Left, -1, 36, new Color(1.0f, 0.82f, 0.45f));
        DrawString(_font, new Vector2(cx - 180, top + 32), MenuSubtitle,
            HorizontalAlignment.Left, -1, 16, new Color(0.5f, 0.5f, 0.58f));

        const int maxVisible = 12;
        var startIdx = 0;
        if (MenuItems.Length > maxVisible)
        {
            startIdx = Mathf.Clamp(MenuIndex - maxVisible / 2, 0, MenuItems.Length - maxVisible);
        }
        var endIdx = Mathf.Min(MenuItems.Length, startIdx + maxVisible);

        if (startIdx > 0)
        {
            DrawString(_font, new Vector2(cx - 190, top + 62),
                "^ (more items above)", HorizontalAlignment.Left, -1, 13, new Color(0.6f, 0.6f, 0.68f));
        }

        for (var i = startIdx; i < endIdx; i++)
        {
            var visualIndex = i - startIdx;
            var y = top + 80 + visualIndex * 36;
            var selected = i == MenuIndex;
            if (selected)
            {
                DrawRect(new Rect2(cx - 200, y - 22, 680, 30), new Color(0.18f, 0.14f, 0.06f));
            }
            DrawString(_font, new Vector2(cx - 190, y),
                (selected ? "> " : "  ") + $"{i + 1}. " + MenuItems[i],
                HorizontalAlignment.Left, -1, 18,
                selected ? new Color(1.0f, 0.9f, 0.6f) : new Color(0.72f, 0.72f, 0.78f));
        }

        var visibleCount = endIdx - startIdx;
        if (endIdx < MenuItems.Length)
        {
            DrawString(_font, new Vector2(cx - 190, top + 80 + visibleCount * 36 + 4),
                "v (more items below)", HorizontalAlignment.Left, -1, 13, new Color(0.6f, 0.6f, 0.68f));
        }

        var footerY = top + 95 + visibleCount * 36 + (endIdx < MenuItems.Length ? 22 : 0);
        DrawString(_font, new Vector2(cx - 190, footerY),
            "up/down to choose, Enter to select, Esc to back/close",
            HorizontalAlignment.Left, -1, 14, new Color(0.45f, 0.45f, 0.52f));
    }

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

        var cols = Mathf.FloorToInt(View.X / _cell.X);
        var lines = Mathf.FloorToInt((View.Y - _cell.Y * 3) / _cell.Y);
        var ox = Mathf.Clamp(player.GetProperty("x").GetInt32() - cols / 2, 0, Mathf.Max(0, w - cols));
        var oy = Mathf.Clamp(player.GetProperty("y").GetInt32() - lines / 2, 0, Mathf.Max(0, h - lines));
        var top = _cell.Y * 2;

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

                var colour = AngbandColors.Get(AngbandColors.ParseAttr(attrs, cell));
                if ((flag & 0x2) == 0)
                {
                    colour = colour.Darkened(0.55f);
                }

                DrawString(_font, new Vector2(c * _cell.X, top + row * _cell.Y + _font.GetAscent(_fontSize)),
                    ch.ToString(), HorizontalAlignment.Left, -1, _fontSize, colour);
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
        var w = term.GetProperty("w").GetInt32();

        for (var y = 0; y < rows.GetArrayLength(); y++)
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
                DrawString(_font, new Vector2(x * _cell.X, y * _cell.Y + _font.GetAscent(_fontSize)),
                    glyphs[x].ToString(), HorizontalAlignment.Left, -1, _fontSize,
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
        var headerH = 18f;
        var headerRect = new Rect2(mapRect.Position.X, mapRect.Position.Y, mapRect.Size.X, headerH);
        var contentRect = new Rect2(mapRect.Position.X + 2, mapRect.Position.Y + headerH, mapRect.Size.X - 4, mapRect.Size.Y - headerH - 2);

        // Semi-transparent background panel with shadow/border
        DrawRect(mapRect, new Color(0.02f, 0.03f, 0.04f, 0.88f));
        DrawRect(headerRect, new Color(0.06f, 0.08f, 0.12f, 0.95f));
        DrawRect(mapRect, new Color(0.28f, 0.35f, 0.48f, 0.85f), false, 1.5f);
        DrawLine(
            new Vector2(headerRect.Position.X, headerRect.Position.Y + headerH),
            new Vector2(headerRect.Position.X + headerRect.Size.X, headerRect.Position.Y + headerH),
            new Color(0.25f, 0.32f, 0.42f, 0.8f), 1f);

        // Header Title and Info
        var titleFont = 11;
        DrawString(_font, new Vector2(headerRect.Position.X + 6, headerRect.Position.Y + _font.GetAscent(titleFont) + 2),
            "MINIMAP", HorizontalAlignment.Left, -1, titleFont, new Color(0.95f, 0.82f, 0.45f, 0.95f));

        var depth = player.GetProperty("depth").GetInt32();
        var locStr = depth == 0 ? "Town" : $"{depth * 50}ft";
        var coordStr = $"({px},{py}) {locStr}";
        var coordSize = _font.GetStringSize(coordStr, HorizontalAlignment.Left, -1, 10);
        DrawString(_font, new Vector2(headerRect.Position.X + headerRect.Size.X - coordSize.X - 6, headerRect.Position.Y + _font.GetAscent(10) + 3),
            coordStr, HorizontalAlignment.Left, -1, 10, new Color(0.6f, 0.68f, 0.8f, 0.9f));

        // Compute dynamic font size for content based on minimap height
        var miniFontSize = Mathf.Clamp(Mathf.RoundToInt(contentRect.Size.Y / 16f), 8, 22);
        var miniCell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, miniFontSize).X,
            _font.GetHeight(miniFontSize));

        if (miniCell.X > 0 && miniCell.Y > 0)
        {
            var cols = Mathf.FloorToInt(contentRect.Size.X / miniCell.X);
            var lines = Mathf.FloorToInt(contentRect.Size.Y / miniCell.Y);
            var ox = Mathf.Clamp(px - cols / 2, 0, Mathf.Max(0, w - cols));
            var oy = Mathf.Clamp(py - lines / 2, 0, Mathf.Max(0, h - lines));

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

                    var colour = AngbandColors.Get(AngbandColors.ParseAttr(attrs, cell));
                    if ((flag & 0x2) == 0)
                    {
                        colour = colour.Darkened(0.55f);
                    }

                    var isPlayer = (cell == px && oy + row == py);
                    if (isPlayer)
                    {
                        colour = new Color(1.0f, 0.95f, 0.4f);
                    }

                    var drawPos = new Vector2(
                        contentRect.Position.X + c * miniCell.X,
                        contentRect.Position.Y + row * miniCell.Y + _font.GetAscent(miniFontSize));

                    if (drawPos.Y <= mapRect.Position.Y + mapRect.Size.Y)
                    {
                        DrawString(_font, drawPos, ch.ToString(), HorizontalAlignment.Left, -1, miniFontSize, colour);
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
        var bg = prompt != null ? new Color(0.20f, 0.13f, 0.02f, 0.92f) : new Color(0, 0, 0, 0.55f);
        var fg = prompt != null ? new Color(1.0f, 0.88f, 0.55f) : Colors.White;

        DrawRect(new Rect2(0, 0, View.X, _cell.Y + 6), bg);
        DrawString(_font, new Vector2(6, _font.GetAscent(_fontSize) + 3), text,
            HorizontalAlignment.Left, -1, _fontSize, fg);

        var wizard = p.GetProperty("wizard").GetBoolean() ? "  [WIZARD]" : "";
        var depth = p.GetProperty("depth").GetInt32();
        // Depth 0 is the town, which is the same every visit; everything below
        // is generated fresh each time you arrive.
        var place = depth == 0 ? "Town" : $"Depth {depth}  ({depth * 50}ft)";
        var lightRadius = p.GetProperty("light").GetInt32();
        var hasLightItem = p.TryGetProperty("light_item", out var liProp) && !string.IsNullOrEmpty(liProp.GetString());
        var lightItemName = hasLightItem ? liProp.GetString() : "None";
        var lightStr = depth == 0
            ? (hasLightItem ? $"{lightItemName} (day)" : "None")
            : (hasLightItem ? $"{lightItemName} (R:{lightRadius})" : (lightRadius > 0 ? $"Light {lightRadius}" : "None"));

        var line =
            $"{p.GetProperty("race").GetString()} {p.GetProperty("class").GetString()}  " +
            $"L{p.GetProperty("level").GetInt32()}  " +
            $"HP {p.GetProperty("hp").GetInt32()}/{p.GetProperty("hp_max").GetInt32()}  " +
            $"AU {p.GetProperty("gold").GetInt32()}  " +
            $"{place}  " +
            $"Light: {lightStr}  " +
            $"Facing {FacingName}{wizard}";

        var barH = _cell.Y * 2 + 10;
        DrawRect(new Rect2(0, View.Y - barH, View.X, barH), new Color(0, 0, 0, 0.6f));
        DrawString(_font, new Vector2(6, View.Y - barH + _font.GetAscent(_fontSize) + 3),
            (StairsHint != null ? StairsHint + "   |   " : "") +
            "arrows: turn/walk   Shift-M: map   Tab: terminal   PgUp/PgDn: minimap   " +
            "i: inventory   ^S: save   ?: help",
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.45f, 0.45f, 0.52f));
        DrawString(_font, new Vector2(6, View.Y - 8), line,
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.72f, 0.86f, 1.0f));

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
        // Facing angles: N=0 (up), E=90 (right), S=180 (down), W=270 (left)
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
}
