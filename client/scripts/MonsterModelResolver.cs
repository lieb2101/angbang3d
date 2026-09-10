using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

public enum CreatureTokenType
{
    None,
    Rodent,
    Insect,
    Centipede,
    Arachnid,
    Bat,
    Bird,
    Beast,
    Canine,
    Feline,
    Snake,
    Reptile,
    Quadruped,
    Hybrid,
    Slime,
    Worm,
    Mushroom,
    Eye,
    Dragon,
    Elemental,
    Golem,
    Quylthulg,
    Skeleton,
    Zombie,
    Humanoid,
    Orc,
    Demon,
    Troll,
    Giant,
    Vampire,
    Wraith,
    Lich,
    Ainu,
    Townsperson,
    Naga,
    Ent,
    Mimic,
    CoinSwarm,
    GenericToken
}

public enum EquipmentRole
{
    Auto,           // Inferred automatically from race name and glyph
    Unarmed,        // Bare hands (both left and right hand weapon slots hidden)
    Warrior,        // Sword in right hand, Shield in left hand
    Barbarian,      // Battleaxe in right hand, optional shield in left hand
    Rogue,          // Dagger / Knife in right hand, empty left hand
    Archer,         // Crossbow / Bow in right hand, empty left hand
    Mage,           // Wand / Staff in right hand, Spellbook or empty in left hand
    Cleric,         // Mace in right hand, Spellbook or Shield in left hand
    TwoHandedSword, // 2H Sword in right hand
    TwoHandedAxe,   // 2H Axe in right hand
    TwoHandedStaff  // 2H Staff in right hand
}

public class MonsterEntity
{
    public string Id { get; set; }
    public Node3D RootNode { get; set; }
    public Node3D CharacterNode { get; set; }
    public AnimationPlayer AnimPlayer { get; set; }
    public Label3D Nameplate { get; set; }
    public Vector3 CurrentPos { get; set; }
    public Vector3 TargetPos { get; set; }
    public float CurrentYaw { get; set; }
    public float TargetYaw { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int Hp { get; set; }
    public int HpMax { get; set; }
    public string RaceName { get; set; }
    public char Glyph { get; set; }
    public Color Color { get; set; }
    public bool IsMoving { get; set; }
    public bool IsEthereal { get; set; }
    public bool IsFloating { get; set; }
    public bool IsSpinning { get; set; }
    public float BaseY { get; set; }
    public float FloatOffset { get; set; }
    public float ModelHeight { get; set; }
    public string IdleAnim { get; set; }
    public string WalkAnim { get; set; }
    public int LastSeenFrame { get; set; }

    // Procedural Token Animation references
    public CreatureTokenType TokenType { get; set; } = CreatureTokenType.None;
    public Sprite3D BillboardSprite { get; set; }
    public Node3D BodyNode { get; set; }
    public Node3D HeadNode { get; set; }
    public Node3D TailNode { get; set; }
    public Node3D LeftWingNode { get; set; }
    public Node3D RightWingNode { get; set; }
    public Node3D LeftAntennaNode { get; set; }
    public Node3D RightAntennaNode { get; set; }
    public Node3D LeftMandibleNode { get; set; }
    public Node3D RightMandibleNode { get; set; }
    public Node3D NucleusNode { get; set; }
    public Node3D IrisNode { get; set; }
    public List<Node3D> Legs { get; } = new();
    public List<Node3D> Segments { get; } = new();
    public Vector3 InitialBodyScale { get; set; } = Vector3.One;

    // Monster Status Cues & Target Reticle (Step 10)
    public bool IsAsleep { get; set; }
    public bool IsAfraid { get; set; }
    public bool IsConfused { get; set; }
    public bool IsStunned { get; set; }
    public bool IsTargeted { get; set; }
    public Label3D StatusBadge { get; set; }
    public Label3D TargetBadge { get; set; }
}

/// <summary>
/// Rule defining how a monster race or glyph maps to a 3D character model.
/// </summary>
public class MonsterModelRule
{
    public string ModelPath { get; set; }
    public float Scale { get; set; } = 1.0f;
    public bool IsEthereal { get; set; }
    public bool IsFloating { get; set; }
    public float Speed { get; set; } = 1.0f;
    public EquipmentRole Equipment { get; set; } = EquipmentRole.Auto;
    public Func<string, bool> Matcher { get; set; }

    public MonsterModelRule(
        string modelPath,
        float scale = 1.0f,
        bool isEthereal = false,
        bool isFloating = false,
        float speed = 1.0f,
        EquipmentRole equipment = EquipmentRole.Auto,
        Func<string, bool> matcher = null)
    {
        ModelPath = modelPath;
        Scale = scale;
        IsEthereal = isEthereal;
        IsFloating = isFloating;
        Speed = speed;
        Equipment = equipment;
        Matcher = matcher;
    }

    public MonsterModelRule(
        string modelPath,
        float scale,
        bool isEthereal,
        bool isFloating,
        float speed,
        Func<string, bool> matcher)
        : this(modelPath, scale, isEthereal, isFloating, speed, EquipmentRole.Auto, matcher)
    {
    }
}

/// <summary>
/// Resolves Angband monsters to rich, accurate CC0 3D character models or
/// specialized 3D procedural creature tokens with smooth animations and dynamic lighting.
/// </summary>
public static class MonsterModelResolver
{
    private static readonly Dictionary<string, PackedScene> _modelCache = new();
    private static readonly Dictionary<char, List<MonsterModelRule>> _modelRules = new();
    private static readonly Dictionary<string, (Texture2D Albedo, Texture2D Normal)> _spriteCache = new();

    static MonsterModelResolver()
    {
        InitModelRules();
    }

    /// <summary>
    /// Register a custom 3D model mapping rule for a specific monster glyph.
    /// Rules registered first take precedence over subsequent rules.
    /// </summary>
    public static void RegisterModelRule(char glyph, MonsterModelRule rule)
    {
        if (!_modelRules.TryGetValue(glyph, out var list))
        {
            list = new List<MonsterModelRule>();
            _modelRules[glyph] = list;
        }
        list.Insert(0, rule);
    }

    /// <summary>
    /// Helper to register a model rule with fluent parameters.
    /// </summary>
    public static void RegisterModelRule(
        char glyph,
        string modelPath,
        float scale = 1.0f,
        bool isEthereal = false,
        bool isFloating = false,
        float speed = 1.0f,
        EquipmentRole equipment = EquipmentRole.Auto,
        Func<string, bool> matcher = null)
    {
        RegisterModelRule(glyph, new MonsterModelRule(modelPath, scale, isEthereal, isFloating, speed, equipment, matcher));
    }

    public static MonsterEntity CreateMonsterEntity(JsonElement monster, Vector3 worldPos, Vector3 playerPos, string entityId)
    {
        var glyphStr = monster.TryGetProperty("glyph", out var gProp) ? gProp.GetString() ?? "?" : "?";
        var glyph = glyphStr.Length > 0 ? glyphStr[0] : '?';
        var attr = monster.TryGetProperty("attr", out var aProp) ? aProp.GetInt32() : 1;
        var color = AngbandColors.Get(attr);

        string raceName = monster.TryGetProperty("race", out var rProp) ? rProp.GetString() : null;
        int hp = monster.TryGetProperty("hp", out var hProp) ? hProp.GetInt32() : 0;
        int hpMax = monster.TryGetProperty("hp_max", out var hmProp) ? hmProp.GetInt32() : 0;
        int gridX = monster.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;
        int gridY = monster.TryGetProperty("y", out var yProp) ? yProp.GetInt32() : 0;

        var root = new Node3D { Position = worldPos };
        var charNode = new Node3D();
        root.AddChild(charNode);

        var entity = new MonsterEntity
        {
            Id = entityId,
            RootNode = root,
            CharacterNode = charNode,
            CurrentPos = worldPos,
            TargetPos = worldPos,
            GridX = gridX,
            GridY = gridY,
            Hp = hp,
            HpMax = hpMax,
            RaceName = raceName,
            Glyph = glyph,
            Color = color,
            FloatOffset = (float)(new Random(raceName?.GetHashCode() ?? (gridX * 31 + gridY)).NextDouble() * Math.PI * 2.0),
        };

        // Determine facing to player on initial spawn
        var toPlayer = playerPos - worldPos;
        toPlayer.Y = 0;
        var initialYaw = toPlayer.LengthSquared() > 0.001f ? Mathf.Atan2(toPlayer.X, toPlayer.Z) : 0f;
        entity.CurrentYaw = initialYaw;
        entity.TargetYaw = initialYaw;
        charNode.Rotation = new Vector3(0, initialYaw, 0);

        BuildMonsterVisuals(entity, glyph, raceName, color);

        // Overhead billboarding nameplate with health bar bracket
        var nameplateText = FormatNameplateText(raceName, glyphStr, hp, hpMax);
        var caption = new Label3D
        {
            Text = nameplateText,
            Modulate = hpMax > 0 ? HealthColour(hp, hpMax) : color,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 6,
            FontSize = 24,
            PixelSize = 0.0035f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = true,
            RenderPriority = 10,
            Position = new Vector3(0, entity.ModelHeight + 0.20f, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        root.AddChild(caption);
        entity.Nameplate = caption;

        // Overhead Status Badge (Sleep, Fear, Confusion, Stun)
        var statusBadge = new Label3D
        {
            Text = "",
            Visible = false,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 6,
            FontSize = 26,
            PixelSize = 0.0035f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = true,
            RenderPriority = 12,
            Position = new Vector3(0, entity.ModelHeight + 0.65f, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        root.AddChild(statusBadge);
        entity.StatusBadge = statusBadge;

        // Overhead Target Reticle Badge
        var targetBadge = new Label3D
        {
            Text = "[ ⌖ TARGET ⌖ ]",
            Modulate = new Color(1.0f, 0.92f, 0.30f, 0.98f),
            OutlineModulate = new Color(0.20f, 0.12f, 0.02f, 0.98f),
            OutlineSize = 8,
            FontSize = 28,
            PixelSize = 0.0038f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = true,
            RenderPriority = 14,
            Visible = false,
            Position = new Vector3(0, entity.ModelHeight + 0.95f, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        root.AddChild(targetBadge);
        entity.TargetBadge = targetBadge;

        return entity;
    }

    public static void UpdateMonsterVisual(MonsterEntity entity, JsonElement monster, bool isTargeted = false)
    {
        int hp = monster.TryGetProperty("hp", out var hProp) ? hProp.GetInt32() : entity.Hp;
        int hpMax = monster.TryGetProperty("hp_max", out var hmProp) ? hmProp.GetInt32() : entity.HpMax;
        entity.Hp = hp;
        entity.HpMax = hpMax;

        entity.IsAsleep = monster.TryGetProperty("asleep", out var slProp) && slProp.GetBoolean();
        entity.IsAfraid = monster.TryGetProperty("afraid", out var afProp) && afProp.GetBoolean();
        entity.IsConfused = monster.TryGetProperty("confused", out var cfProp) && cfProp.GetBoolean();
        entity.IsStunned = monster.TryGetProperty("stunned", out var stProp) && stProp.GetBoolean();
        entity.IsTargeted = isTargeted;

        if (entity.Nameplate != null)
        {
            var glyphStr = entity.Glyph.ToString();
            entity.Nameplate.Text = FormatNameplateText(entity.RaceName, glyphStr, hp, hpMax);
            if (hpMax > 0)
            {
                entity.Nameplate.Modulate = HealthColour(hp, hpMax);
            }
        }

        if (entity.StatusBadge != null)
        {
            if (entity.IsAsleep)
            {
                entity.StatusBadge.Text = "💤 Zzz...";
                entity.StatusBadge.Modulate = new Color(0.65f, 0.85f, 1.0f, 0.95f);
                entity.StatusBadge.Visible = true;
            }
            else if (entity.IsAfraid)
            {
                entity.StatusBadge.Text = "⚠ FLEEING";
                entity.StatusBadge.Modulate = new Color(1.0f, 0.35f, 0.20f, 0.98f);
                entity.StatusBadge.Visible = true;
            }
            else if (entity.IsConfused)
            {
                entity.StatusBadge.Text = "🌀 CONFUSED";
                entity.StatusBadge.Modulate = new Color(0.85f, 0.50f, 1.0f, 0.95f);
                entity.StatusBadge.Visible = true;
            }
            else if (entity.IsStunned)
            {
                entity.StatusBadge.Text = "💫 STUNNED";
                entity.StatusBadge.Modulate = new Color(1.0f, 0.90f, 0.25f, 0.95f);
                entity.StatusBadge.Visible = true;
            }
            else
            {
                entity.StatusBadge.Visible = false;
            }
        }

        if (entity.TargetBadge != null)
        {
            entity.TargetBadge.Visible = isTargeted;
        }
    }

    private static string FormatNameplateText(string raceName, string glyphStr, int hp, int hpMax)
    {
        var name = !string.IsNullOrEmpty(raceName) ? raceName : glyphStr;
        if (hpMax > 0)
        {
            var bar = FormatHealthBar(hp, hpMax);
            return $"{name}\n[{glyphStr}] {bar}  {hp}/{hpMax}";
        }
        return !string.IsNullOrEmpty(raceName) ? $"[{glyphStr}] {raceName}" : $"[{glyphStr}]";
    }

    private static void BuildMonsterVisuals(MonsterEntity entity, char glyph, string raceName, Color color)
    {
        var lowerName = (raceName ?? "").ToLowerInvariant();

        // Check for high-fidelity custom rigs (e.g. user-placed Mixamo rigs in assets/models/mixamo/)
        var (modelPath, scale, isEthereal, isFloating, customSpeed, equipmentRole) = ResolveModelConfig(glyph, raceName, lowerName);
        scale = Mathf.Clamp(scale, 0.45f, 2.0f);

        if (modelPath != null && modelPath.Contains("mixamo") && ResourceLoader.Exists(modelPath))
        {
            var scene = GetModel(modelPath);
            if (scene != null)
            {
                var instance = scene.Instantiate<Node3D>();
                var effectiveRole = equipmentRole != EquipmentRole.Auto ? equipmentRole : InferEquipmentRole(glyph, raceName);
                ApplyMonsterEquipment(instance, effectiveRole, glyph, raceName, modelPath);

                if (isEthereal)
                {
                    ApplyEtherealMaterial(instance, color);
                    entity.IsEthereal = true;
                }
                else
                {
                    ApplyCharacterSkinAndTint(instance, glyph, lowerName, color);
                }

                entity.IsFloating = isFloating;
                entity.BaseY = isFloating ? 0.35f : 0.0f;
                entity.ModelHeight = 2.05f * scale;

                instance.Scale = new Vector3(scale * 0.90f, scale * 1.05f, scale * 0.90f);
                instance.Position = Vector3.Zero;

                var ap = FindAnimationPlayer(instance);
                if (ap != null)
                {
                    entity.AnimPlayer = ap;
                    SetupAnimations(entity, ap, customSpeed);
                }

                entity.CharacterNode.AddChild(instance);
                return;
            }
        }

        // Approach 1: High-Resolution Daggerfall 2.5D Dark Fantasy Billboards with PBR Normal Mapping
        BuildDaggerfallBillboard(entity, glyph, lowerName, color, scale, isEthereal, isFloating);
    }

    private static void BuildDaggerfallBillboard(MonsterEntity entity, char glyph, string lowerName, Color color, float scale, bool isEthereal, bool isFloating)
    {
        var spriteArchetype = ResolveDaggerfallArchetype(glyph, lowerName);
        var (albedoTex, normalTex) = GetOrCreateDaggerfallSprite(spriteArchetype, glyph, color);

        var spriteHeight = 2.10f * scale;

        var spriteMat = new StandardMaterial3D
        {
            AlbedoTexture = albedoTex,
            NormalEnabled = true,
            NormalTexture = normalTex,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            AlbedoColor = isEthereal ? new Color(color.R, color.G, color.B, 0.70f) : Colors.White,
        };

        var quadMesh = new QuadMesh
        {
            Size = new Vector2(1.55f * scale, spriteHeight),
        };

        var meshInst = new MeshInstance3D
        {
            Mesh = quadMesh,
            MaterialOverride = spriteMat,
            Position = new Vector3(0, spriteHeight * 0.5f, 0),
        };

        entity.IsFloating = isFloating;
        entity.IsEthereal = isEthereal;
        entity.BaseY = isFloating ? 0.35f : 0.0f;
        entity.ModelHeight = spriteHeight;

        entity.CharacterNode.AddChild(meshInst);
    }

    private static string SelectTownspersonModel(string raceName)
    {
        return "res://assets/models/characters/Rogue.glb";
    }

    private static void AddModelRule(char glyph, MonsterModelRule rule)
    {
        if (!_modelRules.TryGetValue(glyph, out var list))
        {
            list = new List<MonsterModelRule>();
            _modelRules[glyph] = list;
        }
        list.Add(rule);
    }

    private static void InitModelRules()
    {
        // =========================================================================
        // Option 2: Mixamo Dark Fantasy High-Fidelity Rig Registry
        // (Prefers Mixamo rigs when present in assets/models/mixamo/ or assets/models/characters/)
        // =========================================================================

        // 1. Ghosts, Spectres, Poltergeists (G) - Spectral floating robes
        AddModelRule('G', new MonsterModelRule("res://assets/models/mixamo/Wraith.glb", 1.0f, isEthereal: true, isFloating: true, speed: 0.85f, equipment: EquipmentRole.Unarmed));
        AddModelRule('G', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 0.90f, isEthereal: true, isFloating: true, speed: 0.8f, equipment: EquipmentRole.Unarmed));

        // 2. Wights, Wraiths, Nazgul, Ringwraiths (W)
        AddModelRule('W', new MonsterModelRule("res://assets/models/mixamo/Wraith.glb", 1.05f, isEthereal: true, isFloating: true, speed: 0.85f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("nazgul") || n.Contains("ringwraith") || n.Contains("wraith")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/mixamo/DeathKnight.glb", 1.05f, isEthereal: true, isFloating: false, speed: 0.85f, equipment: EquipmentRole.Warrior, matcher: n => n.Contains("wight")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Skeleton_Warrior.glb", 0.95f, isEthereal: true, isFloating: false, speed: 0.85f, equipment: EquipmentRole.Warrior, matcher: n => n.Contains("wight")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 0.95f, isEthereal: true, isFloating: true, speed: 0.85f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("nazgul") || n.Contains("ringwraith") || n.Contains("wraith")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 0.95f, isEthereal: true, isFloating: true, speed: 0.85f, equipment: EquipmentRole.Warrior));

        // 3. Liches and Arch-Liches (L)
        AddModelRule('L', new MonsterModelRule("res://assets/models/mixamo/Necromancer.glb", 1.15f, speed: 0.9f, equipment: EquipmentRole.Mage));
        AddModelRule('L', new MonsterModelRule("res://assets/models/characters/Skeleton_Mage.glb", 1.10f, speed: 0.9f, equipment: EquipmentRole.Mage));

        // 4. Skeletons (s)
        AddModelRule('s', new MonsterModelRule("res://assets/models/mixamo/SkeletonArcher.glb", 0.95f, speed: 1.0f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout") || n.Contains("sniper")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/mixamo/SkeletonMage.glb", 0.95f, speed: 0.9f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("mage") || n.Contains("sorcerer") || n.Contains("druj")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/mixamo/SkeletonWarrior.glb", 1.10f, speed: 1.0f, equipment: EquipmentRole.Warrior, matcher: n => n.Contains("lord") || n.Contains("king") || n.Contains("knight") || n.Contains("champion")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/mixamo/SkeletonWarrior.glb", 0.95f, speed: 1.0f, equipment: EquipmentRole.Warrior));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Rogue.glb", 0.85f, speed: 1.0f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout") || n.Contains("sniper")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Mage.glb", 0.90f, speed: 0.9f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("mage") || n.Contains("sorcerer") || n.Contains("druj")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.70f, speed: 1.1f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("decayed") || n.Contains("crawler") || n.Contains("broken")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.70f, speed: 1.1f, equipment: EquipmentRole.Rogue, matcher: n => n.Contains("minion") || n.Contains("small")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Warrior.glb", 1.20f, speed: 1.0f, equipment: EquipmentRole.Warrior, matcher: n => n.Contains("lord") || n.Contains("king") || n.Contains("knight") || n.Contains("champion")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Skeleton_Warrior.glb", 0.95f, speed: 1.0f, equipment: EquipmentRole.Warrior));

        // 5. Zombies, Mummies, Ghouls (z) - Sinewy rotting ghoul & mutant forms
        AddModelRule('z', new MonsterModelRule("res://assets/models/mixamo/Ghoul.glb", 1.0f, speed: 0.85f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("ghoul") || n.Contains("zombie") || n.Contains("crawler")));
        AddModelRule('z', new MonsterModelRule("res://assets/models/mixamo/Mummy.glb", 1.05f, speed: 0.75f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("mummy") || n.Contains("pharaoh")));
        AddModelRule('z', new MonsterModelRule("res://assets/models/characters/Skeleton_Warrior.glb", 1.05f, speed: 0.75f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("mummy") || n.Contains("greater") || n.Contains("pharaoh")));
        AddModelRule('z', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.85f, speed: 0.70f, equipment: EquipmentRole.Unarmed));

        // 6. Vampires (V) - High-poly gothic vampire lord
        AddModelRule('V', new MonsterModelRule("res://assets/models/mixamo/Vampire.glb", 1.05f, speed: 1.05f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("lord") || n.Contains("master") || n.Contains("ancient") || n.Contains("nosferatu")));
        AddModelRule('V', new MonsterModelRule("res://assets/models/mixamo/Vampire.glb", 1.0f, speed: 1.05f, equipment: EquipmentRole.Unarmed));
        AddModelRule('V', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 1.05f, speed: 1.05f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("lord") || n.Contains("master") || n.Contains("ancient") || n.Contains("nosferatu")));
        AddModelRule('V', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 1.0f, speed: 1.05f, equipment: EquipmentRole.Unarmed));

        // 7. Ainur, Maiar (A)
        AddModelRule('A', new MonsterModelRule("res://assets/models/mixamo/Paladin.glb", 1.20f, speed: 1.0f, equipment: EquipmentRole.Warrior));
        AddModelRule('A', new MonsterModelRule("res://assets/models/characters/Mage.glb", 1.15f, speed: 1.0f, equipment: EquipmentRole.Mage));

        // 8. Major Demons, Balrogs, Pit Fiends (U) - Demonic mutant brute
        AddModelRule('U', new MonsterModelRule("res://assets/models/mixamo/Brute.glb", 1.65f, speed: 1.0f, equipment: EquipmentRole.TwoHandedAxe));
        AddModelRule('U', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 1.65f, speed: 1.0f, equipment: EquipmentRole.TwoHandedAxe));

        // 9. Minor Demons, Imps, Quasits, Lemures (u)
        AddModelRule('u', new MonsterModelRule("res://assets/models/mixamo/Mutant.glb", 0.70f, speed: 1.2f, equipment: EquipmentRole.Unarmed));
        AddModelRule('u', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.65f, speed: 1.2f, equipment: EquipmentRole.Unarmed));

        // 10. Giants, Titans, Cyclops, Morgoth (P)
        AddModelRule('P', new MonsterModelRule("res://assets/models/mixamo/Brute.glb", 2.10f, speed: 0.85f, equipment: EquipmentRole.TwoHandedAxe, matcher: n => n.Contains("morgoth")));
        AddModelRule('P', new MonsterModelRule("res://assets/models/mixamo/Brute.glb", 1.85f, speed: 0.85f, equipment: EquipmentRole.Barbarian));
        AddModelRule('P', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 2.0f, speed: 0.85f, equipment: EquipmentRole.TwoHandedAxe, matcher: n => n.Contains("morgoth")));
        AddModelRule('P', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 1.75f, speed: 0.85f, equipment: EquipmentRole.Barbarian));

        // 11. Trolls (T)
        AddModelRule('T', new MonsterModelRule("res://assets/models/mixamo/Mutant.glb", 1.45f, speed: 0.90f, equipment: EquipmentRole.Barbarian));
        AddModelRule('T', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 1.45f, speed: 0.90f, equipment: EquipmentRole.Barbarian));

        // 12. Ogres (O)
        AddModelRule('O', new MonsterModelRule("res://assets/models/mixamo/Mutant.glb", 1.30f, speed: 0.95f, equipment: EquipmentRole.Barbarian));
        AddModelRule('O', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 1.30f, speed: 0.95f, equipment: EquipmentRole.Barbarian));

        // 13. Yetis (Y) - Bare hands
        AddModelRule('Y', new MonsterModelRule("res://assets/models/mixamo/Brute.glb", 1.35f, speed: 0.90f, equipment: EquipmentRole.Unarmed));
        AddModelRule('Y', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 1.30f, speed: 0.90f, equipment: EquipmentRole.Unarmed));

        // 14. Orcs, Goblins, Snagas, Uruks (o)
        AddModelRule('o', new MonsterModelRule("res://assets/models/mixamo/OrcWarrior.glb", 0.90f, speed: 1.0f, equipment: EquipmentRole.Barbarian));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Skeleton_Mage.glb", 0.85f, speed: 1.0f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("shaman") || n.Contains("mage") || n.Contains("curse")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Skeleton_Rogue.glb", 0.85f, speed: 1.05f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout") || n.Contains("tracker") || n.Contains("sniper")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 0.88f, speed: 1.0f, equipment: EquipmentRole.Barbarian));

        // 15. Kobolds (k) and Yeeks (y)
        AddModelRule('k', new MonsterModelRule("res://assets/models/mixamo/Mutant.glb", 0.60f, speed: 1.15f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout")));
        AddModelRule('k', new MonsterModelRule("res://assets/models/characters/Skeleton_Rogue.glb", 0.60f, speed: 1.15f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout")));
        AddModelRule('k', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.60f, speed: 1.15f, equipment: EquipmentRole.Rogue));
        AddModelRule('y', new MonsterModelRule("res://assets/models/characters/Skeleton_Minion.glb", 0.60f, speed: 1.15f, equipment: EquipmentRole.Unarmed));

        // 16. Ents & Trees (l)
        AddModelRule('l', new MonsterModelRule("res://assets/models/props/tree_dead_large.gltf", 1.10f, speed: 0.5f, equipment: EquipmentRole.Unarmed));

        // 17. Mimics (?) and Creeping Coins ($)
        AddModelRule('?', new MonsterModelRule("res://assets/models/dungeon/chest.glb", 0.85f, speed: 1.0f, equipment: EquipmentRole.Unarmed));
        AddModelRule('$', new MonsterModelRule("res://assets/models/dungeon/coin_stack_large.gltf.glb", 0.90f, speed: 1.0f, equipment: EquipmentRole.Unarmed));

        // 18. Humanoids (h) and People / Adventurers (p)
        Func<string, bool> isKnight = n => n.Contains("knight") || n.Contains("paladin") || n.Contains("veteran") ||
            n.Contains("warrior") || n.Contains("soldier") || n.Contains("guard") || n.Contains("captain") ||
            n.Contains("fighter") || n.Contains("champion") || n.Contains("swordsman") || n.Contains("centurion");

        Func<string, bool> isBarb = n => n.Contains("barbarian") || n.Contains("mercenary") || n.Contains("gladiator") ||
            n.Contains("berserker") || n.Contains("bouncer") || n.Contains("ruffian") || n.Contains("beastman");

        Func<string, bool> isMage = n => n.Contains("mage") || n.Contains("wizard") || n.Contains("warlock") ||
            n.Contains("sorcerer") || n.Contains("alchemist") || n.Contains("scholar") || n.Contains("priest") ||
            n.Contains("cleric") || n.Contains("sage") || n.Contains("acolyte") || n.Contains("cultist") ||
            n.Contains("druid") || n.Contains("seer") || n.Contains("shaman") || n.Contains("enchanter") || n.Contains("necromancer");

        Func<string, bool> isThief = n => n.Contains("thief") || n.Contains("rogue") || n.Contains("burglar") ||
            n.Contains("assassin") || n.Contains("cutpurse") || n.Contains("beggar") || n.Contains("scoundrel") ||
            n.Contains("bandit") || n.Contains("brigand") || n.Contains("ninja") || n.Contains("scout") || n.Contains("stalker");

        // Rules for 'h'
        AddModelRule('h', new MonsterModelRule("res://assets/models/mixamo/Paladin.glb", 1.0f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('h', new MonsterModelRule("res://assets/models/mixamo/Barbarian.glb", 1.05f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('h', new MonsterModelRule("res://assets/models/mixamo/Necromancer.glb", 1.0f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('h', new MonsterModelRule("res://assets/models/mixamo/Rogue.glb", 1.0f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Knight.glb", 0.90f * 1.05f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 0.90f * 1.10f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Mage.glb", 0.90f * 0.95f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 0.90f * 0.95f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Rogue.glb", 0.90f));

        // Rules for 'p'
        AddModelRule('p', new MonsterModelRule("res://assets/models/mixamo/Paladin.glb", 1.0f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('p', new MonsterModelRule("res://assets/models/mixamo/Barbarian.glb", 1.05f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('p', new MonsterModelRule("res://assets/models/mixamo/Necromancer.glb", 1.0f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('p', new MonsterModelRule("res://assets/models/mixamo/Rogue.glb", 1.0f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Knight.glb", 0.95f * 1.05f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Barbarian.glb", 0.95f * 1.10f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Mage.glb", 0.95f * 0.95f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Rogue_Hooded.glb", 0.95f * 0.95f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Rogue.glb", 0.95f));

        // 21. Nagas (n)
        AddModelRule('n', new MonsterModelRule("res://assets/models/characters/Mage.glb", 0.90f, speed: 0.95f, equipment: EquipmentRole.Mage));
    }

    private static (string ModelPath, float Scale, bool IsEthereal, bool IsFloating, float Speed, EquipmentRole Equipment) ResolveModelConfig(
        char glyph, string raceName, string lowerName)
    {
        // 20. Townsfolk (t) - completely unarmed / bare-handed
        if (glyph == 't')
        {
            float scale = 0.85f;
            if (lowerName.Contains("dwarf") || lowerName.Contains("hobbit") || lowerName.Contains("gnome") ||
                lowerName.Contains("halfling"))
            {
                scale = 0.65f;
            }
            var townModel = SelectTownspersonModel(raceName);
            return (townModel, Mathf.Clamp(scale, 0.45f, 2.0f), false, false, 1.0f, EquipmentRole.Unarmed);
        }

        if (_modelRules.TryGetValue(glyph, out var rules))
        {
            foreach (var rule in rules)
            {
                if (rule.Matcher == null || rule.Matcher(lowerName))
                {
                    var scale = rule.Scale;
                    if (glyph is 'h' or 'p')
                    {
                        if (lowerName.Contains("dwarf") || lowerName.Contains("hobbit") || lowerName.Contains("gnome") ||
                            lowerName.Contains("halfling") || lowerName.Contains("leprechaun"))
                        {
                            scale *= (0.65f / 0.90f);
                        }
                        else if (lowerName.Contains("elf") || lowerName.Contains("ranger") || lowerName.Contains("dunedain"))
                        {
                            scale *= (0.98f / 0.90f);
                        }
                    }
                    return (rule.ModelPath, Mathf.Clamp(scale, 0.45f, 2.0f), rule.IsEthereal, rule.IsFloating, rule.Speed, rule.Equipment);
                }
            }
        }

        return (null, 1.0f, false, false, 1.0f, EquipmentRole.Auto);
    }

    private static string ResolveDaggerfallArchetype(char glyph, string lowerName)
    {
        return glyph switch
        {
            's' => "skeleton_warrior",
            'z' => "ghoul_undead",
            'L' => "arch_lich",
            'V' => "vampire_lord",
            'W' => "wraith_nazgul",
            'G' => "spectral_ghost",
            'o' => "orc_berserker",
            'k' => "kobold_stalker",
            'y' => "yeek_creature",
            'h' or 'p' => lowerName.Contains("knight") || lowerName.Contains("paladin") ? "knight_armored" :
                          lowerName.Contains("mage") || lowerName.Contains("wizard") ? "mage_hooded" :
                          lowerName.Contains("barbarian") ? "barbarian_warrior" : "rogue_shadow",
            't' => "townsperson_peasant",
            'A' => "celestial_angel",
            'U' => "balrog_greater_demon",
            'u' => "imp_lesser_demon",
            'P' => "giant_titan",
            'T' => "troll_cave",
            'O' => "ogre_brute",
            'Y' => "yeti_abominable",
            'd' or 'D' => "dragon_ancient",
            'M' => "hydra_wyrm",
            'S' => "giant_spider",
            'c' => "centipede_horror",
            'a' or 'I' or 'K' => "insect_beetle",
            'b' or 'B' => "bat_vampiric",
            'C' or 'Z' => "hound_shadow",
            'f' => "feline_predator",
            'r' => "rat_plague",
            'J' => "serpent_viper",
            'R' => "reptile_basilisk",
            'q' => "quadruped_beast",
            'H' => "manticore_chimera",
            'e' => "beholder_eye",
            'j' or 'i' => "ooze_slime",
            'w' => "worm_mass",
            'm' or ',' => "mushroom_spore",
            'E' or 'v' => "elemental_vortex",
            'g' or 'X' or 'x' => "golem_stone",
            'Q' => "quylthulg_aberration",
            'l' => "dead_tree_treant",
            '?' => "mimic_chest",
            '$' => "creeping_coins",
            _ => "dark_shadow_entity"
        };
    }

    private static (Texture2D Albedo, Texture2D Normal) GetOrCreateDaggerfallSprite(string archetype, char glyph, Color accentColor)
    {
        var key = $"{archetype}_{glyph}_{accentColor.ToHtml()}";
        if (_spriteCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        const int w = 256;
        const int h = 384;

        var albedoImg = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);
        var normalImg = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);

        // Procedural high-grit Daggerfall dark fantasy sprite & tangent normal generator
        GenerateDaggerfallSpritePixels(albedoImg, normalImg, archetype, glyph, accentColor);

        albedoImg.GenerateMipmaps();
        normalImg.GenerateMipmaps();

        var albedoTex = ImageTexture.CreateFromImage(albedoImg);
        var normalTex = ImageTexture.CreateFromImage(normalImg);

        var result = (albedoTex, normalTex);
        _spriteCache[key] = result;
        return result;
    }

    private static void GenerateDaggerfallSpritePixels(Image albedo, Image normal, string archetype, char glyph, Color accent)
    {
        var w = albedo.GetWidth();
        var h = albedo.GetHeight();

        // Dark fantasy base tones
        var baseBodyColor = archetype switch
        {
            "skeleton_warrior" or "arch_lich" => new Color(0.82f, 0.80f, 0.72f),
            "ghoul_undead" => new Color(0.35f, 0.40f, 0.32f),
            "vampire_lord" => new Color(0.20f, 0.16f, 0.22f),
            "wraith_nazgul" => new Color(0.12f, 0.12f, 0.15f),
            "spectral_ghost" => new Color(0.60f, 0.75f, 0.90f),
            "orc_berserker" => new Color(0.30f, 0.38f, 0.25f),
            "knight_armored" => new Color(0.65f, 0.68f, 0.72f),
            "dragon_ancient" or "balrog_greater_demon" => new Color(0.40f, 0.12f, 0.10f),
            "giant_spider" => new Color(0.16f, 0.14f, 0.18f),
            _ => new Color(0.25f, 0.24f, 0.28f)
        };

        baseBodyColor = baseBodyColor.Lerp(accent, 0.35f);

        for (int y = 0; y < h; y++)
        {
            var ny = 1.0f - (float)y / h; // 0 at bottom, 1 at top
            for (int x = 0; x < w; x++)
            {
                var nx = (float)(x - w / 2) / (w / 2); // -1 left, +1 right

                // Compute anatomical silhouette profile
                var inSilhouette = EvaluateAnatomy(archetype, nx, ny);
                if (!inSilhouette)
                {
                    albedo.SetPixel(x, y, new Color(0, 0, 0, 0));
                    normal.SetPixel(x, y, new Color(0.5f, 0.5f, 1.0f, 0));
                    continue;
                }

                // Shading & gritty relief
                var rimDist = MathF.Abs(nx);
                var lighting = 1.0f - rimDist * 0.45f;
                var noise = ((MathF.Sin(x * 0.4f) * MathF.Cos(y * 0.4f)) + 1.0f) * 0.08f - 0.04f;

                var r = Mathf.Clamp(baseBodyColor.R * lighting + noise, 0f, 1f);
                var g = Mathf.Clamp(baseBodyColor.G * lighting + noise, 0f, 1f);
                var b = Mathf.Clamp(baseBodyColor.B * lighting + noise, 0f, 1f);

                // Eyes / Emissive focal point
                var isEye = ny is >= 0.75f and <= 0.82f && MathF.Abs(nx) is >= 0.10f and <= 0.22f;
                if (isEye)
                {
                    r = Mathf.Clamp(accent.R * 1.5f + 0.3f, 0f, 1f);
                    g = Mathf.Clamp(accent.G * 1.5f + 0.3f, 0f, 1f);
                    b = Mathf.Clamp(accent.B * 1.5f + 0.3f, 0f, 1f);
                }

                albedo.SetPixel(x, y, new Color(r, g, b, 1.0f));

                // High-fidelity tangent normal vector
                var normX = -nx * 0.65f;
                var normY = (ny - 0.5f) * 0.50f;
                var normZ = MathF.Sqrt(Mathf.Clamp(1.0f - (normX * normX + normY * normY), 0.1f, 1.0f));
                var nCol = new Color(normX * 0.5f + 0.5f, normY * 0.5f + 0.5f, normZ * 0.5f + 0.5f, 1.0f);
                normal.SetPixel(x, y, nCol);
            }
        }
    }

    private static bool EvaluateAnatomy(string archetype, float x, float y)
    {
        var absX = MathF.Abs(x);

        // Head (y: 0.70..0.92)
        if (y is >= 0.70f and <= 0.92f)
        {
            var headRadius = 0.28f;
            var dy = y - 0.81f;
            if (absX * absX / (headRadius * headRadius) + (dy * dy) / (0.11f * 0.11f) <= 1.0f)
                return true;
        }

        // Torso / Shoulders (y: 0.38..0.72)
        if (y is >= 0.38f and <= 0.72f)
        {
            var torsoWidth = 0.48f - (0.72f - y) * 0.20f;
            if (absX <= torsoWidth)
                return true;
        }

        // Arms / Wings / Weapons (y: 0.35..0.75, wider)
        if (archetype.Contains("dragon") || archetype.Contains("bat") || archetype.Contains("balrog"))
        {
            if (y is >= 0.40f and <= 0.88f && absX <= 0.88f - (0.88f - y) * 0.3f)
                return true;
        }
        else
        {
            if (y is >= 0.35f and <= 0.65f && absX <= 0.62f)
                return true;
        }

        // Legs / Lower body (y: 0.04..0.40)
        if (y is >= 0.04f and <= 0.40f)
        {
            var legWidth = 0.38f;
            if (absX <= legWidth && !(y < 0.28f && absX < 0.08f)) // bifurcated legs
                return true;
        }

        return false;
    }

    private static void BuildCreatureToken(MonsterEntity entity, char glyph, string lowerName, Color color)
    {
        Node3D tokenBody;
        float height = 1.2f;
        bool isFloating = false;
        bool isSpinning = false;

        switch (glyph)
        {
            case 'e': // Floating Eye
                tokenBody = CreateEyeToken(entity, color);
                height = 1.15f;
                isFloating = true;
                break;

            case 'w': // Worm Mass
                tokenBody = CreateWormToken(entity, color);
                height = 0.45f;
                break;

            case 'j' or 'i': // Jelly, Slime, Ooze, Icky Thing
                tokenBody = CreateSlimeToken(entity, color);
                height = 0.70f;
                break;

            case 'm' or ',': // Mold, Mushroom
                tokenBody = CreateMushroomToken(entity, color);
                height = 0.75f;
                break;

            case 'c': // Centipede (Giant white centipede, etc.)
                tokenBody = CreateCentipedeToken(entity, color, 0.95f);
                height = 0.50f;
                break;

            case 'a' or 'I' or 'K' or 'F': // Ant, Insect, Killer Beetle, Dragonfly
                var isBeetle = glyph == 'K';
                var isFly = glyph == 'F';
                var insectScale = Mathf.Clamp(isBeetle ? 1.25f : (glyph == 'a' ? 0.95f : 0.85f), 0.45f, 2.0f);
                tokenBody = CreateInsectToken(entity, color, insectScale, isFly, isBeetle);
                height = 0.45f + 0.35f * insectScale;
                if (isFly) isFloating = true;
                break;

            case 'S': // Spider, Scorpion
                var spiderScale = Mathf.Clamp(1.0f, 0.45f, 2.0f);
                tokenBody = CreateArachnidToken(entity, color, spiderScale);
                height = 0.45f + 0.35f * spiderScale;
                break;

            case 'b' or 'B': // Bat, Bird, Crow, Raven, Eagle
                var isBird = glyph == 'B';
                tokenBody = CreateBatToken(entity, color, isBird);
                height = 1.05f;
                isFloating = true;
                break;

            case 'r': // Rodent (Giant white mouse, rat, cave rat)
                var rodentScale = Mathf.Clamp(0.85f, 0.45f, 2.0f);
                tokenBody = CreateRodentToken(entity, color, rodentScale);
                height = 0.55f;
                break;

            case 'C' or 'Z': // Canine (Dog, Wolf, Jackal, War Dog, Hound, Zephyr Hound)
                var isHound = glyph == 'Z' || lowerName.Contains("hound") || lowerName.Contains("zephyr");
                var canineScale = Mathf.Clamp(isHound ? 0.95f : (lowerName.Contains("wolf") || lowerName.Contains("warg") ? 1.05f : 0.85f), 0.45f, 2.0f);
                tokenBody = CreateCanineToken(entity, color, canineScale, isHound, lowerName);
                height = 0.55f + 0.35f * canineScale;
                break;

            case 'f': // Feline (Cat, Panther, Tiger, Lion, Leopard, Cheetah, Jagwal)
                var isBigCat = lowerName.Contains("tiger") || lowerName.Contains("lion") || lowerName.Contains("panther") || lowerName.Contains("leopard") || lowerName.Contains("jagwal");
                var felineScale = Mathf.Clamp(isBigCat ? 1.05f : 0.75f, 0.45f, 2.0f);
                tokenBody = CreateFelineToken(entity, color, felineScale, isBigCat, lowerName);
                height = 0.45f + 0.30f * felineScale;
                break;

            case 'J': // Snake (Snake, Viper, Cobra, Python, Rattlesnake, Copperhead)
                var isCobra = lowerName.Contains("cobra") || lowerName.Contains("viper") || lowerName.Contains("asp");
                var snakeScale = Mathf.Clamp(lowerName.Contains("giant") || lowerName.Contains("python") || lowerName.Contains("anaconda") ? 1.15f : 0.85f, 0.45f, 2.0f);
                tokenBody = CreateSnakeToken(entity, color, snakeScale, isCobra, lowerName);
                height = 0.45f * snakeScale;
                break;

            case 'R': // Reptile / Amphibian (Lizard, Gecko, Salamander, Crocodile, Alligator, Basilisk, Frog)
                var isCroc = lowerName.Contains("croc") || lowerName.Contains("alligator") || lowerName.Contains("basilisk");
                var reptScale = Mathf.Clamp(isCroc ? 1.15f : 0.85f, 0.45f, 2.0f);
                tokenBody = CreateReptileToken(entity, color, reptScale, isCroc, lowerName);
                height = 0.45f * reptScale;
                break;

            case 'q': // Quadruped (Boar, Bull, Stag, Bear, Rhino, Horse, Mammoth, Hippo)
                var quadScale = Mathf.Clamp(lowerName.Contains("giant") || lowerName.Contains("mammoth") || lowerName.Contains("rhino") || lowerName.Contains("elephant") ? 1.35f : (lowerName.Contains("bear") ? 1.15f : 0.95f), 0.45f, 2.0f);
                tokenBody = CreateQuadrupedToken(entity, color, quadScale, lowerName);
                height = 0.55f + 0.45f * quadScale;
                break;

            case 'H': // Hybrid (Griffon, Chimera, Manticore, Minotaur, Hippogriff)
                var hybridScale = Mathf.Clamp(1.10f, 0.45f, 2.0f);
                tokenBody = CreateHybridToken(entity, color, hybridScale, lowerName);
                height = 0.65f + 0.50f * hybridScale;
                break;

            case 'd' or 'D' or 'M': // Dragon, Ancient Dragon, Wyrm, Hydra
                var dragonScale = Mathf.Clamp(glyph == 'D' ? 1.75f : glyph == 'M' ? 1.4f : 1.25f, 0.45f, 2.0f);
                tokenBody = CreateDragonToken(entity, color, dragonScale);
                height = 0.25f + 1.10f * dragonScale;
                break;

            case 'E' or 'v': // Elemental, Vortex
                tokenBody = CreateElementalToken(entity, color);
                height = 1.25f;
                isFloating = true;
                isSpinning = true;
                break;

            case 'g' or 'X' or 'x': // Golem, Xorn, Lurker
                tokenBody = CreateGolemToken(entity, color);
                height = 1.10f;
                break;

            case 'Q': // Quylthulg
                tokenBody = CreateQuylthulgToken(entity, color);
                height = 1.05f;
                isFloating = true;
                break;

            default:
                tokenBody = CreateFloatingRunicGem(entity, color, glyph.ToString());
                height = 1.00f;
                break;
        }

        entity.IsFloating = isFloating;
        entity.IsSpinning = isSpinning;
        entity.BaseY = isFloating ? 0.25f : 0.0f;
        entity.ModelHeight = height;

        entity.CharacterNode.AddChild(tokenBody);
    }

    private static Node3D CreateRodentToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Rodent;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var furColor = new Color(0.22f, 0.20f, 0.18f).Lerp(glowColor, 0.60f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.75f };
        var pinkMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.65f, 0.68f), Roughness = 0.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.14f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Main torso
        var bodyMesh = new BoxMesh { Size = new Vector3(0.28f, 0.22f, 0.46f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat });

        // Head with snout
        var headNode = new Node3D { Position = new Vector3(0, 0.05f, 0.26f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new BoxMesh { Size = new Vector3(0.20f, 0.17f, 0.24f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0, 0) });

        // Snout / Nose
        var noseMesh = new BoxMesh { Size = new Vector3(0.06f, 0.05f, 0.06f) };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = pinkMat, Position = new Vector3(0, -0.02f, 0.14f) });

        // Upright rounded ears
        var earMesh = new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.03f) };
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = pinkMat, Position = new Vector3(0.09f, 0.10f, -0.04f), Rotation = new Vector3(0, 0, Mathf.DegToRad(-15)) });
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = pinkMat, Position = new Vector3(-0.09f, 0.10f, -0.04f), Rotation = new Vector3(0, 0, Mathf.DegToRad(15)) });

        // Glowing beady eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        var eyeColor = (glowColor == Colors.White || glowColor.V > 0.85f) ? new Color(0.95f, 0.25f, 0.30f) : glowColor;
        var eyeMat = new StandardMaterial3D { AlbedoColor = eyeColor, EmissionEnabled = true, Emission = eyeColor, EmissionEnergyMultiplier = 1.8f };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.08f, 0.04f, 0.07f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.08f, 0.04f, 0.07f) });

        // Tail (articulated chain extending back)
        var tailNode = new Node3D { Position = new Vector3(0, 0.02f, -0.23f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.035f, Height = 0.22f, RadialSegments = 6 };
        var tailInst1 = new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = pinkMat, Position = new Vector3(0, 0.04f, -0.10f), Rotation = new Vector3(Mathf.DegToRad(-60), 0, 0) };
        tailNode.AddChild(tailInst1);

        var tailMesh2 = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.025f, Height = 0.20f, RadialSegments = 6 };
        var tailInst2 = new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = pinkMat, Position = new Vector3(0, 0.12f, -0.22f), Rotation = new Vector3(Mathf.DegToRad(-25), 0, 0) };
        tailNode.AddChild(tailInst2);

        // 4 Paws on the ground
        var pawMesh = new BoxMesh { Size = new Vector3(0.07f, 0.05f, 0.10f) };
        var pawMat = new StandardMaterial3D { AlbedoColor = furColor * 0.85f, Roughness = 0.8f };
        var pawPositions = new[]
        {
            new Vector3(0.12f, -0.09f, 0.14f),   // Front-Right
            new Vector3(-0.12f, -0.09f, 0.14f),  // Front-Left
            new Vector3(0.13f, -0.09f, -0.14f),  // Back-Right
            new Vector3(-0.13f, -0.09f, -0.14f), // Back-Left
        };
        foreach (var pos in pawPositions)
        {
            var legNode = new Node3D { Position = pos };
            legNode.AddChild(new MeshInstance3D { Mesh = pawMesh, MaterialOverride = pawMat });
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateInsectToken(MonsterEntity entity, Color glowColor, float scale, bool isFlying, bool isBeetle)
    {
        entity.TokenType = CreatureTokenType.Insect;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var chitinColor = new Color(0.16f, 0.15f, 0.17f).Lerp(glowColor, isBeetle ? 0.45f : 0.35f);
        var chitinMat = new StandardMaterial3D { AlbedoColor = chitinColor, Roughness = 0.35f, Metallic = 0.3f };
        var darkMat = new StandardMaterial3D { AlbedoColor = chitinColor * 0.75f, Roughness = 0.4f };

        var thoraxNode = new Node3D { Position = new Vector3(0, 0.16f, 0) };
        container.AddChild(thoraxNode);
        entity.BodyNode = thoraxNode;
        entity.InitialBodyScale = Vector3.One;

        // Thorax (central body)
        var thoraxSize = isBeetle ? new Vector3(0.32f, 0.22f, 0.32f) : new Vector3(0.20f, 0.18f, 0.28f);
        var thoraxMesh = new SphereMesh { Radius = thoraxSize.X * 0.5f, Height = thoraxSize.Y, RadialSegments = 10, Rings = 5 };
        thoraxNode.AddChild(new MeshInstance3D { Mesh = thoraxMesh, MaterialOverride = chitinMat });

        // Abdomen (rear gaster, bulbous and raised)
        var abdomenSize = isBeetle ? new Vector3(0.38f, 0.26f, 0.44f) : new Vector3(0.28f, 0.24f, 0.40f);
        var abdomenMesh = new SphereMesh { Radius = abdomenSize.X * 0.5f, Height = abdomenSize.Z, RadialSegments = 12, Rings = 6 };
        var abdomenPos = isBeetle ? new Vector3(0, 0.05f, -0.32f) : new Vector3(0, 0.08f, -0.30f);
        thoraxNode.AddChild(new MeshInstance3D { Mesh = abdomenMesh, MaterialOverride = chitinMat, Position = abdomenPos });

        // Head
        var headNode = new Node3D { Position = new Vector3(0, 0.02f, isBeetle ? 0.24f : 0.20f) };
        thoraxNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = 0.12f, Height = 0.18f, RadialSegments = 10, Rings = 5 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = chitinMat });

        // Faceted Glowing Compound Eyes
        var eyeMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.6f };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.09f, 0.05f, 0.07f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.09f, 0.05f, 0.07f) });

        // Antennae (Left & Right)
        var antMesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.015f, Height = 0.24f, RadialSegments = 5 };
        var antMat = new StandardMaterial3D { AlbedoColor = glowColor * 0.9f, EmissionEnabled = true, Emission = glowColor * 0.4f, EmissionEnergyMultiplier = 0.8f };

        var leftAnt = new Node3D { Position = new Vector3(0.05f, 0.08f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(35), Mathf.DegToRad(20), Mathf.DegToRad(15)) };
        leftAnt.AddChild(new MeshInstance3D { Mesh = antMesh, MaterialOverride = antMat, Position = new Vector3(0, 0.10f, 0) });
        headNode.AddChild(leftAnt);
        entity.LeftAntennaNode = leftAnt;

        var rightAnt = new Node3D { Position = new Vector3(-0.05f, 0.08f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(35), Mathf.DegToRad(-20), Mathf.DegToRad(-15)) };
        rightAnt.AddChild(new MeshInstance3D { Mesh = antMesh, MaterialOverride = antMat, Position = new Vector3(0, 0.10f, 0) });
        headNode.AddChild(rightAnt);
        entity.RightAntennaNode = rightAnt;

        // Mandibles (curved pinchers)
        var mandMesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.12f) };
        var leftMand = new Node3D { Position = new Vector3(0.06f, -0.04f, 0.10f) };
        leftMand.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(0, 0, 0.05f), Rotation = new Vector3(0, Mathf.DegToRad(-20), 0) });
        headNode.AddChild(leftMand);
        entity.LeftMandibleNode = leftMand;

        var rightMand = new Node3D { Position = new Vector3(-0.06f, -0.04f, 0.10f) };
        rightMand.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(0, 0, 0.05f), Rotation = new Vector3(0, Mathf.DegToRad(20), 0) });
        headNode.AddChild(rightMand);
        entity.RightMandibleNode = rightMand;

        // 6 Angled Legs (3 pairs)
        var legZPositions = new[] { 0.10f, 0.0f, -0.10f };
        var legMeshUpper = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.02f, Height = 0.16f, RadialSegments = 5 };
        var legMeshLower = new CylinderMesh { TopRadius = 0.010f, BottomRadius = 0.015f, Height = 0.16f, RadialSegments = 5 };

        // 3 Left legs
        for (int i = 0; i < 3; i++)
        {
            var legNode = new Node3D { Position = new Vector3(thoraxSize.X * 0.45f, 0, legZPositions[i]) };
            var upper = new MeshInstance3D { Mesh = legMeshUpper, MaterialOverride = darkMat, Position = new Vector3(0.06f, 0.04f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-45)) };
            var lower = new MeshInstance3D { Mesh = legMeshLower, MaterialOverride = darkMat, Position = new Vector3(0.12f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(30)) };
            legNode.AddChild(upper);
            legNode.AddChild(lower);
            thoraxNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }
        // 3 Right legs
        for (int i = 0; i < 3; i++)
        {
            var legNode = new Node3D { Position = new Vector3(-thoraxSize.X * 0.45f, 0, legZPositions[i]) };
            var upper = new MeshInstance3D { Mesh = legMeshUpper, MaterialOverride = darkMat, Position = new Vector3(-0.06f, 0.04f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(45)) };
            var lower = new MeshInstance3D { Mesh = legMeshLower, MaterialOverride = darkMat, Position = new Vector3(-0.12f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-30)) };
            legNode.AddChild(upper);
            legNode.AddChild(lower);
            thoraxNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        // Wings if flying / dragonfly
        if (isFlying)
        {
            var wingMesh = new BoxMesh { Size = new Vector3(0.40f, 0.01f, 0.16f) };
            var wingMat = new StandardMaterial3D { AlbedoColor = new Color(glowColor.R, glowColor.G, glowColor.B, 0.70f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.2f };

            var leftWing = new Node3D { Position = new Vector3(0.10f, 0.10f, 0) };
            leftWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(0.20f, 0, 0) });
            thoraxNode.AddChild(leftWing);
            entity.LeftWingNode = leftWing;

            var rightWing = new Node3D { Position = new Vector3(-0.10f, 0.10f, 0) };
            rightWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(-0.20f, 0, 0) });
            thoraxNode.AddChild(rightWing);
            entity.RightWingNode = rightWing;
        }

        return container;
    }

    private static Node3D CreateCentipedeToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Centipede;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var bodyColor = new Color(0.18f, 0.16f, 0.15f).Lerp(glowColor, 0.40f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.4f, Metallic = 0.25f };
        var darkMat = new StandardMaterial3D { AlbedoColor = bodyColor * 0.75f, Roughness = 0.5f };

        // Head
        var headNode = new Node3D { Position = new Vector3(0, 0.14f, 0.35f) };
        container.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = 0.14f, Height = 0.20f, RadialSegments = 10, Rings = 5 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Glowing Eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.6f };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.08f, 0.05f, 0.07f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.08f, 0.05f, 0.07f) });

        // Long twitching antennae
        var antMesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.015f, Height = 0.26f, RadialSegments = 5 };
        var leftAnt = new Node3D { Position = new Vector3(0.06f, 0.06f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(30), Mathf.DegToRad(25), 0) };
        leftAnt.AddChild(new MeshInstance3D { Mesh = antMesh, MaterialOverride = eyeMat, Position = new Vector3(0, 0.12f, 0) });
        headNode.AddChild(leftAnt);
        entity.LeftAntennaNode = leftAnt;

        var rightAnt = new Node3D { Position = new Vector3(-0.06f, 0.06f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(30), Mathf.DegToRad(-25), 0) };
        rightAnt.AddChild(new MeshInstance3D { Mesh = antMesh, MaterialOverride = eyeMat, Position = new Vector3(0, 0.12f, 0) });
        headNode.AddChild(rightAnt);
        entity.RightAntennaNode = rightAnt;

        // Mandibles
        var mandMesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.10f) };
        var leftMand = new Node3D { Position = new Vector3(0.06f, -0.03f, 0.09f) };
        leftMand.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(0, 0, 0.04f), Rotation = new Vector3(0, Mathf.DegToRad(-25), 0) });
        headNode.AddChild(leftMand);
        entity.LeftMandibleNode = leftMand;

        var rightMand = new Node3D { Position = new Vector3(-0.06f, -0.03f, 0.09f) };
        rightMand.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(0, 0, 0.04f), Rotation = new Vector3(0, Mathf.DegToRad(25), 0) });
        headNode.AddChild(rightMand);
        entity.RightMandibleNode = rightMand;

        // 5 Articulated Segments (with leg pairs on each)
        var segPositionsZ = new[] { 0.20f, 0.06f, -0.08f, -0.22f, -0.36f };
        var segMesh = new SphereMesh { Radius = 0.13f, Height = 0.22f, RadialSegments = 10, Rings = 5 };
        var legMesh = new CylinderMesh { TopRadius = 0.010f, BottomRadius = 0.015f, Height = 0.15f, RadialSegments = 5 };

        for (int i = 0; i < segPositionsZ.Length; i++)
        {
            var segNode = new Node3D { Position = new Vector3(0, 0.12f, segPositionsZ[i]) };
            segNode.AddChild(new MeshInstance3D { Mesh = segMesh, MaterialOverride = bodyMat });

            // Left leg
            var legL = new Node3D { Position = new Vector3(0.12f, 0, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-35)) };
            legL.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = darkMat, Position = new Vector3(0.05f, -0.04f, 0) });
            segNode.AddChild(legL);
            entity.Legs.Add(legL);

            // Right leg
            var legR = new Node3D { Position = new Vector3(-0.12f, 0, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(35)) };
            legR.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = darkMat, Position = new Vector3(-0.05f, -0.04f, 0) });
            segNode.AddChild(legR);
            entity.Legs.Add(legR);

            container.AddChild(segNode);
            entity.Segments.Add(segNode);
        }

        return container;
    }

    private static Node3D CreateArachnidToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Arachnid;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var bodyColor = new Color(0.15f, 0.14f, 0.16f).Lerp(glowColor, 0.35f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.35f, Metallic = 0.3f };
        var darkMat = new StandardMaterial3D { AlbedoColor = bodyColor * 0.70f, Roughness = 0.4f };

        var thoraxNode = new Node3D { Position = new Vector3(0, 0.18f, 0) };
        container.AddChild(thoraxNode);
        entity.BodyNode = thoraxNode;
        entity.InitialBodyScale = Vector3.One;

        // Cephalothorax (front body)
        var cephMesh = new SphereMesh { Radius = 0.18f, Height = 0.26f, RadialSegments = 10, Rings = 5 };
        thoraxNode.AddChild(new MeshInstance3D { Mesh = cephMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0, 0.12f) });

        // Abdomen (large bulbous rear)
        var abdMesh = new SphereMesh { Radius = 0.28f, Height = 0.42f, RadialSegments = 12, Rings = 6 };
        thoraxNode.AddChild(new MeshInstance3D { Mesh = abdMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.08f, -0.22f) });

        // Multi-eye cluster (4 glowing eyes)
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };
        thoraxNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.06f, 0.08f, 0.24f) });
        thoraxNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.06f, 0.08f, 0.24f) });
        thoraxNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.03f, 0.03f, 0.26f) });
        thoraxNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.03f, 0.03f, 0.26f) });

        // 8 Long Angled Legs (4 Left, 4 Right) spanning wide
        var legZPositions = new[] { 0.16f, 0.06f, -0.04f, -0.14f };
        var legAngles = new[] { 35f, 15f, -15f, -35f };
        var legUpperMesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.02f, Height = 0.24f, RadialSegments = 5 };
        var legLowerMesh = new CylinderMesh { TopRadius = 0.010f, BottomRadius = 0.015f, Height = 0.26f, RadialSegments = 5 };

        for (int i = 0; i < 4; i++)
        {
            // Left leg
            var legNodeL = new Node3D { Position = new Vector3(0.14f, 0, legZPositions[i]), Rotation = new Vector3(0, Mathf.DegToRad(legAngles[i]), 0) };
            var upperL = new MeshInstance3D { Mesh = legUpperMesh, MaterialOverride = darkMat, Position = new Vector3(0.08f, 0.08f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-55)) };
            var lowerL = new MeshInstance3D { Mesh = legLowerMesh, MaterialOverride = darkMat, Position = new Vector3(0.20f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(40)) };
            legNodeL.AddChild(upperL);
            legNodeL.AddChild(lowerL);
            thoraxNode.AddChild(legNodeL);
            entity.Legs.Add(legNodeL);

            // Right leg
            var legNodeR = new Node3D { Position = new Vector3(-0.14f, 0, legZPositions[i]), Rotation = new Vector3(0, Mathf.DegToRad(-legAngles[i]), 0) };
            var upperR = new MeshInstance3D { Mesh = legUpperMesh, MaterialOverride = darkMat, Position = new Vector3(-0.08f, 0.08f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(55)) };
            var lowerR = new MeshInstance3D { Mesh = legLowerMesh, MaterialOverride = darkMat, Position = new Vector3(-0.20f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-40)) };
            legNodeR.AddChild(upperR);
            legNodeR.AddChild(lowerR);
            thoraxNode.AddChild(legNodeR);
            entity.Legs.Add(legNodeR);
        }

        return container;
    }

    private static Node3D CreateBatToken(MonsterEntity entity, Color glowColor, bool isBird)
    {
        entity.TokenType = isBird ? CreatureTokenType.Bird : CreatureTokenType.Bat;
        var container = new Node3D { Position = new Vector3(0, 0.70f, 0) };

        var bodyColor = isBird
            ? new Color(0.25f, 0.22f, 0.20f).Lerp(glowColor, 0.45f)
            : new Color(0.15f, 0.12f, 0.14f).Lerp(glowColor, 0.35f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.6f };

        var bodyMesh = new BoxMesh { Size = isBird ? new Vector3(0.24f, 0.20f, 0.38f) : new Vector3(0.20f, 0.20f, 0.32f) };
        var bodyInst = new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat };
        container.AddChild(bodyInst);
        entity.BodyNode = bodyInst;
        entity.InitialBodyScale = Vector3.One;

        // Glowing Eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.07f, 0.06f, 0.18f) });
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.07f, 0.06f, 0.18f) });

        // Left Wing with Pivot Node
        var wingMesh = new BoxMesh { Size = isBird ? new Vector3(0.48f, 0.02f, 0.26f) : new Vector3(0.42f, 0.02f, 0.24f) };
        var wingMat = new StandardMaterial3D { AlbedoColor = bodyColor * 0.9f, Roughness = 0.7f };

        var leftWing = new Node3D { Position = new Vector3(0.10f, 0.05f, 0) };
        leftWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(0.22f, 0, 0) });
        container.AddChild(leftWing);
        entity.LeftWingNode = leftWing;

        var rightWing = new Node3D { Position = new Vector3(-0.10f, 0.05f, 0) };
        rightWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(-0.22f, 0, 0) });
        container.AddChild(rightWing);
        entity.RightWingNode = rightWing;

        return container;
    }

    private static Node3D CreateEyeToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Eye;
        var container = new Node3D { Position = new Vector3(0, 0.75f, 0) };

        // Sclera (central demonic eyeball) with glossy wet specular
        var eyeMesh = new SphereMesh { Radius = 0.32f, Height = 0.64f, RadialSegments = 16, Rings = 8 };
        var eyeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.90f, 0.88f, 0.82f),
            Roughness = 0.12f,
            Metallic = 0.05f,
            RimEnabled = true,
            Rim = 0.5f,
            RimTint = 0.3f,
        };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat });

        // Iris & Slit Pupil attached to an Iris pivot node
        var irisPivot = new Node3D { Position = new Vector3(0, 0, 0.30f) };
        container.AddChild(irisPivot);
        entity.IrisNode = irisPivot;

        var irisMesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.05f, RadialSegments = 12 };
        var irisMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.8f,
        };
        var irisInst = new MeshInstance3D
        {
            Mesh = irisMesh,
            MaterialOverride = irisMat,
            Rotation = new Vector3(Mathf.DegToRad(90), 0, 0),
        };
        irisPivot.AddChild(irisInst);

        var pupilMesh = new BoxMesh { Size = new Vector3(0.04f, 0.22f, 0.06f) }; // Vertical demonic slit pupil
        var pupilMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.04f),
            Roughness = 0.1f,
        };
        var pupilInst = new MeshInstance3D
        {
            Mesh = pupilMesh,
            MaterialOverride = pupilMat,
            Position = new Vector3(0, 0, 0.02f),
        };
        irisPivot.AddChild(pupilInst);

        // 4 Writhing Eyestalks emerging around the crown (Beholder / Gazer anatomy)
        var stalkAngles = new[] { 35f, 125f, -35f, -125f };
        var stalkMesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.04f, Height = 0.28f, RadialSegments = 6 };
        var stalkMat = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.32f, 0.38f), Roughness = 0.4f };
        var miniEyeMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 8, Rings = 4 };

        for (int i = 0; i < stalkAngles.Length; i++)
        {
            var stalkNode = new Node3D
            {
                Position = new Vector3(Mathf.Cos(Mathf.DegToRad(stalkAngles[i])) * 0.22f, 0.24f, Mathf.Sin(Mathf.DegToRad(stalkAngles[i])) * 0.22f),
                Rotation = new Vector3(Mathf.DegToRad(20), Mathf.DegToRad(stalkAngles[i]), 0),
            };
            stalkNode.AddChild(new MeshInstance3D { Mesh = stalkMesh, MaterialOverride = stalkMat, Position = new Vector3(0, 0.12f, 0) });
            stalkNode.AddChild(new MeshInstance3D { Mesh = miniEyeMesh, MaterialOverride = irisMat, Position = new Vector3(0, 0.26f, 0) });
            container.AddChild(stalkNode);
        }

        return container;
    }

    private static Node3D CreateSlimeToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Slime;
        var container = new Node3D { Position = new Vector3(0, 0, 0) };

        var domeMesh = new SphereMesh { Radius = 0.42f, Height = 0.50f, RadialSegments = 16, Rings = 8 };
        var domeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(glowColor.R, glowColor.G, glowColor.B, 0.68f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.1f,
            EmissionEnabled = true,
            Emission = glowColor * 0.6f,
            EmissionEnergyMultiplier = 1.0f,
        };
        var domeInst = new MeshInstance3D { Mesh = domeMesh, MaterialOverride = domeMat, Position = new Vector3(0, 0.25f, 0) };
        container.AddChild(domeInst);
        entity.BodyNode = domeInst;
        entity.InitialBodyScale = Vector3.One;

        // Inner glowing floating nucleus
        var coreMesh = new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 12, Rings = 6 };
        var coreMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.8f,
        };
        var coreInst = new MeshInstance3D { Mesh = coreMesh, MaterialOverride = coreMat, Position = new Vector3(0, 0.20f, 0) };
        container.AddChild(coreInst);
        entity.NucleusNode = coreInst;

        return container;
    }

    private static Node3D CreateWormToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Worm;
        var container = new Node3D { Position = new Vector3(0, 0, 0) };

        var wormColor = new Color(0.25f, 0.20f, 0.18f).Lerp(glowColor, 0.45f);
        var wormMat = new StandardMaterial3D { AlbedoColor = wormColor, Roughness = 0.5f };

        var segMesh = new SphereMesh { Radius = 0.15f, Height = 0.26f, RadialSegments = 10, Rings = 5 };
        var segZ = new[] { 0.28f, 0.14f, 0.0f, -0.14f, -0.28f };

        for (int i = 0; i < segZ.Length; i++)
        {
            var seg = new MeshInstance3D { Mesh = segMesh, MaterialOverride = wormMat, Position = new Vector3(0, 0.14f, segZ[i]) };
            container.AddChild(seg);
            entity.Segments.Add(seg);
        }

        return container;
    }

    private static Node3D CreateMushroomToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Mushroom;
        var container = new Node3D { Position = new Vector3(0, 0, 0) };

        var bodyNode = new Node3D { Position = new Vector3(0, 0, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var stemMesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 0.40f, RadialSegments = 8 };
        var stemMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.70f, 0.62f), Roughness = 0.8f };

        var capMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.24f, Height = 0.18f, RadialSegments = 12 };
        var capMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.1f,
        };

        // Main central mushroom
        bodyNode.AddChild(new MeshInstance3D { Mesh = stemMesh, MaterialOverride = stemMat, Position = new Vector3(0, 0.20f, 0) });
        bodyNode.AddChild(new MeshInstance3D { Mesh = capMesh, MaterialOverride = capMat, Position = new Vector3(0, 0.44f, 0) });

        // Side smaller spores
        var sideStemMesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.06f, Height = 0.25f, RadialSegments = 8 };
        var sideCapMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.14f, Height = 0.12f, RadialSegments = 10 };

        bodyNode.AddChild(new MeshInstance3D { Mesh = sideStemMesh, MaterialOverride = stemMat, Position = new Vector3(0.20f, 0.12f, 0.10f) });
        bodyNode.AddChild(new MeshInstance3D { Mesh = sideCapMesh, MaterialOverride = capMat, Position = new Vector3(0.20f, 0.26f, 0.10f) });

        bodyNode.AddChild(new MeshInstance3D { Mesh = sideStemMesh, MaterialOverride = stemMat, Position = new Vector3(-0.18f, 0.10f, -0.08f) });
        bodyNode.AddChild(new MeshInstance3D { Mesh = sideCapMesh, MaterialOverride = capMat, Position = new Vector3(-0.18f, 0.22f, -0.08f) });

        return container;
    }

    private static Node3D CreateCanineToken(MonsterEntity entity, Color glowColor, float scale, bool isHound, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Canine;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        // Canine fur material
        var baseFur = isHound ? new Color(0.35f, 0.22f, 0.15f) : new Color(0.26f, 0.24f, 0.22f);
        if (lowerName.Contains("hell") || lowerName.Contains("fire")) baseFur = new Color(0.35f, 0.12f, 0.08f);
        else if (lowerName.Contains("frost") || lowerName.Contains("white")) baseFur = new Color(0.85f, 0.85f, 0.90f);
        else if (lowerName.Contains("shadow") || lowerName.Contains("black")) baseFur = new Color(0.10f, 0.10f, 0.12f);

        var furColor = baseFur.Lerp(glowColor, 0.40f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.70f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f), Roughness = 0.50f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.26f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Athletic canine torso: deeper in chest/shoulders, slimmer waist towards hips
        var chestMesh = new BoxMesh { Size = new Vector3(0.34f, 0.36f, 0.38f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = chestMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.02f, 0.14f) });

        var waistMesh = new BoxMesh { Size = new Vector3(0.28f, 0.28f, 0.36f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = waistMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.01f, -0.16f) });

        // Neck angled upward/forward
        var neckMesh = new BoxMesh { Size = new Vector3(0.20f, 0.26f, 0.22f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = neckMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.14f, 0.28f), Rotation = new Vector3(Mathf.DegToRad(30), 0, 0) });

        // Head node
        var headNode = new Node3D { Position = new Vector3(0, 0.24f, 0.38f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new BoxMesh { Size = new Vector3(0.24f, 0.22f, 0.26f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Elongated canine muzzle / snout
        var muzzleMesh = new BoxMesh { Size = new Vector3(0.14f, 0.12f, 0.24f) };
        headNode.AddChild(new MeshInstance3D { Mesh = muzzleMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.04f, 0.18f) });

        // Dark nose pad
        var noseMesh = new BoxMesh { Size = new Vector3(0.08f, 0.06f, 0.06f) };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.02f, 0.30f) });

        // Canine Ears: pointed wolf ears or folded hound ears
        if (isHound)
        {
            // Drooping / folded hound ears
            var houndEarMesh = new BoxMesh { Size = new Vector3(0.05f, 0.18f, 0.08f) };
            headNode.AddChild(new MeshInstance3D { Mesh = houndEarMesh, MaterialOverride = bodyMat, Position = new Vector3(0.13f, 0.02f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(15), 0, Mathf.DegToRad(-15)) });
            headNode.AddChild(new MeshInstance3D { Mesh = houndEarMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.13f, 0.02f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(15), 0, Mathf.DegToRad(15)) });
        }
        else
        {
            // Upright triangular pointed wolf/dog ears
            var wolfEarMesh = new BoxMesh { Size = new Vector3(0.06f, 0.16f, 0.05f) };
            headNode.AddChild(new MeshInstance3D { Mesh = wolfEarMesh, MaterialOverride = bodyMat, Position = new Vector3(0.10f, 0.16f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-10), 0, Mathf.DegToRad(-15)) });
            headNode.AddChild(new MeshInstance3D { Mesh = wolfEarMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.10f, 0.16f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-10), 0, Mathf.DegToRad(15)) });
        }

        // Glowing forward eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.08f, 0.05f, 0.10f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.08f, 0.05f, 0.10f) });

        // Canine tail: arched upward and extending back
        var tailNode = new Node3D { Position = new Vector3(0, 0.08f, -0.32f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.045f, Height = 0.26f, RadialSegments = 6 };
        var tailInst1 = new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = bodyMat, Position = new Vector3(0, 0.10f, -0.08f), Rotation = new Vector3(Mathf.DegToRad(40), 0, 0) };
        tailNode.AddChild(tailInst1);

        var tailMesh2 = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.035f, Height = 0.22f, RadialSegments = 6 };
        var tailInst2 = new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = bodyMat, Position = new Vector3(0, 0.22f, -0.18f), Rotation = new Vector3(Mathf.DegToRad(15), 0, 0) };
        tailNode.AddChild(tailInst2);

        // 4 Athletic Legs with Paws
        var upperLegMesh = new BoxMesh { Size = new Vector3(0.09f, 0.22f, 0.11f) };
        var lowerLegMesh = new BoxMesh { Size = new Vector3(0.07f, 0.18f, 0.08f) };
        var pawMesh = new BoxMesh { Size = new Vector3(0.08f, 0.05f, 0.12f) };

        var legPositions = new[]
        {
            new Vector3(0.14f, -0.12f, 0.22f),  // Front Right
            new Vector3(-0.14f, -0.12f, 0.22f), // Front Left
            new Vector3(0.13f, -0.14f, -0.22f), // Back Right
            new Vector3(-0.13f, -0.14f, -0.22f) // Back Left
        };

        for (int i = 0; i < 4; i++)
        {
            var legNode = new Node3D { Position = legPositions[i] };
            var upper = new MeshInstance3D { Mesh = upperLegMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0, 0) };
            var lower = new MeshInstance3D { Mesh = lowerLegMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.10f, -0.02f) };
            var paw = new MeshInstance3D { Mesh = pawMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.18f, 0.02f) };
            legNode.AddChild(upper);
            legNode.AddChild(lower);
            legNode.AddChild(paw);
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateFelineToken(MonsterEntity entity, Color glowColor, float scale, bool isBigCat, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Feline;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        // Feline fur / coat color
        var baseFur = isBigCat ? (lowerName.Contains("panther") ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.72f, 0.45f, 0.18f)) : new Color(0.40f, 0.35f, 0.30f);
        var furColor = baseFur.Lerp(glowColor, 0.35f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.65f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.10f, 0.10f), Roughness = 0.5f };
        var pinkMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.60f, 0.65f), Roughness = 0.7f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.9f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.18f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Sleek, low-slung, flexible feline torso
        var torsoSize = isBigCat ? new Vector3(0.32f, 0.28f, 0.64f) : new Vector3(0.24f, 0.20f, 0.48f);
        var torsoMesh = new BoxMesh { Size = torsoSize };
        bodyNode.AddChild(new MeshInstance3D { Mesh = torsoMesh, MaterialOverride = bodyMat });

        // Compact rounded feline head
        var headNode = new Node3D { Position = new Vector3(0, 0.08f, torsoSize.Z * 0.52f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headSize = isBigCat ? new Vector3(0.28f, 0.22f, 0.26f) : new Vector3(0.20f, 0.16f, 0.20f);
        var headMesh = new BoxMesh { Size = headSize };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Short blunt feline muzzle
        var muzzleSize = new Vector3(headSize.X * 0.55f, headSize.Y * 0.45f, 0.12f);
        var muzzleMesh = new BoxMesh { Size = muzzleSize };
        headNode.AddChild(new MeshInstance3D { Mesh = muzzleMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -headSize.Y * 0.20f, headSize.Z * 0.50f) });

        // Cute/fierce pink or dark nose
        var noseMesh = new BoxMesh { Size = new Vector3(0.05f, 0.04f, 0.04f) };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = pinkMat, Position = new Vector3(0, -headSize.Y * 0.12f, headSize.Z * 0.50f + 0.06f) });

        // Distinct triangular perked cat ears set wide on top crown
        var earMesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.03f) };
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(headSize.X * 0.35f, headSize.Y * 0.50f + 0.03f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(-15)) });
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(-headSize.X * 0.35f, headSize.Y * 0.50f + 0.03f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(15)) });

        // Glowing feline almond eyes
        var eyeMesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(headSize.X * 0.28f, headSize.Y * 0.12f, headSize.Z * 0.40f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-headSize.X * 0.28f, headSize.Y * 0.12f, headSize.Z * 0.40f) });

        // Long graceful curving feline whip tail
        var tailNode = new Node3D { Position = new Vector3(0, 0.04f, -torsoSize.Z * 0.48f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailSeg1 = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 0.24f, RadialSegments = 6 };
        var tailInst1 = new MeshInstance3D { Mesh = tailSeg1, MaterialOverride = bodyMat, Position = new Vector3(0, 0.04f, -0.10f), Rotation = new Vector3(Mathf.DegToRad(-35), 0, 0) };
        tailNode.AddChild(tailInst1);

        var tailSeg2 = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.02f, Height = 0.22f, RadialSegments = 6 };
        var tailInst2 = new MeshInstance3D { Mesh = tailSeg2, MaterialOverride = bodyMat, Position = new Vector3(0, 0.14f, -0.22f), Rotation = new Vector3(Mathf.DegToRad(30), 0, 0) };
        tailNode.AddChild(tailInst2);

        // 4 Slender stealthy feline legs
        var legMesh = new BoxMesh { Size = new Vector3(0.08f, 0.18f, 0.09f) };
        var pawMesh = new BoxMesh { Size = new Vector3(0.08f, 0.04f, 0.10f) };

        var legPositions = new[]
        {
            new Vector3(torsoSize.X * 0.42f, -0.09f, torsoSize.Z * 0.35f),
            new Vector3(-torsoSize.X * 0.42f, -0.09f, torsoSize.Z * 0.35f),
            new Vector3(torsoSize.X * 0.42f, -0.09f, -torsoSize.Z * 0.35f),
            new Vector3(-torsoSize.X * 0.42f, -0.09f, -torsoSize.Z * 0.35f)
        };

        for (int i = 0; i < 4; i++)
        {
            var legNode = new Node3D { Position = legPositions[i] };
            legNode.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = bodyMat });
            legNode.AddChild(new MeshInstance3D { Mesh = pawMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.10f, 0.02f) });
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateSnakeToken(MonsterEntity entity, Color glowColor, float scale, bool isCobra, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Snake;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        // Snake scales color
        var baseScales = isCobra ? new Color(0.20f, 0.28f, 0.18f) : new Color(0.30f, 0.25f, 0.15f);
        if (lowerName.Contains("black") || lowerName.Contains("shadow")) baseScales = new Color(0.12f, 0.12f, 0.15f);
        else if (lowerName.Contains("fire") || lowerName.Contains("red")) baseScales = new Color(0.45f, 0.15f, 0.10f);

        var scaleColor = baseScales.Lerp(glowColor, 0.40f);
        var snakeMat = new StandardMaterial3D { AlbedoColor = scaleColor, Roughness = 0.45f, Metallic = 0.2f };
        var underMat = new StandardMaterial3D { AlbedoColor = scaleColor.Lightened(0.25f), Roughness = 0.5f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 2.0f };
        var tongueMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.15f, 0.20f), EmissionEnabled = true, Emission = new Color(0.95f, 0.15f, 0.20f), EmissionEnergyMultiplier = 1.0f };

        // Head node raised alertly off the ground
        var headNode = new Node3D { Position = new Vector3(0, 0.24f, 0.42f) };
        container.AddChild(headNode);
        entity.HeadNode = headNode;

        // Triangular serpent head
        var headMesh = new BoxMesh { Size = new Vector3(0.24f, 0.12f, 0.28f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = snakeMat });

        // Tapered snout
        var snoutMesh = new BoxMesh { Size = new Vector3(0.16f, 0.08f, 0.14f) };
        headNode.AddChild(new MeshInstance3D { Mesh = snoutMesh, MaterialOverride = snakeMat, Position = new Vector3(0, -0.02f, 0.16f) });

        // Cobra hood if cobra
        if (isCobra)
        {
            var hoodMesh = new BoxMesh { Size = new Vector3(0.46f, 0.22f, 0.06f) };
            headNode.AddChild(new MeshInstance3D { Mesh = hoodMesh, MaterialOverride = snakeMat, Position = new Vector3(0, -0.04f, -0.08f) });
        }

        // Forked tongue darting from snout
        var tongueNode = new Node3D { Position = new Vector3(0, -0.03f, 0.23f) };
        var tongueStem = new BoxMesh { Size = new Vector3(0.02f, 0.01f, 0.12f) };
        var forkL = new BoxMesh { Size = new Vector3(0.015f, 0.01f, 0.06f) };
        var forkR = new BoxMesh { Size = new Vector3(0.015f, 0.01f, 0.06f) };

        tongueNode.AddChild(new MeshInstance3D { Mesh = tongueStem, MaterialOverride = tongueMat, Position = new Vector3(0, 0, 0.06f) });
        tongueNode.AddChild(new MeshInstance3D { Mesh = forkL, MaterialOverride = tongueMat, Position = new Vector3(0.02f, 0, 0.13f), Rotation = new Vector3(0, Mathf.DegToRad(30), 0) });
        tongueNode.AddChild(new MeshInstance3D { Mesh = forkR, MaterialOverride = tongueMat, Position = new Vector3(-0.02f, 0, 0.13f), Rotation = new Vector3(0, Mathf.DegToRad(-30), 0) });
        headNode.AddChild(tongueNode);
        entity.LeftAntennaNode = tongueNode; // Re-use node ref for tongue darting animation

        // Glowing slit serpent eyes
        var eyeMesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.10f, 0.03f, 0.08f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.10f, 0.03f, 0.08f) });

        // 7 Articulated Serpentine Body Segments cascading back
        var segZPositions = new[] { 0.28f, 0.14f, 0.00f, -0.14f, -0.28f, -0.42f, -0.56f };
        var segRadii = new[] { 0.12f, 0.11f, 0.10f, 0.09f, 0.08f, 0.06f, 0.04f };

        for (int i = 0; i < segZPositions.Length; i++)
        {
            var r = segRadii[i];
            var segNode = new Node3D { Position = new Vector3(0, r + 0.02f, segZPositions[i]) };
            var segMesh = new SphereMesh { Radius = r, Height = r * 2.2f, RadialSegments = 10, Rings = 5 };
            segNode.AddChild(new MeshInstance3D { Mesh = segMesh, MaterialOverride = snakeMat });

            // Underbelly plating
            var bellyMesh = new BoxMesh { Size = new Vector3(r * 1.5f, 0.03f, r * 1.8f) };
            segNode.AddChild(new MeshInstance3D { Mesh = bellyMesh, MaterialOverride = underMat, Position = new Vector3(0, -r * 0.7f, 0) });

            container.AddChild(segNode);
            entity.Segments.Add(segNode);
        }

        return container;
    }

    private static Node3D CreateReptileToken(MonsterEntity entity, Color glowColor, float scale, bool isCroc, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Reptile;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var baseSkin = isCroc ? new Color(0.22f, 0.28f, 0.18f) : new Color(0.25f, 0.35f, 0.22f);
        if (lowerName.Contains("basilisk")) baseSkin = new Color(0.30f, 0.20f, 0.35f);
        else if (lowerName.Contains("salamander") || lowerName.Contains("fire")) baseSkin = new Color(0.55f, 0.20f, 0.10f);

        var skinColor = baseSkin.Lerp(glowColor, 0.40f);
        var reptMat = new StandardMaterial3D { AlbedoColor = skinColor, Roughness = 0.50f, Metallic = 0.15f };
        var darkMat = new StandardMaterial3D { AlbedoColor = skinColor * 0.70f, Roughness = 0.60f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.14f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Broad, flat, low-slung reptilian body
        var bodySize = isCroc ? new Vector3(0.44f, 0.20f, 0.72f) : new Vector3(0.34f, 0.18f, 0.54f);
        var bodyMesh = new BoxMesh { Size = bodySize };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = reptMat });

        // Scaly dorsal spine ridge along back
        var ridgeMesh = new BoxMesh { Size = new Vector3(0.04f, 0.08f, bodySize.Z * 0.85f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = ridgeMesh, MaterialOverride = darkMat, Position = new Vector3(0, bodySize.Y * 0.50f + 0.03f, 0) });

        // Flat broad reptilian head
        var headNode = new Node3D { Position = new Vector3(0, 0.02f, bodySize.Z * 0.52f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headSize = isCroc ? new Vector3(0.32f, 0.14f, 0.38f) : new Vector3(0.24f, 0.12f, 0.26f);
        var headMesh = new BoxMesh { Size = headSize };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = reptMat });

        // Lateral reptilian eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(headSize.X * 0.40f, headSize.Y * 0.35f, 0.04f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-headSize.X * 0.40f, headSize.Y * 0.35f, 0.04f) });

        // Heavy tapering muscular tail
        var tailNode = new Node3D { Position = new Vector3(0, 0, -bodySize.Z * 0.50f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.12f, Height = 0.55f, RadialSegments = 8 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh, MaterialOverride = reptMat, Position = new Vector3(0, 0, -0.26f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // 4 Sprawling reptilian legs (splayed outwards horizontally, then down)
        var upperLegMesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 0.18f, RadialSegments = 6 };
        var lowerLegMesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.035f, Height = 0.16f, RadialSegments = 6 };
        var clawMesh = new BoxMesh { Size = new Vector3(0.09f, 0.03f, 0.11f) };

        var legZ = new[] { bodySize.Z * 0.32f, -bodySize.Z * 0.32f };
        for (int i = 0; i < 2; i++)
        {
            // Right leg (splayed right)
            var legR = new Node3D { Position = new Vector3(bodySize.X * 0.50f, 0, legZ[i]) };
            legR.AddChild(new MeshInstance3D { Mesh = upperLegMesh, MaterialOverride = reptMat, Position = new Vector3(0.08f, 0.02f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(75)) });
            legR.AddChild(new MeshInstance3D { Mesh = lowerLegMesh, MaterialOverride = reptMat, Position = new Vector3(0.16f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-15)) });
            legR.AddChild(new MeshInstance3D { Mesh = clawMesh, MaterialOverride = darkMat, Position = new Vector3(0.18f, -0.12f, 0.03f) });
            bodyNode.AddChild(legR);
            entity.Legs.Add(legR);

            // Left leg (splayed left)
            var legL = new Node3D { Position = new Vector3(-bodySize.X * 0.50f, 0, legZ[i]) };
            legL.AddChild(new MeshInstance3D { Mesh = upperLegMesh, MaterialOverride = reptMat, Position = new Vector3(-0.08f, 0.02f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-75)) });
            legL.AddChild(new MeshInstance3D { Mesh = lowerLegMesh, MaterialOverride = reptMat, Position = new Vector3(-0.16f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(15)) });
            legL.AddChild(new MeshInstance3D { Mesh = clawMesh, MaterialOverride = darkMat, Position = new Vector3(-0.18f, -0.12f, 0.03f) });
            bodyNode.AddChild(legL);
            entity.Legs.Add(legL);
        }

        return container;
    }

    private static Node3D CreateQuadrupedToken(MonsterEntity entity, Color glowColor, float scale, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Quadruped;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var isBoar = lowerName.Contains("boar") || lowerName.Contains("pig") || lowerName.Contains("swine");
        var isBull = lowerName.Contains("bull") || lowerName.Contains("ox") || lowerName.Contains("minotaur") || lowerName.Contains("bison") || lowerName.Contains("yak");
        var isStag = lowerName.Contains("stag") || lowerName.Contains("deer") || lowerName.Contains("elk") || lowerName.Contains("moose");
        var isBear = lowerName.Contains("bear");

        var baseFur = isBear ? new Color(0.24f, 0.18f, 0.14f) : isBoar ? new Color(0.28f, 0.22f, 0.20f) : new Color(0.32f, 0.25f, 0.18f);
        var furColor = baseFur.Lerp(glowColor, 0.35f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.75f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.08f, 0.08f), Roughness = 0.60f };
        var hornMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.82f, 0.75f), Roughness = 0.40f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.26f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Massive sturdy barrel torso
        var torsoSize = new Vector3(0.44f, 0.38f, 0.70f);
        var torsoMesh = new BoxMesh { Size = torsoSize };
        bodyNode.AddChild(new MeshInstance3D { Mesh = torsoMesh, MaterialOverride = bodyMat });

        // Heavy withers / shoulder hump
        var humpMesh = new BoxMesh { Size = new Vector3(0.40f, 0.14f, 0.35f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = humpMesh, MaterialOverride = bodyMat, Position = new Vector3(0, torsoSize.Y * 0.50f + 0.05f, 0.14f) });

        // Head node
        var headNode = new Node3D { Position = new Vector3(0, 0.10f, torsoSize.Z * 0.50f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headSize = new Vector3(0.30f, 0.28f, 0.34f);
        var headMesh = new BoxMesh { Size = headSize };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Glowing eyes
        var eyeMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.12f, 0.08f, 0.12f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.12f, 0.08f, 0.12f) });

        // Special features: Boar tusks, Bull horns, Stag antlers, Bear ears
        if (isBoar)
        {
            var tuskMesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.035f, Height = 0.18f, RadialSegments = 6 };
            headNode.AddChild(new MeshInstance3D { Mesh = tuskMesh, MaterialOverride = hornMat, Position = new Vector3(0.12f, -0.06f, 0.20f), Rotation = new Vector3(Mathf.DegToRad(35), 0, Mathf.DegToRad(30)) });
            headNode.AddChild(new MeshInstance3D { Mesh = tuskMesh, MaterialOverride = hornMat, Position = new Vector3(-0.12f, -0.06f, 0.20f), Rotation = new Vector3(Mathf.DegToRad(35), 0, Mathf.DegToRad(-30)) });
        }
        else if (isBull || isStag)
        {
            var hornMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.05f, Height = 0.32f, RadialSegments = 6 };
            headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(0.16f, 0.22f, 0.02f), Rotation = new Vector3(Mathf.DegToRad(-20), 0, Mathf.DegToRad(-45)) });
            headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(-0.16f, 0.22f, 0.02f), Rotation = new Vector3(Mathf.DegToRad(-20), 0, Mathf.DegToRad(45)) });
        }
        else
        {
            var earMesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.04f) };
            headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(0.14f, 0.14f, -0.05f) });
            headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.14f, 0.14f, -0.05f) });
        }

        // Tail
        var tailNode = new Node3D { Position = new Vector3(0, 0.06f, -torsoSize.Z * 0.48f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 0.24f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.06f, -0.06f), Rotation = new Vector3(Mathf.DegToRad(-30), 0, 0) });

        // 4 Sturdy Columnar Legs with Hooves/Paws
        var legMesh = new BoxMesh { Size = new Vector3(0.12f, 0.24f, 0.14f) };
        var hoofMesh = new BoxMesh { Size = new Vector3(0.13f, 0.06f, 0.15f) };

        var legPositions = new[]
        {
            new Vector3(0.16f, -0.15f, 0.24f),
            new Vector3(-0.16f, -0.15f, 0.24f),
            new Vector3(0.16f, -0.15f, -0.24f),
            new Vector3(-0.16f, -0.15f, -0.24f)
        };

        for (int i = 0; i < 4; i++)
        {
            var legNode = new Node3D { Position = legPositions[i] };
            legNode.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = bodyMat });
            legNode.AddChild(new MeshInstance3D { Mesh = hoofMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.14f, 0) });
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateHybridToken(MonsterEntity entity, Color glowColor, float scale, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Hybrid;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var furColor = new Color(0.38f, 0.25f, 0.15f).Lerp(glowColor, 0.40f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.65f };
        var wingMat = new StandardMaterial3D { AlbedoColor = furColor.Lightened(0.15f), Roughness = 0.50f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.9f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.24f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var bodyMesh = new BoxMesh { Size = new Vector3(0.40f, 0.34f, 0.65f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat });

        // Head
        var headNode = new Node3D { Position = new Vector3(0, 0.14f, 0.38f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new BoxMesh { Size = new Vector3(0.28f, 0.25f, 0.30f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Horns / Crest
        var hornMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.06f, Height = 0.26f, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = eyeMat, Position = new Vector3(0.12f, 0.18f, -0.04f), Rotation = new Vector3(0, 0, Mathf.DegToRad(-25)) });
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.12f, 0.18f, -0.04f), Rotation = new Vector3(0, 0, Mathf.DegToRad(25)) });

        // Glowing eyes
        var eyeMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.10f, 0.06f, 0.12f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.10f, 0.06f, 0.12f) });

        // Large hybrid wings
        var wingMesh = new BoxMesh { Size = new Vector3(0.55f, 0.03f, 0.32f) };
        var leftWing = new Node3D { Position = new Vector3(0.18f, 0.15f, 0.05f) };
        leftWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(0.28f, 0, 0) });
        bodyNode.AddChild(leftWing);
        entity.LeftWingNode = leftWing;

        var rightWing = new Node3D { Position = new Vector3(-0.18f, 0.15f, 0.05f) };
        rightWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = wingMat, Position = new Vector3(-0.28f, 0, 0) });
        bodyNode.AddChild(rightWing);
        entity.RightWingNode = rightWing;

        // Tail
        var tailNode = new Node3D { Position = new Vector3(0, 0.08f, -0.32f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 0.38f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.12f, -0.14f), Rotation = new Vector3(Mathf.DegToRad(-45), 0, 0) });

        // 4 Legs
        var legMesh = new BoxMesh { Size = new Vector3(0.10f, 0.22f, 0.12f) };
        var legPositions = new[]
        {
            new Vector3(0.15f, -0.16f, 0.20f),
            new Vector3(-0.15f, -0.16f, 0.20f),
            new Vector3(0.15f, -0.16f, -0.20f),
            new Vector3(-0.15f, -0.16f, -0.20f)
        };
        foreach (var pos in legPositions)
        {
            var legNode = new Node3D { Position = pos };
            legNode.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = bodyMat });
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateDragonToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Dragon;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.45f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var scaleColor = new Color(0.12f, 0.10f, 0.12f).Lerp(glowColor, 0.45f);
        var scaleMat = new StandardMaterial3D
        {
            AlbedoColor = scaleColor,
            Roughness = 0.38f,
            Metallic = 0.42f,
            RimEnabled = true,
            Rim = 0.4f,
            RimTint = 0.4f,
        };
        var underbellyMat = new StandardMaterial3D
        {
            AlbedoColor = scaleColor.Lerp(new Color(0.85f, 0.70f, 0.50f), 0.35f),
            Roughness = 0.55f,
        };
        var fireMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 2.2f,
        };

        // Muscular Torso & Chest
        var chestMesh = new BoxMesh { Size = new Vector3(0.55f, 0.48f, 0.75f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = chestMesh, MaterialOverride = scaleMat, Position = new Vector3(0, 0, 0) });

        var bellyMesh = new BoxMesh { Size = new Vector3(0.42f, 0.20f, 0.65f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bellyMesh, MaterialOverride = underbellyMat, Position = new Vector3(0, -0.16f, 0) });

        // Serpentine S-curved Neck & Head
        var neckNode = new Node3D { Position = new Vector3(0, 0.22f, 0.32f) };
        bodyNode.AddChild(neckNode);

        var neckMesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.26f, Height = 0.45f, RadialSegments = 8 };
        neckNode.AddChild(new MeshInstance3D { Mesh = neckMesh, MaterialOverride = scaleMat, Position = new Vector3(0, 0.18f, 0.10f), Rotation = new Vector3(Mathf.DegToRad(35), 0, 0) });

        // Draconic Skull & Snout
        var headNode = new Node3D { Position = new Vector3(0, 0.36f, 0.22f) };
        neckNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new BoxMesh { Size = new Vector3(0.32f, 0.24f, 0.38f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = scaleMat, Position = new Vector3(0, 0, 0) });

        var snoutMesh = new BoxMesh { Size = new Vector3(0.22f, 0.15f, 0.30f) };
        headNode.AddChild(new MeshInstance3D { Mesh = snoutMesh, MaterialOverride = scaleMat, Position = new Vector3(0, -0.04f, 0.28f) });

        // Glowing Fire Gullet (open maw emission)
        var mawMesh = new BoxMesh { Size = new Vector3(0.14f, 0.08f, 0.22f) };
        headNode.AddChild(new MeshInstance3D { Mesh = mawMesh, MaterialOverride = fireMat, Position = new Vector3(0, -0.06f, 0.26f) });

        // Curved Horns
        var hornMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.07f, Height = 0.42f, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = fireMat, Position = new Vector3(0.15f, 0.20f, -0.12f), Rotation = new Vector3(Mathf.DegToRad(-30), 0, Mathf.DegToRad(-25)) });
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = fireMat, Position = new Vector3(-0.15f, 0.20f, -0.12f), Rotation = new Vector3(Mathf.DegToRad(-30), 0, Mathf.DegToRad(25)) });

        // Blazing Draconic Eyes
        var eyeMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = fireMat, Position = new Vector3(0.12f, 0.06f, 0.10f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = fireMat, Position = new Vector3(-0.12f, 0.06f, 0.10f) });

        // Articulated Wings (Left & Right)
        var wingMesh = new BoxMesh { Size = new Vector3(0.95f, 0.02f, 0.55f) };
        var leftWing = new Node3D { Position = new Vector3(0.24f, 0.18f, 0) };
        leftWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = scaleMat, Position = new Vector3(0.45f, 0.15f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(25)) });
        bodyNode.AddChild(leftWing);
        entity.LeftWingNode = leftWing;

        var rightWing = new Node3D { Position = new Vector3(-0.24f, 0.18f, 0) };
        rightWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = scaleMat, Position = new Vector3(-0.45f, 0.15f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-25)) });
        bodyNode.AddChild(rightWing);
        entity.RightWingNode = rightWing;

        // Long Spiked Tail
        var tailNode = new Node3D { Position = new Vector3(0, -0.05f, -0.38f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.10f, BottomRadius = 0.18f, Height = 0.55f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = scaleMat, Position = new Vector3(0, 0.06f, -0.24f), Rotation = new Vector3(Mathf.DegToRad(-75), 0, 0) });

        var tailMesh2 = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.10f, Height = 0.55f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = scaleMat, Position = new Vector3(0, 0.22f, -0.68f), Rotation = new Vector3(Mathf.DegToRad(-60), 0, 0) });

        // 4 Clawed Legs
        var legPositions = new[]
        {
            new Vector3(0.26f, -0.30f, 0.25f),
            new Vector3(-0.26f, -0.30f, 0.25f),
            new Vector3(0.26f, -0.30f, -0.25f),
            new Vector3(-0.26f, -0.30f, -0.25f)
        };
        var legUpperMesh = new CylinderMesh { TopRadius = 0.10f, BottomRadius = 0.08f, Height = 0.35f, RadialSegments = 6 };
        foreach (var pos in legPositions)
        {
            var legNode = new Node3D { Position = pos };
            legNode.AddChild(new MeshInstance3D { Mesh = legUpperMesh, MaterialOverride = scaleMat });
            bodyNode.AddChild(legNode);
            entity.Legs.Add(legNode);
        }

        return container;
    }

    private static Node3D CreateElementalToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Elemental;
        var container = new Node3D { Position = new Vector3(0, 0.65f, 0) };

        // Faceted crystal prism
        var prismMesh = new BoxMesh { Size = new Vector3(0.42f, 0.42f, 0.42f) };
        var prismMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(glowColor.R, glowColor.G, glowColor.B, 0.75f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.15f,
            Metallic = 0.4f,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.35f,
        };

        var prism = new MeshInstance3D
        {
            Mesh = prismMesh,
            MaterialOverride = prismMat,
            Rotation = new Vector3(Mathf.DegToRad(45), Mathf.DegToRad(45), 0),
        };
        container.AddChild(prism);

        // Outer spinning orbital ring
        var ringMesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.48f, Rings = 16, RingSegments = 8 };
        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.5f,
        };
        var ring = new MeshInstance3D
        {
            Mesh = ringMesh,
            MaterialOverride = ringMat,
            Rotation = new Vector3(Mathf.DegToRad(30), 0, 0),
        };
        container.AddChild(ring);

        return container;
    }

    private static Node3D CreateGolemToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Golem;
        var container = new Node3D { Position = new Vector3(0, 0, 0) };

        var bodyNode = new Node3D { Position = new Vector3(0, 0, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Heavy monolithic column
        var colMesh = new BoxMesh { Size = new Vector3(0.55f, 0.85f, 0.45f) };
        var colMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.25f, 0.28f),
            Roughness = 0.8f,
            Metallic = 0.6f,
            EmissionEnabled = true,
            Emission = glowColor * 0.3f,
            EmissionEnergyMultiplier = 0.6f,
        };
        bodyNode.AddChild(new MeshInstance3D { Mesh = colMesh, MaterialOverride = colMat, Position = new Vector3(0, 0.42f, 0) });

        // Glowing Rune Visor
        var visorMesh = new BoxMesh { Size = new Vector3(0.38f, 0.08f, 0.06f) };
        var visorMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.6f,
        };
        bodyNode.AddChild(new MeshInstance3D { Mesh = visorMesh, MaterialOverride = visorMat, Position = new Vector3(0, 0.62f, 0.23f) });

        return container;
    }

    private static Node3D CreateQuylthulgToken(MonsterEntity entity, Color glowColor)
    {
        entity.TokenType = CreatureTokenType.Quylthulg;
        var container = new Node3D { Position = new Vector3(0, 0.60f, 0) };

        var orbMesh = new SphereMesh { Radius = 0.35f, Height = 0.70f, RadialSegments = 16, Rings = 8 };
        var orbMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(glowColor.R * 0.6f, glowColor.G * 0.6f, glowColor.B * 0.6f, 0.80f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.2f,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.2f,
        };
        var orbInst = new MeshInstance3D { Mesh = orbMesh, MaterialOverride = orbMat };
        container.AddChild(orbInst);
        entity.BodyNode = orbInst;
        entity.InitialBodyScale = Vector3.One;

        return container;
    }

    private static Node3D CreateFloatingRunicGem(MonsterEntity entity, Color glowColor, string glyphStr)
    {
        entity.TokenType = CreatureTokenType.GenericToken;
        var container = new Node3D { Position = new Vector3(0, 0.65f, 0) };

        var gemMesh = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.35f) };
        var gemMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(glowColor.R * 0.4f, glowColor.G * 0.4f, glowColor.B * 0.4f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.2f,
            Metallic = 0.4f,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 0.8f,
        };

        var gemInstance = new MeshInstance3D
        {
            Mesh = gemMesh,
            MaterialOverride = gemMat,
            Rotation = new Vector3(Mathf.DegToRad(45), Mathf.DegToRad(45), 0),
        };
        container.AddChild(gemInstance);

        var glyphLabel = new Label3D
        {
            Text = glyphStr,
            Modulate = Colors.White,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
            OutlineSize = 6,
            FontSize = 48,
            PixelSize = 0.0040f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false,
            NoDepthTest = false,
            Position = new Vector3(0, 0, 0),
        };
        container.AddChild(glyphLabel);

        return container;
    }

    private static AnimationPlayer FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindAnimationPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetupAnimations(MonsterEntity entity, AnimationPlayer ap, float speedScale)
    {
        var anims = ap.GetAnimationList();
        if (anims.Length == 0) return;

        string idle = null;
        string walk = null;

        foreach (var name in anims)
        {
            var s = name.ToString().ToLowerInvariant();
            if (idle == null && (s.Contains("idle") || s.Contains("breath") || s.Contains("stand") || s.Contains("take 001") || s.Contains("layer0")))
            {
                idle = name;
            }
            if (walk == null && (s.Contains("walk") || s.Contains("run") || s.Contains("march") || s.Contains("locomotion")))
            {
                walk = name;
            }
        }

        idle ??= anims[0];
        walk ??= (anims.Length > 1 ? anims[1] : idle);

        entity.IdleAnim = idle;
        entity.WalkAnim = walk;

        var idleObj = ap.GetAnimation(idle);
        if (idleObj != null) idleObj.LoopMode = Animation.LoopModeEnum.Linear;

        var walkObj = ap.GetAnimation(walk);
        if (walkObj != null) walkObj.LoopMode = Animation.LoopModeEnum.Linear;

        ap.SpeedScale = speedScale;
        ap.Play(idle);
    }

    public static void PlayIdleAnimation(Node node)
    {
        var ap = FindAnimationPlayer(node);
        if (ap == null) return;
        var anims = ap.GetAnimationList();
        foreach (var name in anims)
        {
            if (name.ToString().ToLowerInvariant().Contains("idle"))
            {
                if (ap.CurrentAnimation != name)
                {
                    ap.Play(name, 0.2f);
                }
                return;
            }
        }
        if (anims.Length > 0 && ap.CurrentAnimation != anims[0])
        {
            ap.Play(anims[0], 0.2f);
        }
    }

    public static void PlayWalkAnimation(Node node)
    {
        var ap = FindAnimationPlayer(node);
        if (ap == null) return;
        var anims = ap.GetAnimationList();
        foreach (var name in anims)
        {
            var s = name.ToString().ToLowerInvariant();
            if (s.Contains("walk") || s.Contains("run"))
            {
                if (ap.CurrentAnimation != name)
                {
                    ap.Play(name, 0.15f);
                }
                return;
            }
        }
    }

    private static void ApplyCharacterSkinAndTint(Node node, char glyph, string lowerName, Color tintColor)
    {
        bool isDemon = glyph == 'U' || glyph == 'u';
        bool isAinu = glyph == 'A';
        bool isUndead = glyph == 's' || glyph == 'z' || glyph == 'L' || glyph == 'V';

        ApplySkinRecursive(node, tintColor, isDemon, isAinu, isUndead);
    }

    private static void ApplySkinRecursive(Node node, Color tintColor, bool isDemon, bool isAinu, bool isUndead)
    {
        if (node is MeshInstance3D mi)
        {
            Texture2D origTex = null;
            if (mi.GetActiveMaterial(0) is StandardMaterial3D existingMat)
            {
                origTex = existingMat.AlbedoTexture;
            }

            var mat = new StandardMaterial3D
            {
                AlbedoTexture = origTex,
                Roughness = 0.55f,
            };

            if (isDemon)
            {
                mat.AlbedoColor = Colors.White.Lerp(new Color(0.95f, 0.35f, 0.20f), 0.55f);
                mat.EmissionEnabled = true;
                mat.Emission = new Color(0.95f, 0.30f, 0.10f);
                mat.EmissionEnergyMultiplier = 0.85f;
            }
            else if (isAinu)
            {
                mat.AlbedoColor = Colors.White.Lerp(tintColor, 0.35f);
                mat.EmissionEnabled = true;
                mat.Emission = tintColor;
                mat.EmissionEnergyMultiplier = 0.65f;
            }
            else if (isUndead)
            {
                // Subtle bone/cold tint for undead
                mat.AlbedoColor = Colors.White.Lerp(tintColor, 0.30f);
            }
            else
            {
                // Natural character skin tint modulated with creature color
                mat.AlbedoColor = Colors.White.Lerp(tintColor, 0.40f);
            }

            mi.MaterialOverride = mat;
        }

        foreach (var child in node.GetChildren())
        {
            ApplySkinRecursive(child, tintColor, isDemon, isAinu, isUndead);
        }
    }

    private static void ApplyEtherealMaterial(Node node, Color spectralColor)
    {
        if (node is MeshInstance3D mi)
        {
            Texture2D origTex = null;
            if (mi.GetActiveMaterial(0) is StandardMaterial3D existingMat)
            {
                origTex = existingMat.AlbedoTexture;
            }

            var mat = new StandardMaterial3D
            {
                AlbedoTexture = origTex,
                AlbedoColor = new Color(spectralColor.R, spectralColor.G, spectralColor.B, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.25f,
                Metallic = 0.1f,
                EmissionEnabled = true,
                Emission = spectralColor * 0.6f,
                EmissionEnergyMultiplier = 0.9f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            mi.MaterialOverride = mat;
        }

        foreach (var child in node.GetChildren())
        {
            ApplyEtherealMaterial(child, spectralColor);
        }
    }

    /// <summary>
    /// Infers an EquipmentRole dynamically based on monster glyph and race name
    /// if not explicitly defined by a MonsterModelRule.
    /// </summary>
    public static EquipmentRole InferEquipmentRole(char glyph, string raceName)
    {
        var lower = (raceName ?? "").ToLowerInvariant();

        // 1. Unarmed non-combatants / townsfolk / creatures without weapon attacks
        if (glyph == 't' ||
            lower.Contains("towns") || lower.Contains("beggar") || lower.Contains("drunkard") ||
            lower.Contains("drunk") || lower.Contains("idiot") || lower.Contains("peasant") ||
            lower.Contains("farmer") || lower.Contains("merchant") || lower.Contains("shopkeeper") ||
            lower.Contains("babe") || lower.Contains("child") || lower.Contains("maiden") ||
            lower.Contains("leper") || lower.Contains("crier") || lower.Contains("hermit") ||
            lower.Contains("scullion") || lower.Contains("scribe") || lower.Contains("pilgrim") ||
            lower.Contains("clerk") || lower.Contains("urchin") || lower.Contains("retainer") ||
            lower.Contains("servant") || lower.Contains("commoner") || lower.Contains("villager") ||
            lower.Contains("citizen") || lower.Contains("cook") || lower.Contains("butcher") ||
            lower.Contains("baker") || lower.Contains("apprentice") || lower.Contains("fool") ||
            lower.Contains("jester") || lower.Contains("naked") || lower.Contains("unarmed") ||
            lower.Contains("brawler") || lower.Contains("ghost") || lower.Contains("spectre") ||
            lower.Contains("poltergeist") || lower.Contains("wraith") || lower.Contains("shadow") ||
            lower.Contains("phantom") || lower.Contains("yeti") || lower.Contains("zombie") ||
            lower.Contains("ghoul") || lower.Contains("vampire"))
        {
            return EquipmentRole.Unarmed;
        }

        // 2. Archers / Scouts / Snipers / Ranged
        if (lower.Contains("archer") || lower.Contains("scout") || lower.Contains("sniper") ||
            lower.Contains("tracker") || lower.Contains("marksman") || lower.Contains("bowman") ||
            lower.Contains("crossbow") || lower.Contains("ranger") || lower.Contains("hunter"))
        {
            return EquipmentRole.Archer;
        }

        // 3. Clerics / Priests / Patriarchs / Acolytes / Cultists
        if (lower.Contains("priest") || lower.Contains("cleric") || lower.Contains("acolyte") ||
            lower.Contains("patriarch") || lower.Contains("cultist") || lower.Contains("bishop") ||
            lower.Contains("zealot") || lower.Contains("disciple"))
        {
            return EquipmentRole.Cleric;
        }

        // 4. Mages / Sorcerers / Shamans / Necromancers / Casters
        if (lower.Contains("mage") || lower.Contains("wizard") || lower.Contains("warlock") ||
            lower.Contains("sorcerer") || lower.Contains("shaman") || lower.Contains("necromancer") ||
            lower.Contains("alchemist") || lower.Contains("scholar") ||
            lower.Contains("sage") ||
            lower.Contains("druid") || lower.Contains("seer") ||
            lower.Contains("enchanter") || lower.Contains("witch") || lower.Contains("curse") ||
            lower.Contains("druj") || lower.Contains("lich"))
        {
            return EquipmentRole.Mage;
        }

        // 5. Rogues / Assassins / Thieves / Cutpurses
        if (lower.Contains("thief") || lower.Contains("rogue") || lower.Contains("burglar") ||
            lower.Contains("assassin") || lower.Contains("cutpurse") || lower.Contains("scoundrel") ||
            lower.Contains("bandit") || lower.Contains("brigand") || lower.Contains("ninja") ||
            lower.Contains("stalker") || lower.Contains("pickpocket"))
        {
            return EquipmentRole.Rogue;
        }

        // 5. Barbarians / Berserkers / Ruffians / Heavy Melee
        if (lower.Contains("barbarian") || lower.Contains("berserker") || lower.Contains("mercenary") ||
            lower.Contains("gladiator") || lower.Contains("bouncer") || lower.Contains("ruffian") ||
            lower.Contains("beastman") || lower.Contains("brute") || lower.Contains("ogre") ||
            lower.Contains("troll") || lower.Contains("giant") || lower.Contains("titan") ||
            lower.Contains("cyclops") || lower.Contains("morgoth") || lower.Contains("balrog") ||
            lower.Contains("demon") || lower.Contains("orc") || lower.Contains("goblin") ||
            lower.Contains("uruk") || lower.Contains("snaga"))
        {
            return EquipmentRole.Barbarian;
        }

        // 6. Warriors / Knights / Guards / Centurions / Paladins
        if (lower.Contains("knight") || lower.Contains("paladin") || lower.Contains("veteran") ||
            lower.Contains("warrior") || lower.Contains("soldier") || lower.Contains("guard") ||
            lower.Contains("captain") || lower.Contains("fighter") || lower.Contains("champion") ||
            lower.Contains("swordsman") || lower.Contains("centurion") || lower.Contains("lord") ||
            lower.Contains("king") || lower.Contains("commander") || lower.Contains("templar") ||
            lower.Contains("patrol"))
        {
            return EquipmentRole.Warrior;
        }

        // Default based on glyph
        return glyph switch
        {
            's' or 'W' => EquipmentRole.Warrior,
            'h' or 'p' => EquipmentRole.Warrior,
            'o' or 'T' or 'P' or 'O' or 'U' => EquipmentRole.Barbarian,
            'A' or 'L' => EquipmentRole.Mage,
            'k' or 'y' or 'u' => EquipmentRole.Rogue,
            _ => EquipmentRole.Unarmed
        };
    }

    /// <summary>
    /// Configures weapons, shields, and items in the left and right hand slots
    /// of a 3D character instance based on their assigned or inferred EquipmentRole.
    /// Non-combatants and unarmed creatures have all weapons hidden (bare hands).
    /// </summary>
    private static void ApplyMonsterEquipment(Node3D instance, EquipmentRole role, char glyph, string raceName, string modelPath)
    {
        Node3D leftHand = null;
        Node3D rightHand = null;
        FindHandSlots(instance, ref leftHand, ref rightHand);

        if (leftHand == null && rightHand == null) return;

        // First, hide all existing embedded weapon and shield meshes in both hands
        HideAllChildren(leftHand);
        HideAllChildren(rightHand);

        if (role == EquipmentRole.Unarmed)
        {
            // Unarmed / bare hands: leave all weapons hidden!
            return;
        }

        var isSkeleton = (modelPath != null && modelPath.Contains("Skeleton_")) || glyph == 's';

        switch (role)
        {
            case EquipmentRole.Warrior:
            {
                // Right hand: 1H Sword
                var sword = FindChildByNameSubstrings(rightHand, "1h_sword", "sword");
                if (sword != null)
                {
                    sword.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Blade.gltf"
                        : "res://assets/models/characters/sword_1handed.gltf");
                }

                // Left hand: Shield
                var shield = FindChildByNameSubstrings(leftHand, "badge_shield", "round_shield", "rectangle_shield", "spike_shield", "shield");
                if (shield != null)
                {
                    shield.Visible = true;
                }
                else
                {
                    AttachWeapon(leftHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Shield_Small_A.gltf"
                        : "res://assets/models/characters/shield_badge.gltf");
                }
                break;
            }

            case EquipmentRole.Barbarian:
            {
                // Right hand: Battleaxe (1H or 2H)
                var axe = FindChildByNameSubstrings(rightHand, "1h_axe", "2h_axe", "axe");
                if (axe != null)
                {
                    axe.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Axe.gltf"
                        : "res://assets/models/characters/axe_1handed.gltf");
                }

                // Left hand: round shield or empty
                var barbShield = FindChildByNameSubstrings(leftHand, "barbarian_round_shield");
                if (barbShield != null)
                {
                    barbShield.Visible = true;
                }
                break;
            }

            case EquipmentRole.Rogue:
            {
                // Right hand: Dagger / Knife
                var knife = FindChildByNameSubstrings(rightHand, "knife", "dagger");
                if (knife != null)
                {
                    knife.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Blade.gltf"
                        : "res://assets/models/characters/dagger.gltf");
                }
                break;
            }

            case EquipmentRole.Archer:
            {
                // Right hand: Crossbow / Bow
                var xbow = FindChildByNameSubstrings(rightHand, "1h_crossbow", "2h_crossbow", "crossbow", "bow");
                if (xbow != null)
                {
                    xbow.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Crossbow.gltf"
                        : "res://assets/models/characters/crossbow_1handed.gltf");
                }
                break;
            }

            case EquipmentRole.Mage:
            {
                // Right hand: Wand / Staff
                var wand = FindChildByNameSubstrings(rightHand, "1h_wand", "2h_staff", "wand", "staff");
                if (wand != null)
                {
                    wand.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Staff.gltf"
                        : "res://assets/models/characters/wand.gltf");
                }

                // Left hand: Spellbook
                var book = FindChildByNameSubstrings(leftHand, "spellbook");
                if (book != null)
                {
                    book.Visible = true;
                }
                break;
            }

            case EquipmentRole.Cleric:
            {
                // Right hand: Mace
                AttachWeapon(rightHand, "res://assets/models/characters/mace.gltf");

                // Left hand: Spellbook or Shield
                var book = FindChildByNameSubstrings(leftHand, "spellbook");
                if (book != null)
                {
                    book.Visible = true;
                }
                else
                {
                    AttachWeapon(leftHand, "res://assets/models/characters/spellbook_closed.gltf");
                }
                break;
            }

            case EquipmentRole.TwoHandedSword:
            {
                var sword2H = FindChildByNameSubstrings(rightHand, "2h_sword", "sword");
                if (sword2H != null)
                {
                    sword2H.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, "res://assets/models/characters/sword_2handed.gltf");
                }
                break;
            }

            case EquipmentRole.TwoHandedAxe:
            {
                var axe2H = FindChildByNameSubstrings(rightHand, "2h_axe", "axe");
                if (axe2H != null)
                {
                    axe2H.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Axe.gltf"
                        : "res://assets/models/characters/axe_2handed.gltf");
                }
                break;
            }

            case EquipmentRole.TwoHandedStaff:
            {
                var staff2H = FindChildByNameSubstrings(rightHand, "2h_staff", "staff");
                if (staff2H != null)
                {
                    staff2H.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, isSkeleton
                        ? "res://assets/models/characters/Skeleton_Staff.gltf"
                        : "res://assets/models/characters/staff.gltf");
                }
                break;
            }
        }
    }

    private static void FindHandSlots(Node node, ref Node3D leftHand, ref Node3D rightHand)
    {
        if (node == null) return;
        var name = node.Name.ToString().ToLowerInvariant();
        if (leftHand == null && (name == "handslot.l" || name == "handslot_l" || name == "handslotleft" || (name.StartsWith("handslot") && name.EndsWith("l"))))
        {
            if (node is Node3D n3d) leftHand = n3d;
        }
        else if (rightHand == null && (name == "handslot.r" || name == "handslot_r" || name == "handslotright" || (name.StartsWith("handslot") && name.EndsWith("r"))))
        {
            if (node is Node3D n3d) rightHand = n3d;
        }

        foreach (var child in node.GetChildren())
        {
            FindHandSlots(child, ref leftHand, ref rightHand);
        }
    }

    private static void HideAllChildren(Node parent)
    {
        if (parent == null) return;
        foreach (var child in parent.GetChildren())
        {
            if (child is Node3D n3d)
            {
                n3d.Visible = false;
            }
        }
    }

    private static Node3D FindChildByNameSubstrings(Node parent, params string[] patterns)
    {
        if (parent == null) return null;
        foreach (var child in parent.GetChildren())
        {
            if (child is Node3D n3d)
            {
                var childName = child.Name.ToString().ToLowerInvariant();
                foreach (var p in patterns)
                {
                    if (childName.Contains(p.ToLowerInvariant()))
                    {
                        return n3d;
                    }
                }
            }
        }
        return null;
    }

    private static void AttachWeapon(Node3D handNode, string modelPath)
    {
        if (handNode == null || string.IsNullOrEmpty(modelPath)) return;
        var scene = GetModel(modelPath);
        if (scene != null)
        {
            var weaponInst = scene.Instantiate<Node3D>();
            weaponInst.Position = Vector3.Zero;
            weaponInst.Rotation = Vector3.Zero;
            weaponInst.Scale = Vector3.One;
            handNode.AddChild(weaponInst);
        }
    }

    public static void UpdateProceduralAnimation(MonsterEntity entity, double delta, float time)
    {
        if (entity.TokenType == CreatureTokenType.None || entity.CharacterNode == null) return;

        var offset = entity.FloatOffset;
        var t = time + offset;
        var isMoving = entity.IsMoving;

        switch (entity.TokenType)
        {
            case CreatureTokenType.Rodent:
            {
                // Breathing squash & stretch
                var breath = Mathf.Sin(t * 6.5f);
                if (entity.BodyNode != null)
                {
                    var sy = 1.0f + 0.08f * breath;
                    var sx = 1.0f - 0.04f * breath;
                    var sz = 1.0f - 0.04f * breath;
                    if (isMoving)
                    {
                        // Rapid bounding hops & pitch/roll wobble
                        var hop = Mathf.Abs(Mathf.Sin(t * 16.0f));
                        entity.BodyNode.Position = new Vector3(0, hop * 0.10f, 0);
                        entity.BodyNode.Rotation = new Vector3(Mathf.Sin(t * 16.0f) * 0.14f, 0, Mathf.Sin(t * 16.0f) * 0.08f);
                    }
                    else
                    {
                        entity.BodyNode.Position = Vector3.Zero;
                        entity.BodyNode.Rotation = Vector3.Zero;
                    }
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(sx, sy, sz);
                }

                // Head sniffing / micro-twitch
                if (entity.HeadNode != null)
                {
                    var twitchY = Mathf.Sin(t * 12.0f) * 0.08f + Mathf.Sin(t * 23.0f) * 0.04f;
                    var twitchX = Mathf.Sin(t * 9.0f) * 0.06f;
                    entity.HeadNode.Rotation = new Vector3(twitchX, twitchY, 0);
                }

                // Tail swish
                if (entity.TailNode != null)
                {
                    var tailSpeed = isMoving ? 18.0f : 5.0f;
                    var tailAmp = isMoving ? 0.60f : 0.35f;
                    var tailYaw = Mathf.Sin(t * tailSpeed) * tailAmp;
                    var tailPitch = 0.15f + Mathf.Sin(t * (tailSpeed * 0.5f)) * 0.10f;
                    entity.TailNode.Rotation = new Vector3(tailPitch, tailYaw, 0);
                }

                // 4 Legs pedaling
                if (entity.Legs.Count >= 4)
                {
                    var legSpeed = isMoving ? 16.0f : 0f;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * legSpeed + phase) * 0.40f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Insect:
            {
                // Abdomen breathing & pulsing
                if (entity.BodyNode != null)
                {
                    var pulse = Mathf.Sin(t * 5.0f);
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.04f * pulse, 1.0f + 0.07f * pulse, 1.0f + 0.05f * pulse);
                }

                // Mandibles snapping
                var mandSnap = (Mathf.Sin(t * 8.0f) + 1.0f) * 0.5f;
                if (entity.LeftMandibleNode != null)
                    entity.LeftMandibleNode.Rotation = new Vector3(0, mandSnap * 0.30f, 0);
                if (entity.RightMandibleNode != null)
                    entity.RightMandibleNode.Rotation = new Vector3(0, -mandSnap * 0.30f, 0);

                // Antennae sensing / twitching
                var antTwitch = Mathf.Sin(t * 11.0f) * 0.20f + Mathf.Sin(t * 21.0f) * 0.10f;
                if (entity.LeftAntennaNode != null)
                    entity.LeftAntennaNode.Rotation = new Vector3(antTwitch * 0.5f, antTwitch, 0);
                if (entity.RightAntennaNode != null)
                    entity.RightAntennaNode.Rotation = new Vector3(-antTwitch * 0.5f, -antTwitch, 0);

                // 6 Legs tripod gait
                if (entity.Legs.Count > 0)
                {
                    var legSpeed = isMoving ? 20.0f : 4.0f;
                    var legAmp = isMoving ? 0.45f : 0.08f;
                    var halfCount = entity.Legs.Count / 2;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var isLeft = i < halfCount;
                        var index = isLeft ? i : (i - halfCount);
                        var phase = (index % 2 == 0 ^ isLeft) ? 0f : Mathf.Pi;
                        var legAngle = Mathf.Sin(t * legSpeed + phase) * legAmp;
                        var legLift = isMoving ? Mathf.Max(0f, Mathf.Sin(t * legSpeed + phase + Mathf.Pi * 0.5f)) * 0.25f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, isLeft ? -legLift : legLift);
                    }
                }

                // Flying insect wings
                if (entity.LeftWingNode != null && entity.RightWingNode != null)
                {
                    var wingFlap = Mathf.Sin(t * 28.0f) * 0.70f;
                    entity.LeftWingNode.Rotation = new Vector3(0, 0, wingFlap);
                    entity.RightWingNode.Rotation = new Vector3(0, 0, -wingFlap);
                }
                break;
            }

            case CreatureTokenType.Centipede:
            {
                // Sinuous undulation through body segments
                var segSpeed = isMoving ? 14.0f : 5.0f;
                var segAmp = isMoving ? 0.09f : 0.04f;
                var rotAmp = isMoving ? 0.28f : 0.10f;

                for (int i = 0; i < entity.Segments.Count; i++)
                {
                    var wave = Mathf.Sin(t * segSpeed - i * 1.1f);
                    var waveCos = Mathf.Cos(t * segSpeed - i * 1.1f);
                    var seg = entity.Segments[i];
                    seg.Position = new Vector3(wave * segAmp, seg.Position.Y, seg.Position.Z);
                    seg.Rotation = new Vector3(0, waveCos * rotAmp, 0);
                }

                // Head rearing / swaying
                if (entity.HeadNode != null)
                {
                    var headSway = Mathf.Sin(t * (segSpeed * 0.8f)) * 0.15f;
                    var headPitch = 0.10f + Mathf.Sin(t * (segSpeed * 0.5f)) * 0.08f;
                    entity.HeadNode.Rotation = new Vector3(headPitch, headSway, 0);
                }

                // Mandibles & antennae
                if (entity.LeftMandibleNode != null)
                    entity.LeftMandibleNode.Rotation = new Vector3(0, Mathf.Sin(t * 9.0f) * 0.25f, 0);
                if (entity.RightMandibleNode != null)
                    entity.RightMandibleNode.Rotation = new Vector3(0, -Mathf.Sin(t * 9.0f) * 0.25f, 0);
                if (entity.LeftAntennaNode != null)
                    entity.LeftAntennaNode.Rotation = new Vector3(0, Mathf.Sin(t * 12.0f) * 0.25f, 0);
                if (entity.RightAntennaNode != null)
                    entity.RightAntennaNode.Rotation = new Vector3(0, -Mathf.Sin(t * 12.0f) * 0.25f, 0);

                // Legs paddling wave
                for (int i = 0; i < entity.Legs.Count; i++)
                {
                    var legPhase = (i * 0.7f);
                    var legAngle = Mathf.Sin(t * segSpeed * 1.2f + legPhase) * 0.35f;
                    entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                }
                break;
            }

            case CreatureTokenType.Arachnid:
            {
                // Spider / Scorpion breathing & pulsing abdomen
                if (entity.BodyNode != null)
                {
                    var pulse = Mathf.Sin(t * 4.5f);
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.03f * pulse, 1.0f + 0.06f * pulse, 1.0f + 0.04f * pulse);
                }

                // Spider 8-leg creeping motion
                var legSpeed = isMoving ? 18.0f : 3.5f;
                var legAmp = isMoving ? 0.35f : 0.06f;
                for (int i = 0; i < entity.Legs.Count; i++)
                {
                    var phase = (i % 2 == 0 ? 0f : Mathf.Pi) + (i >= entity.Legs.Count / 2 ? Mathf.Pi * 0.5f : 0f);
                    var angle = Mathf.Sin(t * legSpeed + phase) * legAmp;
                    entity.Legs[i].Rotation = new Vector3(angle, 0, 0);
                }

                // Scorpion stinger tail if present
                if (entity.TailNode != null)
                {
                    var stingSway = Mathf.Sin(t * 4.0f) * 0.18f;
                    var stingPitch = 0.25f + Mathf.Sin(t * 3.0f) * 0.12f;
                    entity.TailNode.Rotation = new Vector3(stingPitch, stingSway, 0);
                }
                break;
            }

            case CreatureTokenType.Bat:
            case CreatureTokenType.Bird:
            {
                var flapSpeed = entity.TokenType == CreatureTokenType.Bird ? 8.0f : 14.0f;
                var flapAmp = 0.65f;
                var flap = Mathf.Sin(t * flapSpeed) * flapAmp;

                if (entity.LeftWingNode != null)
                    entity.LeftWingNode.Rotation = new Vector3(0, 0, flap);
                if (entity.RightWingNode != null)
                    entity.RightWingNode.Rotation = new Vector3(0, 0, -flap);
                break;
            }

            case CreatureTokenType.Slime:
            {
                // Dramatic gelatinous squash & stretch
                var pulseSpeed = isMoving ? 9.0f : 4.5f;
                var pulse = Mathf.Sin(t * pulseSpeed);
                if (entity.BodyNode != null)
                {
                    var sy = 1.0f + 0.22f * pulse;
                    var sx = 1.0f - 0.11f * pulse;
                    var sz = 1.0f - 0.11f * pulse;
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(sx, sy, sz);
                }
                if (entity.NucleusNode != null)
                {
                    var nucY = 0.15f + Mathf.Sin(t * (pulseSpeed * 0.7f)) * 0.05f;
                    var nucX = Mathf.Sin(t * 2.0f) * 0.04f;
                    entity.NucleusNode.Position = new Vector3(nucX, nucY, 0);
                }
                break;
            }

            case CreatureTokenType.Worm:
            {
                // Peristaltic crawling wave
                var crawlSpeed = isMoving ? 12.0f : 5.0f;
                for (int i = 0; i < entity.Segments.Count; i++)
                {
                    var phase = t * crawlSpeed - i * 0.8f;
                    var seg = entity.Segments[i];
                    var sz = 1.0f + Mathf.Sin(phase) * 0.25f;
                    var sy = 1.0f - Mathf.Sin(phase) * 0.15f;
                    seg.Scale = new Vector3(sy, sy, sz);
                }
                break;
            }

            case CreatureTokenType.Eye:
            {
                // Pupil dilation & iris tracking
                if (entity.IrisNode != null)
                {
                    var lookX = Mathf.Sin(t * 1.5f) * 0.04f + Mathf.Sin(t * 7.0f) * 0.02f;
                    var lookY = Mathf.Cos(t * 1.2f) * 0.03f;
                    entity.IrisNode.Position = new Vector3(lookX, lookY, 0.30f);
                }
                break;
            }

            case CreatureTokenType.Mushroom:
            {
                // Spore cap breathing expansion
                var spore = Mathf.Sin(t * 3.5f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f + 0.06f * spore, 1.0f - 0.04f * spore, 1.0f + 0.06f * spore);
                }
                break;
            }

            case CreatureTokenType.Canine:
            {
                // Energetic canine breathing / panting
                var breath = Mathf.Sin(t * 7.5f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.03f * breath, 1.0f + 0.06f * breath, 1.0f - 0.02f * breath);
                    if (isMoving)
                    {
                        var bounce = Mathf.Abs(Mathf.Sin(t * 16.0f)) * 0.08f;
                        entity.BodyNode.Position = new Vector3(0, 0.26f + bounce, 0);
                        entity.BodyNode.Rotation = new Vector3(Mathf.Sin(t * 16.0f) * 0.10f, 0, 0);
                    }
                    else
                    {
                        entity.BodyNode.Position = new Vector3(0, 0.26f, 0);
                        entity.BodyNode.Rotation = Vector3.Zero;
                    }
                }
                if (entity.HeadNode != null)
                {
                    var sniffX = Mathf.Sin(t * 12.0f) * 0.05f;
                    var sniffY = Mathf.Sin(t * 18.0f) * 0.06f;
                    entity.HeadNode.Rotation = new Vector3(sniffX, sniffY, 0);
                }
                if (entity.TailNode != null)
                {
                    // Energetic tail wagging
                    var wagSpeed = isMoving ? 20.0f : 8.0f;
                    var wagAmp = isMoving ? 0.60f : 0.38f;
                    entity.TailNode.Rotation = new Vector3(0.2f, Mathf.Sin(t * wagSpeed) * wagAmp, 0);
                }
                if (entity.Legs.Count >= 4)
                {
                    var legSpeed = isMoving ? 16.0f : 0f;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * legSpeed + phase) * 0.40f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Feline:
            {
                // Sleek, subtle predatory breathing
                var breath = Mathf.Sin(t * 3.8f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.02f * breath, 1.0f + 0.04f * breath, 1.0f - 0.02f * breath);
                    if (isMoving)
                    {
                        var sway = Mathf.Sin(t * 12.0f) * 0.06f;
                        entity.BodyNode.Position = new Vector3(0, 0.18f, 0);
                        entity.BodyNode.Rotation = new Vector3(0, 0, sway);
                    }
                    else
                    {
                        entity.BodyNode.Position = new Vector3(0, 0.18f, 0);
                        entity.BodyNode.Rotation = Vector3.Zero;
                    }
                }
                if (entity.TailNode != null)
                {
                    // Graceful feline tail curling & swaying
                    var tailYaw = Mathf.Sin(t * 4.2f) * 0.35f;
                    var tailPitch = Mathf.Sin(t * 3.2f) * 0.15f;
                    var tailRoll = Mathf.Cos(t * 4.2f) * 0.20f;
                    entity.TailNode.Rotation = new Vector3(tailPitch, tailYaw, tailRoll);
                }
                if (entity.Legs.Count >= 4)
                {
                    var legSpeed = isMoving ? 12.0f : 0f;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * legSpeed + phase) * 0.32f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Snake:
            {
                // Sinuous serpentine travelling S-curve wave along segments
                var waveSpeed = isMoving ? 14.0f : 4.5f;
                var waveAmp = isMoving ? 0.14f : 0.06f;
                for (int i = 0; i < entity.Segments.Count; i++)
                {
                    var phase = t * waveSpeed - i * 0.75f;
                    var segX = Mathf.Sin(phase) * waveAmp;
                    var segYaw = Mathf.Cos(phase) * (waveAmp * 2.2f);
                    var origZ = entity.Segments[i].Position.Z;
                    var origY = entity.Segments[i].Position.Y;
                    entity.Segments[i].Position = new Vector3(segX, origY, origZ);
                    entity.Segments[i].Rotation = new Vector3(0, segYaw, 0);
                }
                if (entity.HeadNode != null)
                {
                    var headYaw = Mathf.Sin(t * waveSpeed) * (waveAmp * 1.6f);
                    entity.HeadNode.Rotation = new Vector3(0.08f, headYaw, 0);
                }
                // Flickering darting forked tongue (stored in LeftAntennaNode)
                if (entity.LeftAntennaNode != null)
                {
                    var dart = Mathf.Clamp(Mathf.Sin(t * 18.0f), 0f, 1f);
                    entity.LeftAntennaNode.Position = new Vector3(0, -0.03f, 0.23f + dart * 0.08f);
                }
                break;
            }

            case CreatureTokenType.Reptile:
            {
                // Sprawling crawling gait & lateral undulating body
                var crawlSpeed = isMoving ? 12.0f : 3.0f;
                if (entity.BodyNode != null)
                {
                    var bodyYaw = isMoving ? Mathf.Sin(t * crawlSpeed) * 0.12f : 0f;
                    var bodyRoll = isMoving ? Mathf.Cos(t * crawlSpeed) * 0.06f : 0f;
                    entity.BodyNode.Rotation = new Vector3(0, bodyYaw, bodyRoll);
                }
                if (entity.TailNode != null)
                {
                    var tailYaw = -Mathf.Sin(t * crawlSpeed) * 0.45f;
                    entity.TailNode.Rotation = new Vector3(0, tailYaw, 0);
                }
                if (entity.Legs.Count >= 4)
                {
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * crawlSpeed + phase) * 0.40f : 0f;
                        entity.Legs[i].Rotation = new Vector3(0, legAngle, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Quadruped:
            {
                // Heavy stomping gait with body bounce & head nodding
                var stompSpeed = isMoving ? 13.0f : 3.5f;
                var breath = Mathf.Sin(t * 4.0f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.03f * breath, 1.0f + 0.05f * breath, 1.0f - 0.02f * breath);
                    if (isMoving)
                    {
                        var bounce = Mathf.Abs(Mathf.Sin(t * stompSpeed)) * 0.07f;
                        entity.BodyNode.Position = new Vector3(0, 0.26f + bounce, 0);
                    }
                    else
                    {
                        entity.BodyNode.Position = new Vector3(0, 0.26f, 0);
                    }
                }
                if (entity.HeadNode != null)
                {
                    var nod = isMoving ? Mathf.Sin(t * stompSpeed) * 0.14f : 0f;
                    entity.HeadNode.Rotation = new Vector3(nod, 0, 0);
                }
                if (entity.Legs.Count >= 4)
                {
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * stompSpeed + phase) * 0.35f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Hybrid:
            {
                // Powerful hybrid wing flapping & aggressive stance
                var flapSpeed = isMoving ? 14.0f : 5.5f;
                var flapAngle = Mathf.Sin(t * flapSpeed) * 0.45f;
                if (entity.LeftWingNode != null) entity.LeftWingNode.Rotation = new Vector3(0, 0, flapAngle);
                if (entity.RightWingNode != null) entity.RightWingNode.Rotation = new Vector3(0, 0, -flapAngle);
                if (entity.TailNode != null)
                {
                    var tailYaw = Mathf.Sin(t * 8.0f) * 0.35f;
                    entity.TailNode.Rotation = new Vector3(0.1f, tailYaw, 0);
                }
                if (entity.Legs.Count >= 4)
                {
                    var legSpeed = isMoving ? 14.0f : 0f;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * legSpeed + phase) * 0.35f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Beast:
            {
                // Generic beast breathing rise & fall
                var breath = Mathf.Sin(t * 5.0f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.03f * breath, 1.0f + 0.06f * breath, 1.0f - 0.02f * breath);
                }
                if (entity.TailNode != null)
                {
                    var tailSpeed = isMoving ? 16.0f : 4.0f;
                    var tailAmp = isMoving ? 0.50f : 0.25f;
                    entity.TailNode.Rotation = new Vector3(0.1f, Mathf.Sin(t * tailSpeed) * tailAmp, 0);
                }
                if (entity.Legs.Count >= 4)
                {
                    var legSpeed = isMoving ? 14.0f : 0f;
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        if (i >= 2) phase += Mathf.Pi;
                        var legAngle = isMoving ? Mathf.Sin(t * legSpeed + phase) * 0.35f : 0f;
                        entity.Legs[i].Rotation = new Vector3(legAngle, 0, 0);
                    }
                }
                break;
            }

            case CreatureTokenType.Dragon:
            {
                var breath = Mathf.Sin(t * 3.5f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.02f * breath, 1.0f + 0.04f * breath, 1.0f - 0.02f * breath);
                }
                break;
            }

            case CreatureTokenType.Golem:
            {
                if (isMoving && entity.BodyNode != null)
                {
                    var stomp = Mathf.Abs(Mathf.Sin(t * 10.0f)) * 0.08f;
                    entity.BodyNode.Position = new Vector3(0, stomp, 0);
                    entity.BodyNode.Rotation = new Vector3(0, 0, Mathf.Sin(t * 10.0f) * 0.08f);
                }
                break;
            }

            case CreatureTokenType.Quylthulg:
            {
                var pulse = Mathf.Sin(t * 3.0f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f + 0.12f * pulse, 1.0f - 0.08f * pulse, 1.0f + 0.12f * pulse);
                }
                break;
            }
        }
    }

    private static string FormatHealthBar(int hp, int max)
    {
        if (max <= 0) return "";
        var totalSegments = 10;
        var fill = Mathf.Clamp((int)Math.Round((hp / (float)max) * totalSegments), 0, totalSegments);
        return $"[{new string('█', fill)}{new string('░', totalSegments - fill)}]";
    }

    private static Color HealthColour(int hp, int max)
    {
        if (max <= 0) return Colors.White;
        var f = Mathf.Clamp(hp / (float)max, 0f, 1f);
        return f > 0.6f ? new Color(0.45f, 0.95f, 0.45f)
            : f > 0.3f ? new Color(1.0f, 0.85f, 0.35f)
            : new Color(1.0f, 0.42f, 0.38f);
    }

    private static PackedScene GetModel(string path)
    {
        if (_modelCache.TryGetValue(path, out var scene))
        {
            return scene;
        }

        if (ResourceLoader.Exists(path))
        {
            scene = GD.Load<PackedScene>(path);
            if (scene != null)
            {
                _modelCache[path] = scene;
                return scene;
            }
        }
        return null;
    }
}
