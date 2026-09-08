using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// First-person greybox view of the dungeon.
/// </summary>
/// <remarks>
/// Terrain is built from the map's feature indices rather than its glyphs:
/// a glyph is whatever Angband would draw there, so a square with a monster on
/// it reports the monster's letter, not the floor underneath.
/// </remarks>
public partial class DungeonWorld : Node3D
{
    public const float Cell = 2.0f;
    private const float WallHeight = 3.0f;
    private const float EyeHeight = 1.5f;
    private const float StepSeconds = 0.14f;

    // Feature indices from engine/src/list-terrain.h.
    private enum Feat
    {
        None = 0, Floor = 1, Closed = 2, Open = 3, Broken = 4,
        Less = 5, More = 6,
        StoreGeneral = 7, Home = 14, Secret = 15, Rubble = 16,
        Magma = 17, Quartz = 18, MagmaK = 19, QuartzK = 20,
        Granite = 21, Perm = 22, Lava = 23, PassRubble = 24,
    }

    private enum Kind { Skip, Floor, Ceiling, Wall, Door, Stairs, Store, Rubble, Lava }

    private Camera3D _camera;
    private OmniLight3D _torch;
    private Godot.Environment _env;
    private Node3D _entities;
    private Node3D _terrainLabels;
    private readonly Dictionary<Kind, MultiMeshInstance3D> _buckets = new();

    private Vector3 _targetPos;
    private float _targetYaw;
    private float _yaw;
    /// <summary>0 = north, 1 = east, 2 = south, 3 = west.</summary>
    private int _facing;
    private string _levelKey = "";
    private bool _outdoors;
    private double _flicker;
    private int _torchRadius = 1;

    public int Facing => _facing;

    public override void _Ready()
    {
        _env = BuildEnvironment();
        AddChild(new WorldEnvironment { Environment = _env });
        _camera = new Camera3D { Current = true, Fov = 75, Near = 0.05f };
        AddChild(_camera);

        // The torch is the only meaningful light source; everything beyond its
        // radius falls to near black, which is the core mechanic as well as the
        // thing that hides greybox geometry.
        _torch = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.82f, 0.58f),
            LightEnergy = 0.42f,
            OmniRange = 6.0f,
            // Falls off fast so nearby walls do not blow out to flat white.
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            Position = new Vector3(0, 0.25f, 0),
        };
        _camera.AddChild(_torch);

        _entities = new Node3D();
        AddChild(_entities);

        _terrainLabels = new Node3D();
        AddChild(_terrainLabels);

        foreach (Kind k in Enum.GetValues<Kind>())
        {
            if (k == Kind.Skip)
            {
                continue;
            }
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = MeshFor(k),
                },
                MaterialOverride = MaterialFor(k),
            };
            AddChild(mmi);
            _buckets[k] = mmi;
        }
    }

    private static Godot.Environment BuildEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = Colors.Black,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.10f, 0.11f, 0.14f),
        AmbientLightEnergy = 0.35f,
        FogEnabled = true,
        FogLightColor = new Color(0.02f, 0.02f, 0.03f),
        FogDensity = 0.06f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
    };

    private static Mesh MeshFor(Kind k) => k switch
    {
        Kind.Wall or Kind.Store => new BoxMesh { Size = new Vector3(Cell, WallHeight, Cell) },
        Kind.Door => new BoxMesh { Size = new Vector3(Cell * 0.9f, WallHeight * 0.8f, Cell * 0.25f) },
        Kind.Rubble => new BoxMesh { Size = new Vector3(Cell * 0.7f, WallHeight * 0.35f, Cell * 0.7f) },
        Kind.Stairs => new BoxMesh { Size = new Vector3(Cell * 0.8f, 0.35f, Cell * 0.8f) },
        Kind.Ceiling => new BoxMesh { Size = new Vector3(Cell, 0.1f, Cell) },
        _ => new BoxMesh { Size = new Vector3(Cell, 0.1f, Cell) },
    };

    private static StandardMaterial3D MaterialFor(Kind k) => new()
    {
        VertexColorUseAsAlbedo = true,
        AlbedoTexture = GridTexture(),
        // World-space triplanar so the grid lines up across neighbouring cells
        // without any UV work on the primitives.
        Uv1Triplanar = true,
        Uv1WorldTriplanar = true,
        Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
        Roughness = k == Kind.Lava ? 0.4f : 0.95f,
        Metallic = 0.0f,
        EmissionEnabled = k == Kind.Lava,
        Emission = new Color(0.8f, 0.25f, 0.05f),
        EmissionEnergyMultiplier = k == Kind.Lava ? 1.5f : 0.0f,
    };

    private static ImageTexture _grid;

    /// <summary>
    /// Flat untextured boxes give the eye nothing to judge distance by, so the
    /// greybox gets a generated grid rather than a downloaded texture.
    /// </summary>
    private static ImageTexture GridTexture()
    {
        if (_grid != null)
        {
            return _grid;
        }

        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(1234);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var edge = x < 3 || y < 3 || x > size - 4 || y > size - 4;
                var v = edge ? 0.38f : 0.72f + (float)rng.NextDouble() * 0.06f;
                img.SetPixel(x, y, new Color(v, v, v));
            }
        }
        _grid = ImageTexture.CreateFromImage(img);
        return _grid;
    }

    private static Kind KindOf(int feat) => (Feat)feat switch
    {
        Feat.None => Kind.Skip,
        Feat.Floor or Feat.PassRubble => Kind.Floor,
        Feat.Closed or Feat.Open or Feat.Broken or Feat.Secret => Kind.Door,
        Feat.Less or Feat.More => Kind.Stairs,
        Feat.Rubble => Kind.Rubble,
        Feat.Lava => Kind.Lava,
        >= Feat.StoreGeneral and <= Feat.Home => Kind.Store,
        >= Feat.Magma and <= Feat.Perm => Kind.Wall,
        _ => Kind.Wall,
    };

    private static Color BaseColour(Kind k) => k switch
    {
        Kind.Wall => new Color(0.30f, 0.28f, 0.26f),
        Kind.Floor => new Color(0.19f, 0.18f, 0.17f),
        Kind.Ceiling => new Color(0.14f, 0.135f, 0.13f),
        Kind.Door => new Color(0.52f, 0.33f, 0.16f),
        Kind.Stairs => new Color(0.35f, 0.50f, 0.70f),
        Kind.Store => new Color(0.28f, 0.40f, 0.32f),
        Kind.Rubble => new Color(0.34f, 0.32f, 0.30f),
        Kind.Lava => new Color(0.75f, 0.25f, 0.08f),
        _ => Colors.Magenta,
    };

    public void OnFrame(JsonElement frame)
    {
        if (frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var map = frame.GetProperty("map");
        var player = frame.GetProperty("player");

        // Rebuilding every frame would be wasteful, but the known area grows as
        // the player explores, so key on depth plus how much is known.
        var key = $"{player.GetProperty("depth").GetInt32()}:{map.GetProperty("w").GetInt32()}" +
                  $"x{map.GetProperty("h").GetInt32()}:{KnownCount(map)}";
        if (key != _levelKey)
        {
            var firstOnLevel = !_levelKey.StartsWith($"{player.GetProperty("depth").GetInt32()}:");
            _levelKey = key;
            _outdoors = player.GetProperty("depth").GetInt32() == 0;
            // The town is an open street, not a lightless dungeon.
            _env.AmbientLightEnergy = _outdoors ? 1.6f : 0.30f;
            _env.AmbientLightColor = _outdoors
                ? new Color(0.42f, 0.45f, 0.55f)
                : new Color(0.10f, 0.11f, 0.14f);
            _env.FogDensity = _outdoors ? 0.012f : 0.06f;
            Rebuild(map);
            if (firstOnLevel)
            {
                FaceSomethingOpen(map, player.GetProperty("x").GetInt32(),
                    player.GetProperty("y").GetInt32());
            }
        }

        _targetPos = new Vector3(
            player.GetProperty("x").GetInt32() * Cell,
            EyeHeight,
            player.GetProperty("y").GetInt32() * Cell);

        // Necromancers and other lightless classes report 0; keep a sliver of
        // visibility so the view is playable rather than pitch black.
        _torchRadius = Math.Max(1, player.GetProperty("light").GetInt32());
        RebuildEntities(frame);
    }

    private static int KnownCount(JsonElement map)
    {
        var n = 0;
        foreach (var row in map.GetProperty("rows").EnumerateArray())
        {
            var l = row.GetProperty("l").GetString() ?? "";
            foreach (var c in l)
            {
                if ((AngbandColors.HexVal(c) & 0x1) != 0)
                {
                    n++;
                }
            }
        }
        return n;
    }

    private static bool IsWalkable(int feat) => (Feat)feat switch
    {
        Feat.Floor or Feat.Open or Feat.Broken or Feat.Less or Feat.More
            or Feat.PassRubble => true,
        >= Feat.StoreGeneral and <= Feat.Home => true,
        _ => false,
    };

    private static int FeatAt(JsonElement map, int x, int y)
    {
        if (y < 0 || y >= map.GetProperty("h").GetInt32()
            || x < 0 || x >= map.GetProperty("w").GetInt32())
        {
            return 0;
        }
        var f = map.GetProperty("rows")[y].GetProperty("f").GetString() ?? "";
        if (x * 2 + 1 >= f.Length)
        {
            return 0;
        }
        return (AngbandColors.HexVal(f[x * 2]) << 4) | AngbandColors.HexVal(f[x * 2 + 1]);
    }

    /// <summary>Arriving on a level facing a blank wall reads as a broken game.</summary>
    private void FaceSomethingOpen(JsonElement map, int px, int py)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { -1, 0, 1, 0 };
        for (var i = 0; i < 4; i++)
        {
            var f = FeatAt(map, px + dx[i], py + dy[i]);
            if (IsWalkable(f))
            {
                _facing = i;
                _targetYaw = -Mathf.Pi / 2f * i;
                _yaw = _targetYaw;
                return;
            }
        }
    }

    /// <summary>Diagnostic: what the client thinks is around the player.</summary>
    public string DescribeAround(JsonElement map, int px, int py)
    {
        var sb = new System.Text.StringBuilder();
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var f = FeatAt(map, px + dx, py + dy);
                sb.Append(KindOf(f)).Append('(').Append(f).Append(") ");
            }
            sb.Append("| ");
        }
        sb.Append(" instances: ");
        foreach (var (k, mmi) in _buckets)
        {
            if (mmi.Multimesh.InstanceCount > 0)
            {
                sb.Append(k).Append('=').Append(mmi.Multimesh.InstanceCount).Append(' ');
            }
        }
        return sb.ToString();
    }

    private void Rebuild(JsonElement map)
    {
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();
        var rows = map.GetProperty("rows");

        var xf = new Dictionary<Kind, List<Transform3D>>();
        var col = new Dictionary<Kind, List<Color>>();
        foreach (var k in _buckets.Keys)
        {
            xf[k] = new List<Transform3D>();
            col[k] = new List<Color>();
        }

        for (var y = 0; y < h; y++)
        {
            var row = rows[y];
            var feats = row.GetProperty("f").GetString() ?? "";
            var flags = row.GetProperty("l").GetString() ?? "";

            for (var x = 0; x < w; x++)
            {
                if (x >= flags.Length || x * 2 + 1 >= feats.Length)
                {
                    break;
                }

                var flag = AngbandColors.HexVal(flags[x]);
                var known = (flag & 0x1) != 0;
                var inView = (flag & 0x2) != 0;
                if (!known && !inView)
                {
                    continue;
                }

                var feat = (AngbandColors.HexVal(feats[x * 2]) << 4) | AngbandColors.HexVal(feats[x * 2 + 1]);
                var kind = KindOf(feat);
                if (kind == Kind.Skip)
                {
                    continue;
                }

                var yOff = kind switch
                {
                    Kind.Wall or Kind.Store => WallHeight / 2f,
                    Kind.Door => WallHeight * 0.4f,
                    Kind.Rubble => WallHeight * 0.175f,
                    Kind.Stairs => 0.175f,
                    _ => 0f,
                };

                xf[kind].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, yOff, y * Cell)));

                // Remembered-but-unseen geometry is dimmed: the roguelike map
                // memory, carried straight through into 3D.
                var c = BaseColour(kind);
                // Slight per-cell variation so a run of identical boxes still
                // reads as separate blocks.
                var jitter = 1.0f + ((x * 7 + y * 13) % 5 - 2) * 0.035f;
                c = new Color(c.R * jitter, c.G * jitter, c.B * jitter);
                var shade = inView ? c : c.Darkened(0.62f) * new Color(0.75f, 0.8f, 1.0f);
                col[kind].Add(shade);

                // Roof over anything walkable, so corridors read as enclosed
                // rather than as trenches under an open sky. The town is
                // outdoors and gets none.
                if (!_outdoors && kind is Kind.Floor or Kind.Stairs or Kind.Store)
                {
                    xf[Kind.Ceiling].Add(new Transform3D(Basis.Identity,
                        new Vector3(x * Cell, WallHeight, y * Cell)));
                    var cc = BaseColour(Kind.Ceiling);
                    col[Kind.Ceiling].Add(inView ? cc : cc.Darkened(0.62f));
                }
            }
        }

        foreach (var (kind, mmi) in _buckets)
        {
            var list = xf[kind];
            var mm = mmi.Multimesh;
            mm.InstanceCount = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                mm.SetInstanceTransform(i, list[i]);
                mm.SetInstanceColor(i, col[kind][i]);
            }
        }

        RebuildTerrainLabels(map, h, w);
    }

    /// <summary>Stairs and shopfronts are captioned; they are static per level.</summary>
    private void RebuildTerrainLabels(JsonElement map, int h, int w)
    {
        foreach (var child in _terrainLabels.GetChildren())
        {
            child.QueueFree();
        }

        var rows = map.GetProperty("rows");
        for (var y = 0; y < h; y++)
        {
            var flags = rows[y].GetProperty("l").GetString() ?? "";
            var feats = rows[y].GetProperty("f").GetString() ?? "";
            for (var x = 0; x < w && x < flags.Length && x * 2 + 1 < feats.Length; x++)
            {
                if ((AngbandColors.HexVal(flags[x]) & 0x3) == 0)
                {
                    continue;
                }
                var feat = (AngbandColors.HexVal(feats[x * 2]) << 4) | AngbandColors.HexVal(feats[x * 2 + 1]);
                var name = TerrainName(feat);
                if (name == null)
                {
                    continue;
                }
                var isShop = KindOf(feat) == Kind.Store;
                // Shopfronts are solid blocks, so the sign has to sit above the
                // roofline or it is buried inside the geometry.
                _terrainLabels.AddChild(Caption(name,
                    new Vector3(x * Cell, isShop ? WallHeight + 0.6f : 0.9f, y * Cell),
                    isShop ? new Color(0.55f, 0.95f, 0.65f) : new Color(0.6f, 0.8f, 1.0f), 44));
            }
        }
    }

    /// <summary>
    /// A floating caption. Outlined so it stays legible against both lit walls
    /// and darkness, and kept short: the greybox has little else to read.
    /// </summary>
    private static Label3D Caption(string text, Vector3 pos, Color colour, int size = 48)
    {
        return new Label3D
        {
            Text = text,
            Modulate = colour,
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            OutlineSize = 14,
            FontSize = size,
            PixelSize = 0.0055f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = false,
            Position = pos,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    private static Color HealthColour(int hp, int max)
    {
        if (max <= 0)
        {
            return Colors.White;
        }
        var f = Mathf.Clamp(hp / (float)max, 0f, 1f);
        return f > 0.6f ? new Color(0.45f, 0.95f, 0.45f)
            : f > 0.3f ? new Color(1.0f, 0.85f, 0.35f)
            : new Color(1.0f, 0.42f, 0.38f);
    }

    /// <summary>Terrain names from the engine, so nothing is hard-coded here.</summary>
    public System.Collections.Generic.Dictionary<int, (string Name, bool Passable)> Features { get; set; }

    private string TerrainName(int feat)
    {
        if (Features == null || !Features.TryGetValue(feat, out var info))
        {
            return null;
        }
        // Only caption things worth walking towards; everything else would be
        // noise on top of an already busy view.
        var kind = KindOf(feat);
        return kind is Kind.Stairs or Kind.Store ? info.Name : null;
    }

    /// <summary>Greybox stand-ins: billboarded glyphs with names and health.</summary>
    private void RebuildEntities(JsonElement frame)
    {
        foreach (var child in _entities.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var m in frame.GetProperty("monsters").EnumerateArray())
        {
            var pos = new Vector3(m.GetProperty("x").GetInt32() * Cell, 0,
                                  m.GetProperty("y").GetInt32() * Cell);
            var colour = AngbandColors.Get(m.GetProperty("attr").GetInt32());

            _entities.AddChild(Caption(m.GetProperty("glyph").GetString() ?? "?",
                pos + new Vector3(0, 1.0f, 0), colour, 140));

            if (m.TryGetProperty("race", out var race))
            {
                _entities.AddChild(Caption(race.GetString(),
                    pos + new Vector3(0, 1.85f, 0), colour));
            }
            if (m.TryGetProperty("hp", out var hp) && m.TryGetProperty("hp_max", out var hpMax))
            {
                _entities.AddChild(Caption($"{hp.GetInt32()}/{hpMax.GetInt32()}",
                    pos + new Vector3(0, 1.55f, 0),
                    HealthColour(hp.GetInt32(), hpMax.GetInt32()), 40));
            }
        }

        foreach (var o in frame.GetProperty("objects").EnumerateArray())
        {
            var pos = new Vector3(o.GetProperty("x").GetInt32() * Cell, 0,
                                  o.GetProperty("y").GetInt32() * Cell);
            var colour = AngbandColors.Get(o.GetProperty("attr").GetInt32());

            _entities.AddChild(Caption(o.GetProperty("glyph").GetString() ?? "?",
                pos + new Vector3(0, 0.35f, 0), colour, 90));

            if (o.TryGetProperty("name", out var name))
            {
                var text = name.GetString();
                if (o.TryGetProperty("pile", out var pile) && pile.GetBoolean())
                {
                    text += " (pile)";
                }
                _entities.AddChild(Caption(text, pos + new Vector3(0, 0.95f, 0),
                    new Color(0.86f, 0.86f, 0.78f), 40));
            }
        }
    }

    /// <summary>Turn in place. Free: Angband has no facing, so this costs no game turn.</summary>
    public void Turn(int delta)
    {
        _facing = ((_facing + delta) % 4 + 4) % 4;
        _targetYaw = -Mathf.Pi / 2f * _facing;
    }

    /// <summary>Angband movement key for a direction relative to the current facing.</summary>
    public string MoveKey(bool forward)
    {
        // Arrow keys, not digits: in Angband's original keyset a digit is a
        // repeat-count prefix, so "6" does not walk east.
        string[] dirs = { "up", "right", "down", "left" };
        var index = forward ? _facing : (_facing + 2) % 4;
        return dirs[index];
    }

    public override void _Process(double delta)
    {
        if (_camera == null)
        {
            return;
        }

        var t = (float)Math.Min(1.0, delta / StepSeconds);
        _camera.Position = _camera.Position.Lerp(_targetPos, t);

        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);
        _camera.Rotation = new Vector3(0, _yaw, 0);

        _flicker += delta;
        var f = 1.0f + 0.06f * Mathf.Sin((float)_flicker * 11f) + 0.04f * Mathf.Sin((float)_flicker * 23f);
        _torch.OmniRange = _torchRadius * Cell + 1.5f;
        _torch.LightEnergy = 2.2f * f;
    }
}
