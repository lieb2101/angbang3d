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

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        _cell = new Vector2(
            _font.GetStringSize("#", HorizontalAlignment.Left, -1, _fontSize).X,
            _font.GetHeight(_fontSize) + 2);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // A Control inside a CanvasLayer has no meaningful size of its own, so lay
    // out against the viewport instead.
    private Vector2 View => GetViewportRect().Size;

    public override void _Draw()
    {
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
                break;
        }

        if (Waiting > 2.0)
        {
            DrawString(_font, new Vector2(4, View.Y - _cell.Y * 3),
                $"waiting for the engine ({Waiting:F0}s)...",
                HorizontalAlignment.Left, -1, _fontSize, Colors.Orange);
        }
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
        var line =
            $"{p.GetProperty("race").GetString()} {p.GetProperty("class").GetString()}  " +
            $"L{p.GetProperty("level").GetInt32()}  " +
            $"HP {p.GetProperty("hp").GetInt32()}/{p.GetProperty("hp_max").GetInt32()}  " +
            $"AU {p.GetProperty("gold").GetInt32()}  " +
            $"Depth {p.GetProperty("depth").GetInt32() * 50}ft  " +
            $"Torch {p.GetProperty("light").GetInt32()}  " +
            $"Facing {FacingName}{wizard}";

        var barH = _cell.Y * 2 + 10;
        DrawRect(new Rect2(0, View.Y - barH, View.X, barH), new Color(0, 0, 0, 0.6f));
        DrawString(_font, new Vector2(6, View.Y - barH + _font.GetAscent(_fontSize) + 3),
            "arrows: turn/walk   M: map   Tab: terminal   >: stairs   " +
            "i: inventory   ^S: save   ^X: save+quit   ?: help",
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.45f, 0.45f, 0.52f));
        DrawString(_font, new Vector2(6, View.Y - 8), line,
            HorizontalAlignment.Left, -1, _fontSize, new Color(0.72f, 0.86f, 1.0f));
    }
}
