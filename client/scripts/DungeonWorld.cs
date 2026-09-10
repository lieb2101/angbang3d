using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Authoritative First-Person 3D World Renderer for grid-based dungeon crawlers.
/// Renders procedural PBR stone masonry, orientation-aware archways/doors, mineral veins,
/// volumetric atmospheric lighting, smooth entity interpolation, and dynamic first-person camera kinematics.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architecture & Engine Abstraction:</b><br/>
/// <c>DungeonWorld</c> translates a 2D discrete cell grid $(x, y)$ into a continuous 3D world $(X, Y, Z)$.
/// While built for Angband 4.2.6, this class is completely agnostic of game logic and can be used to render
/// ANY turn-based or real-time tile engine (e.g. NetHack, DCSS, Moria, ADOM, Brogue, Rogue, Sil, or custom RPGs).
/// </para>
/// <para>
/// <b>Coordinate System Transformation:</b><br/>
/// <list type="bullet">
///   <item><description>$X = \text{gridX} \times \text{Cell}$ (East/West)</description></item>
///   <item><description>$Z = \text{gridY} \times \text{Cell}$ (South/North)</description></item>
///   <item><description>$Y = \text{CurrentEyeHeight} + \text{bob}$ (Vertical elevation)</description></item>
/// </list>
/// </para>
/// <para>
/// <b>MultiMesh Batching Pipeline:</b><br/>
/// To maintain 144+ FPS on integrated and discrete GPUs with thousands of dungeon blocks:
/// <list type="bullet">
///   <item><description>All static level geometry is grouped into <see cref="Kind"/> enum buckets.</description></item>
///   <item><description>Each <see cref="Kind"/> is backed by a single <see cref="MultiMeshInstance3D"/> draw call.</description></item>
///   <item><description>Instance transforms and lighting vertex colors (<c>UseColors = true</c>) are streamed in 1 pass per frame.</description></item>
/// </list>
/// </para>
/// <para>
/// <b>Visibility & Fog of War:</b><br/>
/// In dungeons (depth &gt; 0), only tiles currently in direct Line of Sight (<c>in_view</c>) are rendered in 3D.
/// Solid earth beyond walls remains empty black void, preventing see-through wall glitches and eliminating overdraw.
/// In town (depth 0), the full mapped town street is visible under open daylight sky.
/// </para>
/// </remarks>
public partial class DungeonWorld : Node3D
{
    /// <summary>Grid cell size in 3D world meters (2.0 meters per dungeon square).</summary>
    public const float Cell = 2.0f;

    /// <summary>Ceiling and wall height in 3D world meters (3.0 meters).</summary>
    private const float WallHeight = 3.0f;

    /// <summary>Baseline eye height for a standard 72" (6 ft) human character (1.62 meters).</summary>
    private const float BaseEyeHeight = 1.62f;

    /// <summary>Duration in seconds for camera step interpolation between discrete turns.</summary>
    private const float StepSeconds = 0.14f;

    /// <summary>
    /// Effective character eye height in meters, calculated dynamically from player race &amp; height stats.
    /// Standard human (72 inches / 6 ft) maps to 1.62m eye height.
    /// Halflings/Gnomes ~0.85-1.05m; Dwarves ~1.15-1.30m; Half-Trolls/High-Elves ~1.80-2.35m.
    /// </summary>
    public float CurrentEyeHeight { get; private set; } = BaseEyeHeight;

    /// <summary>
    /// Relative height ratio compared to baseline human (1.0 = standard 72" human).
    /// Used for camera FOV, footstep pitch modulation, and viewmodel scaling.
    /// </summary>
    public float CurrentHeightRatio { get; private set; } = 1.0f;

    /// <summary>
    /// Feature indices from engine/src/list-terrain.h.
    /// For non-Angband engines, map external tile IDs to equivalent <see cref="Feat"/> values in <see cref="KindOf(int)"/>.
    /// </summary>
    private enum Feat
    {
        None = 0, Floor = 1, Closed = 2, Open = 3, Broken = 4,
        Less = 5, More = 6,
        StoreGeneral = 7, StoreArmor = 8, StoreWeapon = 9, StoreBook = 10,
        StoreAlchemy = 11, StoreMagic = 12, StoreBlack = 13, Home = 14,
        Secret = 15, Rubble = 16,
        Magma = 17, Quartz = 18, MagmaK = 19, QuartzK = 20,
        Granite = 21, Perm = 22, Lava = 23, PassRubble = 24,
    }

    /// <summary>
    /// Visual rendering buckets. Each bucket corresponds to a single batched MultiMesh draw call.
    /// </summary>
    private enum Kind
    {
        Skip,
        Floor,
        Ceiling,
        Wall,
        Magma,
        Quartz,
        Store1,
        Store2,
        Store3,
        Store4,
        Store5,
        Store6,
        Store7,
        Store8,
        DoorClosed,
        DoorOpen,
        DoorBroken,
        StairsDown,
        StairsUp,
        Rubble,
        Lava
    }

    private static bool IsStoreKind(Kind k) => k is >= Kind.Store1 and <= Kind.Store8;
    private static int StoreNumOf(Kind k) => k is >= Kind.Store1 and <= Kind.Store8 ? (int)(k - Kind.Store1 + 1) : 0;

    private enum DoorState { Closed, Open, Broken }

    private Camera3D _camera;
    private ViewModel _viewModel;
    private OmniLight3D _torch;
    private Godot.Environment _env;
    private Node3D _entities;
    private Node3D _terrainLabels;
    private Node3D _clutterRoot;
    private readonly Dictionary<Kind, MultiMeshInstance3D> _buckets = new();
    private readonly Dictionary<Kind, List<Transform3D>> _xf = new();
    private readonly Dictionary<Kind, List<Color>> _col = new();
    private readonly Dictionary<string, MonsterEntity> _activeMonsters = new();
    private readonly Dictionary<string, Node3D> _activeItems = new();
    private int _frameSeq;

    private int _lastPlayerHp = -1;
    private int _lastPlayerMaxHp = -1;
    private bool _stepPlayedThisMove;

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
    /// <summary>Camera compass facing: 0 = north, 1 = east, 2 = south, 3 = west.</summary>
    private int _facing;
    private string _levelKey = "";
    private bool _outdoors;
    private double _flicker;
    private int _torchRadius = 1;

    /// <summary>Current compass facing direction (0=N, 1=E, 2=S, 3=W).</summary>
    public int Facing => _facing;

    /// <summary>Bearing and distance to the nearest known down staircase (e.g. "stairs down: 12 SE").</summary>
    public string StairsHint { get; private set; }

    /// <summary>
    /// Depth-based atmospheric biome configuration profile (Priority 1).
    /// Defines sky/background colors, ambient lighting, volumetric fog density/albedo,
    /// tonemap exposure, material tints, and ambient particulate emissions.
    /// </summary>
    public class BiomeProfile
    {
        public string Name { get; set; } = "Upper Crypts";
        public Color BackgroundColor { get; set; } = new Color(0.005f, 0.006f, 0.010f);
        public Color AmbientLightColor { get; set; } = new Color(0.18f, 0.20f, 0.26f);
        public float AmbientLightEnergy { get; set; } = 0.08f;
        public Color FogLightColor { get; set; } = new Color(0.005f, 0.006f, 0.010f);
        public float FogDensity { get; set; } = 0.024f;
        public float VolumetricFogDensity { get; set; } = 0.014f;
        public Color VolumetricFogAlbedo { get; set; } = new Color(0.14f, 0.16f, 0.22f);
        public Color VolumetricFogEmission { get; set; } = Colors.Black;
        public float TonemapExposure { get; set; } = 1.12f;
        public Color TorchLightColor { get; set; } = new Color(1.0f, 0.84f, 0.60f);
        public float TorchLightEnergy { get; set; } = 2.4f;
        public Color WallColor { get; set; } = new Color(0.78f, 0.78f, 0.80f);
        public Color FloorColor { get; set; } = new Color(0.70f, 0.70f, 0.72f);
        public Color CeilingColor { get; set; } = new Color(0.55f, 0.55f, 0.58f);
        public float FloorRoughness { get; set; } = 0.84f;
        public Color ParticleColor { get; set; } = new Color(0.85f, 0.82f, 0.75f, 0.35f);
        public int ParticleAmount { get; set; } = 22;
        public float ParticleScaleMin { get; set; } = 0.015f;
        public float ParticleScaleMax { get; set; } = 0.035f;
        public float ParticleSpeedMin { get; set; } = 0.08f;
        public float ParticleSpeedMax { get; set; } = 0.25f;
        public Vector3 ParticleGravity { get; set; } = new Vector3(0, -0.04f, 0);

        public static BiomeProfile GetForDepth(int depth)
        {
            if (depth <= 0)
            {
                // Zone 0: Town / Overworld (Depth 0)
                return new BiomeProfile
                {
                    Name = "Town & Overworld",
                    BackgroundColor = new Color(0.08f, 0.12f, 0.22f),
                    AmbientLightColor = new Color(0.50f, 0.54f, 0.66f),
                    AmbientLightEnergy = 1.6f,
                    FogLightColor = new Color(0.08f, 0.12f, 0.22f),
                    FogDensity = 0.004f,
                    VolumetricFogDensity = 0.003f,
                    VolumetricFogAlbedo = new Color(0.12f, 0.15f, 0.22f),
                    VolumetricFogEmission = Colors.Black,
                    TonemapExposure = 1.05f,
                    TorchLightColor = new Color(1.0f, 0.86f, 0.64f),
                    TorchLightEnergy = 2.2f,
                    WallColor = new Color(0.88f, 0.88f, 0.88f),
                    FloorColor = new Color(0.84f, 0.82f, 0.80f),
                    CeilingColor = new Color(0.70f, 0.70f, 0.75f),
                    FloorRoughness = 0.84f,
                    ParticleColor = new Color(0.75f, 0.82f, 0.95f, 0.35f),
                    ParticleAmount = 16,
                    ParticleScaleMin = 0.015f,
                    ParticleScaleMax = 0.030f,
                    ParticleSpeedMin = 0.1f,
                    ParticleSpeedMax = 0.3f,
                    ParticleGravity = new Vector3(0, -0.05f, 0),
                };
            }
            if (depth <= 15)
            {
                // Zone 1: Upper Crypts (Levels 1–15)
                return new BiomeProfile
                {
                    Name = "Upper Crypts",
                    BackgroundColor = new Color(0.015f, 0.016f, 0.022f),
                    AmbientLightColor = new Color(0.32f, 0.34f, 0.42f),
                    AmbientLightEnergy = 0.28f,
                    FogLightColor = new Color(0.015f, 0.016f, 0.022f),
                    FogDensity = 0.012f,
                    VolumetricFogDensity = 0.008f,
                    VolumetricFogAlbedo = new Color(0.18f, 0.20f, 0.28f),
                    VolumetricFogEmission = Colors.Black,
                    TonemapExposure = 1.15f,
                    TorchLightColor = new Color(1.0f, 0.86f, 0.65f),
                    TorchLightEnergy = 2.8f,
                    WallColor = new Color(0.82f, 0.82f, 0.84f),
                    FloorColor = new Color(0.74f, 0.74f, 0.76f),
                    CeilingColor = new Color(0.60f, 0.60f, 0.64f),
                    FloorRoughness = 0.82f,
                    ParticleColor = new Color(0.85f, 0.82f, 0.75f, 0.35f),
                    ParticleAmount = 24,
                    ParticleScaleMin = 0.015f,
                    ParticleScaleMax = 0.035f,
                    ParticleSpeedMin = 0.08f,
                    ParticleSpeedMax = 0.25f,
                    ParticleGravity = new Vector3(0, -0.04f, 0),
                };
            }
            if (depth <= 35)
            {
                // Zone 2: Overgrown Catacombs (Levels 16–35)
                return new BiomeProfile
                {
                    Name = "Overgrown Catacombs",
                    BackgroundColor = new Color(0.012f, 0.020f, 0.014f),
                    AmbientLightColor = new Color(0.26f, 0.38f, 0.28f),
                    AmbientLightEnergy = 0.30f,
                    FogLightColor = new Color(0.012f, 0.022f, 0.015f),
                    FogDensity = 0.014f,
                    VolumetricFogDensity = 0.010f,
                    VolumetricFogAlbedo = new Color(0.16f, 0.32f, 0.20f),
                    VolumetricFogEmission = new Color(0.015f, 0.04f, 0.02f),
                    TonemapExposure = 1.18f,
                    TorchLightColor = new Color(1.0f, 0.88f, 0.60f),
                    TorchLightEnergy = 2.8f,
                    WallColor = new Color(0.72f, 0.86f, 0.70f),
                    FloorColor = new Color(0.65f, 0.78f, 0.64f),
                    CeilingColor = new Color(0.52f, 0.65f, 0.50f),
                    FloorRoughness = 0.65f, // damp mossy floor
                    ParticleColor = new Color(0.45f, 0.95f, 0.45f, 0.70f),
                    ParticleAmount = 32,
                    ParticleScaleMin = 0.02f,
                    ParticleScaleMax = 0.045f,
                    ParticleSpeedMin = 0.1f,
                    ParticleSpeedMax = 0.35f,
                    ParticleGravity = new Vector3(0, 0.08f, 0), // rising luminous spores
                };
            }
            if (depth <= 60)
            {
                // Zone 3: Crystal Caverns (Levels 36–60)
                return new BiomeProfile
                {
                    Name = "Crystal Caverns",
                    BackgroundColor = new Color(0.010f, 0.016f, 0.028f),
                    AmbientLightColor = new Color(0.26f, 0.36f, 0.52f),
                    AmbientLightEnergy = 0.32f,
                    FogLightColor = new Color(0.010f, 0.018f, 0.030f),
                    FogDensity = 0.014f,
                    VolumetricFogDensity = 0.010f,
                    VolumetricFogAlbedo = new Color(0.18f, 0.30f, 0.48f),
                    VolumetricFogEmission = new Color(0.015f, 0.035f, 0.06f),
                    TonemapExposure = 1.20f,
                    TorchLightColor = new Color(0.98f, 0.86f, 0.70f),
                    TorchLightEnergy = 2.8f,
                    WallColor = new Color(0.68f, 0.78f, 0.96f),
                    FloorColor = new Color(0.60f, 0.70f, 0.90f),
                    CeilingColor = new Color(0.48f, 0.56f, 0.76f),
                    FloorRoughness = 0.55f, // damp reflective flagstones
                    ParticleColor = new Color(0.45f, 0.85f, 1.0f, 0.75f),
                    ParticleAmount = 28,
                    ParticleScaleMin = 0.02f,
                    ParticleScaleMax = 0.04f,
                    ParticleSpeedMin = 0.15f,
                    ParticleSpeedMax = 0.45f,
                    ParticleGravity = new Vector3(0, 0.02f, 0), // floating crystal shimmer
                };
            }
            if (depth <= 85)
            {
                // Zone 4: Magma Underworld (Levels 61–85)
                return new BiomeProfile
                {
                    Name = "Magma Underworld",
                    BackgroundColor = new Color(0.025f, 0.010f, 0.006f),
                    AmbientLightColor = new Color(0.48f, 0.25f, 0.14f),
                    AmbientLightEnergy = 0.36f,
                    FogLightColor = new Color(0.025f, 0.010f, 0.006f),
                    FogDensity = 0.016f,
                    VolumetricFogDensity = 0.012f,
                    VolumetricFogAlbedo = new Color(0.45f, 0.22f, 0.12f),
                    VolumetricFogEmission = new Color(0.06f, 0.025f, 0.010f),
                    TonemapExposure = 1.22f,
                    TorchLightColor = new Color(1.0f, 0.82f, 0.55f),
                    TorchLightEnergy = 3.0f,
                    WallColor = new Color(0.90f, 0.72f, 0.65f),
                    FloorColor = new Color(0.78f, 0.62f, 0.55f),
                    CeilingColor = new Color(0.62f, 0.48f, 0.40f),
                    FloorRoughness = 0.70f,
                    ParticleColor = new Color(1.0f, 0.55f, 0.12f, 0.90f),
                    ParticleAmount = 36,
                    ParticleScaleMin = 0.025f,
                    ParticleScaleMax = 0.055f,
                    ParticleSpeedMin = 0.5f,
                    ParticleSpeedMax = 1.4f,
                    ParticleGravity = new Vector3(0, 0.4f, 0), // rising hot embers
                };
            }

            // Zone 5: Permarock / Abyssal Throne (Levels 86–100+)
            return new BiomeProfile
            {
                Name = "Abyssal Throne",
                BackgroundColor = new Color(0.016f, 0.006f, 0.022f),
                AmbientLightColor = new Color(0.38f, 0.20f, 0.44f),
                AmbientLightEnergy = 0.30f,
                FogLightColor = new Color(0.016f, 0.006f, 0.022f),
                FogDensity = 0.016f,
                VolumetricFogDensity = 0.012f,
                VolumetricFogAlbedo = new Color(0.26f, 0.10f, 0.32f),
                VolumetricFogEmission = new Color(0.03f, 0.008f, 0.035f),
                TonemapExposure = 1.22f,
                TorchLightColor = new Color(0.95f, 0.85f, 0.75f),
                TorchLightEnergy = 2.4f,
                WallColor = new Color(0.75f, 0.65f, 0.85f),
                FloorColor = new Color(0.68f, 0.58f, 0.78f),
                CeilingColor = new Color(0.50f, 0.40f, 0.60f),
                FloorRoughness = 0.60f,
                ParticleColor = new Color(0.85f, 0.30f, 0.95f, 0.80f),
                ParticleAmount = 32,
                ParticleScaleMin = 0.025f,
                ParticleScaleMax = 0.050f,
                ParticleSpeedMin = 0.2f,
                ParticleSpeedMax = 0.6f,
                ParticleGravity = new Vector3(0, 0.15f, 0), // void purple energy wisps
            };
        }
    }

    private class ActiveProjectile
    {
        public Node3D Node { get; set; }
        public Vector3 StartPos { get; set; }
        public Vector3 TargetPos { get; set; }
        public float Elapsed { get; set; }
        public float Duration { get; set; }
        public Color Color { get; set; }
        public string EffectType { get; set; }
    }

    private BiomeProfile _currentBiome;
    private BiomeProfile _targetBiome;
    private CpuParticles3D _biomeParticles;
    private readonly List<ActiveProjectile> _activeProjectiles = new();

    // Materials
    private static StandardMaterial3D _wallMaterial;
    private static StandardMaterial3D _floorMaterial;
    private static StandardMaterial3D _ceilingMaterial;
    private static StandardMaterial3D _magmaMaterial;
    private static StandardMaterial3D _quartzMaterial;
    private static StandardMaterial3D _storeMaterial;
    private static StandardMaterial3D _doorFrameMaterial;
    private static StandardMaterial3D _doorWoodMaterial;
    private static readonly StandardMaterial3D[] _shopDoorMaterials = new StandardMaterial3D[8];
    private static StandardMaterial3D _stairsMaterial;
    private static StandardMaterial3D _rubbleMaterial;
    private static StandardMaterial3D _lavaMaterial;

    private static readonly Dictionary<Kind, Mesh> _meshCache = new();

    public override void _Ready()
    {
        InitMaterials();

        _currentBiome = BiomeProfile.GetForDepth(0);
        _targetBiome = _currentBiome;

        _env = BuildEnvironment(_currentBiome);
        AddChild(new WorldEnvironment { Environment = _env });
        _camera = new Camera3D { Current = true, Fov = 84, Near = 0.05f };
        var audioListener = new AudioListener3D();
        _camera.AddChild(audioListener);
        audioListener.MakeCurrent();
        AddChild(_camera);

        _viewModel = new ViewModel { Name = "ViewModel" };
        _camera.AddChild(_viewModel);

        // Realistic warm torch with smooth wide-angle illumination and clean non-distracting shadows
        _torch = new OmniLight3D
        {
            LightColor = _currentBiome.TorchLightColor,
            LightEnergy = _currentBiome.TorchLightEnergy,
            OmniRange = 12.5f,
            OmniAttenuation = 0.70f,
            ShadowEnabled = false,
            Position = new Vector3(-0.25f, -0.05f, -0.28f),
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
            Position = new Vector3(-0.25f, 0.1f, -0.2f),
        };
        _camera.AddChild(embers);

        // Depth-based atmospheric particulate emitter (dust motes, spores, crystal shimmer, embers, void wisps)
        _biomeParticles = new CpuParticles3D
        {
            Name = "BiomeAtmosphericParticles",
            Amount = _currentBiome.ParticleAmount,
            Lifetime = 3.5f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(8.0f, 2.5f, 8.0f),
            Direction = Vector3.Up,
            Spread = 45f,
            InitialVelocityMin = _currentBiome.ParticleSpeedMin,
            InitialVelocityMax = _currentBiome.ParticleSpeedMax,
            Gravity = _currentBiome.ParticleGravity,
            ScaleAmountMin = _currentBiome.ParticleScaleMin,
            ScaleAmountMax = _currentBiome.ParticleScaleMax,
            Color = _currentBiome.ParticleColor,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = Colors.White,
            },
        };
        AddChild(_biomeParticles);

        _entities = new Node3D { Name = "Entities" };
        AddChild(_entities);

        _terrainLabels = new Node3D { Name = "TerrainLabels" };
        AddChild(_terrainLabels);

        _clutterRoot = new Node3D { Name = "ClutterRoot" };
        AddChild(_clutterRoot);

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

    private static Godot.Environment BuildEnvironment(BiomeProfile initialBiome = null)
    {
        var b = initialBiome ?? BiomeProfile.GetForDepth(0);
        return new()
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = b.BackgroundColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = b.AmbientLightColor,
            AmbientLightEnergy = b.AmbientLightEnergy,
            FogEnabled = true,
            FogLightColor = b.FogLightColor,
            FogDensity = b.FogDensity,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = b.VolumetricFogDensity,
            VolumetricFogAlbedo = b.VolumetricFogAlbedo,
            VolumetricFogEmission = b.VolumetricFogEmission,
            VolumetricFogLength = 28.0f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 1.6f,
            SsaoPower = 1.4f,
            SsaoDetail = 0.5f,
            SsrEnabled = true,
            SsrMaxSteps = 64,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsrDepthTolerance = 0.2f,
            GlowEnabled = true,
            GlowIntensity = 0.50f,
            GlowBloom = 0.08f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = b.TonemapExposure,
        };
    }

    #region Procedural Textures & Materials

    private static float Hash(int x, int y, int seed = 0)
    {
        var n = x + y * 57 + seed * 131;
        n = (n << 13) ^ n;
        return (1.0f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0f) * 0.5f + 0.5f;
    }

    private static float SmoothNoise(float x, float y, int seed = 0)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var fx = x - x0;
        var fy = y - y0;

        var sx = fx * fx * (3.0f - 2.0f * fx);
        var sy = fy * fy * (3.0f - 2.0f * fy);

        var n00 = Hash(x0, y0, seed);
        var n10 = Hash(x1, y0, seed);
        var n01 = Hash(x0, y1, seed);
        var n11 = Hash(x1, y1, seed);

        var nx0 = Mathf.Lerp(n00, n10, sx);
        var nx1 = Mathf.Lerp(n01, n11, sx);

        return Mathf.Lerp(nx0, nx1, sy);
    }

    private static float FractalNoise(float x, float y, int octaves = 3, int seed = 0)
    {
        var value = 0f;
        var amplitude = 0.5f;
        var frequency = 1.0f;
        var maxVal = 0f;

        for (var i = 0; i < octaves; i++)
        {
            value += SmoothNoise(x * frequency, y * frequency, seed + i * 37) * amplitude;
            maxVal += amplitude;
            frequency *= 2.0f;
            amplitude *= 0.5f;
        }

        return value / maxVal;
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
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 128;
            var yInRow = y % 128;
            var distY = Math.Min(yInRow, 127 - yInRow);
            var xOff = (row % 2 == 1) ? 128 : 0;
            var xInRow = (x + size - xOff) % 256;
            var distX = Math.Min(xInRow, 255 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            // Natural dark mortar joints with realistic deep grit
            if (mortarDist <= 4)
            {
                var mn = SmoothNoise(x * 0.15f, y * 0.15f, 101) * 0.05f - 0.025f;
                return new Color(0.11f + mn, 0.11f + mn, 0.12f + mn);
            }

            // Stone block tonal variation
            var blockId = row * 4 + ((x + size - xOff) / 256);
            var blockHue = Hash(blockId, 0, 77);
            var rBase = 0.33f + (blockHue - 0.5f) * 0.06f;
            var gBase = 0.33f + (blockHue - 0.5f) * 0.05f;
            var bBase = 0.35f + (0.5f - blockHue) * 0.05f;

            // Soft bevel from mortar to block face
            var bevel = Mathf.Clamp((mortarDist - 4) / 12.0f, 0.55f, 1.0f);

            // Natural multi-scale stone grain & mineral texture
            var grain = (FractalNoise(x * 0.05f, y * 0.05f, 4, 1) - 0.5f) * 0.14f;
            var microGrain = (SmoothNoise(x * 0.25f, y * 0.25f, 88) - 0.5f) * 0.04f;

            var r = Mathf.Clamp(rBase * bevel + grain + microGrain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + grain + microGrain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + grain + microGrain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateStoneWallNormal()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / 128;
            var yInRow = y % 128;
            var distY = Math.Min(yInRow, 127 - yInRow);
            var xOff = (row % 2 == 1) ? 128 : 0;
            var xInRow = (x + size - xOff) % 256;
            var distX = Math.Min(xInRow, 255 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 4)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (xInRow < 128) ? (1.0f - xInRow / 20.0f) : -(1.0f - (255 - xInRow) / 20.0f);
            var dy = (yInRow < 64) ? (1.0f - yInRow / 20.0f) : -(1.0f - (127 - yInRow) / 20.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var microBump = (FractalNoise(x * 0.06f, y * 0.06f, 3, 50) - 0.5f) * 0.35f;
            var norm = new Vector3((dx + microBump) * 0.55f, -(dy + microBump) * 0.55f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateFloorTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            // 2x2 large paving flagstones (256x256 each) with staggered sub-tiles
            var tileX = x / 256;
            var tileY = y / 256;
            var inTileX = x % 256;
            var inTileY = y % 256;
            var distX = Math.Min(inTileX, 255 - inTileX);
            var distY = Math.Min(inTileY, 255 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 4)
            {
                var mn = SmoothNoise(x * 0.15f, y * 0.15f, 202) * 0.04f - 0.02f;
                return new Color(0.09f + mn, 0.09f + mn, 0.10f + mn);
            }

            var tileId = tileY * 2 + tileX;
            var tileHue = Hash(tileId, 0, 99);
            var rBase = 0.24f + (tileHue - 0.5f) * 0.05f;
            var gBase = 0.25f + (tileHue - 0.5f) * 0.04f;
            var bBase = 0.27f + (0.5f - tileHue) * 0.04f;

            var bevel = Mathf.Clamp((mortarDist - 4) / 16.0f, 0.65f, 1.0f);
            var grain = (FractalNoise(x * 0.05f, y * 0.05f, 4, 3) - 0.5f) * 0.12f;
            var microGrain = (SmoothNoise(x * 0.22f, y * 0.22f, 102) - 0.5f) * 0.03f;

            var r = Mathf.Clamp(rBase * bevel + grain + microGrain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + grain + microGrain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + grain + microGrain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateFloorNormal()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var inTileX = x % 256;
            var inTileY = y % 256;
            var distX = Math.Min(inTileX, 255 - inTileX);
            var distY = Math.Min(inTileY, 255 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 4)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (inTileX < 128) ? (1.0f - inTileX / 24.0f) : -(1.0f - (255 - inTileX) / 24.0f);
            var dy = (inTileY < 128) ? (1.0f - inTileY / 24.0f) : -(1.0f - (255 - inTileY) / 24.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var microBump = (FractalNoise(x * 0.06f, y * 0.06f, 3, 60) - 0.5f) * 0.30f;
            var norm = new Vector3((dx + microBump) * 0.50f, -(dy + microBump) * 0.50f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateCeilingTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var rBase = 0.15f;
            var gBase = 0.16f;
            var bBase = 0.18f;
            var grain = (FractalNoise(x * 0.06f, y * 0.06f, 3, 5) - 0.5f) * 0.08f;

            var r = Mathf.Clamp(rBase + grain, 0f, 1f);
            var g = Mathf.Clamp(gBase + grain, 0f, 1f);
            var b = Mathf.Clamp(bBase + grain, 0f, 1f);
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

            // Natural branching volcanic fissures
            var vein = (FractalNoise(x * 0.04f, y * 0.04f, 3, 77) - 0.5f) * 2.0f;
            var isVein = Math.Abs(vein) < 0.28f || mortarDist <= 2;

            if (isVein)
            {
                var heat = 1.0f - Mathf.Clamp(Math.Abs(vein) / 0.28f, 0f, 1f);
                var r = 0.90f + heat * 0.10f;
                var g = 0.28f + heat * 0.44f;
                var b = 0.03f + heat * 0.15f;
                return new Color(r, g, b);
            }

            // Dark igneous basalt rock
            var rockGrain = (FractalNoise(x * 0.08f, y * 0.08f, 2, 88) - 0.5f) * 0.06f;
            var br = 0.18f + rockGrain;
            var bg = 0.17f + rockGrain;
            var bb = 0.18f + rockGrain;
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

            var vein = (FractalNoise(x * 0.04f, y * 0.04f, 3, 77) - 0.5f) * 2.0f;
            var isVein = Math.Abs(vein) < 0.28f || mortarDist <= 2;

            if (isVein)
            {
                var heat = 1.0f - Mathf.Clamp(Math.Abs(vein) / 0.28f, 0f, 1f);
                return new Color(0.95f, 0.32f + heat * 0.40f, 0.04f + heat * 0.14f);
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

            // Natural crystalline quartz seam
            var crystalVein = (FractalNoise((x * 1.2f - y * 0.8f) * 0.05f, (x * 0.5f + y) * 0.05f, 3, 44) - 0.5f) * 2.0f;
            if (Math.Abs(crystalVein) < 0.22f)
            {
                var glint = SmoothNoise(x * 0.3f, y * 0.3f, 12) * 0.12f;
                return new Color(0.68f + glint, 0.72f + glint, 0.76f + glint);
            }

            if (mortarDist <= 2)
            {
                return new Color(0.14f, 0.14f, 0.15f);
            }

            var grain = (FractalNoise(x * 0.08f, y * 0.08f, 2, 9) - 0.5f) * 0.08f;
            return new Color(0.30f + grain, 0.31f + grain, 0.33f + grain);
        });
    }

    private static readonly byte[][] DigitPatterns =
    {
        // 1
        new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
        // 2
        new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
        // 3
        new byte[] { 0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110 },
        // 4
        new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
        // 5
        new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 },
        // 6
        new byte[] { 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
        // 7
        new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b00100, 0b01000, 0b01000 },
        // 8
        new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
    };

    private static Color StoreColor(int storeNum) => storeNum switch
    {
        1 => new Color(1.0f, 0.88f, 0.35f), // General Store: Warm Gold
        2 => new Color(0.70f, 0.90f, 1.0f),  // Armoury: Steel Cyan
        3 => new Color(1.0f, 0.60f, 0.35f),  // Weaponsmith: Fiery Orange
        4 => new Color(0.55f, 0.95f, 0.65f), // Bookseller: Jade Green
        5 => new Color(0.40f, 1.0f, 0.85f),  // Alchemy: Mystic Emerald
        6 => new Color(0.85f, 0.65f, 1.0f),  // Magic: Arcane Violet
        7 => new Color(1.0f, 0.45f, 0.65f),  // Black Market: Crimson Rose
        8 => new Color(1.0f, 0.95f, 0.70f),  // Home: Cozy Amber
        _ => new Color(1.0f, 0.90f, 0.50f)
    };

    private static ImageTexture CreateShopDoorTexture(int shopNum, Color heraldicColor)
    {
        const int size = 256;
        var pattern = (shopNum >= 1 && shopNum <= 8) ? DigitPatterns[shopNum - 1] : null;

        return CreateTexture(size, size, (x, y) =>
        {
            // Vertical rich dark oak planks (5 planks across 256px)
            var plankIdx = x / 51;
            var inPlankX = x % 51;
            var seamDist = Math.Min(inPlankX, 50 - inPlankX);

            // Horizontal forged-iron reinforcement straps at y ~ 44..62 and y ~ 194..212
            var isIronStrap = (y >= 44 && y <= 62) || (y >= 194 && y <= 212);
            if (isIronStrap)
            {
                var rivetX = inPlankX - 25;
                var rivetY = (y < 100) ? (y - 53) : (y - 203);
                var isRivet = rivetX * rivetX + rivetY * rivetY <= 16;
                if (isRivet)
                {
                    return new Color(0.38f, 0.38f, 0.40f);
                }
                var ironGrain = (SmoothNoise(x * 0.2f, y * 0.2f, 31) - 0.5f) * 0.04f;
                return new Color(0.18f + ironGrain, 0.18f + ironGrain, 0.20f + ironGrain);
            }

            // Central Emblazoned Heraldic Shield / Plaque at (128, 128)
            var dx = x - 128;
            var dy = y - 128;
            var absDx = Math.Abs(dx);

            // Heraldic Shield shape: rectangle on top (dy <= 0), curved taper on bottom (dy > 0)
            var inShield = (dy >= -46 && dy <= 0 && absDx <= 44) ||
                           (dy > 0 && dy <= 50 && absDx <= 44 - (dy * dy) / 58f);

            if (inShield)
            {
                var isShieldRim = (dy <= -42 || absDx >= 40 || (dy > 0 && absDx >= 40 - (dy * dy) / 58f));
                if (isShieldRim)
                {
                    // Gilded brass / bronze rim with metallic bevel
                    var bevel = (dx < 0 || dy < -38) ? 0.15f : -0.10f;
                    return new Color(0.85f + bevel, 0.70f + bevel, 0.25f + bevel);
                }

                // Decorative corner rivets on the shield rim
                var isCornerRivet = ((absDx - 34) * (absDx - 34) + (dy + 34) * (dy + 34) <= 9);
                if (isCornerRivet)
                {
                    return new Color(0.98f, 0.88f, 0.40f);
                }

                // Check for the emblazoned digit inside the shield
                if (pattern != null)
                {
                    // Center the 5x7 digit inside the shield (width: 5 * 8 = 40px, height: 7 * 8 = 56px)
                    // Grid origin: x = 108, y = 100
                    var gx = (x - 108) / 8;
                    var gy = (y - 100) / 8;
                    var cellX = (x - 108) % 8;
                    var cellY = (y - 100) % 8;

                    if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7)
                    {
                        var isBit = (pattern[gy] & (1 << (4 - gx))) != 0;
                        if (isBit)
                        {
                            // Emblazoned Gilded Numeral with 3D specular highlight
                            var specular = (cellX <= 2 && cellY <= 2) ? 0.18f : (cellX >= 6 || cellY >= 6) ? -0.12f : 0.05f;
                            return new Color(0.98f + specular, 0.86f + specular, 0.28f + specular);
                        }

                        // Check 1px shadow/emboss around digit
                        var shadowNear = false;
                        for (var sy = -1; sy <= 1 && !shadowNear; sy++)
                        {
                            for (var sx = -1; sx <= 1; sx++)
                            {
                                var ngx = gx + sx;
                                var ngy = gy + sy;
                                if (ngx >= 0 && ngx < 5 && ngy >= 0 && ngy < 7)
                                {
                                    if ((pattern[ngy] & (1 << (4 - ngx))) != 0)
                                    {
                                        shadowNear = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (shadowNear)
                        {
                            return new Color(0.10f, 0.08f, 0.04f);
                        }
                    }
                }

                // Shield background inlay: Rich burnished heraldic enamel
                var heraldicGrain = (SmoothNoise(x * 0.1f, y * 0.1f, 77 + shopNum) - 0.5f) * 0.05f;
                var bgR = Mathf.Clamp(heraldicColor.R * 0.40f + 0.12f + heraldicGrain, 0f, 1f);
                var bgG = Mathf.Clamp(heraldicColor.G * 0.40f + 0.10f + heraldicGrain, 0f, 1f);
                var bgB = Mathf.Clamp(heraldicColor.B * 0.40f + 0.08f + heraldicGrain, 0f, 1f);
                return new Color(bgR, bgG, bgB);
            }

            // Dark plank seam
            if (seamDist <= 1)
            {
                return new Color(0.10f, 0.07f, 0.04f);
            }

            // Natural dark oak wood grain for door panel
            var woodGrain = Mathf.Sin(y * 0.25f + SmoothNoise(x * 0.1f, y * 0.05f, 55) * 3.0f) * 0.025f + (SmoothNoise(x * 0.15f, y * 0.15f, 66) - 0.5f) * 0.03f;
            var plankHue = Hash(plankIdx, 0, 88 + shopNum);
            var r = Mathf.Clamp(0.26f + (plankHue - 0.5f) * 0.03f + woodGrain, 0f, 1f);
            var g = Mathf.Clamp(0.17f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.8f, 0f, 1f);
            var b = Mathf.Clamp(0.10f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.5f, 0f, 1f);
            return new Color(r, g, b);
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
            var isIronStrap = (y >= 50 && y <= 68) || (y >= 186 && y <= 204);
            if (isIronStrap)
            {
                // Round iron rivets every 51px
                var rivetX = inPlankX - 25;
                var rivetY = (y < 100) ? (y - 59) : (y - 195);
                var isRivet = rivetX * rivetX + rivetY * rivetY <= 16;
                if (isRivet)
                {
                    return new Color(0.32f, 0.32f, 0.35f);
                }
                var ironGrain = (SmoothNoise(x * 0.2f, y * 0.2f, 31) - 0.5f) * 0.04f;
                return new Color(0.17f + ironGrain, 0.17f + ironGrain, 0.19f + ironGrain);
            }

            // Dark plank seam
            if (seamDist <= 1)
            {
                return new Color(0.10f, 0.07f, 0.04f);
            }

            // Natural oak wood grain
            var woodGrain = Mathf.Sin(y * 0.25f + SmoothNoise(x * 0.1f, y * 0.05f, 55) * 3.0f) * 0.025f + (SmoothNoise(x * 0.15f, y * 0.15f, 66) - 0.5f) * 0.03f;
            var plankHue = Hash(plankIdx, 0, 88);
            var r = Mathf.Clamp(0.28f + (plankHue - 0.5f) * 0.04f + woodGrain, 0f, 1f);
            var g = Mathf.Clamp(0.19f + (plankHue - 0.5f) * 0.03f + woodGrain * 0.8f, 0f, 1f);
            var b = Mathf.Clamp(0.12f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.5f, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateStoreTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            // Medieval half-timbered shop facade with dark oak beams and plaster infill
            var isBorderBeam = x < 18 || x > 237 || y < 18 || y > 237;
            var isCrossBeam = Math.Abs(x - 128) < 9 || Math.Abs(y - 128) < 9;
            var isDiagonal = Math.Abs((x - y) % 128) < 7 || Math.Abs((x + y) % 128) < 7;

            if (isBorderBeam || isCrossBeam || isDiagonal)
            {
                var woodGrain = (SmoothNoise(x * 0.15f, y * 0.15f, 12) - 0.5f) * 0.04f;
                return new Color(0.22f + woodGrain, 0.15f + woodGrain * 0.7f, 0.10f + woodGrain * 0.5f);
            }

            // Warm aged stucco / plaster wall infill
            var plasterGrain = (FractalNoise(x * 0.08f, y * 0.08f, 2, 22) - 0.5f) * 0.06f;
            return new Color(0.56f + plasterGrain, 0.53f + plasterGrain, 0.47f + plasterGrain);
        });
    }

    private static ImageTexture CreateLavaTexture()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var pattern = (FractalNoise(x * 0.03f, y * 0.03f, 3, 301) - 0.5f) * 2.0f;

            if (pattern > 0.12f)
            {
                // Molten magma core
                var heat = Mathf.Clamp((pattern - 0.12f) / 0.88f, 0f, 1f);
                var r = 0.95f + heat * 0.05f;
                var g = 0.35f + heat * 0.35f;
                var b = 0.02f + heat * 0.08f;
                return new Color(r, g, b);
            }
            if (pattern > -0.20f)
            {
                // Burning crust transition
                var heat = Mathf.Clamp((pattern + 0.20f) / 0.32f, 0f, 1f);
                var r = 0.70f + heat * 0.25f;
                var g = 0.12f + heat * 0.23f;
                var b = 0.01f + heat * 0.01f;
                return new Color(r, g, b);
            }

            // Cooling obsidian crust
            var crustGrain = (SmoothNoise(x * 0.1f, y * 0.1f, 303) - 0.5f) * 0.04f;
            return new Color(0.14f + crustGrain, 0.08f + crustGrain * 0.5f, 0.06f + crustGrain * 0.3f);
        });
    }

    private static ImageTexture CreateLavaEmission()
    {
        const int size = 256;
        return CreateTexture(size, size, (x, y) =>
        {
            var pattern = (FractalNoise(x * 0.03f, y * 0.03f, 3, 301) - 0.5f) * 2.0f;

            if (pattern > 0.12f)
            {
                var heat = Mathf.Clamp((pattern - 0.12f) / 0.88f, 0f, 1f);
                return new Color(0.95f, 0.35f + heat * 0.32f, 0.03f + heat * 0.08f);
            }
            if (pattern > -0.20f)
            {
                var heat = Mathf.Clamp((pattern + 0.20f) / 0.32f, 0f, 1f);
                return new Color(0.55f + heat * 0.35f, 0.08f + heat * 0.25f, 0.01f);
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
            NormalScale = 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.82f,
            Metallic = 0.04f,
            HeightmapEnabled = true,
            HeightmapDeepParallax = true,
            HeightmapMinLayers = 8,
            HeightmapMaxLayers = 32,
            HeightmapScale = 0.04f,
            HeightmapTexture = wallNormal,
        };

        _floorMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = floorTex,
            NormalEnabled = true,
            NormalTexture = floorNormal,
            NormalScale = 0.90f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.76f,
            Metallic = 0.05f,
            HeightmapEnabled = true,
            HeightmapDeepParallax = true,
            HeightmapMinLayers = 8,
            HeightmapMaxLayers = 32,
            HeightmapScale = 0.035f,
            HeightmapTexture = floorNormal,
        };

        _ceilingMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = ceilingTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.55f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.95f,
            Metallic = 0.0f,
        };

        _magmaMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = magmaTex,
            EmissionEnabled = true,
            EmissionTexture = magmaEmission,
            Emission = new Color(1.0f, 0.40f, 0.06f),
            EmissionEnergyMultiplier = 1.15f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.72f,
            Metallic = 0.04f,
        };

        _quartzMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = quartzTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.70f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.55f,
            Metallic = 0.08f,
        };

        _storeMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = storeTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.60f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.85f,
            Metallic = 0.02f,
        };

        _doorFrameMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.70f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.88f,
            Metallic = 0.02f,
        };

        _doorWoodMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = woodDoorTex,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.78f,
            Metallic = 0.10f,
        };

        for (var i = 0; i < 8; i++)
        {
            var shopNum = i + 1;
            var shopTex = CreateShopDoorTexture(shopNum, StoreColor(shopNum));
            _shopDoorMaterials[i] = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = shopTex,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Roughness = 0.70f,
                Metallic = 0.12f,
            };
        }

        _stairsMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.65f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.85f,
            Metallic = 0.02f,
        };

        _rubbleMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.75f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.92f,
            Metallic = 0.02f,
        };

        _lavaMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = lavaTex,
            EmissionEnabled = true,
            EmissionTexture = lavaEmission,
            Emission = new Color(1.0f, 0.40f, 0.06f),
            EmissionEnergyMultiplier = 1.15f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.45f,
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

    private static Mesh BuildShopDoorMesh(int shopNum)
    {
        // Surface 0: Stone & Timber Doorway Frame with Keystone Lintel & Solid Masonry Structure
        var stFrame = new SurfaceTool();
        stFrame.Begin(Mesh.PrimitiveType.Triangles);

        // Left stone pillar / wall pier (spans full 2.0m tile depth)
        AddBox(stFrame, new Vector3(-0.85f, 1.15f, 0.0f), new Vector3(0.30f, 2.30f, 2.00f));
        // Right stone pillar / wall pier (spans full 2.0m tile depth)
        AddBox(stFrame, new Vector3(0.85f, 1.15f, 0.0f), new Vector3(0.30f, 2.30f, 2.00f));
        // Top stone lintel & transom spanning full width (2.0m) and full depth (2.0m) up to 3m ceiling
        AddBox(stFrame, new Vector3(0, 2.65f, 0), new Vector3(2.00f, 0.70f, 2.00f));
        // Back solid wall sealing building interior
        AddBox(stFrame, new Vector3(0, 1.15f, -0.55f), new Vector3(1.40f, 2.30f, 0.90f));
        // Front threshold step
        AddBox(stFrame, new Vector3(0, 0.04f, 0.45f), new Vector3(1.40f, 0.08f, 0.90f));

        // Front decorative doorpost pilasters
        AddBox(stFrame, new Vector3(-0.72f, 1.15f, 0.15f), new Vector3(0.12f, 2.30f, 0.30f));
        AddBox(stFrame, new Vector3(0.72f, 1.15f, 0.15f), new Vector3(0.12f, 2.30f, 0.30f));

        // Heavy iron hinge brackets on left jamb post
        AddBox(stFrame, new Vector3(-0.64f, 1.75f, 0.12f), new Vector3(0.08f, 0.14f, 0.14f));
        AddBox(stFrame, new Vector3(-0.64f, 0.55f, 0.12f), new Vector3(0.08f, 0.14f, 0.14f));

        stFrame.GenerateNormals();
        stFrame.GenerateTangents();
        var mesh = stFrame.Commit();

        // Surface 1: Wood Door Leaf with Iron Bands & Emblazoned Shop Numeral Plaque
        var stDoor = new SurfaceTool();
        stDoor.Begin(Mesh.PrimitiveType.Triangles);

        // Sturdy wooden door panel recessed slightly into the facade
        AddBox(stDoor, new Vector3(0, 1.15f, 0.05f), new Vector3(1.40f, 2.20f, 0.12f));
        // Top iron strap
        AddBox(stDoor, new Vector3(0, 1.75f, 0.07f), new Vector3(1.36f, 0.12f, 0.14f));
        // Bottom iron strap
        AddBox(stDoor, new Vector3(0, 0.55f, 0.07f), new Vector3(1.36f, 0.12f, 0.14f));
        // Iron handle ring / latch
        AddBox(stDoor, new Vector3(0.45f, 1.10f, 0.12f), new Vector3(0.10f, 0.16f, 0.06f));

        // Raised 3D Bronze Heraldic Number Plaque mounted at chest level on front of door
        AddBox(stDoor, new Vector3(0, 1.25f, 0.12f), new Vector3(0.50f, 0.55f, 0.04f));

        stDoor.GenerateNormals();
        stDoor.GenerateTangents();
        mesh = stDoor.Commit(mesh);

        var matIdx = Math.Clamp(shopNum - 1, 0, 7);
        mesh.SurfaceSetMaterial(0, _doorFrameMaterial);
        mesh.SurfaceSetMaterial(1, _shopDoorMaterials[matIdx]);
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
            Kind.Wall or Kind.Magma or Kind.Quartz =>
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
            _ when IsStoreKind(k) =>
                BuildShopDoorMesh(StoreNumOf(k)),
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
        Kind.Lava => _lavaMaterial,
        // Composite meshes (Doors, Shop Doors, Stairs, Rubble) have materials already assigned per surface
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
        Feat.StoreGeneral => Kind.Store1,
        Feat.StoreArmor => Kind.Store2,
        Feat.StoreWeapon => Kind.Store3,
        Feat.StoreBook => Kind.Store4,
        Feat.StoreAlchemy => Kind.Store5,
        Feat.StoreMagic => Kind.Store6,
        Feat.StoreBlack => Kind.Store7,
        Feat.Home => Kind.Store8,
        Feat.Granite or Feat.Perm => Kind.Wall,
        _ => Kind.Wall,
    };

    private Color BaseColour(Kind k)
    {
        var biome = _targetBiome ?? _currentBiome;
        if (biome == null) return Colors.White;
        return k switch
        {
            Kind.Wall => biome.WallColor,
            Kind.Floor => biome.FloorColor,
            Kind.Ceiling => biome.CeilingColor,
            Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken => biome.WallColor,
            Kind.StairsDown or Kind.StairsUp => biome.FloorColor,
            Kind.Rubble => biome.WallColor,
            Kind.Lava => new Color(1.0f, 0.45f, 0.12f),
            _ when IsStoreKind(k) => Colors.White,
            _ => Colors.White,
        };
    }

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
            var isInitial = string.IsNullOrEmpty(_levelKey);
            _levelKey = depthKey;
            _outdoors = depth == 0;

            // Transition to new depth biome profile
            _targetBiome = BiomeProfile.GetForDepth(depth);
            _currentBiome ??= _targetBiome;
            if (isInitial)
            {
                _currentBiome = _targetBiome;
                _env.BackgroundColor = _targetBiome.BackgroundColor;
                _env.AmbientLightColor = _targetBiome.AmbientLightColor;
                _env.AmbientLightEnergy = _targetBiome.AmbientLightEnergy;
                _env.FogLightColor = _targetBiome.FogLightColor;
                _env.FogDensity = _targetBiome.FogDensity;
                _env.VolumetricFogDensity = _targetBiome.VolumetricFogDensity;
                _env.VolumetricFogAlbedo = _targetBiome.VolumetricFogAlbedo;
                _env.VolumetricFogEmission = _targetBiome.VolumetricFogEmission;
                _env.TonemapExposure = _targetBiome.TonemapExposure;
            }

            if (_floorMaterial != null)
            {
                _floorMaterial.Roughness = _targetBiome.FloorRoughness;
            }

            if (_biomeParticles != null)
            {
                _biomeParticles.Amount = _targetBiome.ParticleAmount;
                _biomeParticles.Color = _targetBiome.ParticleColor;
                _biomeParticles.InitialVelocityMin = _targetBiome.ParticleSpeedMin;
                _biomeParticles.InitialVelocityMax = _targetBiome.ParticleSpeedMax;
                _biomeParticles.Gravity = _targetBiome.ParticleGravity;
                _biomeParticles.ScaleAmountMin = _targetBiome.ParticleScaleMin;
                _biomeParticles.ScaleAmountMax = _targetBiome.ParticleScaleMax;
                _biomeParticles.Restart();
            }

            foreach (var m in _activeMonsters.Values) m.RootNode.QueueFree();
            _activeMonsters.Clear();
            foreach (var item in _activeItems.Values) item.QueueFree();
            _activeItems.Clear();

            if (_clutterRoot != null)
            {
                foreach (var c in _clutterRoot.GetChildren()) c.QueueFree();
            }

            if (!isInitial)
            {
                AudioManager.Play(depth > 0 ? SoundEffect.StairsDown : SoundEffect.StairsUp);
                AudioManager.Play(SoundEffect.LevelEnter, volumeDb: -3f);
            }

            // Calculate character height ratio and eye height
            CalculateCharacterHeight(player);

            FaceSomethingOpen(map, px, py);
            _targetPos = new Vector3(px * Cell, CurrentEyeHeight, py * Cell);
            _camera.Position = _targetPos;
        }

        // Calculate character height ratio and eye height for active level
        CalculateCharacterHeight(player);

        Rebuild(map, px, py);

        _targetPos = new Vector3(px * Cell, CurrentEyeHeight, py * Cell);
        _torchRadius = Math.Max(1, player.GetProperty("light").GetInt32());
        UpdateStairsHint(map, px, py);

        // Player HP delta tracking for floating damage/healing & feedback
        if (player.TryGetProperty("hp", out var hpProp))
        {
            var curHp = hpProp.GetInt32();
            var curMaxHp = player.TryGetProperty("hp_max", out var mhpProp) ? mhpProp.GetInt32() : curHp;

            if (_lastPlayerHp >= 0 && curHp < _lastPlayerHp)
            {
                var dmg = _lastPlayerHp - curHp;
                AddTrauma(Mathf.Clamp((dmg / (float)Math.Max(1, curMaxHp)) * 1.5f + 0.30f, 0.28f, 0.95f));
                _viewModel?.TriggerHurt();
                AudioManager.Play(SoundEffect.PlayerHurt);

                var forward = _camera != null ? -_camera.Transform.Basis.Z : Vector3.Forward;
                var fPos = _targetPos + forward * 0.9f + new Vector3((GD.Randf() - 0.5f) * 0.3f, 0.15f, 0);
                SpawnFloatingText($"-{dmg}", fPos, new Color(1.0f, 0.25f, 0.25f), 1.25f);
                SpawnHitSparks(fPos, -forward, new Color(0.95f, 0.15f, 0.15f), 18);
            }
            else if (_lastPlayerHp >= 0 && curHp > _lastPlayerHp)
            {
                var heal = curHp - _lastPlayerHp;
                var forward = _camera != null ? -_camera.Transform.Basis.Z : Vector3.Forward;
                var fPos = _targetPos + forward * 1.0f + new Vector3(0, 0.2f, 0);
                SpawnFloatingText($"+{heal}", fPos, new Color(0.20f, 1.0f, 0.40f), 1.15f);
                AudioManager.Play(SoundEffect.ItemPickup, 1.2f);
            }

            _lastPlayerHp = curHp;
            _lastPlayerMaxHp = curMaxHp;
        }

        RebuildEntities(frame);
        _viewModel?.UpdateEquipment(player, depth, CurrentHeightRatio);
        ProcessCombatEvents(frame);

        // Populate atmospheric corridor wall sconces, room clutter, and banners
        var mapH = map.GetProperty("h").GetInt32();
        var mapW = map.GetProperty("w").GetInt32();
        DungeonClutterResolver.PopulateClutter(_clutterRoot, map, mapH, mapW, px, py, _outdoors, IsWallOrVoid, IsWalkable, FeatAt);
    }

    /// <summary>
    /// Computes eye height and scale ratio from player race and height stats.
    /// Standard baseline human is 72 inches (height ratio 1.0, eye height 1.62m).
    /// Halflings/Kobolds/Yeeks ~32-38" -> ratio ~0.44-0.53, eye height ~0.72-0.86m.
    /// Gnomes ~40-44" -> ratio ~0.55-0.61, eye height ~0.90-0.99m.
    /// Dwarves ~48-54" -> ratio ~0.67-0.75, eye height ~1.08-1.22m.
    /// Elves/Dunadan ~68-80" -> ratio ~0.94-1.11, eye height ~1.53-1.80m.
    /// Half-Trolls/High-Elves ~84-104" -> ratio ~1.16-1.44, eye height ~1.89-2.34m.
    /// </summary>
    private void CalculateCharacterHeight(JsonElement player)
    {
        var raceStr = player.TryGetProperty("race", out var rProp) ? rProp.GetString() ?? "" : "";
        var lowerRace = raceStr.ToLowerInvariant();
        int ht = player.TryGetProperty("ht", out var hProp) ? hProp.GetInt32() : 0;

        float targetEyeHeight;
        if (ht > 0)
        {
            // Direct life-scale mapping: baseline 72 inches -> 1.62m eye height
            targetEyeHeight = (ht / 72.0f) * BaseEyeHeight;
        }
        else
        {
            // Fallback inferred from race if ht not sent
            if (lowerRace.Contains("halfling") || lowerRace.Contains("hobbit") || lowerRace.Contains("kobold") || lowerRace.Contains("yeek"))
            {
                targetEyeHeight = 0.80f;
            }
            else if (lowerRace.Contains("gnome"))
            {
                targetEyeHeight = 0.96f;
            }
            else if (lowerRace.Contains("dwarf"))
            {
                targetEyeHeight = 1.16f;
            }
            else if (lowerRace.Contains("high-elf") || lowerRace.Contains("dunadan") || lowerRace.Contains("dunedain"))
            {
                targetEyeHeight = 1.78f;
            }
            else if (lowerRace.Contains("elf") || lowerRace.Contains("half-elf"))
            {
                targetEyeHeight = 1.60f;
            }
            else if (lowerRace.Contains("half-orc") || lowerRace.Contains("orc"))
            {
                targetEyeHeight = 1.60f;
            }
            else if (lowerRace.Contains("half-ogre"))
            {
                targetEyeHeight = 1.95f;
            }
            else if (lowerRace.Contains("half-troll") || lowerRace.Contains("troll") || lowerRace.Contains("golem") || lowerRace.Contains("titan"))
            {
                targetEyeHeight = 2.18f;
            }
            else
            {
                targetEyeHeight = BaseEyeHeight;
            }
        }

        // Clamp eye height to realistic dungeon crawling bounds [0.70m (small halfling/yeek), 2.25m (half-troll with ceiling clearance)]
        CurrentEyeHeight = Mathf.Clamp(targetEyeHeight, 0.70f, 2.25f);
        CurrentHeightRatio = CurrentEyeHeight / BaseEyeHeight;

        if (_camera != null)
        {
            // Natural perspective: subtle FOV adjustment (86° for short races, 84° baseline human, 82° for tall races)
            // without fish-eye distortion or tunnel vision.
            var targetFov = Mathf.Lerp(86.0f, 82.0f, Mathf.InverseLerp(0.45f, 1.40f, CurrentHeightRatio));
            _camera.Fov = targetFov;
        }
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
        return k is Kind.Wall or Kind.Magma or Kind.Quartz or Kind.Skip || IsStoreKind(k);
    }

    private static bool IsSolidWall(Kind k) =>
        k is Kind.Wall or Kind.Magma or Kind.Quartz;

    private static bool IsWalkableOrPortal(Kind k) =>
        k is Kind.Floor or Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken
            or Kind.StairsDown or Kind.StairsUp or Kind.Lava or Kind.Rubble;

    private static float DetermineDoorOrientation(JsonElement map, int x, int y, int w, int h)
    {
        var kN = KindOf(FeatAt(map, x, y - 1));
        var kS = KindOf(FeatAt(map, x, y + 1));
        var kW = KindOf(FeatAt(map, x - 1, y));
        var kE = KindOf(FeatAt(map, x + 1, y));

        var wallN = IsSolidWall(kN) || y <= 0;
        var wallS = IsSolidWall(kS) || y >= h - 1;
        var wallW = IsSolidWall(kW) || x <= 0;
        var wallE = IsSolidWall(kE) || x >= w - 1;

        var walkN = IsWalkableOrPortal(kN);
        var walkS = IsWalkableOrPortal(kS);
        var walkW = IsWalkableOrPortal(kW);
        var walkE = IsWalkableOrPortal(kE);

        // Score spanning along X (flanked by West & East walls, passage flows North-South)
        var scoreX = (wallW ? 3 : 0) + (wallE ? 3 : 0) + (walkN ? 1 : 0) + (walkS ? 1 : 0);

        // Score spanning along Z (flanked by North & South walls, passage flows East-West)
        var scoreZ = (wallN ? 3 : 0) + (wallS ? 3 : 0) + (walkW ? 1 : 0) + (walkE ? 1 : 0);

        if (scoreZ > scoreX)
        {
            return Mathf.Pi / 2f;
        }
        if (scoreX > scoreZ)
        {
            return 0f;
        }

        // Tie-breaker: inspect 2-step neighbors
        var walkN2 = IsWalkableOrPortal(KindOf(FeatAt(map, x, y - 2)));
        var walkS2 = IsWalkableOrPortal(KindOf(FeatAt(map, x, y + 2)));
        var walkW2 = IsWalkableOrPortal(KindOf(FeatAt(map, x - 2, y)));
        var walkE2 = IsWalkableOrPortal(KindOf(FeatAt(map, x + 2, y)));

        var extX = (walkN2 ? 1 : 0) + (walkS2 ? 1 : 0);
        var extZ = (walkW2 ? 1 : 0) + (walkE2 ? 1 : 0);

        return extZ > extX ? Mathf.Pi / 2f : 0f;
    }

    private static float DetermineShopDoorOrientation(JsonElement map, int x, int y, int w, int h)
    {
        var kN = KindOf(FeatAt(map, x, y - 1));
        var kS = KindOf(FeatAt(map, x, y + 1));
        var kW = KindOf(FeatAt(map, x - 1, y));
        var kE = KindOf(FeatAt(map, x + 1, y));

        var bldgN = IsSolidWall(kN) || IsStoreKind(kN) || y <= 0;
        var bldgS = IsSolidWall(kS) || IsStoreKind(kS) || y >= h - 1;
        var bldgW = IsSolidWall(kW) || IsStoreKind(kW) || x <= 0;
        var bldgE = IsSolidWall(kE) || IsStoreKind(kE) || x >= w - 1;

        var streetN = !bldgN;
        var streetS = !bldgS;
        var streetW = !bldgW;
        var streetE = !bldgE;

        // Direct exterior wall cases (middle of building edges)
        if (bldgN && streetS && bldgW && bldgE) return 0f; // Facing South (+Z)
        if (bldgS && streetN && bldgW && bldgE) return Mathf.Pi; // Facing North (-Z)
        if (bldgW && streetE && bldgN && bldgS) return Mathf.Pi / 2f; // Facing East (+X)
        if (bldgE && streetW && bldgN && bldgS) return -Mathf.Pi / 2f; // Facing West (-X)

        // Corner cases (building on 2 adjacent sides, street on opposite sides)
        if (bldgN && bldgE && streetS && streetW) return 0f; // SW corner -> Face South
        if (bldgN && bldgW && streetS && streetE) return 0f; // SE corner -> Face South
        if (bldgS && bldgE && streetN && streetW) return Mathf.Pi; // NW corner -> Face North
        if (bldgS && bldgW && streetN && streetE) return Mathf.Pi; // NE corner -> Face North

        // Fallbacks based on accessible street
        if (streetS) return 0f;
        if (streetN) return Mathf.Pi;
        if (streetE) return Mathf.Pi / 2f;
        if (streetW) return -Mathf.Pi / 2f;

        return 0f;
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

    /// <summary>
    /// Reconstructs the 3D MultiMesh instance buffers for the active viewport.
    /// Filters tiles based on Line of Sight, assigns orientation transforms to doors/shops,
    /// and applies depth-based lighting and atmospheric shading.
    /// </summary>
    /// <param name="map">The map JSON element containing width, height, rows, and encodings.</param>
    /// <param name="px">Player discrete grid X coordinate.</param>
    /// <param name="py">Player discrete grid Y coordinate.</param>
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

                if (kind is Kind.Wall or Kind.Magma or Kind.Quartz)
                {
                    // Solid stone blocks spanning from Y=0 to Y=WallHeight=3.0m
                    var pos = new Vector3(x * Cell, WallHeight / 2.0f, y * Cell);
                    _xf[kind].Add(new Transform3D(Basis.Identity, pos));
                    _col[kind].Add(shade);
                }
                else if (IsStoreKind(kind))
                {
                    // Floor underneath shop entrance
                    _xf[Kind.Floor].Add(new Transform3D(Basis.Identity, new Vector3(x * Cell, -0.05f, y * Cell)));
                    _col[Kind.Floor].Add(shade);

                    // Orientation: align shop door frame to face outward into the street/plaza
                    var doorYaw = DetermineShopDoorOrientation(map, x, y, w, h);
                    var basis = Basis.Identity.Rotated(Vector3.Up, doorYaw);
                    _xf[kind].Add(new Transform3D(basis, new Vector3(x * Cell, 0, y * Cell)));
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
                    var doorYaw = DetermineDoorOrientation(map, x, y, w, h);
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
                if (!_outdoors && (kind is Kind.Floor or Kind.StairsDown or Kind.StairsUp
                    or Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken or Kind.Rubble || IsStoreKind(kind)))
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
                if (IsStoreKind(kind))
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

            var storeKind = KindOf(feat);
            var storeNum = StoreNumOf(storeKind);
            var storeCol = StoreColor(storeNum);
            var storeLabel = storeNum > 0 ? $"[{storeNum}] {name}" : name;

            _terrainLabels.AddChild(Caption(storeLabel,
                new Vector3(avgX, WallHeight + 0.85f, avgY),
                storeCol, 36, noDepthTest: true));
        }

        foreach (var (pos, name) in stairsList)
        {
            _terrainLabels.AddChild(Caption(name, pos, new Color(0.65f, 0.85f, 1.0f), 28, noDepthTest: false));
        }
    }

    /// <summary>
    /// A floating caption. Crisp outline and readable size.
    /// </summary>
    private static Label3D Caption(string text, Vector3 pos, Color colour, int size = 28, bool noDepthTest = false)
    {
        return new Label3D
        {
            Text = text,
            Modulate = colour,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 8,
            FontSize = size,
            PixelSize = 0.0040f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = noDepthTest,
            RenderPriority = noDepthTest ? 15 : 0,
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
        return kind is Kind.StairsDown or Kind.StairsUp || IsStoreKind(kind) ? info.Name : null;
    }

    /// <summary>Instantiate and smoothly update 3D monsters and items with persistent tracking.</summary>
    private void RebuildEntities(JsonElement frame)
    {
        _frameSeq++;
        var playerPos = _targetPos;

        // Process Monsters
        string targetMonsterId = null;
        if (frame.TryGetProperty("player", out var pProp) &&
            pProp.TryGetProperty("target", out var targetProp) &&
            targetProp.ValueKind == JsonValueKind.Object)
        {
            if (targetProp.TryGetProperty("id", out var tidProp))
            {
                targetMonsterId = tidProp.GetInt32().ToString();
            }
        }

        var seenMonsterIds = new HashSet<string>();
        foreach (var m in frame.GetProperty("monsters").EnumerateArray())
        {
            var gx = m.GetProperty("x").GetInt32();
            var gy = m.GetProperty("y").GetInt32();
            var monId = m.TryGetProperty("id", out var idProp) ? idProp.GetInt32().ToString() : $"{gx}_{gy}";
            var targetWorldPos = new Vector3(gx * Cell, 0, gy * Cell);
            var isTargeted = targetMonsterId != null && monId == targetMonsterId;

            seenMonsterIds.Add(monId);

            if (_activeMonsters.TryGetValue(monId, out var entity))
            {
                entity.LastSeenFrame = _frameSeq;
                int newHp = m.TryGetProperty("hp", out var hProp) ? hProp.GetInt32() : entity.Hp;
                if (entity.Hp > 0 && newHp < entity.Hp)
                {
                    var dmg = entity.Hp - newHp;
                    var textPos = entity.CurrentPos + new Vector3(0, entity.ModelHeight + 0.35f, 0);
                    SpawnFloatingText($"-{dmg}", textPos, new Color(1.0f, 0.55f, 0.15f), 1.15f);
                    var sparkPos = entity.CurrentPos + new Vector3(0, entity.ModelHeight * 0.5f, 0);
                    var toPlayer = (_targetPos - entity.CurrentPos).Normalized();
                    SpawnHitSparks(sparkPos, toPlayer, entity.Color, 16);
                    AudioManager.PlayAt(SoundEffect.MeleeHit, entity.CurrentPos);
                }

                MonsterModelResolver.UpdateMonsterVisual(entity, m, isTargeted);

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
                MonsterModelResolver.UpdateMonsterVisual(newEntity, m, isTargeted);
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
                if (kvp.Value.Hp <= 0 || kvp.Value.Hp < kvp.Value.HpMax * 0.25f)
                {
                    SpawnDeathVfx(kvp.Value.CurrentPos + new Vector3(0, kvp.Value.ModelHeight * 0.5f, 0), kvp.Value.Color);
                    AudioManager.PlayAt(SoundEffect.MonsterDeath, kvp.Value.CurrentPos);
                }
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

    /// <summary>Spawn directional hit sparks matching monster blood/element color.</summary>
    public void SpawnHitSparks(Vector3 worldPos, Vector3 direction, Color color, int count = 16)
    {
        var sparks = new CpuParticles3D
        {
            Amount = count,
            Lifetime = 0.35f,
            OneShot = true,
            Explosiveness = 0.92f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.08f,
            Direction = direction.LengthSquared() > 0.01f ? direction.Normalized() : Vector3.Up,
            Spread = 45f,
            InitialVelocityMin = 1.6f,
            InitialVelocityMax = 3.2f,
            Gravity = new Vector3(0, -5.5f, 0),
            ScaleAmountMin = 0.025f,
            ScaleAmountMax = 0.050f,
            Color = color,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = color,
            },
            Position = worldPos,
        };
        AddChild(sparks);
        sparks.Emitting = true;

        var timer = GetTree().CreateTimer(0.40f);
        timer.Timeout += () => sparks.QueueFree();
    }

    /// <summary>Spawn ethereal death dissolve / smoke poof when a monster is slain.</summary>
    public void SpawnDeathVfx(Vector3 worldPos, Color color)
    {
        var poof = new CpuParticles3D
        {
            Amount = 28,
            Lifetime = 0.65f,
            OneShot = true,
            Explosiveness = 0.88f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            Direction = Vector3.Up,
            Spread = 65f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 2.2f,
            Gravity = new Vector3(0, 0.6f, 0),
            ScaleAmountMin = 0.04f,
            ScaleAmountMax = 0.09f,
            Color = new Color(color.R, color.G, color.B, 0.85f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = color,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            Position = worldPos,
        };
        AddChild(poof);
        poof.Emitting = true;

        var timer = GetTree().CreateTimer(0.70f);
        timer.Timeout += () => poof.QueueFree();
    }

    /// <summary>Spawn radiant magic burst on spellcast.</summary>
    public void SpawnSpellVfx(Vector3 worldPos, Color color)
    {
        var magic = new CpuParticles3D
        {
            Amount = 22,
            Lifetime = 0.50f,
            OneShot = true,
            Explosiveness = 0.85f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.15f,
            Direction = Vector3.Up,
            Spread = 75f,
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 2.8f,
            Gravity = new Vector3(0, 1.2f, 0),
            ScaleAmountMin = 0.03f,
            ScaleAmountMax = 0.06f,
            Color = color,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = color,
            },
            Position = worldPos,
        };
        AddChild(magic);
        magic.Emitting = true;

        var timer = GetTree().CreateTimer(0.55f);
        timer.Timeout += () => magic.QueueFree();
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

    /// <summary>Spawn kinetic 3D magical or ranged projectile flying between world coordinates.</summary>
    public void SpawnProjectile(Vector3 startPos, Vector3 targetPos, Color color, string effectType = "magic")
    {
        var dist = startPos.DistanceTo(targetPos);
        var duration = Mathf.Clamp(dist / 14.0f, 0.15f, 0.50f);

        var projNode = new Node3D { Position = startPos };

        // Glowing core sphere
        var core = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.05f, Height = 0.10f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
            },
        };
        projNode.AddChild(core);

        // Particle trail
        var trail = new CpuParticles3D
        {
            Amount = 18,
            Lifetime = 0.25f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.06f,
            Direction = Vector3.Up,
            Spread = 25f,
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.6f,
            Gravity = Vector3.Zero,
            ScaleAmountMin = 0.025f,
            ScaleAmountMax = 0.055f,
            Color = color,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = color,
            },
        };
        projNode.AddChild(trail);
        trail.Emitting = true;

        AddChild(projNode);

        _activeProjectiles.Add(new ActiveProjectile
        {
            Node = projNode,
            StartPos = startPos,
            TargetPos = targetPos,
            Elapsed = 0f,
            Duration = duration,
            Color = color,
            EffectType = effectType,
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
            _viewModel?.TriggerHurt();
            AudioManager.Play(SoundEffect.PlayerHurt);
            var hitPos = _targetPos + forward * 0.8f + (GD.Randf() > 0.5f ? right * 0.3f : -right * 0.3f);
            SpawnFloatingText("OUCH!", hitPos, new Color(1.0f, 0.30f, 0.30f), 1.1f);
            SpawnHitSparks(hitPos, -forward, new Color(0.9f, 0.2f, 0.2f), 14);

            if (lower.Contains("casts a spell") || lower.Contains("shoots you") || lower.Contains("burns you"))
            {
                var srcPos = _targetPos + forward * 4.0f + new Vector3(0, 0.4f, 0);
                var projCol = lower.Contains("burns") ? new Color(1.0f, 0.45f, 0.10f) : new Color(0.85f, 0.35f, 1.0f);
                SpawnProjectile(srcPos, _targetPos + new Vector3(0, 0.2f, 0), projCol, "enemy_spell");
            }
        }
        // Critical / heavy hit
        else if (lower.Contains("critical hit") || lower.Contains("great force") || lower.Contains("superb"))
        {
            AddTrauma(0.18f);
            AudioManager.Play(SoundEffect.MeleeCrit);
            SpawnFloatingText("CRITICAL!", spawnInFront + new Vector3(0, 0.3f, 0), new Color(1.0f, 0.88f, 0.20f), 1.35f);
            SpawnHitSparks(spawnInFront, forward, new Color(1.0f, 0.90f, 0.30f), 24);
        }
        // Player casting spell / aiming / zapping
        else if (lower.Contains("you cast") || lower.Contains("you zap") || lower.Contains("you aim") || lower.Contains("you recite"))
        {
            _viewModel?.TriggerCast();
            AudioManager.Play(SoundEffect.SpellCast);

            Color spellCol;
            if (lower.Contains("fire") || lower.Contains("flame")) spellCol = new Color(1.0f, 0.45f, 0.10f);
            else if (lower.Contains("frost") || lower.Contains("cold") || lower.Contains("ice")) spellCol = new Color(0.40f, 0.85f, 1.0f);
            else if (lower.Contains("lightning") || lower.Contains("elec") || lower.Contains("spark")) spellCol = new Color(1.0f, 0.95f, 0.30f);
            else if (lower.Contains("poison") || lower.Contains("acid") || lower.Contains("stinking")) spellCol = new Color(0.35f, 0.95f, 0.30f);
            else if (lower.Contains("heal") || lower.Contains("cure") || lower.Contains("bless")) spellCol = new Color(0.40f, 1.0f, 0.60f);
            else spellCol = new Color(0.55f, 0.75f, 1.0f);

            var startPos = _targetPos + forward * 0.4f + new Vector3(0, -0.1f, 0);
            var targetPos = _targetPos + forward * 6.0f + new Vector3(0, 0.2f, 0);
            SpawnProjectile(startPos, targetPos, spellCol, "player_spell");
            SpawnSpellVfx(spawnInFront, spellCol);
            SpawnFloatingText("CAST", spawnInFront, spellCol, 1.0f);
        }
        // Player shooting bow / sling / crossbow
        else if (lower.Contains("you shoot") || lower.Contains("you fire"))
        {
            _viewModel?.TriggerAttack();
            AudioManager.Play(SoundEffect.BowShoot);
            var startPos = _targetPos + forward * 0.4f + new Vector3(0.15f, -0.1f, 0);
            var targetPos = _targetPos + forward * 8.0f + new Vector3(0, 0.2f, 0);
            SpawnProjectile(startPos, targetPos, new Color(1.0f, 0.85f, 0.40f), "arrow");
            SpawnFloatingText("SHOOT", spawnInFront, new Color(0.95f, 0.85f, 0.40f), 1.0f);
        }
        // Player hitting monster
        else if (lower.Contains("you hit") || lower.Contains("you strike") || lower.Contains("you slash") ||
                 lower.Contains("you smite") || lower.Contains("you crush"))
        {
            _viewModel?.TriggerAttack();
            AudioManager.Play(SoundEffect.MeleeHit);
            SpawnFloatingText("HIT", spawnInFront, new Color(1.0f, 0.75f, 0.20f), 1.0f);
            SpawnHitSparks(spawnInFront, forward, new Color(1.0f, 0.70f, 0.25f), 14);
        }
        // Misses
        else if (lower.Contains("you miss") || lower.Contains("misses you"))
        {
            if (lower.Contains("you miss"))
            {
                _viewModel?.TriggerAttack();
            }
            AudioManager.Play(SoundEffect.MeleeSwing);
            SpawnFloatingText("MISS", spawnInFront, new Color(0.70f, 0.72f, 0.78f), 0.9f);
        }
        // Monster slain / destroyed
        else if (lower.Contains("you have slain") || lower.Contains("is destroyed") || lower.Contains("dies."))
        {
            AudioManager.Play(SoundEffect.MonsterDeath);
            SpawnFloatingText("SLAIN!", spawnInFront + new Vector3(0, 0.25f, 0), new Color(0.95f, 0.40f, 0.95f), 1.25f);
            SpawnDeathVfx(spawnInFront, new Color(0.85f, 0.35f, 0.95f));
        }
        // Door interactions
        else if (lower.Contains("you open the door") || lower.Contains("the door opens"))
        {
            AudioManager.Play(SoundEffect.DoorOpen);
        }
        else if (lower.Contains("you close the door") || lower.Contains("the door closes"))
        {
            AudioManager.Play(SoundEffect.DoorClose);
        }
        else if (lower.Contains("the door is broken") || lower.Contains("you smash open the door") || lower.Contains("door is destroyed"))
        {
            AudioManager.Play(SoundEffect.DoorBreak);
            AddTrauma(0.20f);
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

        // First-person head bob and footstep audio when moving
        var distRemaining = _camera.Position.DistanceTo(_targetPos);
        var bob = 0f;
        var isMoving = distRemaining > 0.02f;
        if (isMoving)
        {
            var progress = 1.0f - Mathf.Clamp(distRemaining / Cell, 0f, 1f);
            bob = Mathf.Sin(progress * Mathf.Pi) * 0.07f * Mathf.Clamp(Mathf.Sqrt(CurrentHeightRatio), 0.72f, 1.22f);

            if (!_stepPlayedThisMove)
            {
                _stepPlayedThisMove = true;
                var pitchScale = Mathf.Clamp(1.0f / Mathf.Pow(CurrentHeightRatio, 0.35f), 0.78f, 1.32f) + (GD.Randf() * 0.06f - 0.03f);
                AudioManager.Play(_outdoors ? SoundEffect.FootstepOutdoor : SoundEffect.Footstep, pitchScale, volumeDb: -7f);
            }
        }
        else
        {
            _stepPlayedThisMove = false;
        }
        _camera.Position = new Vector3(_camera.Position.X, CurrentEyeHeight + bob, _camera.Position.Z);

        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);

        // Subtle camera roll on turn
        var yawDelta = Mathf.Wrap(_targetYaw - _yaw, -Mathf.Pi, Mathf.Pi);
        var roll = Mathf.Clamp(-yawDelta * 0.08f, -0.04f, 0.04f);
        _camera.Rotation = new Vector3(0, _yaw, roll);

        // Update first-person viewmodel motion, bobbing & inertia sway
        _viewModel?.ProcessMotion(delta, isMoving, yawDelta, 0f);

        // Smooth depth biome atmospheric environment transition
        if (_targetBiome != null && _env != null)
        {
            var lerpSpeed = (float)Math.Min(1.0, delta * 3.0);
            _env.BackgroundColor = _env.BackgroundColor.Lerp(_targetBiome.BackgroundColor, lerpSpeed);
            _env.AmbientLightColor = _env.AmbientLightColor.Lerp(_targetBiome.AmbientLightColor, lerpSpeed);
            _env.AmbientLightEnergy = Mathf.Lerp(_env.AmbientLightEnergy, _targetBiome.AmbientLightEnergy, lerpSpeed);
            _env.FogLightColor = _env.FogLightColor.Lerp(_targetBiome.FogLightColor, lerpSpeed);
            _env.FogDensity = Mathf.Lerp(_env.FogDensity, _targetBiome.FogDensity, lerpSpeed);
            _env.VolumetricFogDensity = Mathf.Lerp(_env.VolumetricFogDensity, _targetBiome.VolumetricFogDensity, lerpSpeed);
            _env.VolumetricFogAlbedo = _env.VolumetricFogAlbedo.Lerp(_targetBiome.VolumetricFogAlbedo, lerpSpeed);
            _env.VolumetricFogEmission = _env.VolumetricFogEmission.Lerp(_targetBiome.VolumetricFogEmission, lerpSpeed);
            _env.TonemapExposure = Mathf.Lerp(_env.TonemapExposure, _targetBiome.TonemapExposure, lerpSpeed);
        }

        // Keep atmospheric particulate emitter anchored to camera view
        if (_biomeParticles != null)
        {
            _biomeParticles.Position = _targetPos + new Vector3(0, 1.2f, 0);
        }

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

        // Update kinetic 3D projectiles
        for (var i = _activeProjectiles.Count - 1; i >= 0; i--)
        {
            var proj = _activeProjectiles[i];
            proj.Elapsed += (float)delta;
            var progress = Mathf.Clamp(proj.Elapsed / proj.Duration, 0f, 1f);
            proj.Node.Position = proj.StartPos.Lerp(proj.TargetPos, progress);

            if (proj.Elapsed >= proj.Duration)
            {
                SpawnHitSparks(proj.TargetPos, Vector3.Up, proj.Color, 16);
                proj.Node.QueueFree();
                _activeProjectiles.RemoveAt(i);
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
        var n1 = Mathf.Sin((float)_flicker * 9.5f);
        var n2 = Mathf.Sin((float)_flicker * 17.3f);
        var n3 = Mathf.Sin((float)_flicker * 31.7f);
        var f = 1.0f + 0.045f * n1 + 0.030f * n2 + 0.015f * n3;
        var targetEnergy = (_targetBiome?.TorchLightEnergy ?? 2.4f) * f;
        var targetColor = _targetBiome?.TorchLightColor ?? new Color(1.0f, 0.86f, 0.64f);

        _torch.OmniRange = (_torchRadius + 2.5f) * Cell + 2.0f;
        _torch.LightEnergy = Mathf.Lerp(_torch.LightEnergy, targetEnergy, (float)Math.Min(1.0, delta * 12.0));
        _torch.LightColor = _torch.LightColor.Lerp(targetColor, (float)Math.Min(1.0, delta * 4.0));

        // Subtle dancing torch shadow jitter
        var jitterX = n2 * 0.012f;
        var jitterY = n1 * 0.010f;
        var jitterZ = n3 * 0.012f;
        _torch.Position = new Vector3(-0.32f + jitterX, -0.10f + jitterY, -0.32f + jitterZ);

        // Process smooth monster movement, facing, and hover/animations
        var moveT = (float)Math.Min(1.0, delta / 0.16);
        var rotT = (float)Math.Min(1.0, delta * 8.0);

        foreach (var monster in _activeMonsters.Values)
        {
            // Smooth positional interpolation
            monster.CurrentPos = monster.CurrentPos.Lerp(monster.TargetPos, moveT);
            var distLeft = monster.CurrentPos.DistanceTo(monster.TargetPos);

            // Floating / bobbing / breathing effect
            var floatY = monster.BaseY;
            if (monster.IsFloating)
            {
                floatY += Mathf.Sin((float)_flicker * 3.0f + monster.FloatOffset) * 0.12f;
            }
            else if (monster.IsAsleep)
            {
                // Gentle slow breathing oscillation when asleep
                floatY += Mathf.Sin((float)_flicker * 1.8f + monster.FloatOffset) * 0.03f;
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

                var toPlayer = _targetPos - monster.CurrentPos;
                toPlayer.Y = 0;

                // Fleeing monsters face away from player; others face player when adjacent
                if (monster.IsAfraid)
                {
                    if (toPlayer.LengthSquared() > 0.001f)
                    {
                        monster.TargetYaw = Mathf.Atan2(-toPlayer.X, -toPlayer.Z);
                    }
                }
                else if (toPlayer.LengthSquared() < (Cell * 2.5f) * (Cell * 2.5f) && toPlayer.LengthSquared() > 0.001f)
                {
                    monster.TargetYaw = Mathf.Atan2(toPlayer.X, toPlayer.Z);
                }
            }
            else if (!monster.IsMoving && monster.IsAfraid)
            {
                // Continuously orient fleeing monsters away from player
                var toPlayer = _targetPos - monster.CurrentPos;
                toPlayer.Y = 0;
                if (toPlayer.LengthSquared() > 0.001f)
                {
                    monster.TargetYaw = Mathf.Atan2(-toPlayer.X, -toPlayer.Z);
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

            // Animate overhead status badges and targeting reticles
            if (monster.StatusBadge != null && monster.StatusBadge.Visible)
            {
                var badgeOffset = Mathf.Sin((float)_flicker * 2.5f + monster.FloatOffset) * 0.04f;
                monster.StatusBadge.Position = new Vector3(0, monster.ModelHeight + 0.65f + badgeOffset, 0);
            }

            if (monster.TargetBadge != null && monster.TargetBadge.Visible)
            {
                var reticleOffset = Mathf.Sin((float)_flicker * 4.0f) * 0.06f;
                var reticleAlpha = 0.80f + Mathf.Sin((float)_flicker * 5.0f) * 0.20f;
                monster.TargetBadge.Position = new Vector3(0, monster.ModelHeight + 0.95f + reticleOffset, 0);
                monster.TargetBadge.Modulate = new Color(1.0f, 0.92f, 0.30f, reticleAlpha);
            }

            // Update lively procedural animations for creature tokens (rodents, insects, centipedes, bats, slimes, etc.)
            MonsterModelResolver.UpdateProceduralAnimation(monster, delta, (float)_flicker);
        }
    }
}
