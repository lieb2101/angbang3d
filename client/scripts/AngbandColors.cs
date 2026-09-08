using Godot;

namespace Angband3D;

/// <summary>
/// Angband's colour table, indexed by the attribute values carried in the
/// protocol's `a` fields. Mirrors COLOUR_* in the engine's z-color.c.
/// </summary>
public static class AngbandColors
{
    private static readonly Color[] Table =
    {
        new(0.00f, 0.00f, 0.00f), // 0  dark
        new(1.00f, 1.00f, 1.00f), // 1  white
        new(0.50f, 0.50f, 0.50f), // 2  slate
        new(1.00f, 0.50f, 0.00f), // 3  orange
        new(0.75f, 0.00f, 0.00f), // 4  red
        new(0.00f, 0.55f, 0.28f), // 5  green
        new(0.00f, 0.00f, 1.00f), // 6  blue
        new(0.50f, 0.35f, 0.05f), // 7  umber
        new(0.35f, 0.35f, 0.35f), // 8  light dark
        new(0.75f, 0.75f, 0.75f), // 9  light slate
        new(0.60f, 0.00f, 1.00f), // 10 light purple
        new(1.00f, 1.00f, 0.00f), // 11 yellow
        new(1.00f, 0.25f, 0.25f), // 12 light red
        new(0.00f, 1.00f, 0.00f), // 13 light green
        new(0.00f, 1.00f, 1.00f), // 14 light blue
        new(0.77f, 0.63f, 0.38f), // 15 light umber
        new(0.60f, 0.00f, 0.60f), // 16 purple
        new(0.60f, 0.00f, 0.30f), // 17 violet
        new(0.00f, 0.60f, 0.60f), // 18 teal
        new(0.42f, 0.35f, 0.20f), // 19 mud
        new(1.00f, 1.00f, 0.60f), // 20 light yellow
        new(0.90f, 0.00f, 0.90f), // 21 magenta
        new(0.20f, 0.90f, 0.90f), // 22 light teal
        new(0.70f, 0.40f, 1.00f), // 23 light violet
        new(1.00f, 0.40f, 0.70f), // 24 light pink
        new(0.72f, 0.60f, 0.16f), // 25 mustard
        new(0.44f, 0.56f, 0.70f), // 26 blue slate
        new(0.16f, 0.42f, 0.82f), // 27 deep light blue
    };

    public static Color Get(int attr)
    {
        // Angband can set high bits on the attribute for tile modes.
        var index = attr & 0x1F;
        return index < Table.Length ? Table[index] : Table[1];
    }

    /// <summary>
    /// Parse two hex digits at cell <paramref name="cell"/> of a packed
    /// attribute row.
    /// </summary>
    public static int ParseAttr(string packed, int cell)
    {
        var i = cell * 2;
        if (i + 1 >= packed.Length)
        {
            return 1;
        }
        return (HexVal(packed[i]) << 4) | HexVal(packed[i + 1]);
    }

    public static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
}
