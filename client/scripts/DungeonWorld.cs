using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// First-person 3D view of the dungeon with solid stone block masonry,
/// orientation-aware archway doorways, distinct mineral veins, and atmospheric lighting.
/// </summary>
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

    private enum Kind
    {
        Skip,
        Floor,
        Ceiling,
        Wall,
        Magma,
        Quartz,
        Store,
        DoorClosed,
        DoorOpen,
        DoorBroken,
        StairsDown,
        StairsUp,
        Rubble,
        Lava
    }

    private enum DoorState { Closed, Open, Broken }

    private Camera3D _camera;
    private OmniLight3D _torch;
    private Godot.Environment _env;
    private Node3D _entities;
    private Node3D _terrainLabels;
    private readonly Dictionary<Kind, MultiMeshInstance3D> _buckets = new();
    private readonly Dictionary<Kind, List<Transform3D>> _xf = new();
    private readonly Dictionary<Kind, List<Color>> _col = new();
    private readonly Dictionary<string, MonsterEntity> _activeMonsters = new();
    private readonly Dictionary<string, Node3D> _activeItems = new();
    private int _frameSeq;

    private class CombatFloater
    {
        public Label3D Node { get; set; }
        public Vector3 StartPos { get; set; }
        public float Elapsed { get; set; }
        public float Lifetime { get; set; } = 0.70f;
        public Color BaseColor { get; set; }
    }

    private readonly List<CombatFloater> _activeFloaters = new();
    private float _trauma = 0f;
    private string _lastProcessedMessage = "";
    private string _lastTerrainLabelsSignature = "";

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

    /// <summary>Bearing and distance to the nearest known down staircase.</summary>
    public string StairsHint { get; private set; }

    // Materials
    private static StandardMaterial3D _wallMaterial;
    private static StandardMaterial3D _floorMaterial;
    private static StandardMaterial3D _ceilingMaterial;
    private static StandardMaterial3D _magmaMaterial;
    private static StandardMaterial3D _quartzMaterial;
    private static StandardMaterial3D _storeMaterial;
    private static StandardMaterial3D _doorFrameMaterial;
    private static StandardMaterial3D _doorWoodMaterial;
    private static StandardMaterial3D _stairsMaterial;
    private static StandardMaterial3D _rubbleMaterial;
    private static StandardMaterial3D _lavaMaterial;

    private static readonly Dictionary<Kind, Mesh> _meshCache = new();

    public override void _Ready()
    {
        InitMaterials();

        _env = BuildEnvironment();
        AddChild(new WorldEnvironment { Environment = _env });
        _camera = new Camera3D { Current = true, Fov = 75, Near = 0.05f };
        AddChild(_camera);

        // Realistic warm torch with soft shadows and ember particles
        _torch = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.86f, 0.64f),
            LightEnergy = 2.4f,
            OmniRange = 9.0f,
            OmniAttenuation = 0.85f,
            ShadowEnabled = true,
            ShadowBlur = 1.6f,
            ShadowBias = 0.04f,
            Position = new Vector3(0, 0.25f, 0),
        };
        _camera.AddChild(_torch);

        var embers = new CpuParticles3D
        {
            Amount = 14,
            Lifetime = 1.2f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.18f,
            Direction = new Vector3(0, 1, 0),
            Spread = 30f,
            InitialVelocityMin = 0.3f,
            InitialVelocityMax = 0.7f,
            Gravity = new Vector3(0, 0.15f, 0),
            ScaleAmountMin = 0.02f,
            ScaleAmountMax = 0.04f,
            Color = new Color(1.0f, 0.65f, 0.25f, 0.85f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(1.0f, 0.75f, 0.3f),
            },
            Position = new Vector3(0, 0.25f, 0),
        };
        _camera.AddChild(embers);

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
            _xf[k] = new List<Transform3D>(256);
            _col[k] = new List<Color>(256);
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
        BackgroundColor = new Color(0.005f, 0.006f, 0.010f),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.18f, 0.20f, 0.26f),
        AmbientLightEnergy = 0.08f,
        FogEnabled = true,
        FogLightColor = new Color(0.005f, 0.006f, 0.010f),
        FogDensity = 0.024f,
        VolumetricFogEnabled = true,
        VolumetricFogDensity = 0.014f,
        VolumetricFogAlbedo = new Color(0.14f, 0.16f, 0.22f),
        VolumetricFogEmission = Colors.Black,
        VolumetricFogLength = 28.0f,
        SsaoEnabled = true,
        SsaoRadius = 1.2f,
        SsaoIntensity = 1.0f,
        GlowEnabled = true,
        GlowIntensity = 0.50f,
        GlowBloom = 0.08f,
        GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapExposure = 1.12f,
    };

    #region Procedural Textures & Materials

    private static float Hash(int x, int y, int seed = 0)
    {
        var n = x + y * 57 + seed * 131;
        n = (n << 13) ^ n;
        return (1.0f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0f) * 0.5f + 0.5f;
    }

    private static ImageTexture CreateTexture(int width, int height, Func<int, int, Color> pixelFunc, bool generateMipmaps = true)
    {
        var img = Image.CreateEmpty(width, height, generateMipmaps, Image.Format.Rgba8);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                img.SetPixel(x, y, pixelFunc(x, y));
            }
        }
        if (generateMipmaps)
        {
            img.GenerateMipmaps();
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture CreateStoneWallTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 64;
            var yInRow = y % 64;
            var distY = Math.Min(yInRow, 63 - yInRow);
            var xOff = (row % 2 == 1) ? 64 : 0;
            var xInRow = (x + size - xOff) % 128;
            var distX = Math.Min(xInRow, 127 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            // Mortar joint
            if (mortarDist <= 2)
            {
                var mn = Hash(x, y, 101) * 0.08f - 0.04f;
                return new Color(0.20f + mn, 0.20f + mn, 0.22f + mn);
            }

            // Stone block
            var blockId = row * 4 + ((x + size - xOff) / 128);
            var blockHue = Hash(blockId, 0, 77);
            var rBase = 0.65f + (blockHue - 0.5f) * 0.12f;
            var gBase = 0.65f + (blockHue - 0.5f) * 0.08f;
            var bBase = 0.67f + (0.5f - blockHue) * 0.08f;

            // Bevel from mortar to block face
            var bevel = Mathf.Clamp((mortarDist - 2) / 6.0f, 0.45f, 1.0f);

            // Directional chisel highlights
            var highlight = 0f;
            if (yInRow >= 3 && yInRow <= 9) highlight += (10 - yInRow) * 0.02f;
            if (xInRow >= 3 && xInRow <= 9) highlight += (10 - xInRow) * 0.015f;
            if (distY <= 7 && yInRow > 32) highlight -= (8 - distY) * 0.025f;
            if (distX <= 7 && xInRow > 64) highlight -= (8 - distX) * 0.02f;

            // Stone surface grain
            var grain = (Hash(x, y, 1) * 0.6f + Hash(x * 2, y * 2, 2) * 0.4f) * 0.12f - 0.06f;

            var r = Mathf.Clamp(rBase * bevel + highlight + grain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + highlight + grain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + highlight + grain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateStoneWallNormal()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 64;
            var yInRow = y % 64;
            var distY = Math.Min(yInRow, 63 - yInRow);
            var xOff = (row % 2 == 1) ? 64 : 0;
            var xInRow = (x + size - xOff) % 128;
            var distX = Math.Min(xInRow, 127 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 2)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (xInRow < 64) ? (1.0f - xInRow / 8.0f) : -(1.0f - (127 - xInRow) / 8.0f);
            var dy = (yInRow < 32) ? (1.0f - yInRow / 8.0f) : -(1.0f - (63 - yInRow) / 8.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var norm = new Vector3(dx * 0.7f, -dy * 0.7f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateFloorTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            // 2x2 large paving flagstones (128x128 each) with staggered sub-tiles
            var tileX = x / 128;
            var tileY = y / 128;
            var inTileX = x % 128;
            var inTileY = y % 128;
            var distX = Math.Min(inTileX, 127 - inTileX);
            var distY = Math.Min(inTileY, 127 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 2)
            {
                var mn = Hash(x, y, 202) * 0.06f - 0.03f;
                return new Color(0.18f + mn, 0.18f + mn, 0.20f + mn);
            }

            var tileId = tileY * 2 + tileX;
            var tileHue = Hash(tileId, 0, 99);
            var rBase = 0.58f + (tileHue - 0.5f) * 0.08f;
            var gBase = 0.58f + (tileHue - 0.5f) * 0.06f;
            var bBase = 0.60f + (0.5f - tileHue) * 0.06f;

            var bevel = Mathf.Clamp((mortarDist - 2) / 6.0f, 0.60f, 1.0f);
            var grain = (Hash(x, y, 3) * 0.5f + Hash(x * 3, y * 3, 4) * 0.5f) * 0.10f - 0.05f;

            var r = Mathf.Clamp(rBase * bevel + grain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + grain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + grain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateFloorNormal()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var inTileX = x % 128;
            var inTileY = y % 128;
            var distX = Math.Min(inTileX, 127 - inTileX);
            var distY = Math.Min(inTileY, 127 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 2)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (inTileX < 64) ? (1.0f - inTileX / 8.0f) : -(1.0f - (127 - inTileX) / 8.0f);
            var dy = (inTileY < 64) ? (1.0f - inTileY / 8.0f) : -(1.0f - (127 - inTileY) / 8.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var norm = new Vector3(dx * 0.6f, -dy * 0.6f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateCeilingTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var inTileX = x % 64;
            var inTileY = y % 64;
            var distX = Math.Min(inTileX, 63 - inTileX);
            var distY = Math.Min(inTileY, 63 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 1)
            {
                return new Color(0.16f, 0.16f, 0.18f);
            }

            var rBase = 0.46f;
            var gBase = 0.47f;
            var bBase = 0.50f;
            var bevel = Mathf.Clamp((mortarDist - 1) / 5.0f, 0.50f, 1.0f);
            var grain = (Hash(x, y, 5) * 0.6f + Hash(x * 2, y * 2, 6) * 0.4f) * 0.12f - 0.06f;

            var r = Mathf.Clamp(rBase * bevel + grain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + grain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + grain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateMagmaTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 64;
            var yInRow = y % 64;
            var distY = Math.Min(yInRow, 63 - yInRow);
            var xOff = (row % 2 == 1) ? 64 : 0;
            var xInRow = (x + size - xOff) % 128;
            var distX = Math.Min(xInRow, 127 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            // Diagonal volcanic vein noise
            var vein = Mathf.Sin((x + y * 1.4f) * 0.08f) + Hash(x, y, 77) * 0.4f;
            var isVein = Math.Abs(vein) < 0.25f || mortarDist <= 2;

            if (isVein)
            {
                var heat = 1.0f - Mathf.Clamp((Math.Abs(vein)) / 0.25f, 0f, 1f);
                var r = 1.0f;
                var g = 0.35f + heat * 0.45f;
                var b = 0.05f + heat * 0.15f;
                return new Color(r, g, b);
            }

            // Dark basalt rock
            var rockGrain = Hash(x, y, 88) * 0.08f - 0.04f;
            var br = 0.22f + rockGrain;
            var bg = 0.20f + rockGrain;
            var bb = 0.22f + rockGrain;
            return new Color(br, bg, bb);
        });
    }

    private static ImageTexture CreateMagmaEmission()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 64;
            var yInRow = y % 64;
            var distY = Math.Min(yInRow, 63 - yInRow);
            var xOff = (row % 2 == 1) ? 64 : 0;
            var xInRow = (x + size - xOff) % 128;
            var distX = Math.Min(xInRow, 127 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            var vein = Mathf.Sin((x + y * 1.4f) * 0.08f) + Hash(x, y, 77) * 0.4f;
            var isVein = Math.Abs(vein) < 0.25f || mortarDist <= 2;

            if (isVein)
            {
                var heat = 1.0f - Mathf.Clamp((Math.Abs(vein)) / 0.25f, 0f, 1f);
                return new Color(1.0f, 0.40f + heat * 0.45f, 0.05f + heat * 0.15f);
            }

            return Colors.Black;
        });
    }

    private static ImageTexture CreateQuartzTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 64;
            var yInRow = y % 64;
            var distY = Math.Min(yInRow, 63 - yInRow);
            var xOff = (row % 2 == 1) ? 64 : 0;
            var xInRow = (x + size - xOff) % 128;
            var distX = Math.Min(xInRow, 127 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            // Crystalline quartz vein
            var crystalVein = Mathf.Sin((x * 1.5f - y) * 0.09f) + Hash(x, y, 44) * 0.35f;
            if (Math.Abs(crystalVein) < 0.20f)
            {
                var glint = Hash(x * 4, y * 4, 12);
                return new Color(0.85f + glint * 0.15f, 0.92f + glint * 0.08f, 0.98f);
            }

            if (mortarDist <= 2)
            {
                return new Color(0.20f, 0.20f, 0.22f);
            }

            var grain = Hash(x, y, 9) * 0.10f - 0.05f;
            return new Color(0.55f + grain, 0.56f + grain, 0.60f + grain);
        });
    }

    private static ImageTexture CreateWoodDoorTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            // Vertical oak planks (5 planks across 256px)
            var plankIdx = x / 51;
            var inPlankX = x % 51;
            var seamDist = Math.Min(inPlankX, 50 - inPlankX);

            // Horizontal forged-iron reinforcement straps at y ~ 50..70 and y ~ 185..205
            var isIronStrap = (y >= 48 && y <= 68) || (y >= 184 && y <= 204);
            if (isIronStrap)
            {
                // Round iron rivets every 51px
                var rivetX = inPlankX - 25;
                var rivetY = (y < 100) ? (y - 58) : (y - 194);
                var isRivet = rivetX * rivetX + rivetY * rivetY <= 16;
                if (isRivet)
                {
                    return new Color(0.38f, 0.38f, 0.42f);
                }
                var ironGrain = Hash(x, y, 31) * 0.06f - 0.03f;
                return new Color(0.18f + ironGrain, 0.18f + ironGrain, 0.20f + ironGrain);
            }

            // Dark plank seam
            if (seamDist <= 1)
            {
                return new Color(0.12f, 0.08f, 0.05f);
            }

            // Oak wood grain
            var woodGrain = Mathf.Sin(y * 0.2f + Hash(x, y, 55) * 4.0f) * 0.04f + Hash(x, y, 66) * 0.04f;
            var plankHue = Hash(plankIdx, 0, 88);
            var r = Mathf.Clamp(0.48f + (plankHue - 0.5f) * 0.08f + woodGrain, 0f, 1f);
            var g = Mathf.Clamp(0.30f + (plankHue - 0.5f) * 0.05f + woodGrain * 0.8f, 0f, 1f);
            var b = Mathf.Clamp(0.16f + (plankHue - 0.5f) * 0.03f + woodGrain * 0.5f, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateStoreTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            // Medieval half-timbered shop facade with dark oak beams and stucco/plaster infill
            var isBorderBeam = x < 20 || x > 235 || y < 20 || y > 235;
            var isCrossBeam = Math.Abs(x - 128) < 10 || Math.Abs(y - 128) < 10;
            var isDiagonal = Math.Abs((x - y) % 128) < 8 || Math.Abs((x + y) % 128) < 8;

            if (isBorderBeam || isCrossBeam || isDiagonal)
            {
                var woodGrain = Hash(x, y, 12) * 0.08f - 0.04f;
                return new Color(0.28f + woodGrain, 0.16f + woodGrain * 0.7f, 0.09f + woodGrain * 0.5f);
            }

            // Warm stucco / plaster wall infill
            var plasterGrain = (Hash(x, y, 22) * 0.6f + Hash(x * 2, y * 2, 23) * 0.4f) * 0.10f - 0.05f;
            return new Color(0.78f + plasterGrain, 0.72f + plasterGrain, 0.60f + plasterGrain);
        });
    }

    private static ImageTexture CreateLavaTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            // Multi-octave magma flow noise
            var n1 = Mathf.Sin(x * 0.06f + y * 0.04f) + Mathf.Cos(x * 0.05f - y * 0.07f);
            var n2 = Hash(x / 4, y / 4, 301) * 0.6f + Hash(x, y, 302) * 0.4f;
            var pattern = n1 * 0.5f + (n2 - 0.5f) * 0.8f;

            if (pattern > 0.15f)
            {
                // Molten magma core (deep glowing oranges and yellows)
                var heat = Mathf.Clamp((pattern - 0.15f) / 0.85f, 0f, 1f);
                var r = 1.0f;
                var g = 0.42f + heat * 0.40f;
                var b = 0.02f + heat * 0.10f;
                return new Color(r, g, b);
            }
            if (pattern > -0.25f)
            {
                // Burning molten crust transition
                var heat = Mathf.Clamp((pattern + 0.25f) / 0.40f, 0f, 1f);
                var r = 0.85f + heat * 0.15f;
                var g = 0.16f + heat * 0.26f;
                var b = 0.01f + heat * 0.01f;
                return new Color(r, g, b);
            }

            // Dark cooling basalt crust
            var crustGrain = Hash(x, y, 303) * 0.06f - 0.03f;
            var br = 0.16f + crustGrain;
            var bg = 0.07f + crustGrain * 0.5f;
            var bb = 0.04f + crustGrain * 0.3f;
            return new Color(br, bg, bb);
        });
    }

    private static ImageTexture CreateLavaEmission()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var n1 = Mathf.Sin(x * 0.06f + y * 0.04f) + Mathf.Cos(x * 0.05f - y * 0.07f);
            var n2 = Hash(x / 4, y / 4, 301) * 0.6f + Hash(x, y, 302) * 0.4f;
            var pattern = n1 * 0.5f + (n2 - 0.5f) * 0.8f;

            if (pattern > 0.15f)
            {
                var heat = Mathf.Clamp((pattern - 0.15f) / 0.85f, 0f, 1f);
                return new Color(1.0f, 0.42f + heat * 0.35f, 0.05f + heat * 0.10f);
            }
            if (pattern > -0.25f)
            {
                var heat = Mathf.Clamp((pattern + 0.25f) / 0.40f, 0f, 1f);
                return new Color(0.65f + heat * 0.35f, 0.10f + heat * 0.32f, 0.02f);
            }

            return Colors.Black;
        });
    }

    private static void InitMaterials()
    {
        if (_wallMaterial != null)
        {
            return;
        }

        var wallTex = CreateStoneWallTexture();
        var wallNormal = CreateStoneWallNormal();
        var floorTex = CreateFloorTexture();
        var floorNormal = CreateFloorNormal();
        var ceilingTex = CreateCeilingTexture();
        var magmaTex = CreateMagmaTexture();
        var magmaEmission = CreateMagmaEmission();
        var quartzTex = CreateQuartzTexture();
        var woodDoorTex = CreateWoodDoorTexture();
        var storeTex = CreateStoreTexture();
        var lavaTex = CreateLavaTexture();
        var lavaEmission = CreateLavaEmission();

        _wallMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.35f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.85f,
            Metallic = 0.05f,
        };

        _floorMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = floorTex,
            NormalEnabled = true,
            NormalTexture = floorNormal,
            NormalScale = 1.1f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.80f,
            Metallic = 0.05f,
        };

        _ceilingMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = ceilingTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.8f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.90f,
            Metallic = 0.0f,
        };

        _magmaMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = magmaTex,
            EmissionEnabled = true,
            EmissionTexture = magmaEmission,
            Emission = new Color(1.0f, 0.45f, 0.08f),
            EmissionEnergyMultiplier = 1.5f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.60f,
            Metallic = 0.10f,
        };

        _quartzMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = quartzTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.2f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.40f,
            Metallic = 0.25f,
        };

        _storeMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = storeTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.0f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.80f,
            Metallic = 0.05f,
        };

        _doorFrameMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.2f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.85f,
            Metallic = 0.05f,
        };

        _doorWoodMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = woodDoorTex,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.70f,
            Metallic = 0.18f,
        };

        _stairsMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.1f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.80f,
            Metallic = 0.05f,
        };

        _rubbleMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 1.4f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.90f,
            Metallic = 0.05f,
        };

        _lavaMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = lavaTex,
            EmissionEnabled = true,
            EmissionTexture = lavaEmission,
            Emission = new Color(1.0f, 0.45f, 0.08f),
            EmissionEnergyMultiplier = 1.35f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.40f,
            Metallic = 0.0f,
        };
    }

    #endregion

    #region Procedural Geometry Builders

    private static void AddBox(SurfaceTool st, Vector3 center, Vector3 size)
    {
        AddRotatedBox(st, center, size, 0f);
    }

    private static void AddRotatedBox(SurfaceTool st, Vector3 center, Vector3 size, float yawRadians)
    {
        var h = size * 0.5f;
        Vector3[] localVertices =
        {
            // Front (+Z)
            new(-h.X, -h.Y, +h.Z),
            new(+h.X, -h.Y, +h.Z),
            new(+h.X, +h.Y, +h.Z),
            new(-h.X, +h.Y, +h.Z),

            // Back (-Z)
            new(+h.X, -h.Y, -h.Z),
            new(-h.X, -h.Y, -h.Z),
            new(-h.X, +h.Y, -h.Z),
            new(+h.X, +h.Y, -h.Z),

            // Right (+X)
            new(+h.X, -h.Y, +h.Z),
            new(+h.X, -h.Y, -h.Z),
            new(+h.X, +h.Y, -h.Z),
            new(+h.X, +h.Y, +h.Z),

            // Left (-X)
            new(-h.X, -h.Y, -h.Z),
            new(-h.X, -h.Y, +h.Z),
            new(-h.X, +h.Y, +h.Z),
            new(-h.X, +h.Y, -h.Z),

            // Top (+Y)
            new(-h.X, +h.Y, +h.Z),
            new(+h.X, +h.Y, +h.Z),
            new(+h.X, +h.Y, -h.Z),
            new(-h.X, +h.Y, -h.Z),

            // Bottom (-Y)
            new(-h.X, -h.Y, -h.Z),
            new(+h.X, -h.Y, -h.Z),
            new(+h.X, -h.Y, +h.Z),
            new(-h.X, -h.Y, +h.Z),
        };

        Vector3[] localNormals =
        {
            Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down
        };

        var xf = new Transform3D(Basis.Identity.Rotated(Vector3.Up, yawRadians), center);

        for (var f = 0; f < 6; f++)
        {
            var norm = xf.Basis * localNormals[f];
            var i = f * 4;

            st.SetNormal(norm);
            st.SetUV(new Vector2(0, 1));
            st.AddVertex(xf * localVertices[i]);

            st.SetNormal(norm);
            st.SetUV(new Vector2(1, 0));
            st.AddVertex(xf * localVertices[i + 2]);

            st.SetNormal(norm);
            st.SetUV(new Vector2(1, 1));
            st.AddVertex(xf * localVertices[i + 1]);

            st.SetNormal(norm);
            st.SetUV(new Vector2(0, 1));
            st.AddVertex(xf * localVertices[i]);

            st.SetNormal(norm);
            st.SetUV(new Vector2(0, 0));
            st.AddVertex(xf * localVertices[i + 3]);

            st.SetNormal(norm);
            st.SetUV(new Vector2(1, 0));
            st.AddVertex(xf * localVertices[i + 2]);
        }
    }

    private static Mesh BuildDoorMesh(DoorState state)
    {
        // Surface 0: Stone Frame & Top Lintel (seals against walls & 3m ceiling)
        var stFrame = new SurfaceTool();
        stFrame.Begin(Mesh.PrimitiveType.Triangles);

        // Left jamb post
        AddBox(stFrame, new Vector3(-0.85f, 1.15f, 0), new Vector3(0.30f, 2.30f, 0.40f));
        // Right jamb post
        AddBox(stFrame, new Vector3(0.85f, 1.15f, 0), new Vector3(0.30f, 2.30f, 0.40f));
        // Top stone lintel spanning from height 2.30m to 3.00m
        AddBox(stFrame, new Vector3(0, 2.65f, 0), new Vector3(2.00f, 0.70f, 0.45f));
        // Threshold step
        AddBox(stFrame, new Vector3(0, 0.04f, 0), new Vector3(1.40f, 0.08f, 0.40f));

        // Heavy iron hinge brackets on left jamb post
        AddBox(stFrame, new Vector3(-0.72f, 1.75f, 0.04f), new Vector3(0.10f, 0.14f, 0.16f));
        AddBox(stFrame, new Vector3(-0.72f, 0.55f, 0.04f), new Vector3(0.10f, 0.14f, 0.16f));

        stFrame.GenerateNormals();
        stFrame.GenerateTangents();
        var mesh = stFrame.Commit();

        // Surface 1: Wood Door Leaf & Iron Bands
        var stDoor = new SurfaceTool();
        stDoor.Begin(Mesh.PrimitiveType.Triangles);

        if (state == DoorState.Closed)
        {
            // Sturdy wooden door panel fully closing the doorway
            AddBox(stDoor, new Vector3(0, 1.15f, 0), new Vector3(1.40f, 2.20f, 0.12f));
            // Top iron strap
            AddBox(stDoor, new Vector3(0, 1.75f, 0), new Vector3(1.36f, 0.12f, 0.16f));
            // Bottom iron strap
            AddBox(stDoor, new Vector3(0, 0.55f, 0), new Vector3(1.36f, 0.12f, 0.16f));
            // Iron handle ring / latch (front)
            AddBox(stDoor, new Vector3(0.45f, 1.10f, 0.08f), new Vector3(0.10f, 0.16f, 0.06f));
            // Iron handle ring / latch (back)
            AddBox(stDoor, new Vector3(0.45f, 1.10f, -0.08f), new Vector3(0.10f, 0.16f, 0.06f));
        }
        else if (state == DoorState.Open)
        {
            // Swung-open door leaf angled ajar into the room (~70 degrees)
            const float openAngleDeg = 70.0f;
            var openYaw = openAngleDeg * Mathf.Pi / 180.0f;
            const float doorWidth = 1.28f;
            const float doorThick = 0.10f;
            const float doorHeight = 2.18f;

            // Hinge anchor point at left doorpost
            const float hingeX = -0.68f;
            const float hingeZ = 0.0f;
            var halfW = doorWidth * 0.5f;

            var panelCenter = new Vector3(
                hingeX + halfW * Mathf.Cos(openYaw),
                1.15f,
                hingeZ + halfW * Mathf.Sin(openYaw));

            // Angled wooden door panel
            AddRotatedBox(stDoor, panelCenter, new Vector3(doorWidth, doorHeight, doorThick), -openYaw);

            // Top iron reinforcement strap along the angled door
            var topStrapCenter = new Vector3(panelCenter.X, 1.75f, panelCenter.Z);
            AddRotatedBox(stDoor, topStrapCenter, new Vector3(doorWidth * 0.96f, 0.12f, doorThick + 0.04f), -openYaw);

            // Bottom iron reinforcement strap along the angled door
            var btmStrapCenter = new Vector3(panelCenter.X, 0.55f, panelCenter.Z);
            AddRotatedBox(stDoor, btmStrapCenter, new Vector3(doorWidth * 0.96f, 0.12f, doorThick + 0.04f), -openYaw);

            // Iron handle ring near free edge
            const float handleDist = 1.05f;
            var handleCenter = new Vector3(
                hingeX + handleDist * Mathf.Cos(openYaw),
                1.10f,
                hingeZ + handleDist * Mathf.Sin(openYaw));
            AddRotatedBox(stDoor, handleCenter, new Vector3(0.10f, 0.16f, doorThick + 0.05f), -openYaw);
        }
        else if (state == DoorState.Broken)
        {
            // Shattered wooden planks and iron straps lying splintered on the floor
            AddBox(stDoor, new Vector3(-0.35f, 0.06f, 0.15f), new Vector3(0.85f, 0.06f, 0.26f));
            AddBox(stDoor, new Vector3(0.35f, 0.05f, -0.15f), new Vector3(0.75f, 0.05f, 0.22f));
            AddBox(stDoor, new Vector3(0.10f, 0.09f, 0.30f), new Vector3(0.60f, 0.06f, 0.18f));
            AddBox(stDoor, new Vector3(-0.15f, 0.08f, -0.25f), new Vector3(0.50f, 0.05f, 0.20f));

            // Broken wooden door remnant still clinging to upper hinge
            AddRotatedBox(stDoor, new Vector3(-0.62f, 1.70f, 0.08f), new Vector3(0.26f, 0.48f, 0.08f), -0.35f);
        }

        stDoor.GenerateNormals();
        stDoor.GenerateTangents();
        mesh = stDoor.Commit(mesh);

        mesh.SurfaceSetMaterial(0, _doorFrameMaterial);
        mesh.SurfaceSetMaterial(1, _doorWoodMaterial);
        return mesh;
    }

    private static Mesh BuildStairsMesh(bool down)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Stone side curbs
        AddBox(st, new Vector3(-0.85f, 0.40f, 0), new Vector3(0.30f, 0.80f, Cell));
        AddBox(st, new Vector3(0.85f, 0.40f, 0), new Vector3(0.30f, 0.80f, Cell));

        // 5 stone steps descending/ascending
        const int stepCount = 5;
        const float stepWidth = 1.40f;
        const float stepDepth = Cell / stepCount;
        for (var i = 0; i < stepCount; i++)
        {
            var z = -Cell / 2.0f + stepDepth * (i + 0.5f);
            var stepHeight = down
                ? 0.50f - i * 0.10f
                : 0.10f + i * 0.10f;
            var yCenter = stepHeight / 2.0f;
            AddBox(st, new Vector3(0, yCenter, z), new Vector3(stepWidth, stepHeight, stepDepth));
        }

        st.GenerateNormals();
        st.GenerateTangents();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, _stairsMaterial);
        return mesh;
    }

    private static Mesh BuildRubbleMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        AddBox(st, new Vector3(0, 0.25f, 0), new Vector3(1.10f, 0.50f, 0.90f));
        AddBox(st, new Vector3(-0.35f, 0.18f, 0.30f), new Vector3(0.65f, 0.35f, 0.60f));
        AddBox(st, new Vector3(0.40f, 0.20f, -0.25f), new Vector3(0.70f, 0.40f, 0.55f));
        AddBox(st, new Vector3(-0.25f, 0.12f, -0.35f), new Vector3(0.55f, 0.25f, 0.50f));

        st.GenerateNormals();
        st.GenerateTangents();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, _rubbleMaterial);
        return mesh;
    }

    private static Mesh MeshFor(Kind k)
    {
        if (_meshCache.TryGetValue(k, out var cached) && cached != null)
        {
            return cached;
        }

        Mesh mesh = k switch
        {
            Kind.Wall or Kind.Magma or Kind.Quartz or Kind.Store =>
                new BoxMesh { Size = new Vector3(Cell, WallHeight, Cell) },
            Kind.Floor =>
                new BoxMesh { Size = new Vector3(Cell, 0.1f, Cell) },
            Kind.Ceiling =>
                new BoxMesh { Size = new Vector3(Cell, 0.1f, Cell) },
            Kind.DoorClosed =>
                BuildDoorMesh(DoorState.Closed),
            Kind.DoorOpen =>
                BuildDoorMesh(DoorState.Open),
            Kind.DoorBroken =>
                BuildDoorMesh(DoorState.Broken),
            Kind.StairsDown =>
                BuildStairsMesh(down: true),
            Kind.StairsUp =>
                BuildStairsMesh(down: false),
            Kind.Rubble =>
                BuildRubbleMesh(),
            Kind.Lava =>
                new BoxMesh { Size = new Vector3(Cell, 0.12f, Cell) },
            _ => new BoxMesh { Size = new Vector3(Cell, WallHeight, Cell) },
        };

        _meshCache[k] = mesh;
        return mesh;
    }

    private static StandardMaterial3D MaterialFor(Kind k) => k switch
    {
        Kind.Wall => _wallMaterial,
        Kind.Floor => _floorMaterial,
        Kind.Ceiling => _ceilingMaterial,
        Kind.Magma => _magmaMaterial,
        Kind.Quartz => _quartzMaterial,
        Kind.Store => _storeMaterial,
        Kind.Lava => _lavaMaterial,
        // Composite meshes (Doors, Stairs, Rubble) have materials already assigned per surface
        _ => null,
    };

    #endregion

    private static Kind KindOf(int feat) => (Feat)feat switch
    {
        Feat.None => Kind.Skip,
        Feat.Floor or Feat.PassRubble => Kind.Floor,
        Feat.Closed => Kind.DoorClosed,
        Feat.Open => Kind.DoorOpen,
        Feat.Broken => Kind.DoorBroken,
        Feat.Secret => Kind.Wall,
        Feat.Less => Kind.StairsUp,
        Feat.More => Kind.StairsDown,
        Feat.Rubble => Kind.Rubble,
        Feat.Lava => Kind.Lava,
        Feat.Magma or Feat.MagmaK => Kind.Magma,
        Feat.Quartz or Feat.QuartzK => Kind.Quartz,
        >= Feat.StoreGeneral and <= Feat.Home => Kind.Store,
        Feat.Granite or Feat.Perm => Kind.Wall,
        _ => Kind.Wall,
    };

    private static Color BaseColour(Kind k) => k switch
    {
        Kind.Wall => new Color(1.0f, 1.0f, 1.0f),
        Kind.Floor => new Color(1.0f, 1.0f, 1.0f),
        Kind.Ceiling => new Color(0.90f, 0.90f, 0.90f),
        Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken => new Color(1.0f, 1.0f, 1.0f),
        Kind.StairsDown or Kind.StairsUp => new Color(1.0f, 1.0f, 1.0f),
        Kind.Store => new Color(1.0f, 1.0f, 1.0f),
        Kind.Rubble => new Color(1.0f, 1.0f, 1.0f),
        Kind.Lava => new Color(1.0f, 0.45f, 0.12f),
        _ => Colors.White,
    };

    public void OnFrame(JsonElement frame)
    {
        if (frame.GetProperty("map").ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var map = frame.GetProperty("map");
        var player = frame.GetProperty("player");
        var px = player.GetProperty("x").GetInt32();
        var py = player.GetProperty("y").GetInt32();
        var depth = player.GetProperty("depth").GetInt32();

        var depthKey = $"{depth}:{map.GetProperty("w").GetInt32()}x{map.GetProperty("h").GetInt32()}";
        if (depthKey != _levelKey)
        {
            _levelKey = depthKey;
            _outdoors = depth == 0;
            // The town is an open street under sky, not a lightless dungeon.
            _env.BackgroundColor = _outdoors
                ? new Color(0.08f, 0.12f, 0.22f)
                : new Color(0.005f, 0.006f, 0.010f);
            _env.AmbientLightEnergy = _outdoors ? 1.6f : 0.08f;
            _env.AmbientLightColor = _outdoors
                ? new Color(0.50f, 0.54f, 0.66f)
                : new Color(0.18f, 0.20f, 0.26f);
            _env.FogLightColor = _outdoors
                ? new Color(0.08f, 0.12f, 0.22f)
                : new Color(0.005f, 0.006f, 0.010f);
            _env.FogDensity = _outdoors ? 0.004f : 0.024f;
            _env.VolumetricFogDensity = _outdoors ? 0.003f : 0.014f;

            foreach (var m in _activeMonsters.Values) m.RootNode.QueueFree();
            _activeMonsters.Clear();
            foreach (var item in _activeItems.Values) item.QueueFree();
            _activeItems.Clear();

            FaceSomethingOpen(map, px, py);
            _targetPos = new Vector3(px * Cell, EyeHeight, py * Cell);
            _camera.Position = _targetPos;
        }

        Rebuild(map, px, py);

        _targetPos = new Vector3(px * Cell, EyeHeight, py * Cell);
        _torchRadius = Math.Max(1, player.GetProperty("light").GetInt32());
        UpdateStairsHint(map, px, py);
        RebuildEntities(frame);
        ProcessCombatEvents(frame);
    }

    private static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>
    /// Stairs are easy to lose track of in first person, which makes the game
    /// look like it is not generating new levels.
    /// </summary>
    private void UpdateStairsHint(JsonElement map, int px, int py)
    {
        StairsHint = null;
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();
        var rows = map.GetProperty("rows");
        var best = int.MaxValue;
        int bx = 0, by = 0;
        var isDown = true;

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
                if ((Feat)feat != Feat.More && (Feat)feat != Feat.Less)
                {
                    continue;
                }
                var d = Math.Abs(x - px) + Math.Abs(y - py);
                if (d < best)
                {
                    best = d;
                    bx = x;
                    by = y;
                    isDown = (Feat)feat == Feat.More;
                }
            }
        }

        if (best == int.MaxValue)
        {
            StairsHint = "stairs down: not found yet";
            return;
        }
        if (best == 0)
        {
            StairsHint = isDown ? "stairs down: here - press >" : "stairs up: here - press <";
            return;
        }

        var angle = Mathf.Atan2(bx - px, -(by - py));
        var octant = Mathf.PosMod(Mathf.RoundToInt(angle / (Mathf.Pi / 4f)), 8);
        var label = isDown ? "stairs down" : "stairs up";
        StairsHint = $"{label}: {best} {Compass[octant]}";
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

    private static bool IsWallOrVoid(JsonElement map, int x, int y, int w, int h)
    {
        if (x < 0 || x >= w || y < 0 || y >= h)
        {
            return true;
        }
        var feat = FeatAt(map, x, y);
        var k = KindOf(feat);
        return k is Kind.Wall or Kind.Magma or Kind.Quartz or Kind.Store or Kind.Skip;
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

    private void Rebuild(JsonElement map, int px, int py)
    {
        var h = map.GetProperty("h").GetInt32();
        var w = map.GetProperty("w").GetInt32();
        var rows = map.GetProperty("rows");

        foreach (var k in _buckets.Keys)
        {
            _xf[k].Clear();
            _col[k].Clear();
        }

        // Bounding box around player for active sightline (MAX_SIGHT in Angband is 20)
        const int sightRange = 24;
        var minX = _outdoors ? 0 : Math.Max(0, px - sightRange);
        var maxX = _outdoors ? w - 1 : Math.Min(w - 1, px + sightRange);
        var minY = _outdoors ? 0 : Math.Max(0, py - sightRange);
        var maxY = _outdoors ? h - 1 : Math.Min(h - 1, py + sightRange);

        for (var y = minY; y <= maxY; y++)
        {
            var row = rows[y];
            var feats = row.GetProperty("f").GetString() ?? "";
            var flags = row.GetProperty("l").GetString() ?? "";

            for (var x = minX; x <= maxX; x++)
            {
                if (x >= flags.Length || x * 2 + 1 >= feats.Length)
                {
                    break;
                }

                var flag = AngbandColors.HexVal(flags[x]);
                var known = (flag & 0x1) != 0;
                var inView = (flag & 0x2) != 0;

                // In dungeon, only geometry currently in direct line-of-sight is rendered in 3D.
                // Out-of-LOS solid rock and distant unmapped rooms remain black void and do not
                // reveal floating walls through solid earth.
                if (!_outdoors && !inView)
                {
                    continue;
                }
                if (_outdoors && !known && !inView)
                {
                    continue;
                }

                var feat = (AngbandColors.HexVal(feats[x * 2]) << 4) | AngbandColors.HexVal(feats[x * 2 + 1]);
                var kind = KindOf(feat);
                if (kind == Kind.Skip)
                {
                    continue;
                }

                // Determine lighting level: 0=LOS, 1=torch, 2=lit room/feature, 3=dark
                var lighting = (flag >> 2) & 0x3;
                Color shade;
                if (_outdoors || lighting == 2)
                {
                    shade = BaseColour(kind);
                }
                else if (lighting is 0 or 1)
                {
                    shade = BaseColour(kind);
                }
                else
                {
                    // Dark / unlit space in line of sight (recedes smoothly into pitch blackness)
                    shade = BaseColour(kind) * new Color(0.24f, 0.26f, 0.32f);
                }

                if (kind is Kind.Wall or Kind.Magma or Kind.Quartz or Kind.Store)
                {
                    // Solid stone blocks spanning from Y=0 to Y=WallHeight=3.0m
                    var pos = new Vector3(x * Cell, WallHeight / 2.0f, y * Cell);
                    _xf[kind].Add(new Transform3D(Basis.Identity, pos));
                    _col[kind].Add(shade);
                }
                else if (kind == Kind.Floor)
                {
                    var pos = new Vector3(x * Cell, -0.05f, y * Cell);
                    _xf[Kind.Floor].Add(new Transform3D(Basis.Identity, pos));
                    _col[Kind.Floor].Add(shade);
                }
                else if (kind is Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken)
                {
                    // Floor underneath door
                    _xf[Kind.Floor].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, -0.05f, y * Cell)));
                    _col[Kind.Floor].Add(shade);

                    // Orientation: align door frame across the corridor
                    var wallN = IsWallOrVoid(map, x, y - 1, w, h);
                    var wallS = IsWallOrVoid(map, x, y + 1, w, h);
                    var wallW = IsWallOrVoid(map, x - 1, y, w, h);
                    var wallE = IsWallOrVoid(map, x + 1, y, w, h);

                    var doorYaw = 0f;
                    if ((wallN || wallS) && (!wallW && !wallE))
                    {
                        doorYaw = Mathf.Pi / 2f;
                    }

                    var basis = Basis.Identity.Rotated(Vector3.Up, doorYaw);
                    _xf[kind].Add(new Transform3D(basis, new Vector3(x * Cell, 0, y * Cell)));
                    _col[kind].Add(shade);
                }
                else if (kind is Kind.StairsDown or Kind.StairsUp)
                {
                    // Floor underneath stairs
                    _xf[Kind.Floor].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, -0.05f, y * Cell)));
                    _col[Kind.Floor].Add(shade);

                    _xf[kind].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, 0, y * Cell)));
                    _col[kind].Add(shade);
                }
                else if (kind == Kind.Rubble)
                {
                    _xf[Kind.Floor].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, -0.05f, y * Cell)));
                    _col[Kind.Floor].Add(shade);

                    _xf[Kind.Rubble].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, 0, y * Cell)));
                    _col[Kind.Rubble].Add(shade);
                }
                else if (kind == Kind.Lava)
                {
                    _xf[Kind.Lava].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, 0.06f, y * Cell)));
                    _col[Kind.Lava].Add(shade);
                }

                // Ceiling over walkable areas and doorways in the dungeon
                if (!_outdoors && kind is Kind.Floor or Kind.StairsDown or Kind.StairsUp or Kind.Store
                    or Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken or Kind.Rubble)
                {
                    _xf[Kind.Ceiling].Add(new Transform3D(Basis.Identity,
                        new Vector3(x * Cell, WallHeight + 0.05f, y * Cell)));
                    var cc = (lighting == 3)
                        ? BaseColour(Kind.Ceiling) * new Color(0.20f, 0.22f, 0.28f)
                        : BaseColour(Kind.Ceiling);
                    _col[Kind.Ceiling].Add(cc);
                }
            }
        }

        foreach (var (kind, mmi) in _buckets)
        {
            var list = _xf[kind];
            var mm = mmi.Multimesh;
            mm.InstanceCount = list.Count;
            for (var i = 0; i < list.Count; i++)
            {
                mm.SetInstanceTransform(i, list[i]);
                mm.SetInstanceColor(i, _col[kind][i]);
            }
        }

        RebuildTerrainLabels(map, h, w, px, py);
    }

    /// <summary>Stairs and shopfronts are captioned; placed cleanly without duplicates.</summary>
    private void RebuildTerrainLabels(JsonElement map, int h, int w, int px, int py)
    {
        var rows = map.GetProperty("rows");
        var storePositions = new Dictionary<int, List<Vector2>>();
        var stairsList = new List<(Vector3 Pos, string Name)>();

        const int labelRange = 24;
        var minX = _outdoors ? 0 : Math.Max(0, px - labelRange);
        var maxX = _outdoors ? w - 1 : Math.Min(w - 1, px + labelRange);
        var minY = _outdoors ? 0 : Math.Max(0, py - labelRange);
        var maxY = _outdoors ? h - 1 : Math.Min(h - 1, py + labelRange);

        for (var y = minY; y <= maxY; y++)
        {
            var flags = rows[y].GetProperty("l").GetString() ?? "";
            var feats = rows[y].GetProperty("f").GetString() ?? "";
            for (var x = minX; x <= maxX && x < flags.Length && x * 2 + 1 < feats.Length; x++)
            {
                var flag = AngbandColors.HexVal(flags[x]);
                var inView = (flag & 0x2) != 0;
                var known = (flag & 0x1) != 0;
                if (!_outdoors && !inView)
                {
                    continue;
                }
                if (_outdoors && !known && !inView)
                {
                    continue;
                }

                var feat = (AngbandColors.HexVal(feats[x * 2]) << 4) | AngbandColors.HexVal(feats[x * 2 + 1]);
                var kind = KindOf(feat);
                if (kind == Kind.Store)
                {
                    if (!storePositions.TryGetValue(feat, out var list))
                    {
                        list = new List<Vector2>();
                        storePositions[feat] = list;
                    }
                    list.Add(new Vector2(x * Cell, y * Cell));
                }
                else if (kind is Kind.StairsDown or Kind.StairsUp)
                {
                    var name = TerrainName(feat);
                    if (name != null)
                    {
                        stairsList.Add((new Vector3(x * Cell, 1.1f, y * Cell), name));
                    }
                }
            }
        }

        // Check if the label set has changed to avoid redundant node churn
        var sigBuilder = new System.Text.StringBuilder();
        foreach (var (feat, pts) in storePositions)
        {
            sigBuilder.Append(feat).Append(':').Append(pts.Count).Append(';');
        }
        foreach (var (pos, name) in stairsList)
        {
            sigBuilder.Append(name).Append(':').Append(pos.X).Append(',').Append(pos.Z).Append(';');
        }
        var sig = sigBuilder.ToString();
        if (sig == _lastTerrainLabelsSignature)
        {
            return;
        }
        _lastTerrainLabelsSignature = sig;

        foreach (var child in _terrainLabels.GetChildren())
        {
            child.QueueFree();
        }

        // Add exactly 1 label per store cluster at its centroid
        foreach (var (feat, points) in storePositions)
        {
            var name = TerrainName(feat);
            if (name == null || points.Count == 0) continue;

            float avgX = 0, avgY = 0;
            foreach (var pt in points)
            {
                avgX += pt.X;
                avgY += pt.Y;
            }
            avgX /= points.Count;
            avgY /= points.Count;

            _terrainLabels.AddChild(Caption(name,
                new Vector3(avgX, WallHeight + 0.5f, avgY),
                new Color(0.60f, 0.95f, 0.70f), 28));
        }

        foreach (var (pos, name) in stairsList)
        {
            _terrainLabels.AddChild(Caption(name, pos, new Color(0.65f, 0.85f, 1.0f), 28));
        }
    }

    /// <summary>
    /// A floating caption. Crisp outline and readable size.
    /// </summary>
    private static Label3D Caption(string text, Vector3 pos, Color colour, int size = 28)
    {
        return new Label3D
        {
            Text = text,
            Modulate = colour,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 5,
            FontSize = size,
            PixelSize = 0.0035f,
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
        var kind = KindOf(feat);
        return kind is Kind.StairsDown or Kind.StairsUp or Kind.Store ? info.Name : null;
    }

    /// <summary>Instantiate and smoothly update 3D monsters and items with persistent tracking.</summary>
    private void RebuildEntities(JsonElement frame)
    {
        _frameSeq++;
        var playerPos = _targetPos;

        // Process Monsters
        var seenMonsterIds = new HashSet<string>();
        foreach (var m in frame.GetProperty("monsters").EnumerateArray())
        {
            var gx = m.GetProperty("x").GetInt32();
            var gy = m.GetProperty("y").GetInt32();
            var monId = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : $"{gx}_{gy}";
            var targetWorldPos = new Vector3(gx * Cell, 0, gy * Cell);

            seenMonsterIds.Add(monId);

            if (_activeMonsters.TryGetValue(monId, out var entity))
            {
                entity.LastSeenFrame = _frameSeq;
                MonsterModelResolver.UpdateMonsterVisual(entity, m);

                if (entity.GridX != gx || entity.GridY != gy)
                {
                    var moveDir = targetWorldPos - entity.CurrentPos;
                    moveDir.Y = 0;
                    var dist = entity.CurrentPos.DistanceTo(targetWorldPos);

                    if (dist > Cell * 2.5f)
                    {
                        // Teleport / blink (e.g. thief theft, phase door, teleport away)
                        entity.CurrentPos = targetWorldPos;
                        entity.TargetPos = targetWorldPos;
                        entity.RootNode.Position = new Vector3(targetWorldPos.X, entity.BaseY, targetWorldPos.Z);
                        entity.IsMoving = false;
                    }
                    else
                    {
                        // Normal 1-cell movement
                        if (moveDir.LengthSquared() > 0.001f)
                        {
                            entity.TargetYaw = Mathf.Atan2(moveDir.X, moveDir.Z);
                        }
                        entity.TargetPos = targetWorldPos;
                        entity.IsMoving = true;

                        if (entity.AnimPlayer != null && entity.WalkAnim != null)
                        {
                            MonsterModelResolver.PlayWalkAnimation(entity.CharacterNode);
                        }
                    }

                    entity.GridX = gx;
                    entity.GridY = gy;
                }
            }
            else
            {
                // Newly appeared monster
                var newEntity = MonsterModelResolver.CreateMonsterEntity(m, targetWorldPos, playerPos, monId);
                newEntity.LastSeenFrame = _frameSeq;
                _entities.AddChild(newEntity.RootNode);
                _activeMonsters[monId] = newEntity;
            }
        }

        // Cleanup monsters that are no longer visible or dead
        var toRemoveMonsters = new List<string>();
        foreach (var kvp in _activeMonsters)
        {
            if (kvp.Value.LastSeenFrame != _frameSeq)
            {
                kvp.Value.RootNode.QueueFree();
                toRemoveMonsters.Add(kvp.Key);
            }
        }
        foreach (var k in toRemoveMonsters)
        {
            _activeMonsters.Remove(k);
        }

        // Process Items
        var seenItems = new HashSet<string>();
        foreach (var o in frame.GetProperty("objects").EnumerateArray())
        {
            var gx = o.GetProperty("x").GetInt32();
            var gy = o.GetProperty("y").GetInt32();
            var glyphStr = o.TryGetProperty("glyph", out var gProp) ? gProp.GetString() ?? "?" : "?";
            var key = $"{gx}_{gy}_{glyphStr}";
            seenItems.Add(key);

            if (!_activeItems.ContainsKey(key))
            {
                var pos = new Vector3(gx * Cell, 0, gy * Cell);
                var itemNode = ItemModelResolver.CreateItemNode(o, pos);
                _entities.AddChild(itemNode);
                _activeItems[key] = itemNode;
            }
        }

        // Cleanup picked up / vanished items
        var toRemoveItems = new List<string>();
        foreach (var kvp in _activeItems)
        {
            if (!seenItems.Contains(kvp.Key))
            {
                kvp.Value.QueueFree();
                toRemoveItems.Add(kvp.Key);
            }
        }
        foreach (var k in toRemoveItems)
        {
            _activeItems.Remove(k);
        }
    }

    /// <summary>Turn in place. Free: Angband has no facing, so this costs no game turn.</summary>
    public void Turn(int delta)
    {
        _facing = ((_facing + delta) % 4 + 4) % 4;
        _targetYaw = -Mathf.Pi / 2f * _facing;
    }

    /// <summary>
    /// Angband movement key for a local direction (numpad 1-9) relative to the current facing.
    /// 8 = Forward, 2 = Backward, 4 = Strafe Left, 6 = Strafe Right,
    /// 7 = Forward-Left, 9 = Forward-Right, 1 = Backward-Left, 3 = Backward-Right, 5 = Stay.
    /// </summary>
    public string RelativeMoveKey(int numpadDir)
    {
        if (numpadDir == 5)
        {
            return "5";
        }
        int localIndex = numpadDir switch
        {
            8 => 0,
            9 => 1,
            6 => 2,
            3 => 3,
            2 => 4,
            1 => 5,
            4 => 6,
            7 => 7,
            _ => -1
        };
        if (localIndex < 0)
        {
            return null;
        }
        string[] dirKeys = { "8", "9", "6", "3", "2", "1", "4", "7" };
        var worldIndex = (localIndex + _facing * 2) % 8;
        return dirKeys[worldIndex];
    }

    /// <summary>Angband movement key for a direction relative to the current facing.</summary>
    public string MoveKey(bool forward)
    {
        return RelativeMoveKey(forward ? 8 : 2);
    }

    /// <summary>Add kinetic screen trauma/shake on taking damage or heavy impacts.</summary>
    public void AddTrauma(float amount)
    {
        _trauma = Mathf.Clamp(_trauma + amount, 0f, 1.0f);
    }

    /// <summary>Spawn 3D billboarded floating combat text at a world position.</summary>
    public void SpawnFloatingText(string text, Vector3 worldPos, Color color, float scale = 1.0f)
    {
        var label = new Label3D
        {
            Text = text,
            Modulate = color,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 6,
            FontSize = (int)(28 * scale),
            PixelSize = 0.0040f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = true,
            RenderPriority = 15,
            Position = worldPos,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AddChild(label);
        _activeFloaters.Add(new CombatFloater
        {
            Node = label,
            StartPos = worldPos,
            Elapsed = 0f,
            Lifetime = 0.70f,
            BaseColor = color,
        });
    }

    private void ProcessCombatEvents(JsonElement frame)
    {
        if (!frame.TryGetProperty("messages", out var msgs) || msgs.ValueKind != JsonValueKind.Array || msgs.GetArrayLength() == 0)
        {
            return;
        }

        var lastMsg = msgs[msgs.GetArrayLength() - 1];
        var text = lastMsg.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(text) || text == _lastProcessedMessage)
        {
            return;
        }
        _lastProcessedMessage = text;

        var lower = text.ToLowerInvariant();
        var forward = _camera != null ? -_camera.Transform.Basis.Z : Vector3.Forward;
        var right = _camera != null ? _camera.Transform.Basis.X : Vector3.Right;
        var spawnInFront = _targetPos + forward * 1.6f + new Vector3(0, 0.2f, 0);

        // Player taking damage
        if (lower.Contains("hits you") || lower.Contains("bites you") || lower.Contains("touches you") ||
            lower.Contains("claws you") || lower.Contains("crushes you") || lower.Contains("burns you") ||
            lower.Contains("shoots you") || lower.Contains("stings you") || lower.Contains("casts a spell"))
        {
            AddTrauma(0.40f);
            var hitPos = _targetPos + forward * 0.8f + (GD.Randf() > 0.5f ? right * 0.3f : -right * 0.3f);
            SpawnFloatingText("OUCH!", hitPos, new Color(1.0f, 0.30f, 0.30f), 1.1f);
        }
        // Critical / heavy hit
        else if (lower.Contains("critical hit") || lower.Contains("great force") || lower.Contains("superb"))
        {
            AddTrauma(0.15f);
            SpawnFloatingText("CRITICAL!", spawnInFront + new Vector3(0, 0.3f, 0), new Color(1.0f, 0.90f, 0.25f), 1.35f);
        }
        // Player hitting monster
        else if (lower.Contains("you hit") || lower.Contains("you strike") || lower.Contains("you slash") ||
                 lower.Contains("you shoot") || lower.Contains("you smite") || lower.Contains("you crush"))
        {
            SpawnFloatingText("HIT", spawnInFront, new Color(1.0f, 0.75f, 0.20f), 1.0f);
        }
        // Misses
        else if (lower.Contains("you miss") || lower.Contains("misses you"))
        {
            SpawnFloatingText("MISS", spawnInFront, new Color(0.70f, 0.72f, 0.78f), 0.9f);
        }
        // Monster slain / destroyed
        else if (lower.Contains("you have slain") || lower.Contains("is destroyed") || lower.Contains("dies."))
        {
            SpawnFloatingText("SLAIN!", spawnInFront + new Vector3(0, 0.25f, 0), new Color(0.95f, 0.40f, 0.95f), 1.25f);
        }
    }

    public override void _Process(double delta)
    {
        if (_camera == null)
        {
            return;
        }

        var t = (float)Math.Min(1.0, delta / StepSeconds);
        _camera.Position = _camera.Position.Lerp(_targetPos, t);

        // First-person head bob when moving
        var distRemaining = _camera.Position.DistanceTo(_targetPos);
        var bob = 0f;
        if (distRemaining > 0.02f)
        {
            var progress = 1.0f - Mathf.Clamp(distRemaining / Cell, 0f, 1f);
            bob = Mathf.Sin(progress * Mathf.Pi) * 0.08f;
        }
        _camera.Position = new Vector3(_camera.Position.X, EyeHeight + bob, _camera.Position.Z);

        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);

        // Subtle camera roll on turn
        var yawDelta = Mathf.Wrap(_targetYaw - _yaw, -Mathf.Pi, Mathf.Pi);
        var roll = Mathf.Clamp(-yawDelta * 0.08f, -0.04f, 0.04f);
        _camera.Rotation = new Vector3(0, _yaw, roll);

        // Update floating combat text
        for (var i = _activeFloaters.Count - 1; i >= 0; i--)
        {
            var fText = _activeFloaters[i];
            fText.Elapsed += (float)delta;
            var progress = Mathf.Clamp(fText.Elapsed / fText.Lifetime, 0f, 1f);

            fText.Node.Position = fText.StartPos + new Vector3(0, progress * 0.75f, 0);
            var alpha = 1.0f - progress * progress;
            fText.Node.Modulate = new Color(fText.BaseColor.R, fText.BaseColor.G, fText.BaseColor.B, alpha);

            if (fText.Elapsed >= fText.Lifetime)
            {
                fText.Node.QueueFree();
                _activeFloaters.RemoveAt(i);
            }
        }

        // Camera trauma / kinetic impact shake
        if (_trauma > 0.001f)
        {
            _trauma = Mathf.Max(0f, _trauma - (float)delta * 1.6f);
            var shake = _trauma * _trauma;
            var shakePitch = (GD.Randf() * 2f - 1f) * 0.035f * shake;
            var shakeYaw = (GD.Randf() * 2f - 1f) * 0.035f * shake;
            var shakeRoll = (GD.Randf() * 2f - 1f) * 0.045f * shake;
            _camera.Rotation += new Vector3(shakePitch, shakeYaw, shakeRoll);
        }

        _flicker += delta;
        var f = 1.0f + 0.05f * Mathf.Sin((float)_flicker * 11f) + 0.03f * Mathf.Sin((float)_flicker * 23f);
        _torch.OmniRange = (_torchRadius + 2.5f) * Cell + 2.0f;
        _torch.LightEnergy = 2.2f * f;

        // Process smooth monster movement, facing, and hover/animations
        var moveT = (float)Math.Min(1.0, delta / 0.16);
        var rotT = (float)Math.Min(1.0, delta * 8.0);

        foreach (var monster in _activeMonsters.Values)
        {
            // Smooth positional interpolation
            monster.CurrentPos = monster.CurrentPos.Lerp(monster.TargetPos, moveT);
            var distLeft = monster.CurrentPos.DistanceTo(monster.TargetPos);

            // Floating / bobbing effect
            var floatY = monster.BaseY;
            if (monster.IsFloating)
            {
                floatY += Mathf.Sin((float)_flicker * 3.0f + monster.FloatOffset) * 0.12f;
            }

            monster.RootNode.Position = new Vector3(monster.CurrentPos.X, floatY, monster.CurrentPos.Z);

            if (monster.IsMoving && distLeft < 0.04f)
            {
                monster.IsMoving = false;
                monster.CurrentPos = monster.TargetPos;
                if (monster.AnimPlayer != null)
                {
                    MonsterModelResolver.PlayIdleAnimation(monster.CharacterNode);
                }

                // When stopped and adjacent to player, face the player
                var toPlayer = _targetPos - monster.CurrentPos;
                toPlayer.Y = 0;
                if (toPlayer.LengthSquared() < (Cell * 2.5f) * (Cell * 2.5f) && toPlayer.LengthSquared() > 0.001f)
                {
                    monster.TargetYaw = Mathf.Atan2(toPlayer.X, toPlayer.Z);
                }
            }

            // Smooth rotation towards target yaw (or spin for elementals/vortices)
            if (monster.IsSpinning)
            {
                monster.CurrentYaw += (float)(delta * 2.5);
            }
            else
            {
                monster.CurrentYaw = Mathf.LerpAngle(monster.CurrentYaw, monster.TargetYaw, rotT);
            }
            monster.CharacterNode.Rotation = new Vector3(0, monster.CurrentYaw, 0);

            // Update lively procedural animations for creature tokens (rodents, insects, centipedes, bats, slimes, etc.)
            MonsterModelResolver.UpdateProceduralAnimation(monster, delta, (float)_flicker);
        }
    }
}
