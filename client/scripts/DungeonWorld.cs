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
/// Explored/mapped tiles (<c>known</c>) and tiles in direct Line of Sight (<c>in_view</c>) are rendered in 3D.
/// Tiles in active line-of-sight are dynamically illuminated by torchlight and ambient light, while explored
/// areas outside immediate line-of-sight are rendered with a subtle, atmospheric fog-of-war memory shade.
/// Unexplored solid rock beyond walls remains dark void, preventing see-through wall glitches while preserving
/// continuous enclosed corridor and room architecture.
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
    private DirectionalLight3D _sunLight;
    private Godot.Environment _env;
    private Node3D _entities;
    private Node3D _terrainLabels;
    private readonly Dictionary<Kind, MultiMeshInstance3D> _buckets = new();
    private readonly Dictionary<Kind, List<Transform3D>> _xf = new();
    private readonly Dictionary<Kind, List<Color>> _col = new();
    private readonly Dictionary<string, MonsterEntity> _activeMonsters = new();
    private readonly Dictionary<string, Node3D> _activeItems = new();
    private int _frameSeq;

    private int _lastPlayerHp = -1;
    private int _lastPlayerMaxHp = -1;
    private bool _stepPlayedThisMove;
    private int _lastPoisoned = 0;
    private int _lastConfused = 0;
    private int _lastBlind = 0;
    private int _lastParalyzed = 0;
    private int _lastAfraid = 0;
    private int _lastFood = 9999;

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
    private int _lastProcessedCombatSeq = -1;
    private string _lastTerrainLabelsSignature = "";

    private Vector3 _targetPos;
    private float _targetYaw;
    private float _yaw;
    private float _targetPitch;
    private float _pitch;
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
    private static Material _magmaMaterial;
    private static StandardMaterial3D _quartzMaterial;
    private static StandardMaterial3D _storeMaterial;
    private static StandardMaterial3D _doorFrameMaterial;
    private static StandardMaterial3D _doorWoodMaterial;
    private static readonly StandardMaterial3D[] _shopDoorMaterials = new StandardMaterial3D[8];
    private static StandardMaterial3D _stairsMaterial;
    private static StandardMaterial3D _rubbleMaterial;
    private static Material _lavaMaterial;

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

        _sunLight = new DirectionalLight3D
        {
            Name = "TownSunLight",
            LightColor = new Color(0.70f, 0.80f, 0.98f),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            RotationDegrees = new Vector3(-55, 38, 0),
            Visible = false,
        };
        AddChild(_sunLight);

        // Realistic warm torch with smooth wide-angle illumination (shadows disabled for performance & stable lighting)
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
            VolumetricFogEnabled = false,
            SsaoEnabled = true,
            SsaoRadius = 1.6f,
            SsaoIntensity = 2.2f,
            SsaoPower = 1.4f,
            SsaoDetail = 0.6f,
            SsrEnabled = false,
            SsilEnabled = false,
            SdfgiEnabled = false,
            GlowEnabled = true,
            GlowIntensity = 0.45f,
            GlowBloom = 0.12f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = b.TonemapExposure,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.06f,
            AdjustmentSaturation = 1.05f,
        };
    }

    #region Procedural Textures & Materials

    private static Texture2D LoadTextureOrFallback(string resPath, Func<ImageTexture> fallbackFunc)
    {
        if (ResourceLoader.Exists(resPath))
        {
            try
            {
                var tex = GD.Load<Texture2D>(resPath);
                if (tex != null)
                {
                    return tex;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to load texture at {resPath}: {ex.Message}");
            }
        }
        return fallbackFunc?.Invoke();
    }

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
        var data = new byte[width * height * 4];
        var idx = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = pixelFunc(x, y);
                data[idx++] = (byte)Mathf.Clamp((int)(c.R * 255f + 0.5f), 0, 255);
                data[idx++] = (byte)Mathf.Clamp((int)(c.G * 255f + 0.5f), 0, 255);
                data[idx++] = (byte)Mathf.Clamp((int)(c.B * 255f + 0.5f), 0, 255);
                data[idx++] = (byte)Mathf.Clamp((int)(c.A * 255f + 0.5f), 0, 255);
            }
        }
        var img = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        if (generateMipmaps)
        {
            img.GenerateMipmaps();
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture CreateStoneWallTexture()
    {
        const int size = 512;
        const int rowHeight = size / 2;
        const int halfRow = rowHeight / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / rowHeight;
            var yInRow = y % rowHeight;
            var distY = Math.Min(yInRow, rowHeight - 1 - yInRow);
            var xOff = (row % 2 == 1) ? halfRow : 0;
            var xInRow = (x + size - xOff) % rowHeight;
            var distX = Math.Min(xInRow, rowHeight - 1 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            // Clean dark recessed mortar joints (width ~8px)
            if (mortarDist <= 8)
            {
                var mn = SmoothNoise(x * 0.125f, y * 0.125f, 101) * 0.04f - 0.02f;
                return new Color(0.12f + mn, 0.12f + mn, 0.14f + mn);
            }

            // Subtle block tonal variation
            var blockId = row * 2 + ((x + size - xOff) / rowHeight);
            var blockHue = Hash(blockId, 0, 77);
            var rBase = 0.44f + (blockHue - 0.5f) * 0.05f;
            var gBase = 0.44f + (blockHue - 0.5f) * 0.04f;
            var bBase = 0.46f + (0.5f - blockHue) * 0.04f;

            // Soft 3D chiseled bevel lighting (top-left highlight, bottom-right shadow)
            var bevelX = (xInRow < halfRow) ? (xInRow - 8) / 32.0f : (rowHeight - 9 - xInRow) / 32.0f;
            var bevelY = (yInRow < halfRow) ? (yInRow - 8) / 32.0f : (rowHeight - 9 - yInRow) / 32.0f;
            var bevel = Mathf.Clamp(Math.Min(bevelX, bevelY), 0.0f, 1.0f);
            var lightGradient = ((halfRow - xInRow) + (halfRow - yInRow)) * 0.0004f;

            // Fine stone grain and chiseled striations
            var grain = (SmoothNoise(x * 0.06f, y * 0.06f, 1) - 0.5f) * 0.08f;
            var microGrain = (SmoothNoise(x * 0.18f, y * 0.18f, 2) - 0.5f) * 0.04f;
            var chisel = (SmoothNoise(x * 0.18f, y * 0.04f, 14) - 0.5f) * 0.03f;

            var r = Mathf.Clamp(rBase * (0.80f + bevel * 0.20f) + lightGradient + grain + microGrain + chisel, 0f, 1f);
            var g = Mathf.Clamp(gBase * (0.80f + bevel * 0.20f) + lightGradient + grain + microGrain + chisel, 0f, 1f);
            var b = Mathf.Clamp(bBase * (0.80f + bevel * 0.20f) + lightGradient + grain + microGrain + chisel, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateStoneWallNormal()
    {
        const int size = 512;
        const int rowHeight = size / 2;
        const int halfRow = rowHeight / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / rowHeight;
            var yInRow = y % rowHeight;
            var distY = Math.Min(yInRow, rowHeight - 1 - yInRow);
            var xOff = (row % 2 == 1) ? halfRow : 0;
            var xInRow = (x + size - xOff) % rowHeight;
            var distX = Math.Min(xInRow, rowHeight - 1 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 8)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (xInRow < halfRow) ? (1.0f - xInRow / 32.0f) : -(1.0f - (rowHeight - 1 - xInRow) / 32.0f);
            var dy = (yInRow < halfRow) ? (1.0f - yInRow / 32.0f) : -(1.0f - (rowHeight - 1 - yInRow) / 32.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var microBump = (SmoothNoise(x * 0.08f, y * 0.08f, 50) - 0.5f) * 0.15f;
            var fineBump = (SmoothNoise(x * 0.22f, y * 0.22f, 51) - 0.5f) * 0.08f;
            var norm = new Vector3((dx + microBump + fineBump) * 0.55f, -(dy + microBump + fineBump) * 0.55f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateStoneWallRoughness()
    {
        const int size = 512;
        const int rowHeight = size / 2;
        const int halfRow = rowHeight / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var row = y / rowHeight;
            var yInRow = y % rowHeight;
            var distY = Math.Min(yInRow, rowHeight - 1 - yInRow);
            var xOff = (row % 2 == 1) ? halfRow : 0;
            var xInRow = (x + size - xOff) % rowHeight;
            var distX = Math.Min(xInRow, rowHeight - 1 - xInRow);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 8)
            {
                return new Color(0.96f, 0.96f, 0.96f);
            }

            var grain = (SmoothNoise(x * 0.08f, y * 0.08f, 11) - 0.5f) * 0.08f;
            var r = Mathf.Clamp(0.78f + grain, 0.65f, 0.90f);
            return new Color(r, r, r);
        });
    }

    private static ImageTexture CreateFloorTexture()
    {
        const int size = 512;
        const int tileSize = size / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var tileX = x / tileSize;
            var tileY = y / tileSize;
            var inTileX = x % tileSize;
            var inTileY = y % tileSize;
            var distX = Math.Min(inTileX, tileSize - 1 - inTileX);
            var distY = Math.Min(inTileY, tileSize - 1 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 8)
            {
                var mn = SmoothNoise(x * 0.125f, y * 0.125f, 202) * 0.03f - 0.015f;
                return new Color(0.10f + mn, 0.10f + mn, 0.11f + mn);
            }

            var tileId = tileY * 2 + tileX;
            var tileHue = Hash(tileId, 0, 99);
            var rBase = 0.38f + (tileHue - 0.5f) * 0.04f;
            var gBase = 0.39f + (tileHue - 0.5f) * 0.03f;
            var bBase = 0.41f + (0.5f - tileHue) * 0.03f;

            var bevel = Mathf.Clamp((mortarDist - 8) / 32.0f, 0.75f, 1.0f);
            var grain = (SmoothNoise(x * 0.06f, y * 0.06f, 3) - 0.5f) * 0.06f;
            var microGrain = (SmoothNoise(x * 0.18f, y * 0.18f, 4) - 0.5f) * 0.03f;

            var r = Mathf.Clamp(rBase * bevel + grain + microGrain, 0f, 1f);
            var g = Mathf.Clamp(gBase * bevel + grain + microGrain, 0f, 1f);
            var b = Mathf.Clamp(bBase * bevel + grain + microGrain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateFloorNormal()
    {
        const int size = 512;
        const int tileSize = size / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var inTileX = x % tileSize;
            var inTileY = y % tileSize;
            var distX = Math.Min(inTileX, tileSize - 1 - inTileX);
            var distY = Math.Min(inTileY, tileSize - 1 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 8)
            {
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var dx = (inTileX < tileSize / 2) ? (1.0f - inTileX / 36.0f) : -(1.0f - (tileSize - 1 - inTileX) / 36.0f);
            var dy = (inTileY < tileSize / 2) ? (1.0f - inTileY / 36.0f) : -(1.0f - (tileSize - 1 - inTileY) / 36.0f);
            dx = Mathf.Clamp(dx, -1f, 1f);
            dy = Mathf.Clamp(dy, -1f, 1f);

            var microBump = (SmoothNoise(x * 0.08f, y * 0.08f, 52) - 0.5f) * 0.12f;
            var norm = new Vector3((dx + microBump) * 0.45f, -(dy + microBump) * 0.45f, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateFloorRoughness()
    {
        const int size = 512;
        const int tileSize = size / 2;
        return CreateTexture(size, size, (x, y) =>
        {
            var inTileX = x % tileSize;
            var inTileY = y % tileSize;
            var distX = Math.Min(inTileX, tileSize - 1 - inTileX);
            var distY = Math.Min(inTileY, tileSize - 1 - inTileY);
            var mortarDist = Math.Min(distX, distY);

            if (mortarDist <= 8)
            {
                return new Color(0.92f, 0.92f, 0.92f);
            }

            // Smooth foot-worn flagstone centers
            var centerDist = Math.Sqrt((inTileX - tileSize / 2) * (inTileX - tileSize / 2) + (inTileY - tileSize / 2) * (inTileY - tileSize / 2));
            var wear = Mathf.Clamp((float)(1.0 - centerDist / 160.0), 0f, 1f) * 0.12f;
            var grain = (SmoothNoise(x * 0.08f, y * 0.08f, 14) - 0.5f) * 0.05f;
            var r = Mathf.Clamp(0.72f - wear + grain, 0.55f, 0.88f);
            return new Color(r, r, r);
        });
    }

    private static ImageTexture CreateCeilingTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var rBase = 0.22f;
            var gBase = 0.23f;
            var bBase = 0.25f;
            var grain = (SmoothNoise(x * 0.06f, y * 0.06f, 5) - 0.5f) * 0.06f;
            var microGrain = (SmoothNoise(x * 0.18f, y * 0.18f, 6) - 0.5f) * 0.03f;

            var r = Mathf.Clamp(rBase + grain + microGrain, 0f, 1f);
            var g = Mathf.Clamp(gBase + grain + microGrain, 0f, 1f);
            var b = Mathf.Clamp(bBase + grain + microGrain, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateCeilingNormal()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var bumpX = (SmoothNoise(x * 0.08f + 10f, y * 0.08f, 55) - 0.5f) * 0.30f;
            var bumpY = (SmoothNoise(x * 0.08f, y * 0.08f + 10f, 56) - 0.5f) * 0.30f;
            var norm = new Vector3(bumpX, bumpY, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateMagmaTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            // Volcanic fissures in dark basalt
            var vein = (FractalNoise(x * 0.02f, y * 0.02f, 4, 77) - 0.5f) * 2.0f;
            var isVein = Math.Abs(vein) < 0.24f;

            if (isVein)
            {
                var heat = 1.0f - Mathf.Clamp(Math.Abs(vein) / 0.24f, 0f, 1f);
                var r = 0.94f + heat * 0.06f;
                var g = 0.32f + heat * 0.44f;
                var b = 0.04f + heat * 0.12f;
                return new Color(r, g, b);
            }

            var rockGrain = (SmoothNoise(x * 0.08f, y * 0.08f, 88) - 0.5f) * 0.05f;
            return new Color(0.24f + rockGrain, 0.22f + rockGrain, 0.24f + rockGrain);
        });
    }

    private static ImageTexture CreateMagmaEmission()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var vein = (FractalNoise(x * 0.02f, y * 0.02f, 4, 77) - 0.5f) * 2.0f;
            if (Math.Abs(vein) < 0.24f)
            {
                var heat = 1.0f - Mathf.Clamp(Math.Abs(vein) / 0.24f, 0f, 1f);
                return new Color(0.96f, 0.34f + heat * 0.40f, 0.04f + heat * 0.12f);
            }
            return Colors.Black;
        });
    }

    private static ImageTexture CreateQuartzTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            // Crystalline quartz seam
            var crystalVein = (FractalNoise((x * 1.2f - y * 0.8f) * 0.025f, (x * 0.5f + y) * 0.025f, 4, 44) - 0.5f) * 2.0f;
            if (Math.Abs(crystalVein) < 0.22f)
            {
                var glint = SmoothNoise(x * 0.2f, y * 0.2f, 12) * 0.12f;
                return new Color(0.74f + glint, 0.80f + glint, 0.86f + glint);
            }

            var grain = (SmoothNoise(x * 0.08f, y * 0.08f, 9) - 0.5f) * 0.06f;
            return new Color(0.40f + grain, 0.41f + grain, 0.43f + grain);
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
        const int size = 512;
        const int plankWidth = size / 4;
        var pattern = (shopNum >= 1 && shopNum <= 8) ? DigitPatterns[shopNum - 1] : null;

        return CreateTexture(size, size, (x, y) =>
        {
            var plankIdx = x / plankWidth;
            var inPlankX = x % plankWidth;
            var seamDist = Math.Min(inPlankX, plankWidth - 1 - inPlankX);

            // Horizontal forged-iron reinforcement straps
            var isIronStrap = (y >= 88 && y <= 124) || (y >= 388 && y <= 424);
            if (isIronStrap)
            {
                var rivetX = inPlankX - plankWidth / 2;
                var rivetY = (y < 200) ? (y - 106) : (y - 406);
                var isRivet = rivetX * rivetX + rivetY * rivetY <= 64;
                if (isRivet)
                {
                    return new Color(0.48f, 0.48f, 0.52f);
                }
                var ironGrain = (SmoothNoise(x * 0.1f, y * 0.1f, 31) - 0.5f) * 0.04f;
                return new Color(0.20f + ironGrain, 0.20f + ironGrain, 0.22f + ironGrain);
            }

            // Central Emblazoned Heraldic Shield / Plaque at (256, 256)
            var dx = x - 256;
            var dy = y - 256;
            var absDx = Math.Abs(dx);

            // Heraldic Shield shape: rectangle on top (dy <= 0), curved taper on bottom (dy > 0)
            var inShield = (dy >= -92 && dy <= 0 && absDx <= 88) ||
                           (dy > 0 && dy <= 100 && absDx <= 88 - (dy * dy) / 116f);

            if (inShield)
            {
                var isShieldRim = (dy <= -84 || absDx >= 80 || (dy > 0 && absDx >= 80 - (dy * dy) / 116f));
                if (isShieldRim)
                {
                    // Gilded brass rim
                    var bevel = (dx < 0 || dy < -76) ? 0.14f : -0.10f;
                    return new Color(0.88f + bevel, 0.74f + bevel, 0.28f + bevel);
                }

                // Check for the emblazoned digit inside the shield (5x7 grid, 12px per cell)
                if (pattern != null)
                {
                    var gx = (x - 226) / 12;
                    var gy = (y - 214) / 12;

                    if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7)
                    {
                        var isBit = (pattern[gy] & (1 << (4 - gx))) != 0;
                        if (isBit)
                        {
                            return new Color(1.0f, 0.90f, 0.35f);
                        }
                    }
                }

                // Shield background inlay: Rich burnished heraldic enamel
                var bgR = Mathf.Clamp(heraldicColor.R * 0.48f + 0.10f, 0f, 1f);
                var bgG = Mathf.Clamp(heraldicColor.G * 0.48f + 0.08f, 0f, 1f);
                var bgB = Mathf.Clamp(heraldicColor.B * 0.48f + 0.06f, 0f, 1f);
                return new Color(bgR, bgG, bgB);
            }

            // Dark plank seam
            if (seamDist <= 4)
            {
                return new Color(0.10f, 0.07f, 0.04f);
            }

            // Natural oak wood grain
            var woodGrain = (SmoothNoise(x * 0.08f, y * 0.04f, 55) - 0.5f) * 0.04f;
            var plankHue = Hash(plankIdx, 0, 88 + shopNum);
            var r = Mathf.Clamp(0.32f + (plankHue - 0.5f) * 0.03f + woodGrain, 0f, 1f);
            var g = Mathf.Clamp(0.22f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.8f, 0f, 1f);
            var b = Mathf.Clamp(0.14f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.5f, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateWoodDoorTexture()
    {
        const int size = 512;
        const int plankWidth = size / 4;
        return CreateTexture(size, size, (x, y) =>
        {
            var plankIdx = x / plankWidth;
            var inPlankX = x % plankWidth;
            var seamDist = Math.Min(inPlankX, plankWidth - 1 - inPlankX);

            // Horizontal forged-iron reinforcement straps
            var isIronStrap = (y >= 100 && y <= 136) || (y >= 376 && y <= 412);
            if (isIronStrap)
            {
                var rivetX = inPlankX - plankWidth / 2;
                var rivetY = (y < 200) ? (y - 118) : (y - 394);
                var isRivet = rivetX * rivetX + rivetY * rivetY <= 64;
                if (isRivet)
                {
                    return new Color(0.44f, 0.44f, 0.48f);
                }
                var ironGrain = (SmoothNoise(x * 0.1f, y * 0.1f, 31) - 0.5f) * 0.04f;
                return new Color(0.20f + ironGrain, 0.20f + ironGrain, 0.22f + ironGrain);
            }

            if (seamDist <= 4)
            {
                return new Color(0.10f, 0.07f, 0.04f);
            }

            var woodGrain = (SmoothNoise(x * 0.08f, y * 0.04f, 55) - 0.5f) * 0.04f;
            var plankHue = Hash(plankIdx, 0, 88);
            var r = Mathf.Clamp(0.34f + (plankHue - 0.5f) * 0.03f + woodGrain, 0f, 1f);
            var g = Mathf.Clamp(0.24f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.8f, 0f, 1f);
            var b = Mathf.Clamp(0.15f + (plankHue - 0.5f) * 0.02f + woodGrain * 0.5f, 0f, 1f);
            return new Color(r, g, b);
        });
    }

    private static ImageTexture CreateWoodDoorNormal()
    {
        const int size = 512;
        const int plankWidth = size / 4;
        return CreateTexture(size, size, (x, y) =>
        {
            var inPlankX = x % plankWidth;
            var isIronStrap = (y >= 100 && y <= 136) || (y >= 376 && y <= 412);
            if (isIronStrap)
            {
                var rivetX = inPlankX - plankWidth / 2;
                var rivetY = (y < 200) ? (y - 118) : (y - 394);
                if (rivetX * rivetX + rivetY * rivetY <= 64)
                {
                    var rNorm = new Vector3(rivetX / 8f, rivetY / 8f, 1.0f).Normalized();
                    return new Color(rNorm.X * 0.5f + 0.5f, rNorm.Y * 0.5f + 0.5f, rNorm.Z * 0.5f + 0.5f);
                }
                return new Color(0.5f, 0.5f, 1.0f);
            }

            var seamDist = Math.Min(inPlankX, plankWidth - 1 - inPlankX);
            if (seamDist <= 4)
            {
                var sNorm = (inPlankX < plankWidth / 2) ? -0.35f : 0.35f;
                return new Color(sNorm * 0.5f + 0.5f, 0.5f, 0.88f);
            }

            var grainBump = (SmoothNoise(x * 0.1f, y * 0.05f, 66) - 0.5f) * 0.12f;
            var norm = new Vector3(grainBump, 0, 1.0f).Normalized();
            return new Color(norm.X * 0.5f + 0.5f, norm.Y * 0.5f + 0.5f, norm.Z * 0.5f + 0.5f);
        });
    }

    private static ImageTexture CreateStoreTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            // Medieval half-timbered shop facade with dark oak beams and warm plaster infill
            var isBorderBeam = x < 36 || x > 475 || y < 36 || y > 475;
            var isCrossBeam = Math.Abs(x - 256) < 20 || Math.Abs(y - 256) < 20;
            var isDiagonal = Math.Abs((x - y) % 256) < 16 || Math.Abs((x + y) % 256) < 16;

            if (isBorderBeam || isCrossBeam || isDiagonal)
            {
                var woodGrain = (SmoothNoise(x * 0.08f, y * 0.08f, 12) - 0.5f) * 0.04f;
                return new Color(0.24f + woodGrain, 0.16f + woodGrain * 0.7f, 0.11f + woodGrain * 0.5f);
            }

            var plasterGrain = (SmoothNoise(x * 0.08f, y * 0.08f, 22) - 0.5f) * 0.04f;
            return new Color(0.65f + plasterGrain, 0.62f + plasterGrain, 0.56f + plasterGrain);
        });
    }

    private static ImageTexture CreateLavaTexture()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var pattern = (FractalNoise(x * 0.02f, y * 0.02f, 4, 301) - 0.5f) * 2.0f;

            if (pattern > 0.10f)
            {
                var heat = Mathf.Clamp((pattern - 0.10f) / 0.90f, 0f, 1f);
                var r = 0.96f + heat * 0.04f;
                var g = 0.40f + heat * 0.36f;
                var b = 0.03f + heat * 0.08f;
                return new Color(r, g, b);
            }
            if (pattern > -0.20f)
            {
                var heat = Mathf.Clamp((pattern + 0.20f) / 0.30f, 0f, 1f);
                var r = 0.74f + heat * 0.22f;
                var g = 0.15f + heat * 0.25f;
                var b = 0.01f + heat * 0.02f;
                return new Color(r, g, b);
            }

            var crustGrain = (SmoothNoise(x * 0.08f, y * 0.08f, 303) - 0.5f) * 0.04f;
            return new Color(0.18f + crustGrain, 0.10f + crustGrain * 0.5f, 0.08f + crustGrain * 0.3f);
        });
    }

    private static ImageTexture CreateLavaEmission()
    {
        const int size = 512;
        return CreateTexture(size, size, (x, y) =>
        {
            var pattern = (FractalNoise(x * 0.02f, y * 0.02f, 4, 301) - 0.5f) * 2.0f;
            if (pattern > 0.10f)
            {
                var heat = Mathf.Clamp((pattern - 0.10f) / 0.90f, 0f, 1f);
                return new Color(0.98f, 0.40f + heat * 0.35f, 0.04f + heat * 0.08f);
            }
            if (pattern > -0.20f)
            {
                var heat = Mathf.Clamp((pattern + 0.20f) / 0.30f, 0f, 1f);
                return new Color(0.60f + heat * 0.38f, 0.10f + heat * 0.30f, 0.01f);
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

        var wallTex = LoadTextureOrFallback("res://assets/models/town/T_Brick_BaseColor.png", CreateStoneWallTexture);
        var wallNormal = LoadTextureOrFallback("res://assets/models/town/T_Brick_Normal.png", CreateStoneWallNormal);
        var wallRoughness = LoadTextureOrFallback("res://assets/models/town/T_Brick_Roughness.png", CreateStoneWallRoughness);

        var floorTex = LoadTextureOrFallback("res://assets/models/town/T_UnevenBrick_BaseColor.png", CreateFloorTexture);
        var floorNormal = LoadTextureOrFallback("res://assets/models/town/T_UnevenBrick_Normal.png", CreateFloorNormal);
        var floorRoughness = LoadTextureOrFallback("res://assets/models/town/T_UnevenBrick_Roughness.png", CreateFloorRoughness);

        var ceilingTex = LoadTextureOrFallback("res://assets/models/town/T_RockTrim_BaseColor.png", CreateCeilingTexture);
        var ceilingNormal = LoadTextureOrFallback("res://assets/models/town/T_RockTrim_Normal.png", CreateCeilingNormal);

        var woodDoorTex = LoadTextureOrFallback("res://assets/models/town/T_WoodTrim_BaseColor.png", CreateWoodDoorTexture);
        var woodDoorNormal = LoadTextureOrFallback("res://assets/models/town/T_WoodTrim_Normal.png", CreateWoodDoorNormal);
        var woodDoorRoughness = LoadTextureOrFallback("res://assets/models/town/T_WoodTrim_Roughness.png", () => null);

        var storeTex = LoadTextureOrFallback("res://assets/models/town/T_Plaster_BaseColor.png", CreateStoreTexture);
        var storeNormal = LoadTextureOrFallback("res://assets/models/town/T_Plaster_Normal.png", CreateStoneWallNormal);
        var storeRoughness = LoadTextureOrFallback("res://assets/models/town/T_Plaster_ORM.png", () => null);

        var magmaTex = CreateMagmaTexture();
        var magmaEmission = CreateMagmaEmission();
        var quartzTex = CreateQuartzTexture();
        var lavaTex = CreateLavaTexture();
        var lavaEmission = CreateLavaEmission();

        _wallMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.85f,
            RoughnessTexture = wallRoughness,
            Roughness = 0.80f,
            Metallic = 0.02f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
        };

        _floorMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = floorTex,
            NormalEnabled = true,
            NormalTexture = floorNormal,
            NormalScale = 0.80f,
            RoughnessTexture = floorRoughness,
            Roughness = 0.74f,
            Metallic = 0.04f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
        };

        _ceilingMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = ceilingTex,
            NormalEnabled = true,
            NormalTexture = ceilingNormal,
            NormalScale = 0.55f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.95f,
            Metallic = 0.0f,
        };

        _magmaMaterial = CreateEmissiveTerrainMaterial(
            magmaTex,
            magmaEmission,
            new Color(1.0f, 0.40f, 0.06f),
            1.25f,
            0.72f,
            0.04f,
            0.5f);

        _quartzMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = quartzTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.75f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.50f,
            Metallic = 0.10f,
        };

        _storeMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = storeTex,
            NormalEnabled = true,
            NormalTexture = storeNormal,
            NormalScale = 0.65f,
            RoughnessTexture = storeRoughness,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
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
            NormalScale = 0.75f,
            RoughnessTexture = wallRoughness,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.88f,
            Metallic = 0.02f,
        };

        _doorWoodMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = woodDoorTex,
            NormalEnabled = true,
            NormalTexture = woodDoorNormal,
            NormalScale = 0.80f,
            RoughnessTexture = woodDoorRoughness,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.75f,
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
                NormalEnabled = true,
                NormalTexture = woodDoorNormal,
                NormalScale = 0.75f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Roughness = 0.68f,
                Metallic = 0.15f,
            };
        }

        _stairsMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = wallTex,
            NormalEnabled = true,
            NormalTexture = wallNormal,
            NormalScale = 0.70f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
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
            NormalScale = 0.80f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.92f,
            Metallic = 0.02f,
        };

        _lavaMaterial = CreateEmissiveTerrainMaterial(
            lavaTex,
            lavaEmission,
            new Color(1.0f, 0.40f, 0.06f),
            1.25f,
            0.45f,
            0.0f,
            0.5f);
    }

    /// <summary>
    /// Constructs a high-performance triplanar shader material with instance-color-driven emission gating.
    /// In StandardMaterial3D, Emission is a uniform material property that ignores MultiMesh instance color,
    /// causing out-of-LOS or dark/unexplored tiles to incandescently glow through walls and memory fog.
    /// This shader modulates emission by instance alpha (<c>COLOR.a</c>), enabling 100% full incandescent glow
    /// in direct line of sight while completely extinguishing emission (0.0) in player memory or dark voids.
    /// </summary>
    private static ShaderMaterial CreateEmissiveTerrainMaterial(
        Texture2D albedoTex,
        Texture2D emissionTex,
        Color emissionColor,
        float emissionEnergy,
        float roughness,
        float metallic,
        float uvScale = 0.5f)
    {
        var shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D emission_texture : source_color, filter_linear_mipmap_anisotropic;
uniform vec4 emission_color : source_color = vec4(1.0, 0.40, 0.06, 1.0);
uniform float emission_energy : hint_range(0.0, 16.0) = 1.25;
uniform float uv_scale = 0.5;
uniform float roughness : hint_range(0.0, 1.0) = 0.45;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;

varying vec3 world_pos;
varying vec3 world_normal;
varying vec4 v_color;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    v_color = COLOR;
}

void fragment() {
    vec3 w_norm = abs(world_normal);
    float sum = w_norm.x + w_norm.y + w_norm.z;
    w_norm = sum > 0.0001 ? w_norm / sum : vec3(0.0, 1.0, 0.0);

    vec2 uv_y = world_pos.xz * uv_scale;
    vec2 uv_x = world_pos.zy * uv_scale;
    vec2 uv_z = world_pos.xy * uv_scale;

    vec3 alb_x = texture(albedo_texture, uv_x).rgb;
    vec3 alb_y = texture(albedo_texture, uv_y).rgb;
    vec3 alb_z = texture(albedo_texture, uv_z).rgb;
    vec3 alb = (alb_x * w_norm.x + alb_y * w_norm.y + alb_z * w_norm.z) * v_color.rgb;

    vec3 em_x = texture(emission_texture, uv_x).rgb;
    vec3 em_y = texture(emission_texture, uv_y).rgb;
    vec3 em_z = texture(emission_texture, uv_z).rgb;
    vec3 em_sample = em_x * w_norm.x + em_y * w_norm.y + em_z * w_norm.z;
    vec3 emi = em_sample * emission_color.rgb * emission_energy * v_color.a;

    ALBEDO = alb;
    ROUGHNESS = roughness;
    METALLIC = metallic;
    EMISSION = emi;
}
"
        };

        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("albedo_texture", albedoTex);
        mat.SetShaderParameter("emission_texture", emissionTex);
        mat.SetShaderParameter("emission_color", emissionColor);
        mat.SetShaderParameter("emission_energy", emissionEnergy);
        mat.SetShaderParameter("uv_scale", uvScale);
        mat.SetShaderParameter("roughness", roughness);
        mat.SetShaderParameter("metallic", metallic);
        return mat;
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

    private static Material MaterialFor(Kind k) => k switch
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

            if (_sunLight != null)
            {
                _sunLight.Visible = _outdoors;
                _sunLight.LightEnergy = _outdoors ? 1.35f : 0.0f;
            }

            // Transition to new depth biome profile
            _targetBiome = BiomeProfile.GetForDepth(depth);
            _currentBiome = _targetBiome;
            _env.BackgroundColor = _targetBiome.BackgroundColor;
            _env.AmbientLightColor = _targetBiome.AmbientLightColor;
            _env.AmbientLightEnergy = _targetBiome.AmbientLightEnergy;
            _env.FogLightColor = _targetBiome.FogLightColor;
            _env.FogDensity = _targetBiome.FogDensity;
            _env.TonemapExposure = _targetBiome.TonemapExposure;

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

                // Any adjacent attacking monsters immediately face the player
                foreach (var monster in _activeMonsters.Values)
                {
                    if (!monster.IsAfraid && !monster.IsAsleep)
                    {
                        var toP = _targetPos - monster.CurrentPos;
                        toP.Y = 0;
                        if (toP.LengthSquared() <= (Cell * 1.6f) * (Cell * 1.6f) && toP.LengthSquared() > 0.001f)
                        {
                            MonsterModelResolver.FaceTarget(monster, _targetPos, snapImmediately: true);
                        }
                    }
                }
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

        // Status condition audio warnings
        if (player.TryGetProperty("poisoned", out var psnProp))
        {
            var psn = psnProp.GetInt32();
            if (psn > 0 && _lastPoisoned <= 0) AudioManager.Play(SoundEffect.Poison);
            _lastPoisoned = psn;
        }
        if (player.TryGetProperty("confused", out var cnfProp))
        {
            var cnf = cnfProp.GetInt32();
            if (cnf > 0 && _lastConfused <= 0) AudioManager.Play(SoundEffect.Confused);
            _lastConfused = cnf;
        }
        if (player.TryGetProperty("blind", out var blnProp))
        {
            var bln = blnProp.GetInt32();
            if (bln > 0 && _lastBlind <= 0) AudioManager.Play(SoundEffect.Blind);
            _lastBlind = bln;
        }
        if (player.TryGetProperty("paralyzed", out var przProp))
        {
            var prz = przProp.GetInt32();
            if (prz > 0 && _lastParalyzed <= 0) AudioManager.Play(SoundEffect.Paralyzed);
            _lastParalyzed = prz;
        }
        if (player.TryGetProperty("afraid", out var afrProp))
        {
            var afr = afrProp.GetInt32();
            if (afr > 0 && _lastAfraid <= 0) AudioManager.Play(SoundEffect.Afraid);
            _lastAfraid = afr;
        }
        if (player.TryGetProperty("food", out var fdProp))
        {
            var fd = fdProp.GetInt32();
            if (fd < 200 && _lastFood >= 200) AudioManager.Play(SoundEffect.Hunger);
            _lastFood = fd;
        }

        RebuildEntities(frame);
        _viewModel?.UpdateEquipment(player, depth, CurrentHeightRatio);
        ProcessCombatEvents(frame);
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

        // View angle compensation: tall characters tilt view down slightly (negative pitch),
        // short characters tilt view up slightly (positive pitch) to keep dungeon corridors and foes in natural focus.
        _targetPitch = Mathf.DegToRad((BaseEyeHeight - CurrentEyeHeight) * 10.0f);

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

    private static int FlagAt(JsonElement map, int x, int y)
    {
        if (y < 0 || y >= map.GetProperty("h").GetInt32()
            || x < 0 || x >= map.GetProperty("w").GetInt32())
        {
            return 0;
        }
        var l = map.GetProperty("rows")[y].GetProperty("l").GetString() ?? "";
        if (x >= l.Length)
        {
            return 0;
        }
        return AngbandColors.HexVal(l[x]);
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
            or Kind.StairsDown or Kind.StairsUp or Kind.Lava or Kind.Rubble
            || IsStoreKind(k);

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
        const int sightRange = 32;
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
                var lighting = (flag >> 2) & 0x3;

                var feat = (AngbandColors.HexVal(feats[x * 2]) << 4) | AngbandColors.HexVal(feats[x * 2 + 1]);
                var kind = KindOf(feat);

                // Unexplored dark space (not known and not in view) or skip tiles must NOT be rendered.
                // True fog-of-war: open unexplored areas remain pure darkness fading into fog.
                if (kind == Kind.Skip || (!known && !inView))
                {
                    continue;
                }

                if (!_outdoors)
                {
                    var dxP = x - px;
                    var dyP = y - py;
                    if (dxP * dxP + dyP * dyP > 28 * 28)
                    {
                        continue;
                    }
                }

                // For known/in-view wall tiles, infer visibility from adjacent lit walkable floor/doors
                // so walls bordering illuminated corridors/rooms render cleanly without pitch-black gaps.
                var isWallKind = kind is Kind.Wall or Kind.Magma or Kind.Quartz;
                    if (isWallKind)
                    {
                        // Interior rock culling: if all 8 neighbors are solid walls/skip (no adjacent floor, door, or open space),
                        // this wall is completely buried within the dungeon bedrock and has no exposed visible faces.
                        bool hasAdjacentOpen = false;
                        for (int dy = -1; dy <= 1 && !hasAdjacentOpen; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !hasAdjacentOpen; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                var nx = x + dx;
                                var ny = y + dy;
                                if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                                {
                                    var nkind = KindOf(FeatAt(map, nx, ny));
                                    if (IsWalkableOrPortal(nkind))
                                    {
                                        hasAdjacentOpen = true;
                                    }
                                }
                            }
                        }
                        if (!hasAdjacentOpen)
                        {
                            continue;
                        }

                        if (!inView)
                        {
                            for (int dy = -1; dy <= 1 && !inView; dy++)
                            {
                                for (int dx = -1; dx <= 1 && !inView; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    var nx = x + dx;
                                    var ny = y + dy;
                                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                                    {
                                        var nflag = FlagAt(map, nx, ny);
                                        var nInView = (nflag & 0x2) != 0;
                                        if (nInView)
                                        {
                                            var nfeat = FeatAt(map, nx, ny);
                                            var nkind = KindOf(nfeat);
                                            if (IsWalkableOrPortal(nkind))
                                            {
                                                inView = true;
                                                if (lighting == 3) lighting = (nflag >> 2) & 0x3;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                // Determine lighting level: 0=LOS, 1=torch, 2=lit room/feature, 3=dark
                if (lighting == 3 && inView)
                {
                    // If player is close (within torch radius), illuminate the wall face
                    var distSq = (x - px) * (x - px) + (y - py) * (y - py);
                    if (distSq <= (_torchRadius + 1) * (_torchRadius + 1))
                    {
                        lighting = 1;
                    }
                }

                Color shade;
                if (inView && (_outdoors || lighting == 2 || lighting is 0 or 1))
                {
                    shade = BaseColour(kind);
                }
                else if (inView && lighting == 3)
                {
                    // In direct LOS but unlit / dark corridor
                    shade = BaseColour(kind) * new Color(0.24f, 0.26f, 0.32f);
                }
                else
                {
                    // Explored / Known but currently out-of-LOS (Fog of war / player memory).
                    // Dimmed with subtle cool slate memory tint so architecture remains clear
                    // without glowing in unilluminated areas.
                    shade = BaseColour(kind) * (lighting == 2
                        ? new Color(0.55f, 0.58f, 0.65f)
                        : new Color(0.22f, 0.24f, 0.30f));
                }

                // Alpha channel modulates dynamic shader emission (1.0 = active emission in LOS, 0.0 = zero emission in memory/void)
                shade.A = inView ? 1.0f : 0.0f;

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

                // Ceiling over walkable areas and doorways in the dungeon (including subterranean lava pools)
                if (!_outdoors && (kind is Kind.Floor or Kind.StairsDown or Kind.StairsUp
                    or Kind.DoorClosed or Kind.DoorOpen or Kind.DoorBroken or Kind.Rubble or Kind.Lava || IsStoreKind(kind)))
                {
                    _xf[Kind.Ceiling].Add(new Transform3D(Basis.Identity,
                        new Vector3(x * Cell, WallHeight + 0.05f, y * Cell)));
                    Color cc;
                    if (!inView)
                    {
                        cc = BaseColour(Kind.Ceiling) * (lighting == 2
                            ? new Color(0.50f, 0.52f, 0.60f)
                            : new Color(0.20f, 0.22f, 0.28f));
                    }
                    else if (lighting == 3)
                    {
                        cc = BaseColour(Kind.Ceiling) * new Color(0.20f, 0.22f, 0.28f);
                    }
                    else
                    {
                        cc = BaseColour(Kind.Ceiling);
                    }
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

        const int labelRange = 32;
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
                if (!known && !inView)
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
            VisibilityRangeEnd = noDepthTest ? 45.0f : 25.0f,
            VisibilityRangeEndMargin = 4.0f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
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
    public System.Collections.Generic.IReadOnlyDictionary<int, (string Name, bool Passable)> Features { get; set; }

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
        var map = frame.TryGetProperty("map", out var mProp) ? mProp : default;

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

            // Visibility & Illumination rules:
            // Check tile flag: bit 1 = inView (line of sight), bits 2-3 = lighting (0=LOS lit, 1=torch, 2=room lit, 3=dark)
            var flag = map.ValueKind == JsonValueKind.Object ? FlagAt(map, gx, gy) : 0;
            var inView = _outdoors || (flag & 0x2) != 0;
            var lighting = (flag >> 2) & 0x3;
            // In outdoors or when actively in view and illuminated (or nearby torch radius), label is visible
            var isIlluminated = _outdoors || lighting is 0 or 1 or 2;
            var isVisibleInView = inView && isIlluminated;

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

                MonsterModelResolver.UpdateMonsterVisual(entity, m, isTargeted, isVisibleInView);

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
                            MonsterModelResolver.PlayWalkAnimation(entity);
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
                MonsterModelResolver.UpdateMonsterVisual(newEntity, m, isTargeted, isVisibleInView);
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

            var flag = map.ValueKind == JsonValueKind.Object ? FlagAt(map, gx, gy) : 0;
            var inView = _outdoors || (flag & 0x2) != 0;
            var lighting = (flag >> 2) & 0x3;
            var isIlluminated = _outdoors || lighting is 0 or 1 or 2;
            var isVisibleInView = inView && isIlluminated;

            if (_activeItems.TryGetValue(key, out var existingItemNode))
            {
                ItemModelResolver.UpdateItemVisibility(existingItemNode, isVisibleInView);
            }
            else
            {
                var pos = new Vector3(gx * Cell, 0, gy * Cell);
                var itemNode = ItemModelResolver.CreateItemNode(o, pos, isVisibleInView);
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
        string[] dirKeys = { "up", "pageup", "right", "pagedown", "down", "end", "left", "home" };
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

    /// <summary>
    /// Identifies which active monster is performing an attack or action mentioned in a message.
    /// Matches by monster race name, or defaults to the closest adjacent/awake candidate.
    /// </summary>
    private MonsterEntity FindAttackingMonster(string messageText, bool isMelee)
    {
        if (_activeMonsters.Count == 0 || string.IsNullOrEmpty(messageText))
            return null;

        MonsterEntity bestMatch = null;
        float bestDistSq = float.MaxValue;
        var lowerMsg = messageText.ToLowerInvariant();

        // 1. Check for exact or substring match with monster RaceName
        foreach (var monster in _activeMonsters.Values)
        {
            if (string.IsNullOrEmpty(monster.RaceName)) continue;
            var lowerRace = monster.RaceName.ToLowerInvariant();

            if (lowerMsg.Contains(lowerRace) ||
                (lowerRace.Length > 4 && lowerMsg.Contains(lowerRace.Substring(0, Math.Min(lowerRace.Length, 8)))))
            {
                var d2 = monster.CurrentPos.DistanceSquaredTo(_targetPos);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    bestMatch = monster;
                }
            }
        }

        if (bestMatch != null)
            return bestMatch;

        // 2. Proximity fallback: for melee, pick closest adjacent awake monster
        var maxDistSq = isMelee ? (Cell * 1.6f) * (Cell * 1.6f) : float.MaxValue;
        foreach (var monster in _activeMonsters.Values)
        {
            if (monster.IsAsleep) continue;
            var d2 = monster.CurrentPos.DistanceSquaredTo(_targetPos);
            if (d2 <= maxDistSq && d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestMatch = monster;
            }
        }

        return bestMatch;
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

        // Check if player is being attacked by a creature (melee, ranged, breath, spells, or gaze)
        bool isMonsterAttackingPlayer =
            lower.Contains("hits you") || lower.Contains("bites you") || lower.Contains("touches you") ||
            lower.Contains("claws you") || lower.Contains("crushes you") || lower.Contains("burns you") ||
            lower.Contains("shoots you") || lower.Contains("stings you") || lower.Contains("casts a spell") ||
            lower.Contains("casts an evil spell") || lower.Contains("misses you") || lower.Contains("breathes") ||
            lower.Contains("spits") || lower.Contains("gazes") || lower.Contains("wails") ||
            lower.Contains("screams") || lower.Contains("shrieks") || lower.Contains("drains you") ||
            lower.Contains("kicks you") || lower.Contains("butts you") || lower.Contains("charges you") ||
            lower.Contains("engulfs you") || lower.Contains("lashes you") || lower.Contains("moans");

        if (isMonsterAttackingPlayer)
        {
            bool isRangedOrSpell = lower.Contains("casts") || lower.Contains("shoots") || lower.Contains("breathes") || lower.Contains("spits") || lower.Contains("burns");
            var attacker = FindAttackingMonster(text, isMelee: !isRangedOrSpell);

            if (attacker != null)
            {
                // Attacking creature faces player immediately and triggers attack animation/lunge
                MonsterModelResolver.TriggerAttackAction(attacker, _targetPos);
                PlayMonsterVocal(attacker.Glyph);
            }
            else
            {
                // If specific monster could not be resolved from message, orient all adjacent active monsters
                foreach (var m in _activeMonsters.Values)
                {
                    if (!m.IsAfraid && !m.IsAsleep)
                    {
                        var toP = _targetPos - m.CurrentPos;
                        toP.Y = 0;
                        if (toP.LengthSquared() <= (Cell * 1.6f) * (Cell * 1.6f) && toP.LengthSquared() > 0.001f)
                        {
                            MonsterModelResolver.TriggerAttackAction(m, _targetPos);
                            PlayMonsterVocal(m.Glyph);
                        }
                    }
                }
            }
        }

        // Player taking damage
        if (lower.Contains("hits you") || lower.Contains("bites you") || lower.Contains("touches you") ||
            lower.Contains("claws you") || lower.Contains("crushes you") || lower.Contains("burns you") ||
            lower.Contains("shoots you") || lower.Contains("stings you") || lower.Contains("casts a spell") ||
            lower.Contains("breathes"))
        {
            AddTrauma(0.40f);
            _viewModel?.TriggerHurt();
            if (lower.Contains("burns") || lower.Contains("fire")) AudioManager.Play(SoundEffect.SpellFire);
            else if (lower.Contains("frost") || lower.Contains("cold")) AudioManager.Play(SoundEffect.SpellCold);
            else if (lower.Contains("lightning") || lower.Contains("elec")) AudioManager.Play(SoundEffect.SpellLightning);
            else if (lower.Contains("poison") || lower.Contains("acid")) AudioManager.Play(SoundEffect.SpellPoison);
            else AudioManager.Play(SoundEffect.PlayerHurt);
            var hitPos = _targetPos + forward * 0.8f + (GD.Randf() > 0.5f ? right * 0.3f : -right * 0.3f);
            SpawnFloatingText("OUCH!", hitPos, new Color(1.0f, 0.30f, 0.30f), 1.1f);
            SpawnHitSparks(hitPos, -forward, new Color(0.9f, 0.2f, 0.2f), 14);

            if (lower.Contains("casts a spell") || lower.Contains("shoots you") || lower.Contains("burns you") || lower.Contains("breathes"))
            {
                var attacker = FindAttackingMonster(text, isMelee: false);
                var srcPos = attacker != null
                    ? attacker.CurrentPos + new Vector3(0, attacker.ModelHeight * 0.5f, 0)
                    : _targetPos + forward * 4.0f + new Vector3(0, 0.4f, 0);

                var projCol = lower.Contains("burns") || lower.Contains("fire") ? new Color(1.0f, 0.45f, 0.10f)
                            : lower.Contains("frost") || lower.Contains("cold") ? new Color(0.40f, 0.85f, 1.0f)
                            : lower.Contains("lightning") || lower.Contains("elec") ? new Color(1.0f, 0.95f, 0.30f)
                            : lower.Contains("poison") || lower.Contains("acid") ? new Color(0.35f, 0.95f, 0.30f)
                            : new Color(0.85f, 0.35f, 1.0f);

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
            if (lower.Contains("fire") || lower.Contains("flame")) AudioManager.Play(SoundEffect.SpellFire);
            else if (lower.Contains("frost") || lower.Contains("cold") || lower.Contains("ice")) AudioManager.Play(SoundEffect.SpellCold);
            else if (lower.Contains("lightning") || lower.Contains("elec") || lower.Contains("spark")) AudioManager.Play(SoundEffect.SpellLightning);
            else if (lower.Contains("poison") || lower.Contains("acid") || lower.Contains("stinking")) AudioManager.Play(SoundEffect.SpellPoison);
            else AudioManager.Play(SoundEffect.SpellCast);

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

            var defender = FindAttackingMonster(text, isMelee: true);
            if (defender != null)
            {
                MonsterModelResolver.FaceTarget(defender, _targetPos, snapImmediately: false);
            }
        }
        // Misses
        else if (lower.Contains("you miss") || lower.Contains("misses you"))
        {
            if (lower.Contains("you miss"))
            {
                _viewModel?.TriggerAttack();
                var defender = FindAttackingMonster(text, isMelee: true);
                if (defender != null)
                {
                    MonsterModelResolver.FaceTarget(defender, _targetPos, snapImmediately: false);
                }
            }
            AudioManager.Play(SoundEffect.MeleeSwing);
            SpawnFloatingText("MISS", spawnInFront, new Color(0.70f, 0.72f, 0.78f), 0.9f);
        }
        // Defense / Block / Deflect
        else if (lower.Contains("you block") || lower.Contains("blocks your") || lower.Contains("parr"))
        {
            AudioManager.Play(SoundEffect.ShieldBlock);
            SpawnFloatingText("BLOCKED", spawnInFront, new Color(0.85f, 0.85f, 0.90f), 1.0f);
        }
        else if (lower.Contains("deflect") || lower.Contains("glances off"))
        {
            AudioManager.Play(SoundEffect.ArmorDeflect);
            SpawnFloatingText("DEFLECTED", spawnInFront, new Color(0.80f, 0.82f, 0.88f), 0.9f);
        }
        // Wall bump / Obstacle
        else if (lower.Contains("there is a wall") || lower.Contains("door is closed") || lower.Contains("cannot move into"))
        {
            AudioManager.Play(SoundEffect.WallBump);
            AddTrauma(0.08f);
        }
        // Consumables: Potion, Scroll, Food
        else if (lower.Contains("you drink") || lower.Contains("you quaff"))
        {
            AudioManager.Play(SoundEffect.Quaff);
            SpawnFloatingText("QUAFF", spawnInFront, new Color(0.40f, 0.85f, 1.0f), 1.0f);
        }
        else if (lower.Contains("you read a scroll") || lower.Contains("you recite"))
        {
            AudioManager.Play(SoundEffect.Scroll);
            SpawnFloatingText("READ", spawnInFront, new Color(1.0f, 0.90f, 0.50f), 1.0f);
        }
        else if (lower.Contains("you eat") || lower.Contains("delicious") || lower.Contains("ration of food") || lower.Contains("feed on"))
        {
            AudioManager.Play(SoundEffect.Eat);
            SpawnFloatingText("EAT", spawnInFront, new Color(0.95f, 0.70f, 0.30f), 1.0f);
        }
        // Environment: Chest, Traps, Teleport
        else if (lower.Contains("chest") && (lower.Contains("open") || lower.Contains("unlock")))
        {
            AudioManager.Play(SoundEffect.ChestOpen);
        }
        else if (lower.Contains("disarm") && lower.Contains("trap"))
        {
            AudioManager.Play(SoundEffect.TrapDisarm);
            SpawnFloatingText("DISARMED", spawnInFront, new Color(0.40f, 1.0f, 0.60f), 1.0f);
        }
        else if (lower.Contains("triggers a trap") || lower.Contains("springs a trap") || lower.Contains("you are caught in a trap"))
        {
            AudioManager.Play(SoundEffect.TrapTrigger);
            AddTrauma(0.25f);
            SpawnFloatingText("TRAP!", spawnInFront, new Color(1.0f, 0.30f, 0.30f), 1.2f);
        }
        else if (lower.Contains("teleport") || lower.Contains("blink") || lower.Contains("phase door"))
        {
            AudioManager.Play(SoundEffect.Teleport);
            SpawnFloatingText("TELEPORT", spawnInFront, new Color(0.75f, 0.40f, 1.0f), 1.2f);
        }
        // Pickups: Gold vs Items
        else if (lower.Contains("gold") || lower.Contains("coins") || lower.Contains("pieces of"))
        {
            AudioManager.Play(SoundEffect.GoldPickup);
        }
        else if (lower.Contains("you see") || lower.Contains("you have found") || lower.Contains("you pick up"))
        {
            AudioManager.Play(SoundEffect.ItemPickup);
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

    private static void PlayMonsterVocal(char glyph)
    {
        switch (glyph)
        {
            case 'o': case 'T': case 'k': case 'C': case 'Z':
                AudioManager.Play(SoundEffect.MonsterGrowl);
                break;
            case 's': case 'R': case 'H': case 'J': case 'n':
                AudioManager.Play(SoundEffect.MonsterHiss);
                break;
            case 'G': case 'W': case 'V': case 'L': case 'v': case 'M':
                AudioManager.Play(SoundEffect.GhostWail);
                break;
            case 'd': case 'D': case 'U': case 'B':
                AudioManager.Play(SoundEffect.DragonRoar);
                break;
            case 'r': case 'b':
                AudioManager.Play(SoundEffect.RodentSqueak);
                break;
            case 'S': case 'I': case 'a':
                AudioManager.Play(SoundEffect.InsectChitin);
                break;
            default:
                AudioManager.Play(SoundEffect.MonsterGrunt);
                break;
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

        _pitch = Mathf.Lerp(_pitch, _targetPitch, t);
        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);

        // Subtle camera roll on turn and pitch delta for viewmodel inertia
        var yawDelta = Mathf.Wrap(_targetYaw - _yaw, -Mathf.Pi, Mathf.Pi);
        var pitchDelta = _targetPitch - _pitch;
        var roll = Mathf.Clamp(-yawDelta * 0.08f, -0.04f, 0.04f);
        _camera.Rotation = new Vector3(_pitch, _yaw, roll);

        // Update first-person viewmodel motion, bobbing & inertia sway
        _viewModel?.ProcessMotion(delta, isMoving, yawDelta, pitchDelta);

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
        // Ultra-subtle, gentle ambient warmth modulation (barely perceptible, zero distracting flicker)
        var subtleFlicker = 1.0f + 0.008f * Mathf.Sin((float)_flicker * 1.6f);
        var targetEnergy = (_targetBiome?.TorchLightEnergy ?? 2.4f) * subtleFlicker;
        var targetColor = _targetBiome?.TorchLightColor ?? new Color(1.0f, 0.86f, 0.64f);

        _torch.OmniRange = (_torchRadius + 2.5f) * Cell + 2.0f;
        _torch.LightEnergy = Mathf.Lerp(_torch.LightEnergy, targetEnergy, (float)Math.Min(1.0, delta * 4.0));
        _torch.LightColor = _torch.LightColor.Lerp(targetColor, (float)Math.Min(1.0, delta * 4.0));

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
                    MonsterModelResolver.PlayIdleAnimation(monster);
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
                else if (toPlayer.LengthSquared() <= (Cell * 2.5f) * (Cell * 2.5f) && toPlayer.LengthSquared() > 0.001f)
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
            else if (!monster.IsMoving && !monster.IsAfraid && !monster.IsAsleep)
            {
                // Continuously orient adjacent awake monsters towards player so they always face player in melee
                var toPlayer = _targetPos - monster.CurrentPos;
                toPlayer.Y = 0;
                if (toPlayer.LengthSquared() <= (Cell * 1.6f) * (Cell * 1.6f) && toPlayer.LengthSquared() > 0.001f)
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
