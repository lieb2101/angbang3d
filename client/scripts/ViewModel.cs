using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// First-person viewmodel displaying the player's held equipment
/// (left hand torch/shield/book, right hand weapon/wand/staff/bow) parented to the main Camera3D.
/// Completely unobstructed by 1st-person hands so only held items are visible.
/// Handles walk bobbing, inertia sway, and kinetic attack/hit tweens.
/// </summary>
public partial class ViewModel : Node3D
{
    private Node3D _swayRoot;

    // Item slots (parented to sway root)
    private Node3D _leftHandSlot;
    private Node3D _rightHandSlot;
    private Node3D _leftItemSlot;
    private Node3D _rightItemSlot;

    // Dynamic Torch Flame & Smoke VFX (Priority 3)
    private CpuParticles3D _leftTorchFlame;
    private CpuParticles3D _leftTorchSmoke;
    private OmniLight3D _leftTorchLight;

    // Dynamic Ego Weapon Aura & Light (Priority 3)
    private CpuParticles3D _rightWeaponAura;
    private OmniLight3D _rightWeaponLight;

    // Cache loaded scenes and instantiated weapon/item nodes
    private static readonly Dictionary<string, PackedScene> _sceneCache = new();
    private readonly Dictionary<string, Node3D> _modelCache = new();
    private string _currentLeftModel = "";
    private string _currentRightModel = "";

    // Rest transforms (relative to sway root) - calibrated to clean, natural lower corners
    private Vector3 _leftRestPos = new(-0.26f, -0.32f, -0.45f);
    private Vector3 _leftRestRot = new(Mathf.DegToRad(14), Mathf.DegToRad(16), Mathf.DegToRad(-8));
    private Vector3 _rightRestPos = new(0.26f, -0.32f, -0.45f);
    private Vector3 _rightRestRot = new(Mathf.DegToRad(18), Mathf.DegToRad(-16), Mathf.DegToRad(10));

    // Motion & sway state
    private float _bobTimer;
    private Vector3 _swayOffsetPos;
    private Vector3 _swayOffsetRot;
    private float _currentHeightRatio = 1.0f;

    // Action impulse state
    private float _actionTime;
    private float _actionDuration;
    private Vector3 _actionPosOffset;
    private Vector3 _actionRotOffset;
    private float _recoilIntensity;

    public override void _Ready()
    {
        _swayRoot = new Node3D { Name = "SwayRoot" };
        AddChild(_swayRoot);

        // Create Left Item Slot hierarchy
        _leftHandSlot = new Node3D { Name = "LeftItemHolder", Position = _leftRestPos, Rotation = _leftRestRot };
        _swayRoot.AddChild(_leftHandSlot);

        _leftItemSlot = new Node3D { Name = "LeftItemSlot" };
        _leftHandSlot.AddChild(_leftItemSlot);

        // Torch Flame particle emitter - subtle compact steady flame
        _leftTorchFlame = new CpuParticles3D
        {
            Name = "TorchFlameVfx",
            Amount = 8,
            Lifetime = 0.25f,
            LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.015f,
            Direction = Vector3.Up,
            Spread = 10f,
            InitialVelocityMin = 0.15f,
            InitialVelocityMax = 0.35f,
            Gravity = new Vector3(0, 0.25f, 0),
            ScaleAmountMin = 0.015f,
            ScaleAmountMax = 0.030f,
            Color = new Color(1.0f, 0.72f, 0.18f, 0.90f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1.0f, 0.75f, 0.20f),
            },
            Position = new Vector3(0, 0.30f, 0),
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchFlame);

        // Torch Smoke particle emitter - faint subtle rising wisp
        _leftTorchSmoke = new CpuParticles3D
        {
            Name = "TorchSmokeVfx",
            Amount = 4,
            Lifetime = 0.40f,
            LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.015f,
            Direction = Vector3.Up,
            Spread = 15f,
            InitialVelocityMin = 0.08f,
            InitialVelocityMax = 0.18f,
            Gravity = new Vector3(0, 0.15f, 0),
            ScaleAmountMin = 0.018f,
            ScaleAmountMax = 0.035f,
            Color = new Color(0.25f, 0.25f, 0.25f, 0.12f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.35f, 0.35f, 0.38f, 0.20f),
            },
            Position = new Vector3(0, 0.34f, 0),
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchSmoke);

        _leftTorchLight = new OmniLight3D
        {
            Name = "TorchTipLight",
            LightColor = new Color(1.0f, 0.80f, 0.45f),
            LightEnergy = 0.0f,
            OmniRange = 1.0f,
            OmniAttenuation = 1.0f,
            ShadowEnabled = false,
            Position = new Vector3(0, 0.30f, 0),
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchLight);

        // Create Right Item Slot hierarchy
        _rightHandSlot = new Node3D { Name = "RightItemHolder", Position = _rightRestPos, Rotation = _rightRestRot };
        _swayRoot.AddChild(_rightHandSlot);

        _rightItemSlot = new Node3D { Name = "RightItemSlot" };
        _rightHandSlot.AddChild(_rightItemSlot);

        // Right Weapon Elemental Ego Aura
        _rightWeaponAura = new CpuParticles3D
        {
            Name = "WeaponEgoAura",
            Amount = 26,
            Lifetime = 0.40f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.04f, 0.22f, 0.04f),
            Direction = Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = 0.15f,
            InitialVelocityMax = 0.45f,
            Gravity = new Vector3(0, 0.1f, 0),
            ScaleAmountMin = 0.025f,
            ScaleAmountMax = 0.055f,
            Color = new Color(1.0f, 0.60f, 0.10f, 0.85f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = Colors.White,
            },
            Visible = false,
        };
        _rightItemSlot.AddChild(_rightWeaponAura);

        _rightWeaponLight = new OmniLight3D
        {
            Name = "WeaponEgoLight",
            LightColor = new Color(1.0f, 0.60f, 0.15f),
            LightEnergy = 0.8f,
            OmniRange = 1.8f,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            Visible = false,
        };
        _rightItemSlot.AddChild(_rightWeaponLight);
    }

    /// <summary>
    /// Creates a dedicated, high-fidelity 3D handheld wooden torch with tapered shaft,
    /// leather wrap, wrought iron collar, and glowing embers head.
    /// </summary>
    public static Node3D CreateHandheldTorchNode()
    {
        var root = new Node3D { Name = "HandheldTorch" };

        // 1. Tapered Wooden Shaft (octagonal cylinder from Y = -0.16m to Y = +0.26m)
        var shaftMesh = new CylinderMesh
        {
            TopRadius = 0.018f,
            BottomRadius = 0.013f,
            Height = 0.42f,
            RadialSegments = 8,
            Rings = 1,
        };
        var woodMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.34f, 0.22f, 0.13f),
            Roughness = 0.85f,
            Metallic = 0.0f,
        };
        var shaftInst = new MeshInstance3D
        {
            Name = "Shaft",
            Mesh = shaftMesh,
            MaterialOverride = woodMat,
            Position = new Vector3(0, 0.05f, 0),
        };
        root.AddChild(shaftInst);

        // 2. Leather Grip Wrap (around center grip Y = -0.02m to +0.10m)
        var gripMesh = new CylinderMesh
        {
            TopRadius = 0.019f,
            BottomRadius = 0.017f,
            Height = 0.12f,
            RadialSegments = 8,
            Rings = 1,
        };
        var gripMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.13f, 0.07f),
            Roughness = 0.90f,
            Metallic = 0.0f,
        };
        var gripInst = new MeshInstance3D
        {
            Name = "Grip",
            Mesh = gripMesh,
            MaterialOverride = gripMat,
            Position = new Vector3(0, 0.04f, 0),
        };
        root.AddChild(gripInst);

        // 3. Wrought Iron Collar & Crown (at Y = 0.24m)
        var collarMesh = new CylinderMesh
        {
            TopRadius = 0.028f,
            BottomRadius = 0.020f,
            Height = 0.06f,
            RadialSegments = 8,
            Rings = 1,
        };
        var ironMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.18f, 0.20f),
            Roughness = 0.35f,
            Metallic = 0.85f,
        };
        var collarInst = new MeshInstance3D
        {
            Name = "IronCollar",
            Mesh = collarMesh,
            MaterialOverride = ironMat,
            Position = new Vector3(0, 0.24f, 0),
        };
        root.AddChild(collarInst);

        // 4. Burning Pitch & Wrapped Linen Head (at Y = 0.27m)
        var headMesh = new CylinderMesh
        {
            TopRadius = 0.027f,
            BottomRadius = 0.023f,
            Height = 0.06f,
            RadialSegments = 8,
            Rings = 1,
        };
        var emberMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.08f, 0.06f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.35f, 0.05f),
            EmissionEnergyMultiplier = 2.2f,
            Roughness = 0.75f,
        };
        var headInst = new MeshInstance3D
        {
            Name = "PitchHead",
            Mesh = headMesh,
            MaterialOverride = emberMat,
            Position = new Vector3(0, 0.27f, 0),
        };
        root.AddChild(headInst);

        // 5. Glowing Hot Embers Top Dome (top cap at Y = 0.30m)
        var domeMesh = new SphereMesh
        {
            Radius = 0.024f,
            Height = 0.028f,
            RadialSegments = 8,
            Rings = 4,
        };
        var coreMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.50f, 0.10f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.70f, 0.15f),
            EmissionEnergyMultiplier = 3.5f,
            Roughness = 0.5f,
        };
        var domeInst = new MeshInstance3D
        {
            Name = "GlowingCore",
            Mesh = domeMesh,
            MaterialOverride = coreMat,
            Position = new Vector3(0, 0.30f, 0),
        };
        root.AddChild(domeInst);

        return root;
    }

    /// <summary>
    /// Updates player equipment and held 3D models based on bridge JSON frame data.
    /// </summary>
    public void UpdateEquipment(JsonElement player, int depth, float heightRatio = 1.0f)
    {
        _currentHeightRatio = heightRatio;
        var pClass = player.TryGetProperty("class", out var cProp) ? cProp.GetString() ?? "" : "";
        var lightRadius = player.TryGetProperty("light", out var lProp) ? lProp.GetInt32() : 0;
        var hasLightItem = player.TryGetProperty("light_item", out var liProp) && !string.IsNullOrEmpty(liProp.GetString());
        var lightItemName = hasLightItem ? liProp.GetString() ?? "" : "";
        var lowerLight = lightItemName.ToLowerInvariant();

        var weaponItem = player.TryGetProperty("weapon_item", out var wiProp) ? wiProp.GetString() : null;
        var weaponTval = player.TryGetProperty("weapon_tval", out var wtProp) ? wtProp.GetInt32() : 0;
        var bowItem = player.TryGetProperty("bow_item", out var biProp) ? biProp.GetString() : null;
        var bowTval = player.TryGetProperty("bow_tval", out var btProp) ? btProp.GetInt32() : 0;
        var shieldItem = player.TryGetProperty("shield_item", out var siProp) ? siProp.GetString() : null;

        // Left Hand: Light source (Torch, Lantern, Phial, Star) when equipped or in dungeon,
        // otherwise equipped shield or spellbook/class item
        var leftModelPath = "";
        if (!string.IsNullOrEmpty(lowerLight))
        {
            if (lowerLight.Contains("lantern") || lowerLight.Contains("lamp"))
            {
                leftModelPath = "res://assets/models/props/lantern_standing.gltf";
            }
            else if (lowerLight.Contains("phial") || lowerLight.Contains("star") || lowerLight.Contains("arkenstone") || lowerLight.Contains("crystal"))
            {
                leftModelPath = "res://assets/models/items/Crystal1.fbx";
            }
            else
            {
                leftModelPath = "__torch_handheld__";
            }
        }
        else if (lightRadius > 0 || (depth > 0 && lightRadius >= 0))
        {
            leftModelPath = "__torch_handheld__";
        }
        else if (!string.IsNullOrEmpty(shieldItem))
        {
            leftModelPath = ResolveShieldModel(shieldItem);
        }
        else
        {
            var lowerClass = pClass.ToLowerInvariant();
            if (lowerClass.Contains("mage") || lowerClass.Contains("priest") || lowerClass.Contains("sorcerer") || lowerClass.Contains("druid") || lowerClass.Contains("necromancer"))
            {
                leftModelPath = "res://assets/models/items/Book1_Closed.fbx";
            }
            else if (lowerClass.Contains("warrior") || lowerClass.Contains("paladin") || lowerClass.Contains("knight"))
            {
                leftModelPath = "res://assets/models/weapons/Shield_Heater.fbx";
            }
        }

        // Right Hand: Primary equipped weapon or bow, otherwise unarmed fist / class weapon
        var rightModelPath = ResolveRightHandModel(weaponItem, weaponTval, bowItem, bowTval, pClass);

        var (leftScale, leftPos, leftRot, leftItemOffset) = GetModelTransform(leftModelPath, true);
        var (rightScale, rightPos, rightRot, rightItemOffset) = GetModelTransform(rightModelPath, false);

        float posScaleFactor = Mathf.Clamp(Mathf.Pow(heightRatio, 0.25f), 0.85f, 1.15f);

        // Slot rest transforms
        _leftRestPos = new Vector3(leftPos.X * posScaleFactor, leftPos.Y * posScaleFactor, leftPos.Z * posScaleFactor);
        _leftRestRot = leftRot;
        _rightRestPos = new Vector3(rightPos.X * posScaleFactor, rightPos.Y * posScaleFactor, rightPos.Z * posScaleFactor);
        _rightRestRot = rightRot;

        // Position wielded items relative to holder
        _leftItemSlot.Position = leftItemOffset;
        _rightItemSlot.Position = rightItemOffset;

        SetSlotModel(_leftItemSlot, ref _currentLeftModel, leftModelPath, leftScale);
        SetSlotModel(_rightItemSlot, ref _currentRightModel, rightModelPath, rightScale);

        // Dynamic Light Source VFX (Subtle compact flame / Lantern glow / Phial sparkle)
        var isTorch = leftModelPath == "__torch_handheld__" || leftModelPath.ToLowerInvariant().Contains("torch");
        var isLantern = leftModelPath.ToLowerInvariant().Contains("lantern");
        var isCrystal = leftModelPath.ToLowerInvariant().Contains("crystal") || leftModelPath.ToLowerInvariant().Contains("star");

        if (_leftTorchFlame != null)
        {
            if (isTorch)
            {
                _leftTorchFlame.Visible = true;
                _leftTorchFlame.Position = new Vector3(0, 0.30f, 0);
                _leftTorchFlame.ScaleAmountMin = 0.015f;
                _leftTorchFlame.ScaleAmountMax = 0.030f;
                _leftTorchFlame.Color = new Color(1.0f, 0.72f, 0.18f, 0.90f);
                if (!_leftTorchFlame.Emitting) _leftTorchFlame.Emitting = true;
            }
            else if (isLantern)
            {
                _leftTorchFlame.Visible = true;
                _leftTorchFlame.Position = new Vector3(0, 0.12f, 0);
                _leftTorchFlame.ScaleAmountMin = 0.010f;
                _leftTorchFlame.ScaleAmountMax = 0.020f;
                _leftTorchFlame.Color = new Color(1.0f, 0.80f, 0.25f, 0.85f);
                if (!_leftTorchFlame.Emitting) _leftTorchFlame.Emitting = true;
            }
            else if (isCrystal)
            {
                _leftTorchFlame.Visible = true;
                _leftTorchFlame.Position = new Vector3(0, 0.08f, 0);
                _leftTorchFlame.ScaleAmountMin = 0.012f;
                _leftTorchFlame.ScaleAmountMax = 0.025f;
                _leftTorchFlame.Color = new Color(0.65f, 0.85f, 1.0f, 0.80f);
                if (!_leftTorchFlame.Emitting) _leftTorchFlame.Emitting = true;
            }
            else
            {
                _leftTorchFlame.Visible = false;
            }
        }

        if (_leftTorchSmoke != null)
        {
            if (isTorch)
            {
                _leftTorchSmoke.Visible = true;
                _leftTorchSmoke.Position = new Vector3(0, 0.33f, 0);
                if (!_leftTorchSmoke.Emitting) _leftTorchSmoke.Emitting = true;
            }
            else
            {
                _leftTorchSmoke.Visible = false;
            }
        }

        // Keep secondary light disabled - DungeonWorld._torch provides the single unified light source
        if (_leftTorchLight != null)
        {
            _leftTorchLight.Visible = false;
        }

        // Dynamic Weapon Elemental Ego Aura (Priority 3)
        UpdateWeaponEgoAura(weaponItem);
    }

    private void UpdateWeaponEgoAura(string weaponItem)
    {
        if (string.IsNullOrEmpty(weaponItem) || string.IsNullOrEmpty(_currentRightModel))
        {
            if (_rightWeaponAura != null) _rightWeaponAura.Visible = false;
            if (_rightWeaponLight != null) _rightWeaponLight.Visible = false;
            return;
        }

        var lowerW = weaponItem.ToLowerInvariant();
        Color auraCol;
        Color lightCol;
        bool hasEgo = true;

        if (lowerW.Contains("flame") || lowerW.Contains("fire") || lowerW.Contains("hellfire") || lowerW.Contains("dragon") || lowerW.Contains("chaos"))
        {
            auraCol = new Color(1.0f, 0.55f, 0.10f, 0.90f);
            lightCol = new Color(1.0f, 0.55f, 0.15f);
        }
        else if (lowerW.Contains("frost") || lowerW.Contains("ice") || lowerW.Contains("cold") || lowerW.Contains("blizzard"))
        {
            auraCol = new Color(0.35f, 0.85f, 1.0f, 0.85f);
            lightCol = new Color(0.35f, 0.80f, 1.0f);
        }
        else if (lowerW.Contains("lightning") || lowerW.Contains("thunder") || lowerW.Contains("elec") || lowerW.Contains("shock") || lowerW.Contains("spark"))
        {
            auraCol = new Color(1.0f, 0.95f, 0.30f, 0.95f);
            lightCol = new Color(1.0f, 0.95f, 0.35f);
        }
        else if (lowerW.Contains("venom") || lowerW.Contains("acid") || lowerW.Contains("poison") || lowerW.Contains("corros"))
        {
            auraCol = new Color(0.35f, 0.95f, 0.25f, 0.85f);
            lightCol = new Color(0.35f, 0.95f, 0.30f);
        }
        else if (lowerW.Contains("holy") || lowerW.Contains("slay") || lowerW.Contains("westernesse") || lowerW.Contains("defender") || lowerW.Contains("gondolin") || lowerW.Contains("blessed"))
        {
            auraCol = new Color(1.0f, 0.88f, 0.40f, 0.85f);
            lightCol = new Color(1.0f, 0.88f, 0.45f);
        }
        else
        {
            hasEgo = false;
            auraCol = Colors.White;
            lightCol = Colors.White;
        }

        if (_rightWeaponAura != null)
        {
            _rightWeaponAura.Visible = hasEgo;
            _rightWeaponAura.Color = auraCol;
            _rightWeaponAura.Position = new Vector3(0, 0.15f, 0);
            if (hasEgo && !_rightWeaponAura.Emitting) _rightWeaponAura.Emitting = true;
        }
        if (_rightWeaponLight != null)
        {
            _rightWeaponLight.Visible = hasEgo;
            _rightWeaponLight.LightColor = lightCol;
            _rightWeaponLight.Position = new Vector3(0, 0.15f, 0);
        }
    }

    private static (float scale, Vector3 pos, Vector3 rot, Vector3 gripOffset) GetModelTransform(string modelPath, bool isLeft)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            if (isLeft)
            {
                return (
                    1f,
                    new Vector3(-0.24f, -0.22f, -0.38f),
                    new Vector3(Mathf.DegToRad(10), Mathf.DegToRad(18), Mathf.DegToRad(-10)),
                    Vector3.Zero
                );
            }
            else
            {
                return (
                    1f,
                    new Vector3(0.24f, -0.22f, -0.38f),
                    new Vector3(Mathf.DegToRad(26), Mathf.DegToRad(-18), Mathf.DegToRad(10)),
                    Vector3.Zero
                );
            }
        }

        var lower = modelPath.ToLowerInvariant();

        if (isLeft)
        {
            if (modelPath == "__torch_handheld__" || lower.Contains("torch"))
            {
                // Handheld wooden torch with burning ember head (scale 1.0 = real-world meters: ~0.48m tall)
                return (
                    1.0f,
                    new Vector3(-0.26f, -0.30f, -0.42f),
                    new Vector3(Mathf.DegToRad(14), Mathf.DegToRad(16), Mathf.DegToRad(-8)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("lantern"))
            {
                // Handheld standing brass lantern
                return (
                    0.28f,
                    new Vector3(-0.26f, -0.28f, -0.40f),
                    new Vector3(Mathf.DegToRad(6), Mathf.DegToRad(12), Mathf.DegToRad(-4)),
                    new Vector3(0f, -0.08f, 0f)
                );
            }
            if (lower.Contains("crystal") || lower.Contains("star") || lower.Contains("phial"))
            {
                // Radiant starlight crystal / phial artifact
                return (
                    0.22f,
                    new Vector3(-0.25f, -0.26f, -0.38f),
                    new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(16), Mathf.DegToRad(-8)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("shield_round"))
            {
                // Round buckler shield (~0.38m diameter)
                return (
                    0.19f,
                    new Vector3(-0.27f, -0.27f, -0.40f),
                    new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(20), Mathf.DegToRad(-6)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("shield_heater_2"))
            {
                // Heavy / Tower heater shield (~0.51m height)
                return (
                    0.20f,
                    new Vector3(-0.28f, -0.28f, -0.42f),
                    new Vector3(Mathf.DegToRad(8), Mathf.DegToRad(18), Mathf.DegToRad(-6)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("shield_celtic") || lower.Contains("shield_golden"))
            {
                // Elaborate Celtic / Golden kite shield (~0.60m height)
                return (
                    0.14f,
                    new Vector3(-0.28f, -0.28f, -0.42f),
                    new Vector3(Mathf.DegToRad(8), Mathf.DegToRad(18), Mathf.DegToRad(-6)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("shield"))
            {
                // Knight's heater shield (~0.49m height, 0.38m width)
                return (
                    0.19f,
                    new Vector3(-0.27f, -0.27f, -0.40f),
                    new Vector3(Mathf.DegToRad(10), Mathf.DegToRad(18), Mathf.DegToRad(-6)),
                    Vector3.Zero
                );
            }
            if (lower.Contains("book") || lower.Contains("spellbook"))
            {
                // Handheld spellbook / tome (~0.29m height)
                return (
                    0.36f,
                    new Vector3(-0.26f, -0.26f, -0.38f),
                    new Vector3(Mathf.DegToRad(18), Mathf.DegToRad(16), Mathf.DegToRad(-8)),
                    Vector3.Zero
                );
            }
            return (
                0.14f,
                new Vector3(-0.26f, -0.28f, -0.40f),
                new Vector3(Mathf.DegToRad(10), Mathf.DegToRad(16), Mathf.DegToRad(-8)),
                Vector3.Zero
            );
        }
        else
        {
            // 2H Greatswords & Claymores (~0.92m total length)
            if (lower.Contains("claymore") || lower.Contains("sword_big"))
            {
                return (
                    0.14f,
                    new Vector3(0.28f, -0.30f, -0.44f),
                    new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-16), Mathf.DegToRad(8)),
                    new Vector3(0f, -0.04f, 0f)
                );
            }
            // 2H Battleaxes & Double Axes (~0.83m length)
            if (lower.Contains("axe_double"))
            {
                return (
                    0.13f,
                    new Vector3(0.27f, -0.29f, -0.42f),
                    new Vector3(Mathf.DegToRad(22), Mathf.DegToRad(-18), Mathf.DegToRad(10)),
                    Vector3.Zero
                );
            }
            // 2H Heavy War Hammers & Mattocks (~0.70m length)
            if (lower.Contains("hammer_double"))
            {
                return (
                    0.14f,
                    new Vector3(0.27f, -0.29f, -0.40f),
                    new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-18), Mathf.DegToRad(10)),
                    new Vector3(0f, -0.04f, 0f)
                );
            }
            // Staves, Spears, Polearms, Scythes (~1.26m length)
            if (lower.Contains("staff") || lower.Contains("spear") || lower.Contains("scythe"))
            {
                return (
                    0.13f,
                    new Vector3(0.28f, -0.30f, -0.44f),
                    new Vector3(Mathf.DegToRad(18), Mathf.DegToRad(-14), Mathf.DegToRad(8)),
                    new Vector3(0f, -0.20f, 0f)
                );
            }
            // Bows & Crossbows (~0.71m height)
            if (lower.Contains("crossbow") || lower.Contains("bow"))
            {
                return (
                    0.13f,
                    new Vector3(0.26f, -0.26f, -0.38f),
                    new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(-14), Mathf.DegToRad(8)),
                    Vector3.Zero
                );
            }
            // Daggers & Small Blades (~0.36m length, blade 0.28m)
            if (lower.Contains("dagger"))
            {
                return (
                    0.14f,
                    new Vector3(0.25f, -0.26f, -0.38f),
                    new Vector3(Mathf.DegToRad(30), Mathf.DegToRad(-14), Mathf.DegToRad(10)),
                    Vector3.Zero
                );
            }
            // 1-Handed Axes & Cleavers (~0.68m length)
            if (lower.Contains("axe"))
            {
                return (
                    0.13f,
                    new Vector3(0.26f, -0.28f, -0.40f),
                    new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-18), Mathf.DegToRad(10)),
                    new Vector3(0f, -0.06f, 0f)
                );
            }
            // Small Hammers, Maces, Flails, Clubs (~0.56m length)
            if (lower.Contains("mace") || lower.Contains("hammer"))
            {
                return (
                    0.13f,
                    new Vector3(0.26f, -0.28f, -0.40f),
                    new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-18), Mathf.DegToRad(10)),
                    new Vector3(0f, -0.05f, 0f)
                );
            }
            // Wands & Rods
            if (lower.Contains("wand") || lower.Contains("rod"))
            {
                return (
                    0.13f,
                    new Vector3(0.25f, -0.27f, -0.38f),
                    new Vector3(Mathf.DegToRad(26), Mathf.DegToRad(-16), Mathf.DegToRad(8)),
                    new Vector3(0f, -0.15f, 0f)
                );
            }
            // 1-Handed Swords (Standard 1H sword: ~0.74m total length, 0.62m blade)
            return (
                0.135f,
                new Vector3(0.26f, -0.28f, -0.40f),
                new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-18), Mathf.DegToRad(8)),
                Vector3.Zero
            );
        }
    }

    private static string ResolveShieldModel(string shieldItem)
    {
        var lower = (shieldItem ?? "").ToLowerInvariant();
        if (lower.Contains("golden") || lower.Contains("celtic") || lower.Contains("dragon"))
        {
            return "res://assets/models/weapons/Shield_Celtic_Golden.fbx";
        }
        if (lower.Contains("round") || lower.Contains("buckler") || lower.Contains("small metal shield") || lower.Contains("small leather shield"))
        {
            return "res://assets/models/weapons/Shield_Round.fbx";
        }
        if (lower.Contains("large") || lower.Contains("tower") || lower.Contains("square") || lower.Contains("pavise") || lower.Contains("spiked") || lower.Contains("spikes"))
        {
            return "res://assets/models/weapons/Shield_Heater_2.fbx";
        }

        // Default heater shield
        return "res://assets/models/weapons/Shield_Heater.fbx";
    }

    private static string ResolveRightHandModel(string weaponItem, int weaponTval, string bowItem, int bowTval, string pClass)
    {
        // 1. If an actual melee weapon is wielded, map it to the best matching CC0 3D model
        if (!string.IsNullOrEmpty(weaponItem))
        {
            var lowerW = weaponItem.ToLowerInvariant();

            // Daggers & Small Blades
            if (lowerW.Contains("dagger") || lowerW.Contains("knife") || lowerW.Contains("main gauche") ||
                lowerW.Contains("rapier") || lowerW.Contains("stiletto") || lowerW.Contains("scalpel") ||
                lowerW.Contains("shard") || lowerW.Contains("baselard") || lowerW.Contains("bodkin") ||
                lowerW.Contains("athame") || lowerW.Contains("misericorde") || lowerW.Contains("falcon"))
            {
                return "res://assets/models/weapons/Dagger.fbx";
            }

            // Two-Handed Swords & Greatswords
            if (lowerW.Contains("two-handed") || lowerW.Contains("great sword") || lowerW.Contains("claymore") ||
                lowerW.Contains("bastard") || lowerW.Contains("zweihander") || lowerW.Contains("executioner") ||
                lowerW.Contains("flamberge") || lowerW.Contains("blade of chaos"))
            {
                return "res://assets/models/weapons/Claymore.fbx";
            }

            // Two-Handed Axes & Battleaxes & Heavy Polearms
            if (lowerW.Contains("battle axe") || lowerW.Contains("great axe") || lowerW.Contains("broad axe") ||
                lowerW.Contains("lochaber") || lowerW.Contains("halberd") || lowerW.Contains("poleaxe") ||
                lowerW.Contains("beaked axe"))
            {
                return "res://assets/models/weapons/Axe_Double.fbx";
            }

            // 1-Handed Axes & Cleavers & Hatchets
            if (lowerW.Contains("small axe") || lowerW.Contains("hatchet"))
            {
                return "res://assets/models/weapons/Axe_Small.fbx";
            }
            if (lowerW.Contains("axe") || lowerW.Contains("cleaver") ||
                lowerW.Contains("sickle") || lowerW.Contains("tomahawk"))
            {
                return "res://assets/models/weapons/Axe.fbx";
            }

            // Scythes
            if (lowerW.Contains("scythe"))
            {
                return "res://assets/models/weapons/Scythe.fbx";
            }

            // Staves, Quarterstaves, Polearms, Spears, Lances, Tridents
            if (lowerW.Contains("staff") || lowerW.Contains("quarterstaff") || lowerW.Contains("spear") ||
                lowerW.Contains("pike") || lowerW.Contains("lance") || lowerW.Contains("trident") ||
                lowerW.Contains("glaive") || lowerW.Contains("awl-pike") ||
                lowerW.Contains("lucerne") || lowerW.Contains("naginata"))
            {
                return "res://assets/models/weapons/Spear.fbx";
            }

            // Wands & Rods
            if (lowerW.Contains("wand") || lowerW.Contains("rod"))
            {
                return "res://assets/models/weapons/Spear.fbx";
            }

            // War Hammers & Mattocks
            if (lowerW.Contains("war hammer") || lowerW.Contains("great hammer") || lowerW.Contains("mattock"))
            {
                return "res://assets/models/weapons/Hammer_Double.fbx";
            }

            // Maces, Flails, Morning Stars, Clubs, Cudgels, Whips, Small Hammers
            if (lowerW.Contains("mace") || lowerW.Contains("flail") || lowerW.Contains("star") ||
                lowerW.Contains("club") || lowerW.Contains("whip") || lowerW.Contains("cudgel") ||
                lowerW.Contains("ball-and-chain") || lowerW.Contains("morning star") || lowerW.Contains("flanged") ||
                lowerW.Contains("lead-filled") || lowerW.Contains("hammer"))
            {
                return "res://assets/models/weapons/Hammer_Small.fbx";
            }

            // General Swords (Broadsword, Longsword, Shortsword, Scimitar, Sabre, Katana, Cutlass, Tulwar)
            if (lowerW.Contains("golden") || lowerW.Contains("holy") || lowerW.Contains("radiant"))
            {
                return "res://assets/models/weapons/Sword_Golden.fbx";
            }
            return "res://assets/models/weapons/Sword.fbx";
        }

        // 2. If no melee weapon but a shooter/bow is wielded
        if (!string.IsNullOrEmpty(bowItem))
        {
            var lowerB = bowItem.ToLowerInvariant();
            if (lowerB.Contains("golden"))
            {
                return "res://assets/models/weapons/Bow_Golden.fbx";
            }
            if (lowerB.Contains("evil") || lowerB.Contains("dark"))
            {
                return "res://assets/models/weapons/Bow_Evil.fbx";
            }
            return "res://assets/models/weapons/Bow_Wooden.fbx";
        }

        // 3. Unarmed / Bare Fists - return empty string to hide right hand item cleanly
        return "";
    }

    private void SetSlotModel(Node3D slot, ref string currentPath, string newPath, float scale)
    {
        var cacheKey = $"{slot.Name}_{newPath}";
        if (currentPath == newPath)
        {
            if (!string.IsNullOrEmpty(newPath) && _modelCache.TryGetValue(cacheKey, out var existing) && existing != null && IsInstanceValid(existing))
            {
                existing.Scale = Vector3.One * scale;
            }
            return;
        }

        // Hide old item models, preserving VFX nodes
        foreach (var child in slot.GetChildren())
        {
            if (child is Node3D n3d && n3d != _leftTorchFlame && n3d != _leftTorchSmoke && n3d != _leftTorchLight &&
                n3d != _rightWeaponAura && n3d != _rightWeaponLight)
            {
                n3d.Visible = false;
            }
        }

        currentPath = newPath;
        if (string.IsNullOrEmpty(newPath)) return;

        if (!_modelCache.TryGetValue(cacheKey, out var modelNode) || modelNode == null || !IsInstanceValid(modelNode))
        {
            try
            {
                if (newPath == "__torch_handheld__")
                {
                    modelNode = CreateHandheldTorchNode();
                    modelNode.Scale = Vector3.One * scale;
                    slot.AddChild(modelNode);
                    _modelCache[cacheKey] = modelNode;
                }
                else
                {
                    if (!_sceneCache.TryGetValue(newPath, out var scene) || scene == null)
                    {
                        scene = GD.Load<PackedScene>(newPath);
                        if (scene != null)
                        {
                            _sceneCache[newPath] = scene;
                        }
                    }

                    if (scene != null)
                    {
                        modelNode = scene.Instantiate<Node3D>();
                        modelNode.Scale = Vector3.One * scale;
                        slot.AddChild(modelNode);
                        _modelCache[cacheKey] = modelNode;
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ViewModel] Failed loading weapon model '{newPath}': {ex.Message}");
                return;
            }
        }

        if (modelNode != null)
        {
            modelNode.Scale = Vector3.One * scale;
            modelNode.Visible = true;
        }
    }

    /// <summary>
    /// Triggers a fast melee weapon slash/thrust action.
    /// </summary>
    public void TriggerAttack()
    {
        _actionDuration = 0.16f;
        _actionTime = _actionDuration;
        _actionPosOffset = new Vector3(0.04f, 0.03f, -0.16f);
        _actionRotOffset = new Vector3(Mathf.DegToRad(-22), Mathf.DegToRad(-14), Mathf.DegToRad(24));
    }

    /// <summary>
    /// Triggers a spell casting surge / wand elevation.
    /// </summary>
    public void TriggerCast()
    {
        _actionDuration = 0.22f;
        _actionTime = _actionDuration;
        _actionPosOffset = new Vector3(0.0f, 0.06f, -0.08f);
        _actionRotOffset = new Vector3(Mathf.DegToRad(-25), 0, 0);
    }

    /// <summary>
    /// Triggers viewmodel recoil on incoming damage.
    /// </summary>
    public void TriggerHurt()
    {
        _recoilIntensity = Mathf.Min(0.7f, _recoilIntensity + 0.45f);
    }

    /// <summary>
    /// Updates viewmodel bobbing, inertia lag, and action animations.
    /// </summary>
    public void ProcessMotion(double delta, bool isMoving, float yawDelta, float pitchDelta)
    {
        if (_swayRoot == null) return;
        var dt = (float)delta;

        // 1. Walk bobbing - cadence adjusted by character height
        var bobFrequency = 9.0f / Mathf.Sqrt(Mathf.Max(0.5f, _currentHeightRatio));
        if (isMoving)
        {
            _bobTimer += dt * bobFrequency;
        }
        else
        {
            _bobTimer += dt * 1.5f;
        }

        var bobIntensity = isMoving ? 1.0f : 0.15f;
        var bobY = Mathf.Sin(_bobTimer * 2.0f) * 0.007f * bobIntensity;
        var bobX = Mathf.Cos(_bobTimer) * 0.005f * bobIntensity;
        var bobZ = Mathf.Sin(_bobTimer) * 0.003f * bobIntensity;

        // 2. Camera turn sway inertia
        var targetSwayPos = new Vector3(
            Mathf.Clamp(-yawDelta * 0.14f, -0.04f, 0.04f),
            Mathf.Clamp(pitchDelta * 0.12f, -0.03f, 0.03f),
            0f
        );

        var targetSwayRot = new Vector3(
            Mathf.Clamp(pitchDelta * 0.3f, -0.08f, 0.08f),
            Mathf.Clamp(-yawDelta * 0.35f, -0.09f, 0.09f),
            Mathf.Clamp(yawDelta * 0.25f, -0.06f, 0.06f)
        );

        _swayOffsetPos = _swayOffsetPos.Lerp(targetSwayPos, dt * 10.0f);
        _swayOffsetRot = _swayOffsetRot.Lerp(targetSwayRot, dt * 10.0f);

        // 3. Recoil on damage
        if (_recoilIntensity > 0.001f)
        {
            _recoilIntensity = Mathf.Max(0f, _recoilIntensity - dt * 4.0f);
        }
        var recoilZ = _recoilIntensity * 0.06f;
        var recoilRotX = -_recoilIntensity * 0.05f;

        // Apply to sway root
        _swayRoot.Position = new Vector3(bobX, bobY, bobZ) + _swayOffsetPos + new Vector3(0, 0, recoilZ);
        _swayRoot.Rotation = _swayOffsetRot + new Vector3(recoilRotX, 0, 0);

        // 4. Action animation (attack / cast)
        var curActionPos = Vector3.Zero;
        var curActionRot = Vector3.Zero;

        if (_actionTime > 0.001f)
        {
            _actionTime = Mathf.Max(0f, _actionTime - dt);
            var normalized = 1.0f - (_actionTime / _actionDuration); // 0 -> 1

            // Punch curve: fast forward thrust, smooth recovery
            float curve;
            if (normalized < 0.30f)
            {
                curve = normalized / 0.30f; // 0 -> 1
            }
            else
            {
                curve = 1.0f - ((normalized - 0.30f) / 0.70f); // 1 -> 0
            }

            curActionPos = _actionPosOffset * curve;
            curActionRot = _actionRotOffset * curve;
        }

        _leftHandSlot.Position = _leftRestPos;
        _leftHandSlot.Rotation = _leftRestRot;

        _rightHandSlot.Position = _rightRestPos + curActionPos;
        _rightHandSlot.Rotation = _rightRestRot + curActionRot;
    }
}
