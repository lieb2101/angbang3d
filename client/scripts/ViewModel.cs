using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// First-person viewmodel displaying the player's stylized hands, forearms,
/// and held equipment (left hand torch/shield/book, right hand weapon/wand/staff/fist)
/// parented to the main Camera3D.
/// Dynamically scales hand proportions, reach distance, and race skin/sleeve tones
/// based on character height and race/class stats.
/// Handles walk bobbing, inertia sway, and kinetic attack/hit tweens.
/// </summary>
public partial class ViewModel : Node3D
{
    private Node3D _swayRoot;

    // Hand slots and arm rigs
    private Node3D _leftHandSlot;
    private Node3D _rightHandSlot;
    private Node3D _leftArmRig;
    private Node3D _rightArmRig;
    private Node3D _leftItemSlot;
    private Node3D _rightItemSlot;

    // Hand & sleeve mesh instances for dynamic material styling
    private MeshInstance3D _leftSleeveMesh;
    private MeshInstance3D _leftCuffMesh;
    private MeshInstance3D _leftHandMesh;
    private MeshInstance3D _rightSleeveMesh;
    private MeshInstance3D _rightCuffMesh;
    private MeshInstance3D _rightHandMesh;

    // Shared materials
    private StandardMaterial3D _skinMaterial;
    private StandardMaterial3D _sleeveMaterial;
    private StandardMaterial3D _cuffMaterial;
    private StandardMaterial3D _handArmorMaterial;

    // Dynamic Torch Flame & Smoke VFX (Priority 3)
    private CpuParticles3D _leftTorchFlame;
    private CpuParticles3D _leftTorchSmoke;
    private OmniLight3D _leftTorchLight;

    // Dynamic Ego Weapon Aura & Light (Priority 3)
    private CpuParticles3D _rightWeaponAura;
    private OmniLight3D _rightWeaponLight;
    private string _currentWeaponEgo = "";

    // Cache instantiated weapon/item nodes
    private readonly Dictionary<string, Node3D> _modelCache = new();
    private string _currentLeftModel = "";
    private string _currentRightModel = "";

    // Rest transforms (relative to sway root) - calibrated to unobtrusive bottom corners
    private Vector3 _leftRestPos = new(-0.36f, -0.28f, -0.42f);
    private Vector3 _leftRestRot = new(Mathf.DegToRad(-8), Mathf.DegToRad(20), Mathf.DegToRad(-18));
    private Vector3 _rightRestPos = new(0.36f, -0.28f, -0.42f);
    private Vector3 _rightRestRot = new(Mathf.DegToRad(28), Mathf.DegToRad(-22), Mathf.DegToRad(10));

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

        InitMaterials();

        // Create Left Hand hierarchy
        _leftHandSlot = new Node3D { Name = "LeftHandSlot", Position = _leftRestPos, Rotation = _leftRestRot };
        _swayRoot.AddChild(_leftHandSlot);

        _leftArmRig = BuildArmRig(true, out _leftSleeveMesh, out _leftCuffMesh, out _leftHandMesh);
        _leftHandSlot.AddChild(_leftArmRig);

        _leftItemSlot = new Node3D { Name = "LeftItemSlot" };
        _leftHandSlot.AddChild(_leftItemSlot);

        // Torch Flame particle emitter
        _leftTorchFlame = new CpuParticles3D
        {
            Name = "TorchFlameVfx",
            Amount = 24,
            Lifetime = 0.35f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.035f,
            Direction = Vector3.Up,
            Spread = 20f,
            InitialVelocityMin = 0.35f,
            InitialVelocityMax = 0.85f,
            Gravity = new Vector3(0, 0.4f, 0),
            ScaleAmountMin = 0.035f,
            ScaleAmountMax = 0.075f,
            Color = new Color(1.0f, 0.70f, 0.15f, 0.95f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1.0f, 0.75f, 0.20f),
            },
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchFlame);

        // Torch Smoke particle emitter
        _leftTorchSmoke = new CpuParticles3D
        {
            Name = "TorchSmokeVfx",
            Amount = 14,
            Lifetime = 0.75f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.03f,
            Direction = Vector3.Up,
            Spread = 30f,
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.60f,
            Gravity = new Vector3(0, 0.2f, 0),
            ScaleAmountMin = 0.04f,
            ScaleAmountMax = 0.10f,
            Color = new Color(0.20f, 0.20f, 0.22f, 0.35f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.35f, 0.35f, 0.38f, 0.4f),
            },
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchSmoke);

        _leftTorchLight = new OmniLight3D
        {
            Name = "TorchTipLight",
            LightColor = new Color(1.0f, 0.82f, 0.50f),
            LightEnergy = 1.4f,
            OmniRange = 2.2f,
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            Visible = false,
        };
        _leftItemSlot.AddChild(_leftTorchLight);

        // Create Right Hand hierarchy
        _rightHandSlot = new Node3D { Name = "RightHandSlot", Position = _rightRestPos, Rotation = _rightRestRot };
        _swayRoot.AddChild(_rightHandSlot);

        _rightArmRig = BuildArmRig(false, out _rightSleeveMesh, out _rightCuffMesh, out _rightHandMesh);
        _rightHandSlot.AddChild(_rightArmRig);

        _rightItemSlot = new Node3D { Name = "RightItemSlot" };
        _rightHandSlot.AddChild(_rightItemSlot);

        // Right Weapon Elemental Ego Aura
        _rightWeaponAura = new CpuParticles3D
        {
            Name = "WeaponEgoAura",
            Amount = 26,
            Lifetime = 0.40f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.04f, 0.18f, 0.04f),
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

    private void InitMaterials()
    {
        _skinMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.88f, 0.74f, 0.62f),
            Roughness = 0.65f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
        };

        _sleeveMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.32f, 0.28f, 0.25f),
            Roughness = 0.75f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
        };

        _cuffMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.46f, 0.32f),
            Roughness = 0.45f,
            Metallic = 0.40f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
        };

        _handArmorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.75f, 0.76f, 0.80f),
            Roughness = 0.28f,
            Metallic = 0.92f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
        };
    }

    /// <summary>
    /// Builds a stylized low-poly player arm rig (tapered forearm sleeve, trimmed wrist bracer, palm, thumb, and 4 articulated fingers).
    /// </summary>
    private Node3D BuildArmRig(bool isLeft, out MeshInstance3D sleeveInst, out MeshInstance3D cuffInst, out MeshInstance3D handInst)
    {
        var armRoot = new Node3D { Name = isLeft ? "LeftArm" : "RightArm" };
        var mirror = isLeft ? -1.0f : 1.0f;

        // 1. Forearm sleeve tapering towards wrist
        var sleeveMesh = new CylinderMesh
        {
            TopRadius = 0.046f,
            BottomRadius = 0.062f,
            Height = 0.24f,
            RadialSegments = 10,
            Rings = 2,
        };
        sleeveInst = new MeshInstance3D
        {
            Mesh = sleeveMesh,
            MaterialOverride = _sleeveMaterial,
            Position = new Vector3(0.018f * mirror, -0.012f, 0.13f),
            Rotation = new Vector3(Mathf.DegToRad(84), Mathf.DegToRad(-6 * mirror), Mathf.DegToRad(4 * mirror)),
        };
        armRoot.AddChild(sleeveInst);

        // 2. Wrist bracer / cuff with metallic/leather trim
        var cuffMesh = new CylinderMesh
        {
            TopRadius = 0.044f,
            BottomRadius = 0.048f,
            Height = 0.052f,
            RadialSegments = 10,
            Rings = 1,
        };
        cuffInst = new MeshInstance3D
        {
            Mesh = cuffMesh,
            MaterialOverride = _cuffMaterial,
            Position = new Vector3(0.010f * mirror, -0.006f, 0.022f),
            Rotation = new Vector3(Mathf.DegToRad(86), Mathf.DegToRad(-4 * mirror), Mathf.DegToRad(2 * mirror)),
        };
        armRoot.AddChild(cuffInst);

        // 3. Palm & Metacarpal base
        var palmMesh = new BoxMesh { Size = new Vector3(0.066f, 0.044f, 0.056f) };
        handInst = new MeshInstance3D
        {
            Mesh = palmMesh,
            MaterialOverride = _skinMaterial,
            Position = new Vector3(0.004f * mirror, 0f, -0.020f),
        };
        armRoot.AddChild(handInst);

        // 4. Opposable Thumb (Base muscle + articulated grip segment)
        var thumbBaseMesh = new BoxMesh { Size = new Vector3(0.024f, 0.026f, 0.034f) };
        var thumbBase = new MeshInstance3D
        {
            Mesh = thumbBaseMesh,
            MaterialOverride = _skinMaterial,
            Position = new Vector3(-0.036f * mirror, 0.010f, -0.012f),
            Rotation = new Vector3(Mathf.DegToRad(8), Mathf.DegToRad(30 * mirror), Mathf.DegToRad(-18 * mirror)),
        };
        armRoot.AddChild(thumbBase);

        var thumbTipMesh = new BoxMesh { Size = new Vector3(0.020f, 0.022f, 0.028f) };
        var thumbTip = new MeshInstance3D
        {
            Mesh = thumbTipMesh,
            MaterialOverride = _skinMaterial,
            Position = new Vector3(-0.026f * mirror, 0.016f, -0.034f),
            Rotation = new Vector3(Mathf.DegToRad(22), Mathf.DegToRad(42 * mirror), Mathf.DegToRad(-26 * mirror)),
        };
        armRoot.AddChild(thumbTip);

        // 5. Four Articulated Fingers (Index, Middle, Ring, Pinky) wrapping around handle
        float[] fingerWidths = { 0.015f, 0.016f, 0.015f, 0.013f };
        float[] fingerLengths = { 0.036f, 0.038f, 0.035f, 0.030f };
        float[] fingerXOffsets = { -0.022f, -0.007f, 0.008f, 0.022f };

        for (int i = 0; i < 4; i++)
        {
            var fMesh = new BoxMesh
            {
                Size = new Vector3(fingerWidths[i], 0.022f, fingerLengths[i])
            };

            var fInst = new MeshInstance3D
            {
                Mesh = fMesh,
                MaterialOverride = _skinMaterial,
                Position = new Vector3(fingerXOffsets[i] * mirror, -0.012f, -0.046f - (fingerLengths[i] * 0.25f)),
                Rotation = new Vector3(Mathf.DegToRad(35), Mathf.DegToRad((i - 1.5f) * 4f * mirror), 0),
            };
            armRoot.AddChild(fInst);
        }

        return armRoot;
    }

    /// <summary>
    /// Updates player equipment, hand scaling, and race skin/sleeve tones based on bridge JSON frame data.
    /// </summary>
    public void UpdateEquipment(JsonElement player, int depth, float heightRatio = 1.0f)
    {
        _currentHeightRatio = heightRatio;
        var pRace = player.TryGetProperty("race", out var rProp) ? rProp.GetString() ?? "" : "";
        var pClass = player.TryGetProperty("class", out var cProp) ? cProp.GetString() ?? "" : "";
        var lightRadius = player.TryGetProperty("light", out var lProp) ? lProp.GetInt32() : 0;
        var hasLightItem = player.TryGetProperty("light_item", out var liProp) && !string.IsNullOrEmpty(liProp.GetString());

        var weaponItem = player.TryGetProperty("weapon_item", out var wiProp) ? wiProp.GetString() : null;
        var weaponTval = player.TryGetProperty("weapon_tval", out var wtProp) ? wtProp.GetInt32() : 0;
        var bowItem = player.TryGetProperty("bow_item", out var biProp) ? biProp.GetString() : null;
        var bowTval = player.TryGetProperty("bow_tval", out var btProp) ? btProp.GetInt32() : 0;
        var shieldItem = player.TryGetProperty("shield_item", out var siProp) ? siProp.GetString() : null;
        var bodyArmorItem = player.TryGetProperty("body_armor_item", out var baProp) ? baProp.GetString() : null;
        var glovesItem = player.TryGetProperty("gloves_item", out var gvProp) ? gvProp.GetString() : null;

        // Apply dynamic race skin tone, class clothing palette, and armor overlays (Priority 4)
        UpdateArmorAndRaceColors(pRace, pClass, bodyArmorItem, glovesItem);

        // Left Hand: Torch when lit/in dungeon, otherwise equipped shield or spellbook/class item
        var leftModelPath = "";
        if (lightRadius > 0 || hasLightItem || (depth > 0 && lightRadius >= 0))
        {
            leftModelPath = "res://assets/models/dungeon/torch_lit.gltf.glb";
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
                leftModelPath = "res://assets/models/characters/spellbook_closed.gltf";
            }
            else if (lowerClass.Contains("warrior") || lowerClass.Contains("paladin") || lowerClass.Contains("knight"))
            {
                leftModelPath = "res://assets/models/characters/shield_badge.gltf";
            }
        }

        // Right Hand: Primary equipped weapon or bow, otherwise unarmed fist / class weapon
        var rightModelPath = ResolveRightHandModel(weaponItem, weaponTval, bowItem, bowTval, pClass);

        var (leftScale, leftPos, leftRot, leftHandGripOffset) = GetModelTransform(leftModelPath, true);
        var (rightScale, rightPos, rightRot, rightHandGripOffset) = GetModelTransform(rightModelPath, false);

        // Scale hand model sizes and reach positions relative to character height ratio
        // Halflings/Gnomes/Kobolds ~0.70x natural hands (readable, not miniature)
        // Standard human ~1.0x baseline hands
        // Half-Trolls/High-Elves ~1.20-1.28x imposing muscular hands
        float handScaleFactor = Mathf.Clamp(Mathf.Pow(heightRatio, 0.50f), 0.70f, 1.28f);
        float posScaleFactor = Mathf.Clamp(Mathf.Pow(heightRatio, 0.35f), 0.80f, 1.18f);

        // Hand/arm rig scaling
        _leftArmRig.Scale = Vector3.One * handScaleFactor;
        _rightArmRig.Scale = Vector3.One * handScaleFactor;

        // Slot rest transforms
        _leftRestPos = new Vector3(leftPos.X * posScaleFactor, leftPos.Y * posScaleFactor, leftPos.Z * posScaleFactor);
        _leftRestRot = leftRot;
        _rightRestPos = new Vector3(rightPos.X * posScaleFactor, rightPos.Y * posScaleFactor, rightPos.Z * posScaleFactor);
        _rightRestRot = rightRot;

        // Position wielded items relative to hand grip
        _leftItemSlot.Position = leftHandGripOffset * handScaleFactor;
        _rightItemSlot.Position = rightHandGripOffset * handScaleFactor;

        SetSlotModel(_leftItemSlot, ref _currentLeftModel, leftModelPath, leftScale * handScaleFactor);
        SetSlotModel(_rightItemSlot, ref _currentRightModel, rightModelPath, rightScale * handScaleFactor);

        // Dynamic Torch Flame VFX (Priority 3)
        var isTorch = !string.IsNullOrEmpty(leftModelPath) && leftModelPath.ToLowerInvariant().Contains("torch");
        if (_leftTorchFlame != null)
        {
            _leftTorchFlame.Visible = isTorch;
            _leftTorchFlame.Position = new Vector3(0, 0.22f * handScaleFactor, 0);
            if (isTorch && !_leftTorchFlame.Emitting) _leftTorchFlame.Emitting = true;
        }
        if (_leftTorchSmoke != null)
        {
            _leftTorchSmoke.Visible = isTorch;
            _leftTorchSmoke.Position = new Vector3(0, 0.24f * handScaleFactor, 0);
            if (isTorch && !_leftTorchSmoke.Emitting) _leftTorchSmoke.Emitting = true;
        }
        if (_leftTorchLight != null)
        {
            _leftTorchLight.Visible = isTorch;
            _leftTorchLight.Position = new Vector3(0, 0.22f * handScaleFactor, 0);
        }

        // Dynamic Weapon Elemental Ego Aura (Priority 3)
        UpdateWeaponEgoAura(weaponItem, handScaleFactor);
    }

    private void UpdateWeaponEgoAura(string weaponItem, float handScaleFactor)
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
            _rightWeaponAura.Position = new Vector3(0, 0.15f * handScaleFactor, 0);
            if (hasEgo && !_rightWeaponAura.Emitting) _rightWeaponAura.Emitting = true;
        }
        if (_rightWeaponLight != null)
        {
            _rightWeaponLight.Visible = hasEgo;
            _rightWeaponLight.LightColor = lightCol;
            _rightWeaponLight.Position = new Vector3(0, 0.15f * handScaleFactor, 0);
        }
    }

    private void UpdateArmorAndRaceColors(string race, string pClass, string bodyArmorItem, string glovesItem)
    {
        var (skin, sleeve, cuff) = ResolveRaceClassColors(race, pClass);

        // Body Armor Overlays (Priority 4)
        if (!string.IsNullOrEmpty(bodyArmorItem))
        {
            var lowerArmor = bodyArmorItem.ToLowerInvariant();
            if (lowerArmor.Contains("plate") || lowerArmor.Contains("dragon") || lowerArmor.Contains("mithril") || lowerArmor.Contains("adamant"))
            {
                sleeve = new Color(0.40f, 0.42f, 0.46f);
                _sleeveMaterial.Metallic = 0.85f;
                _sleeveMaterial.Roughness = 0.35f;
                cuff = new Color(0.75f, 0.76f, 0.80f);
                _cuffMaterial.Metallic = 0.95f;
                _cuffMaterial.Roughness = 0.20f;
            }
            else if (lowerArmor.Contains("chain") || lowerArmor.Contains("mail") || lowerArmor.Contains("scale") || lowerArmor.Contains("ring"))
            {
                sleeve = new Color(0.35f, 0.36f, 0.40f);
                _sleeveMaterial.Metallic = 0.70f;
                _sleeveMaterial.Roughness = 0.45f;
                cuff = new Color(0.60f, 0.58f, 0.52f);
                _cuffMaterial.Metallic = 0.65f;
                _cuffMaterial.Roughness = 0.35f;
            }
            else if (lowerArmor.Contains("leather") || lowerArmor.Contains("studded") || lowerArmor.Contains("hard leather") || lowerArmor.Contains("soft leather"))
            {
                sleeve = new Color(0.32f, 0.22f, 0.15f);
                _sleeveMaterial.Metallic = 0.15f;
                _sleeveMaterial.Roughness = 0.65f;
                cuff = new Color(0.48f, 0.36f, 0.25f);
                _cuffMaterial.Metallic = 0.30f;
                _cuffMaterial.Roughness = 0.45f;
            }
            else
            {
                _sleeveMaterial.Metallic = 0.05f;
                _sleeveMaterial.Roughness = 0.75f;
                _cuffMaterial.Metallic = 0.40f;
                _cuffMaterial.Roughness = 0.45f;
            }
        }
        else
        {
            _sleeveMaterial.Metallic = 0.05f;
            _sleeveMaterial.Roughness = 0.75f;
            _cuffMaterial.Metallic = 0.40f;
            _cuffMaterial.Roughness = 0.45f;
        }

        // Glove & Gauntlet Hand Armor Overlays (Priority 4)
        if (!string.IsNullOrEmpty(glovesItem))
        {
            var lowerGloves = glovesItem.ToLowerInvariant();
            if (lowerGloves.Contains("gauntlet") || lowerGloves.Contains("mithril") || lowerGloves.Contains("dragon") || lowerGloves.Contains("steel") || lowerGloves.Contains("iron"))
            {
                skin = new Color(0.72f, 0.74f, 0.78f);
                _skinMaterial.Metallic = 0.90f;
                _skinMaterial.Roughness = 0.28f;
            }
            else if (lowerGloves.Contains("cesti") || lowerGloves.Contains("cestus") || lowerGloves.Contains("studded"))
            {
                skin = new Color(0.30f, 0.28f, 0.26f);
                _skinMaterial.Metallic = 0.55f;
                _skinMaterial.Roughness = 0.40f;
            }
            else if (lowerGloves.Contains("leather") || lowerGloves.Contains("soft") || lowerGloves.Contains("hard") || lowerGloves.Contains("dragonhide"))
            {
                skin = new Color(0.28f, 0.20f, 0.14f);
                _skinMaterial.Metallic = 0.10f;
                _skinMaterial.Roughness = 0.65f;
            }
            else
            {
                _skinMaterial.Metallic = 0.0f;
                _skinMaterial.Roughness = 0.65f;
            }
        }
        else
        {
            _skinMaterial.Metallic = 0.0f;
            _skinMaterial.Roughness = 0.65f;
        }

        _skinMaterial.AlbedoColor = skin;
        _sleeveMaterial.AlbedoColor = sleeve;
        _cuffMaterial.AlbedoColor = cuff;
    }

    public static (Color skin, Color sleeve, Color cuff) ResolveRaceClassColors(string race, string pClass)
    {
        var lowerRace = (race ?? "").ToLowerInvariant();
        var lowerClass = (pClass ?? "").ToLowerInvariant();

        // 1. Race skin tones
        Color skin;
        if (lowerRace.Contains("halfling") || lowerRace.Contains("hobbit"))
        {
            skin = new Color(0.87f, 0.68f, 0.54f); // Warm sun-kissed
        }
        else if (lowerRace.Contains("gnome"))
        {
            skin = new Color(0.84f, 0.64f, 0.50f); // Rosy tan
        }
        else if (lowerRace.Contains("dwarf"))
        {
            skin = new Color(0.78f, 0.58f, 0.44f); // Ruddy weathered
        }
        else if (lowerRace.Contains("high-elf") || lowerRace.Contains("elf"))
        {
            skin = new Color(0.96f, 0.92f, 0.86f); // Pale luminous
        }
        else if (lowerRace.Contains("half-elf"))
        {
            skin = new Color(0.92f, 0.80f, 0.68f); // Fair
        }
        else if (lowerRace.Contains("half-orc") || lowerRace.Contains("orc") || lowerRace.Contains("kobold"))
        {
            skin = new Color(0.50f, 0.58f, 0.42f); // Olive green/gray
        }
        else if (lowerRace.Contains("half-troll") || lowerRace.Contains("troll") || lowerRace.Contains("half-ogre") || lowerRace.Contains("golem"))
        {
            skin = new Color(0.44f, 0.50f, 0.54f); // Slate gray stone
        }
        else if (lowerRace.Contains("dunadan"))
        {
            skin = new Color(0.85f, 0.70f, 0.58f); // Regal bronze
        }
        else
        {
            skin = new Color(0.88f, 0.74f, 0.62f); // Baseline human
        }

        // 2. Class sleeve fabrics & metal cuffs
        Color sleeve;
        Color cuff;
        if (lowerClass.Contains("mage") || lowerClass.Contains("sorcerer") || lowerClass.Contains("necromancer"))
        {
            sleeve = new Color(0.24f, 0.18f, 0.38f); // Arcane violet
            cuff = new Color(0.85f, 0.75f, 0.30f);   // Gold trim
        }
        else if (lowerClass.Contains("priest") || lowerClass.Contains("druid") || lowerClass.Contains("shaman"))
        {
            sleeve = new Color(0.68f, 0.64f, 0.56f); // Clerical linen
            cuff = new Color(0.55f, 0.45f, 0.32f);   // Brown leather
        }
        else if (lowerClass.Contains("rogue") || lowerClass.Contains("thief") || lowerClass.Contains("assassin"))
        {
            sleeve = new Color(0.18f, 0.18f, 0.20f); // Midnight shadow cloth
            cuff = new Color(0.32f, 0.32f, 0.34f);   // Steel stud
        }
        else if (lowerClass.Contains("ranger") || lowerClass.Contains("archer") || lowerClass.Contains("hunter"))
        {
            sleeve = new Color(0.20f, 0.34f, 0.22f); // Forest green
            cuff = new Color(0.42f, 0.32f, 0.22f);   // Leather cuff
        }
        else if (lowerClass.Contains("barbarian") || lowerClass.Contains("berserker"))
        {
            sleeve = new Color(0.45f, 0.32f, 0.22f); // Tanned hide
            cuff = new Color(0.35f, 0.25f, 0.18f);   // Rawhide bracer
        }
        else if (lowerClass.Contains("paladin") || lowerClass.Contains("warrior") || lowerClass.Contains("knight"))
        {
            sleeve = new Color(0.36f, 0.38f, 0.42f); // Steel / mail vambrace
            cuff = new Color(0.60f, 0.60f, 0.65f);   // Polished steel rim
        }
        else
        {
            sleeve = new Color(0.32f, 0.28f, 0.25f); // Adventurer tunic
            cuff = new Color(0.45f, 0.38f, 0.30f);   // Leather bracer
        }

        return (skin, sleeve, cuff);
    }

    private static (float scale, Vector3 pos, Vector3 rot, Vector3 gripOffset) GetModelTransform(string modelPath, bool isLeft)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            // Bare fist / unarmed rest pose
            if (isLeft)
            {
                return (
                    1f,
                    new Vector3(-0.32f, -0.28f, -0.38f),
                    new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(20), Mathf.DegToRad(-10)),
                    Vector3.Zero
                );
            }
            else
            {
                return (
                    1f,
                    new Vector3(0.32f, -0.26f, -0.38f),
                    new Vector3(Mathf.DegToRad(16), Mathf.DegToRad(-20), Mathf.DegToRad(10)),
                    Vector3.Zero
                );
            }
        }

        var lower = modelPath.ToLowerInvariant();

        if (isLeft)
        {
            if (lower.Contains("torch"))
            {
                return (
                    0.15f,
                    new Vector3(-0.36f, -0.28f, -0.42f),
                    new Vector3(Mathf.DegToRad(-8), Mathf.DegToRad(20), Mathf.DegToRad(-18)),
                    new Vector3(-0.01f, 0.02f, -0.05f)
                );
            }
            if (lower.Contains("shield"))
            {
                return (
                    0.18f,
                    new Vector3(-0.36f, -0.30f, -0.44f),
                    new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(25), Mathf.DegToRad(-10)),
                    new Vector3(-0.03f, 0.03f, -0.06f)
                );
            }
            if (lower.Contains("book") || lower.Contains("spellbook"))
            {
                return (
                    0.18f,
                    new Vector3(-0.35f, -0.28f, -0.44f),
                    new Vector3(Mathf.DegToRad(18), Mathf.DegToRad(20), Mathf.DegToRad(-12)),
                    new Vector3(-0.02f, 0.03f, -0.05f)
                );
            }
            return (
                0.18f,
                new Vector3(-0.36f, -0.30f, -0.44f),
                new Vector3(Mathf.DegToRad(12), Mathf.DegToRad(18), Mathf.DegToRad(-8)),
                new Vector3(-0.02f, 0.02f, -0.04f)
            );
        }
        else
        {
            if (lower.Contains("staff"))
            {
                return (
                    0.14f,
                    new Vector3(0.38f, -0.32f, -0.46f),
                    new Vector3(Mathf.DegToRad(24), Mathf.DegToRad(-20), Mathf.DegToRad(8)),
                    new Vector3(0.01f, -0.02f, -0.06f)
                );
            }
            if (lower.Contains("crossbow"))
            {
                return (
                    0.15f,
                    new Vector3(0.34f, -0.26f, -0.44f),
                    new Vector3(Mathf.DegToRad(8), Mathf.DegToRad(-14), Mathf.DegToRad(5)),
                    new Vector3(0.02f, 0.02f, -0.04f)
                );
            }
            if (lower.Contains("wand"))
            {
                return (
                    0.16f,
                    new Vector3(0.34f, -0.26f, -0.42f),
                    new Vector3(Mathf.DegToRad(28), Mathf.DegToRad(-18), Mathf.DegToRad(8)),
                    new Vector3(0.01f, 0.02f, -0.03f)
                );
            }
            if (lower.Contains("dagger"))
            {
                return (
                    0.17f,
                    new Vector3(0.34f, -0.26f, -0.42f),
                    new Vector3(Mathf.DegToRad(32), Mathf.DegToRad(-20), Mathf.DegToRad(12)),
                    new Vector3(0.01f, 0.02f, -0.03f)
                );
            }
            if (lower.Contains("axe"))
            {
                return (
                    0.16f,
                    new Vector3(0.36f, -0.28f, -0.44f),
                    new Vector3(Mathf.DegToRad(28), Mathf.DegToRad(-22), Mathf.DegToRad(10)),
                    new Vector3(0.01f, 0.02f, -0.04f)
                );
            }
            if (lower.Contains("mace") || lower.Contains("hammer"))
            {
                return (
                    0.16f,
                    new Vector3(0.36f, -0.28f, -0.44f),
                    new Vector3(Mathf.DegToRad(28), Mathf.DegToRad(-22), Mathf.DegToRad(10)),
                    new Vector3(0.01f, 0.02f, -0.04f)
                );
            }
            // Default sword / weapon
            return (
                0.17f,
                new Vector3(0.36f, -0.28f, -0.44f),
                new Vector3(Mathf.DegToRad(28), Mathf.DegToRad(-22), Mathf.DegToRad(10)),
                new Vector3(0.01f, 0.02f, -0.04f)
            );
        }
    }

    private static string ResolveShieldModel(string shieldItem)
    {
        var lower = (shieldItem ?? "").ToLowerInvariant();
        if (lower.Contains("spiked") || lower.Contains("spikes"))
        {
            return "res://assets/models/characters/shield_spikes.gltf";
        }
        if (lower.Contains("round") || lower.Contains("buckler") || lower.Contains("small metal shield") || lower.Contains("small leather shield"))
        {
            return "res://assets/models/characters/shield_round.gltf";
        }
        if (lower.Contains("large") || lower.Contains("tower") || lower.Contains("square") || lower.Contains("dragon") || lower.Contains("pavise"))
        {
            return "res://assets/models/characters/shield_round_barbarian.gltf";
        }

        // Default badge / heater shield
        return "res://assets/models/characters/shield_badge.gltf";
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
                return "res://assets/models/characters/dagger.gltf";
            }

            // Two-Handed Swords & Greatswords
            if (lowerW.Contains("two-handed") || lowerW.Contains("great sword") || lowerW.Contains("claymore") ||
                lowerW.Contains("bastard") || lowerW.Contains("zweihander") || lowerW.Contains("executioner") ||
                lowerW.Contains("flamberge") || lowerW.Contains("blade of chaos"))
            {
                return "res://assets/models/characters/sword_2handed.gltf";
            }

            // Two-Handed Axes & Battleaxes & Heavy Polearms
            if (lowerW.Contains("battle axe") || lowerW.Contains("great axe") || lowerW.Contains("broad axe") ||
                lowerW.Contains("lochaber") || lowerW.Contains("halberd") || lowerW.Contains("poleaxe") ||
                lowerW.Contains("beaked axe"))
            {
                return "res://assets/models/characters/axe_2handed.gltf";
            }

            // 1-Handed Axes & Cleavers & Hatchets
            if (lowerW.Contains("axe") || lowerW.Contains("cleaver") || lowerW.Contains("hatchet") ||
                lowerW.Contains("sickle") || lowerW.Contains("tomahawk"))
            {
                return "res://assets/models/characters/axe_1handed.gltf";
            }

            // Staves, Quarterstaves, Polearms, Spears, Lances, Tridents
            if (lowerW.Contains("staff") || lowerW.Contains("quarterstaff") || lowerW.Contains("spear") ||
                lowerW.Contains("pike") || lowerW.Contains("lance") || lowerW.Contains("trident") ||
                lowerW.Contains("glaive") || lowerW.Contains("scythe") || lowerW.Contains("awl-pike") ||
                lowerW.Contains("lucerne") || lowerW.Contains("naginata"))
            {
                return "res://assets/models/characters/staff.gltf";
            }

            // Wands & Rods
            if (lowerW.Contains("wand") || lowerW.Contains("rod"))
            {
                return "res://assets/models/characters/wand.gltf";
            }

            // War Hammers & Mattocks
            if (lowerW.Contains("hammer") || lowerW.Contains("mattock"))
            {
                return "res://assets/models/characters/hammer.gltf";
            }

            // Maces, Flails, Morning Stars, Clubs, Cudgels, Whips
            if (lowerW.Contains("mace") || lowerW.Contains("flail") || lowerW.Contains("star") ||
                lowerW.Contains("club") || lowerW.Contains("whip") || lowerW.Contains("cudgel") ||
                lowerW.Contains("ball-and-chain") || lowerW.Contains("morning star") || lowerW.Contains("flanged") ||
                lowerW.Contains("lead-filled"))
            {
                return "res://assets/models/characters/mace.gltf";
            }

            // General Swords (Broadsword, Longsword, Shortsword, Scimitar, Sabre, Katana, Cutlass, Tulwar)
            return "res://assets/models/characters/sword_1handed.gltf";
        }

        // 2. If no melee weapon but a shooter/bow is wielded
        if (!string.IsNullOrEmpty(bowItem))
        {
            var lowerB = bowItem.ToLowerInvariant();
            if (lowerB.Contains("heavy crossbow") || lowerB.Contains("arbalest"))
            {
                return "res://assets/models/characters/crossbow_2handed.gltf";
            }
            return "res://assets/models/characters/crossbow_1handed.gltf";
        }

        // 3. Unarmed / Bare Fists
        // Return empty string to display bare fists with punching animation!
        return "";
    }

    private void SetSlotModel(Node3D slot, ref string currentPath, string newPath, float scale)
    {
        if (currentPath == newPath)
        {
            if (!string.IsNullOrEmpty(newPath) && _modelCache.TryGetValue(newPath, out var existing) && existing != null && IsInstanceValid(existing))
            {
                existing.Scale = Vector3.One * scale;
            }
            return;
        }

        // Hide old children
        foreach (var child in slot.GetChildren())
        {
            if (child is Node3D n3d) n3d.Visible = false;
        }

        currentPath = newPath;
        if (string.IsNullOrEmpty(newPath)) return;

        if (!_modelCache.TryGetValue(newPath, out var modelNode) || modelNode == null || !IsInstanceValid(modelNode))
        {
            try
            {
                var scene = GD.Load<PackedScene>(newPath);
                if (scene != null)
                {
                    modelNode = scene.Instantiate<Node3D>();
                    modelNode.Scale = Vector3.One * scale;
                    slot.AddChild(modelNode);
                    _modelCache[newPath] = modelNode;
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
    /// Triggers a fast melee weapon slash/thrust or bare fist punch action.
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
