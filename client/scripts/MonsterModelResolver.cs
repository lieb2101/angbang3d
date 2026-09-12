using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

public enum CreatureTokenType
{
    None,
    Kobold,
    Demon,
    Yeek,
    Yeti,
    Louse,
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
    public string AttackAnim { get; set; }
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
    public const float MinModelScale = 0.35f;
    public const float MaxModelScale = 1.85f;

    private static readonly Dictionary<string, PackedScene> _modelCache = new();
    private static readonly Dictionary<string, Texture2D> _textureCache = new();
    private static readonly Dictionary<char, List<MonsterModelRule>> _modelRules = new();
    private static readonly Dictionary<string, (Texture2D Albedo, Texture2D Normal)> _spriteCache = new();
    private static readonly Dictionary<string, StandardMaterial3D> _creatureMaterialCache = new();

    /// <summary>
    /// Generates high-resolution procedural PBR textures & materials for creature tokens
    /// (fur, reptilian scales, chitin, wet slime, bark, stone, etc.)
    /// </summary>
    public static StandardMaterial3D GetProceduralCreatureMaterial(string textureType, Color baseTone, float roughness = 0.5f, float metallic = 0.0f, bool isWet = false)
    {
        var key = $"{textureType}_{baseTone.ToHtml()}_{roughness:F2}_{metallic:F2}_{isWet}";
        if (_creatureMaterialCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        const int size = 256;
        var albedoImg = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        var normalImg = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);

        for (int y = 0; y < size; y++)
        {
            float v = (float)y / size;
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float pattern = 0f;
                float nx = 0f;
                float ny = 0f;

                switch (textureType.ToLowerInvariant())
                {
                    case "fur":
                        // Fine directional fur strands with directional grain
                        var strand = Mathf.Sin(x * 1.8f + Mathf.Sin(y * 0.4f) * 4f);
                        var undercoat = Mathf.Sin(x * 0.5f) * Mathf.Cos(y * 0.5f);
                        pattern = (strand * 0.6f + undercoat * 0.4f) * 0.15f;
                        nx = Mathf.Cos(x * 1.8f) * 0.4f;
                        ny = 0.2f;
                        break;

                    case "scales":
                        // Hexagonal / overlapping reptilian scale shingles
                        var sx = x * 0.22f;
                        var sy = y * 0.22f + ((x / 14) % 2 == 0 ? 0.5f : 0f);
                        var cellX = sx - MathF.Floor(sx) - 0.5f;
                        var cellY = sy - MathF.Floor(sy) - 0.5f;
                        var dist = MathF.Sqrt(cellX * cellX + cellY * cellY);
                        pattern = (1.0f - Mathf.Clamp(dist * 2.2f, 0f, 1f)) * 0.28f - 0.12f;
                        nx = cellX * 1.8f;
                        ny = cellY * 1.8f;
                        break;

                    case "chitin":
                        // Polished arthropod shell with fine micro-scratches and plate seams
                        var seam = MathF.Abs(Mathf.Sin(y * 0.12f));
                        var micro = Mathf.Sin(x * 2.5f) * Mathf.Cos(y * 2.5f) * 0.05f;
                        pattern = (seam > 0.92f ? -0.25f : 0.05f) + micro;
                        nx = micro * 2.0f;
                        ny = (seam > 0.92f ? 0.6f : 0f);
                        break;

                    case "bark":
                        // Deep vertical crags & wood grain
                        var crag = Mathf.Sin(x * 0.45f + Mathf.Sin(y * 0.15f) * 3f);
                        pattern = crag * 0.25f;
                        nx = Mathf.Cos(x * 0.45f) * 0.7f;
                        ny = 0.1f;
                        break;

                    default:
                        pattern = (Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.3f)) * 0.10f;
                        nx = Mathf.Cos(x * 0.3f) * 0.3f;
                        ny = Mathf.Sin(y * 0.3f) * 0.3f;
                        break;
                }

                var r = Mathf.Clamp(baseTone.R + pattern, 0f, 1f);
                var g = Mathf.Clamp(baseTone.G + pattern, 0f, 1f);
                var b = Mathf.Clamp(baseTone.B + pattern, 0f, 1f);
                albedoImg.SetPixel(x, y, new Color(r, g, b, 1.0f));

                var nz = MathF.Sqrt(Mathf.Clamp(1.0f - (nx * nx + ny * ny), 0.1f, 1.0f));
                normalImg.SetPixel(x, y, new Color(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f, nz * 0.5f + 0.5f, 1.0f));
            }
        }

        albedoImg.GenerateMipmaps();
        normalImg.GenerateMipmaps();

        var albedoTex = ImageTexture.CreateFromImage(albedoImg);
        var normalTex = ImageTexture.CreateFromImage(normalImg);

        var mat = new StandardMaterial3D
        {
            AlbedoTexture = albedoTex,
            NormalEnabled = true,
            NormalTexture = normalTex,
            NormalScale = 0.85f,
            Roughness = isWet ? 0.15f : roughness,
            Metallic = metallic,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };

        if (isWet)
        {
            mat.RimEnabled = true;
            mat.Rim = 0.45f;
            mat.RimTint = 0.25f;
        }

        _creatureMaterialCache[key] = mat;
        return mat;
    }

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
            NoDepthTest = false,
            VisibilityRangeEnd = 24.0f,
            VisibilityRangeEndMargin = 3.0f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            RenderPriority = 1,
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
            NoDepthTest = false,
            VisibilityRangeEnd = 24.0f,
            VisibilityRangeEndMargin = 3.0f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            RenderPriority = 2,
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
            NoDepthTest = false,
            VisibilityRangeEnd = 24.0f,
            VisibilityRangeEndMargin = 3.0f,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            RenderPriority = 3,
            Visible = false,
            Position = new Vector3(0, entity.ModelHeight + 0.95f, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        root.AddChild(targetBadge);
        entity.TargetBadge = targetBadge;

        return entity;
    }

    public static void UpdateMonsterVisual(MonsterEntity entity, JsonElement monster, bool isTargeted = false, bool isVisibleInView = true)
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
            entity.Nameplate.Visible = isVisibleInView;
        }

        if (entity.StatusBadge != null)
        {
            if (entity.IsAsleep)
            {
                entity.StatusBadge.Text = "💤 Zzz...";
                entity.StatusBadge.Modulate = new Color(0.65f, 0.85f, 1.0f, 0.95f);
                entity.StatusBadge.Visible = isVisibleInView;
            }
            else if (entity.IsAfraid)
            {
                entity.StatusBadge.Text = "⚠ FLEEING";
                entity.StatusBadge.Modulate = new Color(1.0f, 0.35f, 0.20f, 0.98f);
                entity.StatusBadge.Visible = isVisibleInView;
            }
            else if (entity.IsConfused)
            {
                entity.StatusBadge.Text = "🌀 CONFUSED";
                entity.StatusBadge.Modulate = new Color(0.85f, 0.50f, 1.0f, 0.95f);
                entity.StatusBadge.Visible = isVisibleInView;
            }
            else if (entity.IsStunned)
            {
                entity.StatusBadge.Text = "💫 STUNNED";
                entity.StatusBadge.Modulate = new Color(1.0f, 0.90f, 0.25f, 0.95f);
                entity.StatusBadge.Visible = isVisibleInView;
            }
            else
            {
                entity.StatusBadge.Visible = false;
            }
        }

        if (entity.TargetBadge != null)
        {
            entity.TargetBadge.Visible = isTargeted && isVisibleInView;
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

        // Resolve 3D character mesh model path if available
        var (modelPath, scale, isEthereal, isFloating, customSpeed, equipmentRole) = ResolveModelConfig(glyph, raceName, lowerName);
        scale = Mathf.Clamp(scale, MinModelScale, MaxModelScale);

        if (modelPath != null && ResourceLoader.Exists(modelPath))
        {
            var scene = GetModel(modelPath);
            if (scene != null)
            {
                var instance = scene.Instantiate<Node3D>();
                var ap = FindAnimationPlayer(instance);

                // If a character rig lacks an AnimationPlayer, do NOT render it in a static T-pose;
                // fallback to procedural 3D anatomical creature token unless it is a static prop (chest/tree/coins).
                if (ap == null && !modelPath.Contains("props") && !modelPath.Contains("items") && !modelPath.Contains("tree"))
                {
                    instance.QueueFree();
                    BuildCreatureToken(entity, glyph, lowerName, color);
                    return;
                }

                var effectiveRole = equipmentRole != EquipmentRole.Auto ? equipmentRole : InferEquipmentRole(glyph, raceName);
                ApplyMonsterEquipment(instance, effectiveRole, glyph, raceName, modelPath);

                if (isEthereal)
                {
                    ApplyEtherealMaterial(instance, color);
                    entity.IsEthereal = true;
                }
                else
                {
                    ApplyCharacterSkinAndTint(instance, glyph, lowerName, color, modelPath);
                }

                entity.IsFloating = isFloating;
                entity.BaseY = isFloating ? 0.35f : 0.0f;
                if (modelPath.Contains("tree_dead_large"))
                {
                    entity.ModelHeight = 2.80f * scale;
                }
                else if (modelPath.Contains("Chest_Closed") || modelPath.Contains("chest"))
                {
                    entity.ModelHeight = 0.65f * scale;
                }
                else if (modelPath.Contains("Gold_Ingots") || modelPath.Contains("coin"))
                {
                    entity.ModelHeight = 0.50f * scale;
                }
                else if (modelPath.Contains("Spider"))
                {
                    entity.ModelHeight = 0.85f * scale;
                }
                else if (modelPath.Contains("Rat"))
                {
                    entity.ModelHeight = 0.60f * scale;
                }
                else if (modelPath.Contains("Snake"))
                {
                    entity.ModelHeight = 0.65f * scale;
                }
                else if (modelPath.Contains("Frog"))
                {
                    entity.ModelHeight = 0.70f * scale;
                }
                else if (modelPath.Contains("Wasp"))
                {
                    entity.ModelHeight = 0.85f * scale;
                    entity.BaseY = 0.35f;
                    entity.IsFloating = true;
                }
                else if (modelPath.Contains("Imp") || modelPath.Contains("Puglin"))
                {
                    entity.ModelHeight = 1.35f * scale;
                }
                else if (modelPath.Contains("King") || modelPath.Contains("Punk"))
                {
                    entity.ModelHeight = 2.05f * scale;
                }
                else
                {
                    entity.ModelHeight = 1.85f * scale;
                }

                // Realistic scale applied uniformly to character rigs
                instance.Scale = new Vector3(scale, scale, scale);
                instance.Position = Vector3.Zero;

                if (ap != null)
                {
                    entity.AnimPlayer = ap;
                    SetupAnimations(entity, ap, customSpeed);
                }

                entity.CharacterNode.AddChild(instance);
                return;
            }
        }

        // Dedicated High-Fidelity 3D Anatomical Creature Models for Non-Humanoids
        BuildCreatureToken(entity, glyph, lowerName, color);
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
        var lower = (raceName ?? "").ToLowerInvariant();
        if (lower.Contains("female") || lower.Contains("woman") || lower.Contains("lady") || lower.Contains("maiden") || lower.Contains("damsel") || lower.Contains("wench") || lower.Contains("maid"))
        {
            return "res://assets/models/characters/Casual.gltf";
        }
        if (lower.Contains("beggar") || lower.Contains("leper") || lower.Contains("urchin") || lower.Contains("idiot") || lower.Contains("hermit") || lower.Contains("drunk") || lower.Contains("peasant") || lower.Contains("wretch"))
        {
            return "res://assets/models/characters/Farmer.gltf";
        }
        if (lower.Contains("farmer"))
        {
            return "res://assets/models/characters/Farmer.gltf";
        }
        if (lower.Contains("merchant") || lower.Contains("shopkeeper") || lower.Contains("innkeeper") || lower.Contains("clerk") || lower.Contains("crier") || lower.Contains("scribe"))
        {
            return "res://assets/models/characters/Formal.gltf";
        }
        if (lower.Contains("worker") || lower.Contains("smith") || lower.Contains("miner") || lower.Contains("artisan") || lower.Contains("craftsman") || lower.Contains("butcher") || lower.Contains("baker") || lower.Contains("cook"))
        {
            return "res://assets/models/characters/Worker.gltf";
        }
        if (lower.Contains("mercenary") || lower.Contains("veteran") || lower.Contains("rogue") || lower.Contains("scoundrel") || lower.Contains("brawler"))
        {
            return "res://assets/models/characters/Adventurer.gltf";
        }
        return "res://assets/models/characters/Farmer.gltf";
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
        // High-Fidelity Realistic CC0 Fantasy 3D Asset Registry
        // Humanoid characters (elves, dwarves, orcs, knights, rogues, mages, undead)
        // use properly scaled glTF rigs with rich animations!
        // Distinct non-humanoid creatures (demons, kobolds, yeeks, yetis, lice, beasts,
        // insects, reptiles, dragons, elementals) use articulated procedural 3D anatomical models.
        // =========================================================================

        // 1. Orcs, Goblins, Snagas, Uruks (o)
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 0.72f, speed: 1.10f, equipment: EquipmentRole.Rogue, matcher: n => n.Contains("goblin") || n.Contains("snaga")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.85f, speed: 1.05f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout") || n.Contains("sniper")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 0.85f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("shaman") || n.Contains("mage") || n.Contains("curse")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 0.98f, speed: 1.0f, equipment: EquipmentRole.Barbarian, matcher: n => n.Contains("uruk") || n.Contains("captain") || n.Contains("chieftain") || n.Contains("leader") || n.Contains("black orc")));
        AddModelRule('o', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.88f, speed: 1.0f, equipment: EquipmentRole.Barbarian));

        // 2. Skeletons (s) - Animated undead warrior / archer / mage
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Soldier.gltf", 0.88f, speed: 1.05f, equipment: EquipmentRole.Archer, matcher: n => n.Contains("archer") || n.Contains("scout") || n.Contains("sniper")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 0.88f, speed: 0.90f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("mage") || n.Contains("sorcerer") || n.Contains("druj") || n.Contains("necromancer")));
        AddModelRule('s', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 0.92f, speed: 1.0f, equipment: EquipmentRole.Warrior));

        // 3. Zombies, Mummies, Ghouls (z)
        AddModelRule('z', new MonsterModelRule("res://assets/models/characters/Worker.gltf", 0.90f, speed: 0.80f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("worker") || n.Contains("ghoul")));
        AddModelRule('z', new MonsterModelRule("res://assets/models/characters/Farmer.gltf", 0.90f, speed: 0.75f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("mummy")));
        AddModelRule('z', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.90f, speed: 0.80f, equipment: EquipmentRole.Unarmed));

        // 4. Liches and Arch-Liches (L)
        AddModelRule('L', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 1.05f, speed: 0.90f, equipment: EquipmentRole.Mage));

        // 5. Wights, Wraiths, Nazgul, Ringwraiths (W)
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 1.10f, isEthereal: true, isFloating: true, speed: 0.90f, equipment: EquipmentRole.TwoHandedSword, matcher: n => n.Contains("nazgul") || n.Contains("ringwraith") || n.Contains("witch-king")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 1.00f, isEthereal: true, isFloating: true, speed: 0.90f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("wraith")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 0.95f, isEthereal: true, isFloating: false, speed: 0.85f, equipment: EquipmentRole.Warrior, matcher: n => n.Contains("wight")));
        AddModelRule('W', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 0.98f, isEthereal: true, isFloating: true, speed: 0.90f, equipment: EquipmentRole.Warrior));

        // 6. Ghosts, Spectres, Poltergeists (G)
        AddModelRule('G', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 0.90f, isEthereal: true, isFloating: true, speed: 0.85f, equipment: EquipmentRole.Unarmed));

        // 7. Vampires (V)
        AddModelRule('V', new MonsterModelRule("res://assets/models/characters/King.gltf", 1.05f, speed: 1.05f, equipment: EquipmentRole.Mage, matcher: n => n.Contains("lord") || n.Contains("master") || n.Contains("ancient") || n.Contains("nosferatu")));
        AddModelRule('V', new MonsterModelRule("res://assets/models/characters/King.gltf", 0.98f, speed: 1.05f, equipment: EquipmentRole.Warrior));

        // 8. Ainur, Maiar (A)
        AddModelRule('A', new MonsterModelRule("res://assets/models/characters/King.gltf", 1.15f, speed: 1.0f, equipment: EquipmentRole.TwoHandedSword));

        // 9. Giants, Titans, Cyclops, Morgoth (P)
        AddModelRule('P', new MonsterModelRule("res://assets/models/characters/King.gltf", 1.55f, speed: 0.85f, equipment: EquipmentRole.TwoHandedAxe, matcher: n => n.Contains("morgoth")));
        AddModelRule('P', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 1.40f, speed: 0.85f, equipment: EquipmentRole.Barbarian));

        // 10. Trolls (T), Ogres (O)
        AddModelRule('T', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 1.25f, speed: 0.90f, equipment: EquipmentRole.Barbarian));
        AddModelRule('O', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 1.18f, speed: 0.95f, equipment: EquipmentRole.Barbarian));

        // 11. Mimics (?) and Creeping Coins ($)
        AddModelRule('?', new MonsterModelRule("res://assets/models/items/Chest_Closed.fbx", 0.22f, speed: 1.0f, equipment: EquipmentRole.Unarmed));
        AddModelRule('$', new MonsterModelRule("res://assets/models/items/Gold_Ingots.fbx", 0.20f, speed: 1.0f, equipment: EquipmentRole.Unarmed));

        // 12. Ents & Trees (l)
        AddModelRule('l', new MonsterModelRule("res://assets/models/props/tree_dead_large.gltf", 0.40f, speed: 0.5f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("tree") || n.Contains("ent") || n.Contains("huorn") || n.Contains("willow") || n.Contains("wood")));

        // 16. Humanoids (h) and People / Adventurers (p)
        Func<string, bool> isKnight = n => n.Contains("knight") || n.Contains("paladin") || n.Contains("veteran") ||
            n.Contains("warrior") || n.Contains("soldier") || n.Contains("guard") || n.Contains("captain") ||
            n.Contains("fighter") || n.Contains("champion") || n.Contains("swordsman") || n.Contains("centurion") ||
            n.Contains("lord") || n.Contains("templar");

        Func<string, bool> isBarb = n => n.Contains("barbarian") || n.Contains("mercenary") || n.Contains("gladiator") ||
            n.Contains("berserker") || n.Contains("bouncer") || n.Contains("ruffian") || n.Contains("beastman") || n.Contains("brute");

        Func<string, bool> isMage = n => n.Contains("mage") || n.Contains("wizard") || n.Contains("warlock") ||
            n.Contains("sorcerer") || n.Contains("alchemist") || n.Contains("scholar") || n.Contains("priest") ||
            n.Contains("cleric") || n.Contains("sage") || n.Contains("acolyte") || n.Contains("cultist") ||
            n.Contains("druid") || n.Contains("seer") || n.Contains("shaman") || n.Contains("enchanter") || n.Contains("necromancer");

        Func<string, bool> isArcher = n => n.Contains("archer") || n.Contains("scout") || n.Contains("sniper") ||
            n.Contains("tracker") || n.Contains("marksman") || n.Contains("bowman") || n.Contains("crossbow") || n.Contains("hunter") || n.Contains("ranger") || n.Contains("elf") || n.Contains("dunedain");

        Func<string, bool> isThief = n => n.Contains("thief") || n.Contains("rogue") || n.Contains("burglar") ||
            n.Contains("assassin") || n.Contains("cutpurse") || n.Contains("bandit") || n.Contains("brigand") ||
            n.Contains("ninja") || n.Contains("stalker") || n.Contains("pickpocket") || n.Contains("scoundrel");

        Func<string, bool> isPeasant = n => n.Contains("peasant") || n.Contains("drunkard") || n.Contains("drunk") ||
            n.Contains("beggar") || n.Contains("fool") || n.Contains("idiot") || n.Contains("commoner") || n.Contains("villager") ||
            n.Contains("leper") || n.Contains("urchin") || n.Contains("hermit") || n.Contains("scullion") || n.Contains("slave");

        // Rules for 'h'
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 0.95f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f, speed: 1.05f, equipment: EquipmentRole.Archer, matcher: isArcher));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Farmer.gltf", 0.90f, speed: 1.0f, equipment: EquipmentRole.Unarmed, matcher: isPeasant));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 1.00f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 0.92f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('h', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f));

        // Rules for 'p'
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Medieval.gltf", 0.95f, equipment: EquipmentRole.Warrior, matcher: isKnight));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f, speed: 1.05f, equipment: EquipmentRole.Archer, matcher: isArcher));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f, speed: 1.05f, equipment: EquipmentRole.Rogue, matcher: isThief));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Farmer.gltf", 0.90f, speed: 1.0f, equipment: EquipmentRole.Unarmed, matcher: isPeasant));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Punk.gltf", 1.00f, equipment: EquipmentRole.Barbarian, matcher: isBarb));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Witch.gltf", 0.92f, speed: 0.95f, equipment: EquipmentRole.Mage, matcher: isMage));
        AddModelRule('p', new MonsterModelRule("res://assets/models/characters/Adventurer.gltf", 0.95f));

        // 17. Nagas (n) - Rigged 3D Serpent Enemy Models
        AddModelRule('n', new MonsterModelRule("res://assets/models/monsters/enemies/Snake_angry.fbx", 0.38f, speed: 1.15f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("black") || n.Contains("spirit") || n.Contains("guardian") || n.Contains("fire") || n.Contains("red")));
        AddModelRule('n', new MonsterModelRule("res://assets/models/monsters/enemies/Snake.fbx", 0.32f, speed: 1.10f, equipment: EquipmentRole.Unarmed));

        // 18. Spiders & Scorpions (S) - Rigged 3D Enemy Models
        AddModelRule('S', new MonsterModelRule("res://assets/models/monsters/enemies/Spider.fbx", 0.35f, speed: 1.15f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("giant") || n.Contains("shelob") || n.Contains("ungoliant")));
        AddModelRule('S', new MonsterModelRule("res://assets/models/monsters/enemies/Spider.fbx", 0.25f, speed: 1.20f, equipment: EquipmentRole.Unarmed));

        // 19. Rats & Rodents (r) - Rigged 3D Enemy Models
        AddModelRule('r', new MonsterModelRule("res://assets/models/monsters/enemies/Rat.fbx", 0.30f, speed: 1.25f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("giant") || n.Contains("wererat")));
        AddModelRule('r', new MonsterModelRule("res://assets/models/monsters/enemies/Rat.fbx", 0.20f, speed: 1.30f, equipment: EquipmentRole.Unarmed));

        // 20. Snakes & Serpents (J) - Rigged 3D Enemy Models
        AddModelRule('J', new MonsterModelRule("res://assets/models/monsters/enemies/Snake_angry.fbx", 0.28f, speed: 1.15f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("viper") || n.Contains("cobra") || n.Contains("rattle") || n.Contains("asp")));
        AddModelRule('J', new MonsterModelRule("res://assets/models/monsters/enemies/Snake.fbx", 0.25f, speed: 1.15f, equipment: EquipmentRole.Unarmed));

        // 21. Wasps & Flying Insects (F) - Rigged 3D Enemy Models
        AddModelRule('F', new MonsterModelRule("res://assets/models/monsters/enemies/Wasp.fbx", 0.25f, isFloating: true, speed: 1.40f, equipment: EquipmentRole.Unarmed));

        // 22. Frogs & Amphibians (R) - Rigged 3D Frog Model for Frogs/Toads
        AddModelRule('R', new MonsterModelRule("res://assets/models/monsters/enemies/Frog.fbx", 0.25f, speed: 1.10f, equipment: EquipmentRole.Unarmed, matcher: n => n.Contains("frog") || n.Contains("toad")));
    }

    private static (string ModelPath, float Scale, bool IsEthereal, bool IsFloating, float Speed, EquipmentRole Equipment) ResolveModelConfig(
        char glyph, string raceName, string lowerName)
    {
        // Townsfolk (t) - completely unarmed / bare-handed
        if (glyph == 't')
        {
            float scale = 0.88f;
            if (lowerName.Contains("dwarf") || lowerName.Contains("hobbit") || lowerName.Contains("gnome") ||
                lowerName.Contains("halfling"))
            {
                scale = 0.65f;
            }
            var townModel = SelectTownspersonModel(raceName);
            return (townModel, Mathf.Clamp(scale, MinModelScale, MaxModelScale), false, false, 1.0f, EquipmentRole.Unarmed);
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
                            scale *= (0.65f / 0.95f);
                        }
                        else if (lowerName.Contains("elf") || lowerName.Contains("ranger") || lowerName.Contains("dunedain"))
                        {
                            scale *= (0.98f / 0.95f);
                        }
                    }
                    return (rule.ModelPath, Mathf.Clamp(scale, MinModelScale, MaxModelScale), rule.IsEthereal, rule.IsFloating, rule.Speed, rule.Equipment);
                }
            }
        }

        // Return null so unmapped non-humanoid creatures use their dedicated high-res sculpted procedural tokens!
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

        const int w = 512;
        const int h = 768;

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
                var eyeScale = 0.48f;
                tokenBody = CreateEyeToken(entity, color, eyeScale);
                height = 0.80f;
                isFloating = true;
                break;

            case 'w': // Worm Mass
                var wormScale = 0.38f;
                tokenBody = CreateWormToken(entity, color, wormScale);
                height = 0.20f + 0.22f * wormScale;
                break;

            case 'j' or 'i': // Jelly, Slime, Ooze, Icky Thing
                var slimeScale = 0.48f;
                tokenBody = CreateSlimeToken(entity, color, slimeScale);
                height = 0.20f + 0.38f * slimeScale;
                break;

            case 'm' or ',': // Mold, Mushroom
                var mushScale = 0.42f;
                tokenBody = CreateMushroomToken(entity, color, mushScale);
                height = 0.20f + 0.32f * mushScale;
                break;

            case 'c': // Centipede (Giant white centipede, etc.)
                var centScale = lowerName.Contains("giant") ? 0.48f : (lowerName.Contains("metallic") ? 0.52f : 0.38f);
                tokenBody = CreateCentipedeToken(entity, color, centScale);
                height = 0.18f + 0.25f * centScale;
                break;

            case 'a': // Ant (Soldier ant, giant red ant, fire ant, army ant)
                var antScale = lowerName.Contains("queen") ? 0.58f : (lowerName.Contains("soldier") ? 0.36f : (lowerName.Contains("giant") ? 0.42f : 0.30f));
                tokenBody = CreateInsectToken(entity, color, antScale, isFlying: false, isBeetle: false);
                height = 0.22f + 0.30f * antScale;
                break;

            case 'K': // Killer Beetle
                var beetleScale = lowerName.Contains("giant") || lowerName.Contains("rhinoceros") ? 0.60f : 0.46f;
                tokenBody = CreateInsectToken(entity, color, beetleScale, isFlying: false, isBeetle: true);
                height = 0.24f + 0.32f * beetleScale;
                break;

            case 'F': // Dragonfly / Fly
                var flyScale = lowerName.Contains("giant") ? 0.46f : 0.36f;
                tokenBody = CreateInsectToken(entity, color, flyScale, isFlying: true, isBeetle: false);
                height = 0.70f;
                isFloating = true;
                break;

            case 'I': // Generic crawling/swarming insects
                var insectScale = 0.34f;
                tokenBody = CreateInsectToken(entity, color, insectScale, isFlying: false, isBeetle: false);
                height = 0.28f;
                break;

            case 'S': // Spider, Scorpion
                var spiderScale = lowerName.Contains("shelob") || lowerName.Contains("ungoliant") ? 1.05f : (lowerName.Contains("giant") ? 0.62f : (lowerName.Contains("cave") || lowerName.Contains("phase") ? 0.44f : 0.34f));
                tokenBody = CreateArachnidToken(entity, color, spiderScale);
                height = 0.20f + 0.32f * spiderScale;
                break;

            case 'b' or 'B': // Bat, Bird, Crow, Raven, Eagle
                var isBird = glyph == 'B';
                var batScale = isBird ? (lowerName.Contains("eagle") ? 0.65f : 0.38f) : (lowerName.Contains("vampire") ? 0.45f : 0.32f);
                tokenBody = CreateBatToken(entity, color, isBird, batScale);
                height = 0.75f;
                isFloating = true;
                break;

            case 'r': // Rodent (Giant white mouse, rat, cave rat)
                var rodentScale = lowerName.Contains("mouse") ? 0.25f : (lowerName.Contains("giant") || lowerName.Contains("wererat") ? 0.45f : 0.34f);
                tokenBody = CreateRodentToken(entity, color, rodentScale);
                height = 0.20f + 0.25f * rodentScale;
                break;

            case 'C' or 'Z': // Canine (Dog, Wolf, Jackal, War Dog, Hound, Zephyr Hound)
                var isHound = glyph == 'Z' || lowerName.Contains("hound") || lowerName.Contains("zephyr");
                var canineScale = isHound ? 0.68f : (lowerName.Contains("wolf") || lowerName.Contains("warg") ? 0.76f : (lowerName.Contains("jackal") ? 0.44f : 0.55f));
                tokenBody = CreateCanineToken(entity, color, canineScale, isHound, lowerName);
                height = 0.32f + 0.32f * canineScale;
                break;

            case 'f': // Feline (Cat, Panther, Tiger, Lion, Leopard, Cheetah, Jagwal)
                var isBigCat = lowerName.Contains("tiger") || lowerName.Contains("lion") || lowerName.Contains("panther") || lowerName.Contains("leopard") || lowerName.Contains("jagwal");
                var felineScale = isBigCat ? 0.74f : 0.38f;
                tokenBody = CreateFelineToken(entity, color, felineScale, isBigCat, lowerName);
                height = 0.28f + 0.28f * felineScale;
                break;

            case 'J' or 'n': // Snake & Naga (Black naga, Spirit naga, Red naga, Viper, Cobra, Python, Rattlesnake)
                var isCobra = lowerName.Contains("cobra") || lowerName.Contains("viper") || lowerName.Contains("asp") || lowerName.Contains("naga");
                var snakeScale = lowerName.Contains("giant") || lowerName.Contains("python") || lowerName.Contains("anaconda") || lowerName.Contains("guardian") || lowerName.Contains("spirit") ? 0.65f : (isCobra ? 0.45f : 0.34f);
                tokenBody = CreateSnakeToken(entity, color, snakeScale, isCobra, lowerName);
                height = 0.22f + 0.25f * snakeScale;
                break;

            case 'R': // Reptile / Amphibian (Lizard, Gecko, Salamander, Crocodile, Alligator, Basilisk, Frog)
                var isCroc = lowerName.Contains("croc") || lowerName.Contains("alligator") || lowerName.Contains("basilisk");
                var reptScale = isCroc ? 0.65f : (lowerName.Contains("salamander") || lowerName.Contains("lizard") ? 0.42f : 0.36f);
                tokenBody = CreateReptileToken(entity, color, reptScale, isCroc, lowerName);
                height = 0.20f + 0.25f * reptScale;
                break;

            case 'q': // Quadruped (Boar, Bull, Stag, Bear, Rhino, Horse, Mammoth, Hippo)
                var quadScale = lowerName.Contains("giant") || lowerName.Contains("mammoth") || lowerName.Contains("rhino") || lowerName.Contains("elephant") ? 0.88f : (lowerName.Contains("bear") ? 0.78f : 0.60f);
                tokenBody = CreateQuadrupedToken(entity, color, quadScale, lowerName);
                height = 0.38f + 0.38f * quadScale;
                break;

            case 'H': // Hybrid (Griffon, Chimera, Manticore, Minotaur, Hippogriff)
                var hybridScale = 0.75f;
                tokenBody = CreateHybridToken(entity, color, hybridScale, lowerName);
                height = 0.42f + 0.42f * hybridScale;
                break;

            case 'd' or 'D' or 'M': // Dragon, Ancient Dragon, Wyrm, Hydra
                var dragonScale = glyph == 'D' ? 1.35f : (glyph == 'M' ? 1.15f : (lowerName.Contains("baby") ? 0.60f : 0.88f));
                tokenBody = CreateDragonToken(entity, color, dragonScale);
                height = 0.35f + 0.80f * dragonScale;
                break;

            case 'E' or 'v': // Elemental, Vortex
                var elemScale = 0.65f;
                tokenBody = CreateElementalToken(entity, color, elemScale);
                height = 0.85f;
                isFloating = true;
                isSpinning = true;
                break;

            case 'g' or 'X' or 'x': // Golem, Xorn, Lurker
                var golemScale = 0.82f;
                tokenBody = CreateGolemToken(entity, color, golemScale);
                height = 0.30f + 0.60f * golemScale;
                break;

            case 'Q': // Quylthulg
                var quylScale = 0.65f;
                tokenBody = CreateQuylthulgToken(entity, color, quylScale);
                height = 0.75f;
                isFloating = true;
                break;

            case 'k': // Kobold (Small reptilian / draconic humanoid)
                tokenBody = CreateKoboldToken(entity, color, lowerName);
                height = lowerName.Contains("small") ? 0.60f : 0.75f;
                break;

            case 'u' or 'U': // Demons (Imp, Quasit, Lemure, Pit Fiend, Balrog)
                var isMajor = glyph == 'U';
                var demonScale = isMajor ? 1.30f : (lowerName.Contains("small") || lowerName.Contains("homunculus") ? 0.48f : 0.62f);
                tokenBody = CreateDemonToken(entity, color, isMajor, demonScale, lowerName);
                height = isMajor ? 1.80f : 0.70f;
                isFloating = !isMajor;
                break;

            case 'y': // Yeek (Small furry shrieking aberration)
                var yeekScale = lowerName.Contains("king") || lowerName.Contains("master") ? 0.68f : 0.52f;
                tokenBody = CreateYeekToken(entity, color, yeekScale, lowerName);
                height = 0.55f;
                break;

            case 'Y': // Yeti (Abominable snow beast)
                var yetiScale = 1.05f;
                tokenBody = CreateYetiToken(entity, color, yetiScale, lowerName);
                height = 1.65f;
                break;

            case 'l': // Giant Louse / Tick / Flea / Tree
                var isTree = lowerName.Contains("tree") || lowerName.Contains("ent") || lowerName.Contains("huorn") || lowerName.Contains("willow") || lowerName.Contains("wood");
                tokenBody = CreateLouseOrTreeToken(entity, color, isTree, lowerName);
                height = isTree ? 2.20f : 0.24f;
                break;

            default:
                tokenBody = CreateFloatingRunicGem(entity, color, glyph.ToString(), 0.50f);
                height = 0.85f;
                break;
        }

        entity.IsFloating = isFloating;
        entity.IsSpinning = isSpinning;
        entity.BaseY = isFloating ? 0.25f : 0.0f;
        entity.ModelHeight = height;

        entity.CharacterNode.AddChild(tokenBody);
    }

    private static Node3D CreateKoboldToken(MonsterEntity entity, Color glowColor, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Kobold;
        var scale = lowerName.Contains("small") ? 0.58f : (lowerName.Contains("shaman") || lowerName.Contains("mage") ? 0.65f : 0.70f);
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        // Authentic reptilian / draconic scale skin tone with monster color accent
        var baseSkin = new Color(0.38f, 0.28f, 0.18f); // Earthy scaly base
        if (lowerName.Contains("green") || lowerName.Contains("forest")) baseSkin = new Color(0.22f, 0.38f, 0.18f);
        else if (lowerName.Contains("red") || lowerName.Contains("fire")) baseSkin = new Color(0.48f, 0.20f, 0.14f);
        else if (lowerName.Contains("blue") || lowerName.Contains("frost")) baseSkin = new Color(0.18f, 0.28f, 0.44f);
        else if (lowerName.Contains("shadow") || lowerName.Contains("black")) baseSkin = new Color(0.14f, 0.14f, 0.15f);

        var skinColor = baseSkin.Lerp(glowColor, 0.35f);
        var bodyMat = GetProceduralCreatureMaterial("scales", skinColor, roughness: 0.50f, metallic: 0.15f);
        var bellyMat = GetProceduralCreatureMaterial("scales", skinColor.Lightened(0.25f), roughness: 0.55f);
        var hornMat = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.15f, 0.14f), Roughness = 0.45f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        // Hunched bipedal scaly torso
        var bodyNode = new Node3D { Position = new Vector3(0, 0.38f, 0), Rotation = new Vector3(Mathf.DegToRad(18), 0, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var chestMesh = new BoxMesh { Size = new Vector3(0.24f, 0.32f, 0.20f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = chestMesh, MaterialOverride = bodyMat });

        // Scaly belly plate
        var bellyMesh = new BoxMesh { Size = new Vector3(0.20f, 0.26f, 0.05f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bellyMesh, MaterialOverride = bellyMat, Position = new Vector3(0, -0.02f, 0.10f) });

        // Dorsal spine ridges along back
        var spineMesh = new BoxMesh { Size = new Vector3(0.03f, 0.30f, 0.08f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = spineMesh, MaterialOverride = hornMat, Position = new Vector3(0, 0.02f, -0.12f) });

        // Head with reptilian/draconic snout
        var headNode = new Node3D { Position = new Vector3(0, 0.22f, 0.08f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headCranium = new BoxMesh { Size = new Vector3(0.20f, 0.16f, 0.18f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headCranium, MaterialOverride = bodyMat });

        // Tapered reptilian snout / muzzle
        var snoutMesh = new BoxMesh { Size = new Vector3(0.14f, 0.10f, 0.16f) };
        headNode.AddChild(new MeshInstance3D { Mesh = snoutMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.03f, 0.15f) });

        // Draconic horns swept backwards
        var hornMesh = new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.025f, Height = 0.16f, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(0.07f, 0.10f, -0.08f), Rotation = new Vector3(Mathf.DegToRad(-55), 0, Mathf.DegToRad(15)) });
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(-0.07f, 0.10f, -0.08f), Rotation = new Vector3(Mathf.DegToRad(-55), 0, Mathf.DegToRad(-15)) });

        // Glowing draconic eyes
        var eyeMesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.09f, 0.03f, 0.07f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.09f, 0.03f, 0.07f) });

        // Reptilian tail
        var tailNode = new Node3D { Position = new Vector3(0, -0.14f, -0.09f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.055f, Height = 0.22f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = bodyMat, Position = new Vector3(0, -0.06f, -0.08f), Rotation = new Vector3(Mathf.DegToRad(-45), 0, 0) });
        var tailMesh2 = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.035f, Height = 0.20f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = bodyMat, Position = new Vector3(0, -0.16f, -0.20f), Rotation = new Vector3(Mathf.DegToRad(-25), 0, 0) });

        // Arms & Weaponry
        var leftArm = new Node3D { Position = new Vector3(-0.16f, 0.10f, 0.04f) };
        bodyNode.AddChild(leftArm);
        var rightArm = new Node3D { Position = new Vector3(0.16f, 0.10f, 0.04f) };
        bodyNode.AddChild(rightArm);
        entity.LeftAntennaNode = leftArm;
        entity.RightAntennaNode = rightArm;

        var armMesh = new BoxMesh { Size = new Vector3(0.06f, 0.22f, 0.06f) };
        leftArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.08f, 0.04f), Rotation = new Vector3(Mathf.DegToRad(25), 0, 0) });
        rightArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.08f, 0.04f), Rotation = new Vector3(Mathf.DegToRad(25), 0, 0) });

        // Weapon attachment based on archetype
        if (lowerName.Contains("archer") || lowerName.Contains("scout") || lowerName.Contains("sniper"))
        {
            AttachWeapon(rightArm, "res://assets/models/weapons/Bow_Wooden.fbx", 0.16f, new Vector3(0, -0.16f, 0.06f), new Vector3(0, Mathf.DegToRad(90), Mathf.DegToRad(-90)));
        }
        else if (lowerName.Contains("shaman") || lowerName.Contains("mage"))
        {
            AttachWeapon(rightArm, "res://assets/models/characters/Skeleton_Staff.gltf", 0.50f, new Vector3(0, -0.16f, 0.06f), new Vector3(Mathf.DegToRad(-90), 0, 0));
        }
        else
        {
            AttachWeapon(rightArm, "res://assets/models/weapons/Dagger.fbx", 0.15f, new Vector3(0, -0.16f, 0.06f), new Vector3(Mathf.DegToRad(-90), 0, 0));
        }

        // Bipedal legs (digitigrade)
        entity.Legs.Clear();
        for (int i = 0; i < 2; i++)
        {
            var isLeft = i == 0;
            var legRoot = new Node3D { Position = new Vector3(isLeft ? -0.10f : 0.10f, 0.28f, 0) };
            container.AddChild(legRoot);
            entity.Legs.Add(legRoot);

            var thighMesh = new BoxMesh { Size = new Vector3(0.07f, 0.18f, 0.08f) };
            legRoot.AddChild(new MeshInstance3D { Mesh = thighMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.06f, 0.02f), Rotation = new Vector3(Mathf.DegToRad(20), 0, 0) });

            var shinMesh = new BoxMesh { Size = new Vector3(0.06f, 0.16f, 0.06f) };
            legRoot.AddChild(new MeshInstance3D { Mesh = shinMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.16f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(-25), 0, 0) });

            var clawMesh = new BoxMesh { Size = new Vector3(0.07f, 0.04f, 0.10f) };
            legRoot.AddChild(new MeshInstance3D { Mesh = clawMesh, MaterialOverride = hornMat, Position = new Vector3(0, -0.25f, 0.03f) });
        }

        return container;
    }

    private static Node3D CreateDemonToken(MonsterEntity entity, Color glowColor, bool isMajor, float scale, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Demon;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var baseSkin = isMajor ? new Color(0.18f, 0.08f, 0.06f) : new Color(0.42f, 0.12f, 0.10f);
        var skinColor = baseSkin.Lerp(glowColor, 0.40f);
        var bodyMat = GetProceduralCreatureMaterial("scales", skinColor, roughness: 0.40f, metallic: 0.25f);
        var hornMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.08f, 0.08f), Roughness = 0.35f, Metallic = 0.3f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.65f, 0.15f), EmissionEnabled = true, Emission = new Color(1.0f, 0.45f, 0.10f), EmissionEnergyMultiplier = 2.2f };
        var fireMat = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.35f, 0.05f), EmissionEnabled = true, Emission = new Color(1.0f, 0.25f, 0.05f), EmissionEnergyMultiplier = 2.0f };

        // Torso
        var bodyNode = new Node3D { Position = new Vector3(0, isMajor ? 0.65f : 0.32f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var torsoMesh = new BoxMesh { Size = isMajor ? new Vector3(0.55f, 0.65f, 0.38f) : new Vector3(0.24f, 0.30f, 0.18f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = torsoMesh, MaterialOverride = bodyMat });

        // Spines / fiery crest along back
        var spineMesh = new BoxMesh { Size = isMajor ? new Vector3(0.06f, 0.55f, 0.18f) : new Vector3(0.03f, 0.25f, 0.08f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = spineMesh, MaterialOverride = fireMat, Position = new Vector3(0, 0.04f, -(torsoMesh.Size.Z * 0.50f + 0.02f)) });

        // Head with prominent curving horns
        var headNode = new Node3D { Position = new Vector3(0, torsoMesh.Size.Y * 0.50f + (isMajor ? 0.16f : 0.10f), 0.04f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new BoxMesh { Size = isMajor ? new Vector3(0.36f, 0.32f, 0.32f) : new Vector3(0.18f, 0.16f, 0.16f) };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        var hornSize = isMajor ? 0.38f : 0.18f;
        var hornMesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = isMajor ? 0.05f : 0.025f, Height = hornSize, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(headMesh.Size.X * 0.40f, headMesh.Size.Y * 0.45f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-35), 0, Mathf.DegToRad(35)) });
        headNode.AddChild(new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(-headMesh.Size.X * 0.40f, headMesh.Size.Y * 0.45f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-35), 0, Mathf.DegToRad(-35)) });

        // Fiery eyes
        var eyeMesh = new SphereMesh { Radius = isMajor ? 0.04f : 0.02f, Height = isMajor ? 0.08f : 0.04f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(headMesh.Size.X * 0.35f, 0.02f, headMesh.Size.Z * 0.48f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-headMesh.Size.X * 0.35f, 0.02f, headMesh.Size.Z * 0.48f) });

        // Bat wings
        var wingSpan = isMajor ? 0.65f : 0.30f;
        var wingMesh = new BoxMesh { Size = new Vector3(wingSpan, isMajor ? 0.45f : 0.22f, 0.02f) };
        var leftWing = new Node3D { Position = new Vector3(torsoMesh.Size.X * 0.45f, torsoMesh.Size.Y * 0.25f, -torsoMesh.Size.Z * 0.45f) };
        var rightWing = new Node3D { Position = new Vector3(-torsoMesh.Size.X * 0.45f, torsoMesh.Size.Y * 0.25f, -torsoMesh.Size.Z * 0.45f) };
        bodyNode.AddChild(leftWing);
        bodyNode.AddChild(rightWing);
        entity.LeftWingNode = leftWing;
        entity.RightWingNode = rightWing;
        leftWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = bodyMat, Position = new Vector3(wingSpan * 0.50f, 0, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(20)) });
        rightWing.AddChild(new MeshInstance3D { Mesh = wingMesh, MaterialOverride = bodyMat, Position = new Vector3(-wingSpan * 0.50f, 0, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-20)) });

        // Barbed tail
        var tailNode = new Node3D { Position = new Vector3(0, -torsoMesh.Size.Y * 0.40f, -torsoMesh.Size.Z * 0.45f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;
        var tailMesh1 = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.045f, Height = isMajor ? 0.45f : 0.22f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = bodyMat, Position = new Vector3(0, -0.10f, -0.12f), Rotation = new Vector3(Mathf.DegToRad(-55), 0, 0) });
        var barbMesh = new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.035f, Height = isMajor ? 0.16f : 0.08f, RadialSegments = 4 };
        tailNode.AddChild(new MeshInstance3D { Mesh = barbMesh, MaterialOverride = hornMat, Position = new Vector3(0, -0.22f, -0.26f), Rotation = new Vector3(Mathf.DegToRad(-85), 0, 0) });

        // Arms with Demonic Weapon
        var leftArm = new Node3D { Position = new Vector3(-torsoMesh.Size.X * 0.55f, torsoMesh.Size.Y * 0.25f, 0) };
        var rightArm = new Node3D { Position = new Vector3(torsoMesh.Size.X * 0.55f, torsoMesh.Size.Y * 0.25f, 0) };
        bodyNode.AddChild(leftArm);
        bodyNode.AddChild(rightArm);
        entity.LeftAntennaNode = leftArm;
        entity.RightAntennaNode = rightArm;

        var armMesh = new BoxMesh { Size = isMajor ? new Vector3(0.12f, 0.44f, 0.12f) : new Vector3(0.06f, 0.22f, 0.06f) };
        leftArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -(armMesh.Size.Y * 0.40f), 0.04f) });
        rightArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -(armMesh.Size.Y * 0.40f), 0.04f) });

        if (isMajor)
        {
            AttachWeapon(rightArm, "res://assets/models/weapons/Axe_Double.fbx", 0.35f, new Vector3(0, -0.32f, 0.08f), new Vector3(Mathf.DegToRad(-90), 0, 0));
        }
        else
        {
            AttachWeapon(rightArm, "res://assets/models/weapons/Dagger.fbx", 0.18f, new Vector3(0, -0.16f, 0.06f), new Vector3(Mathf.DegToRad(-90), 0, 0));
        }

        // Bipedal legs with hooves
        entity.Legs.Clear();
        for (int i = 0; i < 2; i++)
        {
            var isLeft = i == 0;
            var legRoot = new Node3D { Position = new Vector3(isLeft ? -torsoMesh.Size.X * 0.35f : torsoMesh.Size.X * 0.35f, -(torsoMesh.Size.Y * 0.45f), 0) };
            bodyNode.AddChild(legRoot);
            entity.Legs.Add(legRoot);

            var legMesh = new BoxMesh { Size = isMajor ? new Vector3(0.14f, 0.48f, 0.14f) : new Vector3(0.07f, 0.24f, 0.07f) };
            legRoot.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -(legMesh.Size.Y * 0.45f), 0) });
            var hoofMesh = new CylinderMesh { TopRadius = isMajor ? 0.06f : 0.03f, BottomRadius = isMajor ? 0.08f : 0.04f, Height = isMajor ? 0.08f : 0.04f, RadialSegments = 6 };
            legRoot.AddChild(new MeshInstance3D { Mesh = hoofMesh, MaterialOverride = hornMat, Position = new Vector3(0, -legMesh.Size.Y * 0.90f, 0.02f) });
        }

        return container;
    }

    private static Node3D CreateYeekToken(MonsterEntity entity, Color glowColor, float scale, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Yeek;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var baseFur = lowerName.Contains("blue") ? new Color(0.25f, 0.35f, 0.55f) : new Color(0.48f, 0.42f, 0.28f);
        var furColor = baseFur.Lerp(glowColor, 0.45f);
        var bodyMat = GetProceduralCreatureMaterial("fur", furColor, roughness: 0.85f);
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.05f, 0.05f), Roughness = 0.50f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.9f };

        // Round fuzzy body
        var bodyNode = new Node3D { Position = new Vector3(0, 0.26f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var bodyMesh = new SphereMesh { Radius = 0.22f, Height = 0.40f, RadialSegments = 12, Rings = 6 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat });

        // Wide shrieking open mouth
        var mouthMesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.02f, Height = 0.08f, RadialSegments = 8 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = mouthMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.02f, 0.18f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Beady glowing eyes
        var eyeMesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.08f, 0.08f, 0.16f) });
        bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.08f, 0.08f, 0.16f) });

        // Large floppy twitchy ears
        var earMesh = new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.10f) };
        var leftEar = new Node3D { Position = new Vector3(0.18f, 0.14f, 0) };
        var rightEar = new Node3D { Position = new Vector3(-0.18f, 0.14f, 0) };
        bodyNode.AddChild(leftEar);
        bodyNode.AddChild(rightEar);
        entity.LeftAntennaNode = leftEar;
        entity.RightAntennaNode = rightEar;
        leftEar.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(0.03f, 0.08f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-25)) });
        rightEar.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.03f, 0.08f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(25)) });

        // Little scuttling feet
        entity.Legs.Clear();
        for (int i = 0; i < 2; i++)
        {
            var isLeft = i == 0;
            var leg = new Node3D { Position = new Vector3(isLeft ? -0.10f : 0.10f, -0.16f, 0.04f) };
            bodyNode.AddChild(leg);
            entity.Legs.Add(leg);
            var footMesh = new SphereMesh { Radius = 0.05f, Height = 0.10f, RadialSegments = 8, Rings = 4 };
            leg.AddChild(new MeshInstance3D { Mesh = footMesh, MaterialOverride = bodyMat });
        }

        return container;
    }

    private static Node3D CreateYetiToken(MonsterEntity entity, Color glowColor, float scale, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Yeti;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var furColor = new Color(0.92f, 0.94f, 0.98f).Lerp(glowColor, 0.25f);
        var bodyMat = GetProceduralCreatureMaterial("fur", furColor, roughness: 0.85f);
        var faceMat = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.22f, 0.28f), Roughness = 0.65f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.80f, 1.0f), EmissionEnabled = true, Emission = new Color(0.30f, 0.70f, 1.0f), EmissionEnergyMultiplier = 2.0f };

        // Massive hunched ape torso
        var bodyNode = new Node3D { Position = new Vector3(0, 0.65f, 0), Rotation = new Vector3(Mathf.DegToRad(15), 0, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var chestMesh = new BoxMesh { Size = new Vector3(0.55f, 0.60f, 0.44f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = chestMesh, MaterialOverride = bodyMat });

        // Broad shoulder hump
        var humpMesh = new BoxMesh { Size = new Vector3(0.60f, 0.22f, 0.35f) };
        bodyNode.AddChild(new MeshInstance3D { Mesh = humpMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.32f, -0.04f) });

        // Head with dark ape face
        var headNode = new Node3D { Position = new Vector3(0, 0.35f, 0.18f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = 0.18f, Height = 0.32f, RadialSegments = 10, Rings = 5 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        var faceMesh = new BoxMesh { Size = new Vector3(0.22f, 0.18f, 0.12f) };
        headNode.AddChild(new MeshInstance3D { Mesh = faceMesh, MaterialOverride = faceMat, Position = new Vector3(0, -0.02f, 0.12f) });

        var eyeMesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.07f, 0.04f, 0.18f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.07f, 0.04f, 0.18f) });

        // Powerful gorilla arms
        var leftArm = new Node3D { Position = new Vector3(-0.34f, 0.20f, 0.04f) };
        var rightArm = new Node3D { Position = new Vector3(0.34f, 0.20f, 0.04f) };
        bodyNode.AddChild(leftArm);
        bodyNode.AddChild(rightArm);
        entity.LeftAntennaNode = leftArm;
        entity.RightAntennaNode = rightArm;

        var armMesh = new BoxMesh { Size = new Vector3(0.16f, 0.58f, 0.18f) };
        leftArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.25f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(20), 0, 0) });
        rightArm.AddChild(new MeshInstance3D { Mesh = armMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.25f, 0.08f), Rotation = new Vector3(Mathf.DegToRad(20), 0, 0) });

        // Heavy bipedal legs
        entity.Legs.Clear();
        for (int i = 0; i < 2; i++)
        {
            var isLeft = i == 0;
            var leg = new Node3D { Position = new Vector3(isLeft ? -0.20f : 0.20f, -0.26f, 0) };
            bodyNode.AddChild(leg);
            entity.Legs.Add(leg);
            var legMesh = new BoxMesh { Size = new Vector3(0.18f, 0.44f, 0.20f) };
            leg.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.20f, 0) });
        }

        return container;
    }

    private static Node3D CreateLouseOrTreeToken(MonsterEntity entity, Color glowColor, bool isTree, string lowerName)
    {
        if (isTree)
        {
            entity.TokenType = CreatureTokenType.Ent;
            var container = new Node3D { Position = Vector3.Zero, Scale = Vector3.One };
            var barkMat = GetProceduralCreatureMaterial("scales", new Color(0.25f, 0.18f, 0.12f), roughness: 0.85f);
            var foliageMat = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.32f, 0.14f), Roughness = 0.80f };
            var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

            var trunkMesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.40f, Height = 1.80f, RadialSegments = 8 };
            container.AddChild(new MeshInstance3D { Mesh = trunkMesh, MaterialOverride = barkMat, Position = new Vector3(0, 0.90f, 0) });
            var crownMesh = new SphereMesh { Radius = 0.65f, Height = 1.10f, RadialSegments = 10, Rings = 5 };
            container.AddChild(new MeshInstance3D { Mesh = crownMesh, MaterialOverride = foliageMat, Position = new Vector3(0, 1.95f, 0) });
            var eyeMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 4 };
            container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.12f, 1.20f, 0.26f) });
            container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.12f, 1.20f, 0.26f) });
            return container;
        }
        else
        {
            // Giant Louse, Cave Flea, Tick (flat parasitic crawling horror)
            entity.TokenType = CreatureTokenType.Louse;
            var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(0.40f, 0.40f, 0.40f) };
            var chitinColor = new Color(0.28f, 0.20f, 0.15f).Lerp(glowColor, 0.40f);
            var bodyMat = GetProceduralCreatureMaterial("chitin", chitinColor, roughness: 0.35f, metallic: 0.2f);
            var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.08f, 0.06f), Roughness = 0.4f };
            var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.6f };

            var bodyNode = new Node3D { Position = new Vector3(0, 0.10f, 0) };
            container.AddChild(bodyNode);
            entity.BodyNode = bodyNode;
            entity.InitialBodyScale = Vector3.One;

            // Flat oval segmented carapace
            var carapaceMesh = new SphereMesh { Radius = 0.28f, Height = 0.18f, RadialSegments = 10, Rings = 5 };
            bodyNode.AddChild(new MeshInstance3D { Mesh = carapaceMesh, MaterialOverride = bodyMat, Scale = new Vector3(1.0f, 0.65f, 1.40f) });

            // Biting pincers / mandibles
            var mandMesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.035f, Height = 0.14f, RadialSegments = 6 };
            bodyNode.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(0.08f, 0, 0.38f), Rotation = new Vector3(0, Mathf.DegToRad(-35), Mathf.DegToRad(90)) });
            bodyNode.AddChild(new MeshInstance3D { Mesh = mandMesh, MaterialOverride = darkMat, Position = new Vector3(-0.08f, 0, 0.38f), Rotation = new Vector3(0, Mathf.DegToRad(35), Mathf.DegToRad(90)) });

            var eyeMesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 6, Rings = 4 };
            bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.09f, 0.04f, 0.30f) });
            bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.09f, 0.04f, 0.30f) });

            // 6 crawling sprawling legs
            entity.Legs.Clear();
            float[] legZ = { 0.16f, 0.0f, -0.16f };
            for (int i = 0; i < 3; i++)
            {
                var legL = new Node3D { Position = new Vector3(0.24f, 0, legZ[i]) };
                var legR = new Node3D { Position = new Vector3(-0.24f, 0, legZ[i]) };
                bodyNode.AddChild(legL);
                bodyNode.AddChild(legR);
                entity.Legs.Add(legL);
                entity.Legs.Add(legR);
                var legMesh = new BoxMesh { Size = new Vector3(0.18f, 0.035f, 0.035f) };
                legL.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = darkMat, Position = new Vector3(0.09f, -0.04f, 0) });
                legR.AddChild(new MeshInstance3D { Mesh = legMesh, MaterialOverride = darkMat, Position = new Vector3(-0.09f, -0.04f, 0) });
            }

            return container;
        }
    }

    private static Node3D CreateRodentToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Rodent;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var furColor = new Color(0.22f, 0.20f, 0.18f).Lerp(glowColor, 0.60f);
        var bodyMat = GetProceduralCreatureMaterial("fur", furColor, roughness: 0.75f);
        var pinkMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.65f, 0.68f), Roughness = 0.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.14f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Main torso
        var bodyMesh = new SphereMesh { Radius = 0.15f, Height = 0.44f, RadialSegments = 12, Rings = 6 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat, Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Head with snout
        var headNode = new Node3D { Position = new Vector3(0, 0.05f, 0.24f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = 0.11f, Height = 0.22f, RadialSegments = 10, Rings = 5 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat, Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Snout / Nose
        var noseMesh = new SphereMesh { Radius = 0.03f, Height = 0.05f, RadialSegments = 6, Rings = 3 };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = pinkMat, Position = new Vector3(0, -0.02f, 0.12f) });

        // Upright rounded ears
        var earMesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.04f, Height = 0.07f, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = pinkMat, Position = new Vector3(0.07f, 0.08f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(-20)) });
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = pinkMat, Position = new Vector3(-0.07f, 0.08f, -0.04f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(20)) });

        // Glowing beady eyes
        var eyeMesh = new SphereMesh { Radius = 0.032f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        var eyeColor = (glowColor == Colors.White || glowColor.V > 0.85f) ? new Color(0.95f, 0.25f, 0.30f) : glowColor;
        var eyeMat = new StandardMaterial3D { AlbedoColor = eyeColor, EmissionEnabled = true, Emission = eyeColor, EmissionEnergyMultiplier = 1.8f };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.065f, 0.04f, 0.06f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.065f, 0.04f, 0.06f) });

        // Tail (articulated chain extending back)
        var tailNode = new Node3D { Position = new Vector3(0, 0.02f, -0.22f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 0.22f, RadialSegments = 6 };
        var tailInst1 = new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = pinkMat, Position = new Vector3(0, 0.04f, -0.10f), Rotation = new Vector3(Mathf.DegToRad(-60), 0, 0) };
        tailNode.AddChild(tailInst1);

        var tailMesh2 = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = 0.20f, RadialSegments = 6 };
        var tailInst2 = new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = pinkMat, Position = new Vector3(0, 0.12f, -0.22f), Rotation = new Vector3(Mathf.DegToRad(-25), 0, 0) };
        tailNode.AddChild(tailInst2);

        // 4 Paws on the ground
        var pawMesh = new SphereMesh { Radius = 0.035f, Height = 0.06f, RadialSegments = 6, Rings = 3 };
        var pawMat = new StandardMaterial3D { AlbedoColor = furColor * 0.85f, Roughness = 0.8f };
        var pawPositions = new[]
        {
            new Vector3(0.10f, -0.09f, 0.12f),
            new Vector3(-0.10f, -0.09f, 0.12f),
            new Vector3(0.10f, -0.09f, -0.12f),
            new Vector3(-0.10f, -0.09f, -0.12f),
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
        var chitinMat = GetProceduralCreatureMaterial("chitin", chitinColor, roughness: 0.35f, metallic: 0.3f);
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

    private static Node3D CreateBatToken(MonsterEntity entity, Color glowColor, bool isBird, float scale = 1.0f)
    {
        entity.TokenType = isBird ? CreatureTokenType.Bird : CreatureTokenType.Bat;
        var container = new Node3D { Position = new Vector3(0, 0.70f, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateEyeToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Eye;
        var container = new Node3D { Position = new Vector3(0, 0.75f, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateSlimeToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Slime;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateWormToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Worm;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateMushroomToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Mushroom;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

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
        var bodyMat = GetProceduralCreatureMaterial("fur", furColor, roughness: 0.70f);
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.08f), Roughness = 0.50f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.26f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Athletic canine torso: organic capsule/sphere contours
        var chestMesh = new SphereMesh { Radius = 0.22f, Height = 0.45f, RadialSegments = 14, Rings = 7 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = chestMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.02f, 0.12f) });

        var waistMesh = new SphereMesh { Radius = 0.18f, Height = 0.40f, RadialSegments = 14, Rings = 7 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = waistMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.01f, -0.14f) });

        // Head node
        var headNode = new Node3D { Position = new Vector3(0, 0.24f, 0.36f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = 0.15f, Height = 0.26f, RadialSegments = 12, Rings = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Elongated canine muzzle / snout
        var muzzleMesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.10f, Height = 0.22f, RadialSegments = 8 };
        headNode.AddChild(new MeshInstance3D { Mesh = muzzleMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.04f, 0.16f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Dark nose pad
        var noseMesh = new SphereMesh { Radius = 0.04f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = darkMat, Position = new Vector3(0, -0.02f, 0.27f) });

        // Canine Ears: pointed wolf ears or folded hound ears
        if (isHound)
        {
            var houndEarMesh = new BoxMesh { Size = new Vector3(0.04f, 0.16f, 0.07f) };
            headNode.AddChild(new MeshInstance3D { Mesh = houndEarMesh, MaterialOverride = bodyMat, Position = new Vector3(0.12f, 0.02f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(15), 0, Mathf.DegToRad(-15)) });
            headNode.AddChild(new MeshInstance3D { Mesh = houndEarMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.12f, 0.02f, -0.02f), Rotation = new Vector3(Mathf.DegToRad(15), 0, Mathf.DegToRad(15)) });
        }
        else
        {
            var wolfEarMesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.05f, Height = 0.14f, RadialSegments = 6 };
            headNode.AddChild(new MeshInstance3D { Mesh = wolfEarMesh, MaterialOverride = bodyMat, Position = new Vector3(0.09f, 0.14f, -0.03f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(-20)) });
            headNode.AddChild(new MeshInstance3D { Mesh = wolfEarMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.09f, 0.14f, -0.03f), Rotation = new Vector3(Mathf.DegToRad(-15), 0, Mathf.DegToRad(20)) });
        }

        // Glowing forward eyes
        var eyeMesh = new SphereMesh { Radius = 0.032f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.07f, 0.04f, 0.10f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.07f, 0.04f, 0.10f) });

        // Canine tail: arched upward and extending back
        var tailNode = new Node3D { Position = new Vector3(0, 0.08f, -0.30f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh1 = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.045f, Height = 0.26f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh1, MaterialOverride = bodyMat, Position = new Vector3(0, 0.10f, -0.08f), Rotation = new Vector3(Mathf.DegToRad(40), 0, 0) });

        var tailMesh2 = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.03f, Height = 0.22f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh2, MaterialOverride = bodyMat, Position = new Vector3(0, 0.22f, -0.18f), Rotation = new Vector3(Mathf.DegToRad(15), 0, 0) });

        // 4 Athletic Legs with Paws
        var upperLegMesh = new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.035f, Height = 0.20f, RadialSegments = 6 };
        var lowerLegMesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.025f, Height = 0.18f, RadialSegments = 6 };
        var pawMesh = new SphereMesh { Radius = 0.045f, Height = 0.07f, RadialSegments = 6, Rings = 3 };

        var legPositions = new[]
        {
            new Vector3(0.13f, -0.10f, 0.20f),  // Front Right
            new Vector3(-0.13f, -0.10f, 0.20f), // Front Left
            new Vector3(0.12f, -0.12f, -0.20f), // Back Right
            new Vector3(-0.12f, -0.12f, -0.20f) // Back Left
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
        var baseFur = isBigCat ? (lowerName.Contains("panther") ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.72f, 0.45f, 0.18f)) : new Color(0.42f, 0.36f, 0.30f);
        var furColor = baseFur.Lerp(glowColor, 0.35f);
        var bodyMat = GetProceduralCreatureMaterial("fur", furColor, roughness: 0.65f);
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.10f, 0.10f), Roughness = 0.5f };
        var pinkMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.60f, 0.65f), Roughness = 0.7f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.9f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.18f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Sleek, organic sculpted feline torso (curved shoulders and slender flanks)
        var torsoMesh = new SphereMesh { Radius = isBigCat ? 0.20f : 0.14f, Height = isBigCat ? 0.68f : 0.48f, RadialSegments = 14, Rings = 7 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = torsoMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0, 0), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Compact rounded feline head
        var headNode = new Node3D { Position = new Vector3(0, 0.08f, isBigCat ? 0.34f : 0.25f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = isBigCat ? 0.16f : 0.12f, Height = isBigCat ? 0.24f : 0.18f, RadialSegments = 14, Rings = 7 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Short blunt feline muzzle
        var muzzleMesh = new SphereMesh { Radius = isBigCat ? 0.09f : 0.065f, Height = isBigCat ? 0.12f : 0.09f, RadialSegments = 10, Rings = 5 };
        headNode.AddChild(new MeshInstance3D { Mesh = muzzleMesh, MaterialOverride = bodyMat, Position = new Vector3(0, -0.03f, isBigCat ? 0.12f : 0.09f) });

        // Cute pink/dark nose
        var noseMesh = new SphereMesh { Radius = 0.025f, Height = 0.04f, RadialSegments = 6, Rings = 3 };
        headNode.AddChild(new MeshInstance3D { Mesh = noseMesh, MaterialOverride = pinkMat, Position = new Vector3(0, -0.01f, isBigCat ? 0.18f : 0.14f) });

        // Distinct triangular perked cat ears
        var earMesh = new CylinderMesh { TopRadius = 0.005f, BottomRadius = 0.045f, Height = 0.09f, RadialSegments = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(0.08f, 0.12f, 0.01f), Rotation = new Vector3(Mathf.DegToRad(-20), 0, Mathf.DegToRad(-25)) });
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.08f, 0.12f, 0.01f), Rotation = new Vector3(Mathf.DegToRad(-20), 0, Mathf.DegToRad(25)) });

        // Glowing feline almond eyes
        var eyeMesh = new SphereMesh { Radius = 0.028f, Height = 0.055f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.065f, 0.03f, 0.10f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.065f, 0.03f, 0.10f) });

        // Long graceful curving feline whip tail
        var tailNode = new Node3D { Position = new Vector3(0, 0.04f, isBigCat ? -0.32f : -0.23f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailSeg1 = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 0.24f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailSeg1, MaterialOverride = bodyMat, Position = new Vector3(0, 0.04f, -0.10f), Rotation = new Vector3(Mathf.DegToRad(-35), 0, 0) });

        var tailSeg2 = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = 0.22f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailSeg2, MaterialOverride = bodyMat, Position = new Vector3(0, 0.14f, -0.22f), Rotation = new Vector3(Mathf.DegToRad(30), 0, 0) });

        // 4 Slender stealthy feline legs
        var legMesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.025f, Height = 0.18f, RadialSegments = 6 };
        var pawMesh = new SphereMesh { Radius = 0.038f, Height = 0.055f, RadialSegments = 6, Rings = 3 };

        var halfZ = isBigCat ? 0.20f : 0.14f;
        var halfX = isBigCat ? 0.12f : 0.09f;
        var legPositions = new[]
        {
            new Vector3(halfX, -0.09f, halfZ),
            new Vector3(-halfX, -0.09f, halfZ),
            new Vector3(halfX, -0.09f, -halfZ),
            new Vector3(-halfX, -0.09f, -halfZ)
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

        var baseScales = isCobra ? new Color(0.20f, 0.28f, 0.18f) : new Color(0.30f, 0.25f, 0.15f);
        if (lowerName.Contains("black") || lowerName.Contains("shadow")) baseScales = new Color(0.12f, 0.12f, 0.15f);
        else if (lowerName.Contains("fire") || lowerName.Contains("red")) baseScales = new Color(0.45f, 0.15f, 0.10f);

        var scaleColor = baseScales.Lerp(glowColor, 0.40f);
        var snakeMat = GetProceduralCreatureMaterial("scales", scaleColor, roughness: 0.40f, metallic: 0.15f, isWet: true);
        var underMat = GetProceduralCreatureMaterial("scales", scaleColor.Lightened(0.25f), roughness: 0.50f);
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 2.0f };
        var tongueMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.15f, 0.20f), EmissionEnabled = true, Emission = new Color(0.95f, 0.15f, 0.20f), EmissionEnergyMultiplier = 1.0f };

        // Head node raised alertly off the ground
        var headNode = new Node3D { Position = new Vector3(0, 0.24f, 0.42f) };
        container.AddChild(headNode);
        entity.HeadNode = headNode;

        // Triangular serpent head
        var headMesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 12, Rings = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = snakeMat, Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Cobra hood if cobra
        if (isCobra)
        {
            var hoodMesh = new SphereMesh { Radius = 0.22f, Height = 0.30f, RadialSegments = 10, Rings = 5 };
            headNode.AddChild(new MeshInstance3D { Mesh = hoodMesh, MaterialOverride = snakeMat, Position = new Vector3(0, -0.04f, -0.08f), Scale = new Vector3(1.2f, 0.25f, 0.8f) });
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
        entity.LeftAntennaNode = tongueNode;

        // Glowing slit serpent eyes
        var eyeMesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.09f, 0.03f, 0.08f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.09f, 0.03f, 0.08f) });

        // 7 Articulated Serpentine Body Segments cascading back
        var segZPositions = new[] { 0.28f, 0.14f, 0.00f, -0.14f, -0.28f, -0.42f, -0.56f };
        var segRadii = new[] { 0.12f, 0.11f, 0.10f, 0.09f, 0.08f, 0.06f, 0.04f };

        for (int i = 0; i < segZPositions.Length; i++)
        {
            var r = segRadii[i];
            var segNode = new Node3D { Position = new Vector3(0, r + 0.02f, segZPositions[i]) };
            var segMesh = new SphereMesh { Radius = r, Height = r * 2.2f, RadialSegments = 12, Rings = 6 };
            segNode.AddChild(new MeshInstance3D { Mesh = segMesh, MaterialOverride = snakeMat, Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

            container.AddChild(segNode);
            entity.Segments.Add(segNode);
        }

        return container;
    }

    private static Node3D CreateReptileToken(MonsterEntity entity, Color glowColor, float scale, bool isCroc, string lowerName)
    {
        entity.TokenType = CreatureTokenType.Reptile;
        var container = new Node3D { Position = Vector3.Zero, Scale = new Vector3(scale, scale, scale) };

        var baseSkin = isCroc ? new Color(0.22f, 0.28f, 0.18f) : new Color(0.28f, 0.36f, 0.22f);
        if (lowerName.Contains("basilisk")) baseSkin = new Color(0.30f, 0.20f, 0.35f);
        else if (lowerName.Contains("salamander") || lowerName.Contains("fire")) baseSkin = new Color(0.55f, 0.20f, 0.10f);

        var skinColor = baseSkin.Lerp(glowColor, 0.40f);
        var reptMat = GetProceduralCreatureMaterial("scales", skinColor, roughness: 0.45f, metallic: 0.15f, isWet: true);
        var darkMat = new StandardMaterial3D { AlbedoColor = skinColor * 0.70f, Roughness = 0.60f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.14f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Broad, flat, low-slung reptilian body with organic smooth contours
        var bodyMesh = new SphereMesh { Radius = isCroc ? 0.24f : 0.18f, Height = isCroc ? 0.75f : 0.58f, RadialSegments = 14, Rings = 7 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = reptMat, Scale = new Vector3(1.1f, 0.65f, 1.0f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Scaly dorsal spine ridge along back
        var ridgeMesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.04f, Height = isCroc ? 0.60f : 0.45f, RadialSegments = 6 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = ridgeMesh, MaterialOverride = darkMat, Position = new Vector3(0, 0.12f, 0), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Flat broad reptilian head
        var headNode = new Node3D { Position = new Vector3(0, 0.02f, isCroc ? 0.40f : 0.30f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headMesh = new SphereMesh { Radius = isCroc ? 0.16f : 0.13f, Height = isCroc ? 0.36f : 0.26f, RadialSegments = 12, Rings = 6 };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = reptMat, Scale = new Vector3(1.1f, 0.55f, 1.0f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // Lateral reptilian eyes
        var eyeMesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 4 };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(isCroc ? 0.12f : 0.09f, 0.05f, 0.04f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(isCroc ? -0.12f : -0.09f, 0.05f, 0.04f) });

        // Heavy tapering muscular tail
        var tailNode = new Node3D { Position = new Vector3(0, 0, isCroc ? -0.38f : -0.28f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.11f, Height = 0.55f, RadialSegments = 8 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh, MaterialOverride = reptMat, Position = new Vector3(0, 0, -0.26f), Rotation = new Vector3(Mathf.DegToRad(90), 0, 0) });

        // 4 Sprawling reptilian legs (splayed outwards horizontally, then down)
        var upperLegMesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 0.18f, RadialSegments = 6 };
        var lowerLegMesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.035f, Height = 0.16f, RadialSegments = 6 };
        var clawMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 };

        var halfZ = isCroc ? 0.22f : 0.16f;
        var halfX = isCroc ? 0.20f : 0.15f;
        var legZ = new[] { halfZ, -halfZ };
        for (int i = 0; i < 2; i++)
        {
            // Right leg
            var legR = new Node3D { Position = new Vector3(halfX, 0, legZ[i]) };
            legR.AddChild(new MeshInstance3D { Mesh = upperLegMesh, MaterialOverride = reptMat, Position = new Vector3(0.08f, 0.02f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(75)) });
            legR.AddChild(new MeshInstance3D { Mesh = lowerLegMesh, MaterialOverride = reptMat, Position = new Vector3(0.16f, -0.06f, 0), Rotation = new Vector3(0, 0, Mathf.DegToRad(-15)) });
            legR.AddChild(new MeshInstance3D { Mesh = clawMesh, MaterialOverride = darkMat, Position = new Vector3(0.18f, -0.12f, 0.03f) });
            bodyNode.AddChild(legR);
            entity.Legs.Add(legR);

            // Left leg
            var legL = new Node3D { Position = new Vector3(-halfX, 0, legZ[i]) };
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

    private static Node3D CreateElementalToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Elemental;
        var container = new Node3D { Position = new Vector3(0, 0.65f, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateGolemToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Golem;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateQuylthulgToken(MonsterEntity entity, Color glowColor, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.Quylthulg;
        var container = new Node3D { Position = new Vector3(0, 0.60f, 0), Scale = new Vector3(scale, scale, scale) };

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

    private static Node3D CreateFloatingRunicGem(MonsterEntity entity, Color glowColor, string glyphStr, float scale = 1.0f)
    {
        entity.TokenType = CreatureTokenType.GenericToken;
        var container = new Node3D { Position = new Vector3(0, 0.65f, 0), Scale = new Vector3(scale, scale, scale) };

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
        string attack = null;

        foreach (var name in anims)
        {
            var s = name.ToString().ToLowerInvariant();
            if (idle == null && (s == "idle" || s == "idle_sword" || s == "idle_neutral" || s.Contains("idle") || s.Contains("breath") || s.Contains("stand") || s.Contains("take 001") || s.Contains("layer0")))
            {
                idle = name;
            }
            if (walk == null && (s == "walk" || s == "run" || s.Contains("walk") || s.Contains("run") || s.Contains("march") || s.Contains("locomotion") || s.Contains("fly") || s.Contains("flying") || s.Contains("crawl")))
            {
                walk = name;
            }
            if (attack == null && (s.Contains("attack") || s.Contains("slash") || s.Contains("punch") || s.Contains("shoot") || s.Contains("cast") || s.Contains("hit") || s.Contains("bite") || s.Contains("stab") || s.Contains("strike") || s.Contains("swing")))
            {
                attack = name;
            }
        }

        idle ??= walk ?? anims[0];
        walk ??= (anims.Length > 1 ? anims[1] : idle);
        attack ??= anims[0];

        entity.IdleAnim = idle;
        entity.WalkAnim = walk;
        entity.AttackAnim = attack;

        foreach (var name in anims)
        {
            var animObj = ap.GetAnimation(name);
            if (animObj != null)
            {
                var s = name.ToString().ToLowerInvariant();
                if (s.Contains("idle") || s.Contains("walk") || s.Contains("run") || s.Contains("breath") || s.Contains("stand") || s.Contains("loop") || s.Contains("fly") || s.Contains("flying"))
                {
                    animObj.LoopMode = Animation.LoopModeEnum.Linear;
                }
            }
        }

        var idleObj = ap.GetAnimation(idle);
        if (idleObj != null) idleObj.LoopMode = Animation.LoopModeEnum.Linear;

        var walkObj = ap.GetAnimation(walk);
        if (walkObj != null) walkObj.LoopMode = Animation.LoopModeEnum.Linear;

        ap.SpeedScale = speedScale;
        ap.Play(idle);
    }

    /// <summary>
    /// Orient a monster towards a specific world position (e.g. player position).
    /// If snapImmediately is true, updates CurrentYaw and Rotation instantly.
    /// </summary>
    public static void FaceTarget(MonsterEntity entity, Vector3 targetWorldPos, bool snapImmediately = false)
    {
        if (entity?.CharacterNode == null) return;
        var toTarget = targetWorldPos - entity.CurrentPos;
        toTarget.Y = 0;
        if (toTarget.LengthSquared() > 0.001f)
        {
            var yaw = Mathf.Atan2(toTarget.X, toTarget.Z);
            entity.TargetYaw = yaw;
            if (snapImmediately)
            {
                entity.CurrentYaw = yaw;
                entity.CharacterNode.Rotation = new Vector3(0, yaw, 0);
            }
        }
    }

    /// <summary>
    /// Play attack animation on rigged 3D character models and queue return to idle.
    /// </summary>
    public static void PlayAttackAnimation(MonsterEntity entity)
    {
        if (entity?.AnimPlayer == null) return;
        var anim = entity.AttackAnim;
        if (!string.IsNullOrEmpty(anim) && entity.AnimPlayer.HasAnimation(anim))
        {
            entity.AnimPlayer.Play(anim, 0.10f);
            var idle = !string.IsNullOrEmpty(entity.IdleAnim) ? entity.IdleAnim : "Idle";
            if (entity.AnimPlayer.HasAnimation(idle))
            {
                entity.AnimPlayer.Queue(idle);
            }
        }
    }

    /// <summary>
    /// Trigger a complete attack action: monster turns to face player, plays attack animation, and lunges.
    /// </summary>
    public static void TriggerAttackAction(MonsterEntity entity, Vector3 playerPos)
    {
        if (entity?.CharacterNode == null) return;

        // Immediately orient monster directly facing the player
        FaceTarget(entity, playerPos, snapImmediately: true);

        // Play skeletal attack animation if available
        PlayAttackAnimation(entity);

        // Kinetic lunge towards player along forward facing vector
        TriggerAttackLunge(entity);
    }

    /// <summary>
    /// Perform a kinetic forward strike/lunge along the monster's forward vector.
    /// </summary>
    public static void TriggerAttackLunge(MonsterEntity entity)
    {
        if (entity?.CharacterNode == null || !entity.CharacterNode.IsInsideTree()) return;

        var tree = entity.CharacterNode.GetTree();
        if (tree == null) return;

        var tween = tree.CreateTween();
        if (tween == null) return;

        // Quick punch forward (local +Z faces the target) and smooth return
        var forwardLunge = new Vector3(0, 0, 0.22f);
        tween.TweenProperty(entity.CharacterNode, "position", forwardLunge, 0.08)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(entity.CharacterNode, "position", Vector3.Zero, 0.18)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.In);
    }

    public static void PlayIdleAnimation(MonsterEntity entity)
    {
        if (entity?.AnimPlayer == null) return;
        var anim = !string.IsNullOrEmpty(entity.IdleAnim) ? entity.IdleAnim : "Idle";
        if (entity.AnimPlayer.CurrentAnimation != anim && entity.AnimPlayer.HasAnimation(anim))
        {
            entity.AnimPlayer.Play(anim, 0.2f);
        }
    }

    public static void PlayWalkAnimation(MonsterEntity entity)
    {
        if (entity?.AnimPlayer == null) return;
        var anim = !string.IsNullOrEmpty(entity.WalkAnim) ? entity.WalkAnim : "Walk";
        if (entity.AnimPlayer.CurrentAnimation != anim && entity.AnimPlayer.HasAnimation(anim))
        {
            entity.AnimPlayer.Play(anim, 0.15f);
        }
    }

    public static void PlayIdleAnimation(Node node)
    {
        var ap = FindAnimationPlayer(node);
        if (ap == null) return;
        var anims = ap.GetAnimationList();
        foreach (var name in anims)
        {
            var s = name.ToString().ToLowerInvariant();
            if (s.Contains("idle") || s.Contains("stand") || s.Contains("breath"))
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
        if (anims.Length > 0 && ap.CurrentAnimation != anims[0])
        {
            ap.Play(anims[0], 0.15f);
        }
    }

    private static void ApplyCharacterSkinAndTint(Node node, char glyph, string lowerName, Color tintColor, string modelPath)
    {
        bool isDemon = glyph == 'U' || (glyph == 'u' && !lowerName.Contains("imp"));
        bool isAinu = glyph == 'A';
        bool isUndead = glyph == 's' || glyph == 'z' || glyph == 'L' || glyph == 'V';
        bool isDragon = glyph == 'D' || glyph == 'd' || glyph == 'M';
        bool isElemental = glyph == 'E' || glyph == 'v';

        Texture2D diffuseTex = null;
        Texture2D normalTex = null;
        Texture2D ormTex = null;
        Texture2D emissiveTex = null;

        // Bestiary models: only apply Bestiary textures if the instantiated model is actually the Bestiary asset
        if (modelPath != null && modelPath.Contains("Imp.glb"))
        {
            if (lowerName.Contains("green") || lowerName.Contains("swamp") || lowerName.Contains("poison") || (tintColor.G > tintColor.R && tintColor.G > tintColor.B))
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_BaseColor_3.png");
            }
            else if (lowerName.Contains("blue") || lowerName.Contains("shadow") || lowerName.Contains("frost") || tintColor.B > tintColor.R)
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_BaseColor_2.png");
            }
            else
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_BaseColor_1.png");
            }
            normalTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_Normal.png");
            ormTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_ORM.png");
            emissiveTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Imp_Emissive.png");
        }
        else if (modelPath != null && modelPath.Contains("Puglin.glb"))
        {
            if (lowerName.Contains("blue") || lowerName.Contains("frost") || lowerName.Contains("ice") || (tintColor.B > tintColor.R && tintColor.B > tintColor.G))
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_BaseColor_2.png");
            }
            else if (lowerName.Contains("brown") || lowerName.Contains("cave") || lowerName.Contains("earth"))
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_BaseColor_3.png");
            }
            else
            {
                diffuseTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_BaseColor_1.png");
            }
            normalTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_Normal.png");
            ormTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_ORM.png");
            emissiveTex = GetCachedTexture("res://assets/models/monsters/bestiary/T_Puglin_Emissive.png");
        }
        else if (modelPath != null && modelPath.Contains("Skeleton"))
        {
            if (lowerName.Contains("archer") || lowerName.Contains("scout") || lowerName.Contains("sniper"))
            {
                diffuseTex = GetCachedTexture("res://assets/models/characters/Skeleton_Rogue_skeleton_texture.png");
            }
            else if (lowerName.Contains("mage") || lowerName.Contains("sorcerer") || lowerName.Contains("druj") || lowerName.Contains("necromancer"))
            {
                diffuseTex = GetCachedTexture("res://assets/models/characters/Skeleton_Mage_skeleton_texture.png");
            }
            else
            {
                diffuseTex = GetCachedTexture("res://assets/models/characters/Skeleton_Warrior_skeleton_texture.png")
                          ?? GetCachedTexture("res://assets/models/characters/skeleton_texture.png");
            }
        }

        ApplySkinRecursive(node, glyph, lowerName, tintColor, isDemon, isAinu, isUndead, isDragon, isElemental, diffuseTex, normalTex, ormTex, emissiveTex, modelPath);
    }

    private static void ApplySkinRecursive(
        Node node, char glyph, string lowerName, Color tintColor, bool isDemon, bool isAinu, bool isUndead, bool isDragon, bool isElemental,
        Texture2D diffuseTex, Texture2D normalTex, Texture2D ormTex, Texture2D emissiveTex, string modelPath)
    {
        if (node is MeshInstance3D mi)
        {
            var surfaceCount = mi.Mesh != null ? mi.Mesh.GetSurfaceCount() : 1;
            for (int i = 0; i < surfaceCount; i++)
            {
                if (mi.GetActiveMaterial(i) is BaseMaterial3D orig)
                {
                    var mat = (BaseMaterial3D)orig.Duplicate();
                    var matName = (mat.ResourceName ?? "").ToLowerInvariant();

                    if (diffuseTex != null)
                    {
                        mat.AlbedoTexture = diffuseTex;
                    }
                    if (normalTex != null)
                    {
                        mat.NormalEnabled = true;
                        mat.NormalTexture = normalTex;
                        mat.NormalScale = 0.85f;
                    }
                    if (ormTex != null)
                    {
                        mat.RoughnessTexture = ormTex;
                        mat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
                        mat.MetallicTexture = ormTex;
                        mat.MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue;
                    }
                    if (emissiveTex != null)
                    {
                        mat.EmissionEnabled = true;
                        mat.EmissionTexture = emissiveTex;
                        mat.Emission = tintColor != Colors.White && tintColor.A > 0 ? tintColor : Colors.White;
                        mat.EmissionEnergyMultiplier = 1.20f;
                    }

                    mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;

                    // Specialized handling for Easy Animated Enemy Pack models
                    if (modelPath != null && (modelPath.Contains("Rat.fbx") || modelPath.Contains("Rat.obj")))
                    {
                        if (matName.Contains("pink") || matName.Contains("nose") || matName.Contains("ear") || matName.Contains("tail"))
                        {
                            mat.AlbedoColor = new Color(0.85f, 0.65f, 0.68f);
                        }
                        else
                        {
                            // Body fur
                            if (lowerName.Contains("white") || tintColor == Colors.White || tintColor == new Color(1f, 1f, 1f))
                            {
                                mat.AlbedoColor = new Color(0.95f, 0.95f, 0.95f);
                            }
                            else if (lowerName.Contains("black") || lowerName.Contains("shadow") || lowerName.Contains("dark"))
                            {
                                mat.AlbedoColor = new Color(0.12f, 0.12f, 0.12f);
                            }
                            else if (tintColor != Colors.White && tintColor.A > 0)
                            {
                                mat.AlbedoColor = tintColor * 0.75f;
                            }
                            else
                            {
                                mat.AlbedoColor = new Color(0.35f, 0.28f, 0.22f);
                            }
                        }
                    }
                    else if (modelPath != null && (modelPath.Contains("Spider") || modelPath.Contains("Snake") || modelPath.Contains("Frog") || modelPath.Contains("Wasp")))
                    {
                        if (tintColor != Colors.White && tintColor.A > 0)
                        {
                            mat.AlbedoColor = mat.AlbedoColor.Lerp(tintColor, 0.55f);
                        }
                    }
                    else if (isDemon)
                    {
                        mat.EmissionEnabled = true;
                        mat.Emission = new Color(0.95f, 0.28f, 0.08f);
                        mat.EmissionEnergyMultiplier = 0.85f;
                    }
                    else if (isAinu)
                    {
                        mat.EmissionEnabled = true;
                        mat.Emission = new Color(0.95f, 0.88f, 0.40f);
                        mat.EmissionEnergyMultiplier = 0.65f;
                    }
                    else if (isDragon)
                    {
                        mat.Metallic = 0.35f;
                        mat.Roughness = 0.45f;
                        if (tintColor != Colors.White && tintColor.A > 0)
                        {
                            mat.EmissionEnabled = true;
                            mat.Emission = tintColor * 0.5f;
                            mat.EmissionEnergyMultiplier = 0.8f;
                        }
                    }
                    else if (isElemental)
                    {
                        mat.EmissionEnabled = true;
                        mat.Emission = tintColor != Colors.White && tintColor.A > 0 ? tintColor : new Color(0.7f, 0.8f, 1.0f);
                        mat.EmissionEnergyMultiplier = 1.2f;
                    }
                    else if (isUndead)
                    {
                        mat.Roughness = Mathf.Clamp(mat.Roughness + 0.15f, 0f, 1f);
                        if (matName.Contains("skin") || matName.Contains("head") || matName.Contains("face"))
                        {
                            mat.AlbedoColor = mat.AlbedoColor.Lerp(new Color(0.70f, 0.72f, 0.68f), 0.50f);
                        }
                    }
                    else if (glyph is 'o' or 'k' or 'y' or 'T' or 'O')
                    {
                        // Orcs, goblins, kobolds, trolls - customize skin tone while leaving clothes and gear intact
                        if (matName.Contains("skin") || matName.Contains("head") || matName.Contains("face") || matName.Contains("body"))
                        {
                            if (glyph == 'o')
                            {
                                mat.AlbedoColor = new Color(0.32f, 0.48f, 0.22f); // Orc green skin
                            }
                            else if (glyph == 'k')
                            {
                                mat.AlbedoColor = new Color(0.58f, 0.38f, 0.22f); // Kobold reptilian skin
                            }
                            else if (glyph == 'y')
                            {
                                mat.AlbedoColor = new Color(0.48f, 0.52f, 0.38f); // Yeek mottled skin
                            }
                            else
                            {
                                mat.AlbedoColor = new Color(0.45f, 0.52f, 0.42f); // Troll/ogre hide
                            }
                        }
                        else if (diffuseTex == null && tintColor != Colors.White && tintColor.A > 0)
                        {
                            mat.AlbedoColor = mat.AlbedoColor.Lerp(tintColor, 0.25f);
                        }
                    }
                    else if (tintColor != Colors.White && tintColor.A > 0 && !matName.Contains("skin") && !matName.Contains("face") && !matName.Contains("hair") && diffuseTex == null)
                    {
                        // Accent tinting on garments/armor for distinct monster variants
                        mat.AlbedoColor = mat.AlbedoColor.Lerp(tintColor, 0.28f);
                    }

                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplySkinRecursive(child, glyph, lowerName, tintColor, isDemon, isAinu, isUndead, isDragon, isElemental, diffuseTex, normalTex, ormTex, emissiveTex, modelPath);
        }
    }

    private static void ApplyEtherealMaterial(Node node, Color spectralColor)
    {
        if (node is MeshInstance3D mi)
        {
            var surfaceCount = mi.Mesh != null ? mi.Mesh.GetSurfaceCount() : 1;
            for (int i = 0; i < surfaceCount; i++)
            {
                if (mi.GetActiveMaterial(i) is BaseMaterial3D orig)
                {
                    var mat = (BaseMaterial3D)orig.Duplicate();
                    mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    var c = mat.AlbedoColor;
                    c.A = 0.55f;
                    mat.AlbedoColor = c;
                    mat.EmissionEnabled = true;
                    mat.Emission = spectralColor * 0.7f;
                    mat.EmissionEnergyMultiplier = 0.9f;
                    mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                    mi.SetSurfaceOverrideMaterial(i, mat);
                }
            }
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Sword.fbx", 0.22f, new Vector3(0, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-90), 0, 0));
                }

                // Left hand: Shield
                var shield = FindChildByNameSubstrings(leftHand, "badge_shield", "round_shield", "rectangle_shield", "spike_shield", "shield");
                if (shield != null)
                {
                    shield.Visible = true;
                }
                else
                {
                    AttachWeapon(leftHand, "res://assets/models/weapons/Shield_Heater.fbx", 0.22f, new Vector3(-0.06f, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-90), Mathf.DegToRad(90), 0));
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Axe.fbx", 0.20f, new Vector3(0, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-90), 0, 0));
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Dagger.fbx", 0.18f, new Vector3(0, 0.03f, 0.01f), new Vector3(Mathf.DegToRad(-90), 0, 0));
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Bow_Wooden.fbx", 0.22f, new Vector3(0, 0.04f, 0), new Vector3(0, Mathf.DegToRad(90), Mathf.DegToRad(-90)));
                }
                break;
            }

            case EquipmentRole.Mage:
            {
                // Right hand: Wizard Staff / Spear
                var wand = FindChildByNameSubstrings(rightHand, "1h_wand", "2h_staff", "wand", "staff");
                if (wand != null)
                {
                    wand.Visible = true;
                }
                else
                {
                    AttachWeapon(rightHand, "res://assets/models/characters/Skeleton_Staff.gltf", 0.75f, new Vector3(0, 0.04f, 0), new Vector3(Mathf.DegToRad(-90), 0, 0));
                }

                // Left hand: Spellbook
                var book = FindChildByNameSubstrings(leftHand, "spellbook");
                if (book != null)
                {
                    book.Visible = true;
                }
                else
                {
                    AttachWeapon(leftHand, "res://assets/models/items/Book1_Closed.fbx", 0.22f, new Vector3(-0.04f, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-30), Mathf.DegToRad(60), 0));
                }
                break;
            }

            case EquipmentRole.Cleric:
            {
                // Right hand: Mace / Hammer
                AttachWeapon(rightHand, "res://assets/models/weapons/Hammer_Small.fbx", 0.20f, new Vector3(0, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-90), 0, 0));

                // Left hand: Spellbook or Shield
                var book = FindChildByNameSubstrings(leftHand, "spellbook");
                if (book != null)
                {
                    book.Visible = true;
                }
                else
                {
                    AttachWeapon(leftHand, "res://assets/models/items/Book1_Closed.fbx", 0.22f, new Vector3(-0.04f, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-30), Mathf.DegToRad(60), 0));
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Claymore.fbx", 0.24f, new Vector3(0, 0.05f, 0.02f), new Vector3(Mathf.DegToRad(-90), 0, 0));
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
                    AttachWeapon(rightHand, "res://assets/models/weapons/Axe_Double.fbx", 0.22f, new Vector3(0, 0.04f, 0.02f), new Vector3(Mathf.DegToRad(-90), 0, 0));
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
                    AttachWeapon(rightHand, "res://assets/models/characters/Skeleton_Staff.gltf", 0.80f, new Vector3(0, 0.04f, 0), new Vector3(Mathf.DegToRad(-90), 0, 0));
                }
                break;
            }
        }
    }

    private static void FindHandSlots(Node node, ref Node3D leftHand, ref Node3D rightHand)
    {
        if (node == null) return;
        var name = node.Name.ToString().ToLowerInvariant();

        if (leftHand == null && (name == "handslot.l" || name == "handslot_l" || name == "handslotleft" || (name.StartsWith("handslot") && name.EndsWith("l")) || name == "hand.l" || name == "hand_l" || name == "wrist.l" || name == "wrist_l"))
        {
            if (node is Node3D n3d) leftHand = n3d;
        }
        if (rightHand == null && (name == "handslot.r" || name == "handslot_r" || name == "handslotright" || (name.StartsWith("handslot") && name.EndsWith("r")) || name == "hand.r" || name == "hand_r" || name == "wrist.r" || name == "wrist_r"))
        {
            if (node is Node3D n3d) rightHand = n3d;
        }

        if (node is Skeleton3D skel)
        {
            if (leftHand == null)
            {
                for (int b = 0; b < skel.GetBoneCount(); b++)
                {
                    var bName = skel.GetBoneName(b).ToLowerInvariant();
                    bool isLeft = bName.EndsWith(".l") || bName.EndsWith("_l") || bName.Contains(".l.") || bName.Contains("_l_") || bName.Contains("left") || bName.StartsWith("l_") || bName.StartsWith("l.");
                    bool isHand = bName.Contains("hand") || bName.Contains("wrist") || bName.Contains("palm");
                    if (isLeft && isHand)
                    {
                        var ba = new BoneAttachment3D { Name = "BoneAttach_Hand_L", BoneName = skel.GetBoneName(b) };
                        skel.AddChild(ba);
                        leftHand = ba;
                        break;
                    }
                }
            }
            if (rightHand == null)
            {
                for (int b = 0; b < skel.GetBoneCount(); b++)
                {
                    var bName = skel.GetBoneName(b).ToLowerInvariant();
                    bool isRight = bName.EndsWith(".r") || bName.EndsWith("_r") || bName.Contains(".r.") || bName.Contains("_r_") || bName.Contains("right") || bName.StartsWith("r_") || bName.StartsWith("r.");
                    bool isHand = bName.Contains("hand") || bName.Contains("wrist") || bName.Contains("palm");
                    if (isRight && isHand)
                    {
                        var ba = new BoneAttachment3D { Name = "BoneAttach_Hand_R", BoneName = skel.GetBoneName(b) };
                        skel.AddChild(ba);
                        rightHand = ba;
                        break;
                    }
                }
            }
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

    private static void AttachWeapon(Node3D handNode, string modelPath, float scale = 1.0f, Vector3? positionOffset = null, Vector3? rotationOffset = null)
    {
        if (handNode == null || string.IsNullOrEmpty(modelPath)) return;
        try
        {
            var scene = GetModel(modelPath);
            if (scene != null)
            {
                var weaponInst = scene.Instantiate<Node3D>();
                weaponInst.Position = positionOffset ?? Vector3.Zero;
                weaponInst.Rotation = rotationOffset ?? Vector3.Zero;
                weaponInst.Scale = Vector3.One * scale;
                handNode.AddChild(weaponInst);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MonsterModelResolver] Failed to attach weapon '{modelPath}': {ex.Message}");
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
            case CreatureTokenType.Kobold:
            {
                var breath = Mathf.Sin(t * 5.0f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.03f * breath, 1.0f + 0.04f * breath, 1.0f - 0.02f * breath);
                    if (isMoving)
                    {
                        var trot = Mathf.Abs(Mathf.Sin(t * 14.0f)) * 0.06f;
                        var tilt = Mathf.Sin(t * 14.0f) * 0.08f;
                        entity.BodyNode.Position = new Vector3(0, 0.38f + trot, 0);
                        entity.BodyNode.Rotation = new Vector3(Mathf.DegToRad(22), tilt, 0);
                    }
                    else
                    {
                        entity.BodyNode.Position = new Vector3(0, 0.38f, 0);
                        entity.BodyNode.Rotation = new Vector3(Mathf.DegToRad(18), 0, 0);
                    }
                }

                // Alert predatory head twitching
                if (entity.HeadNode != null)
                {
                    var headTwitchY = isMoving ? Mathf.Sin(t * 7.0f) * 0.15f : (Mathf.Sin(t * 3.0f) * 0.18f + Mathf.Sin(t * 11.0f) * 0.08f);
                    var headTwitchX = Mathf.Sin(t * 6.0f) * 0.06f;
                    entity.HeadNode.Rotation = new Vector3(headTwitchX, headTwitchY, 0);
                }

                // Counterbalancing tail flick
                if (entity.TailNode != null)
                {
                    var tailSpeed = isMoving ? 14.0f : 4.0f;
                    var tailAmp = isMoving ? 0.45f : 0.25f;
                    entity.TailNode.Rotation = new Vector3(0.12f, Mathf.Sin(t * tailSpeed) * tailAmp, 0);
                }

                // Bipedal leg stride
                if (entity.Legs.Count >= 2)
                {
                    var strideSpeed = isMoving ? 14.0f : 0f;
                    var legAngleL = isMoving ? Mathf.Sin(t * strideSpeed) * 0.55f : 0f;
                    var legAngleR = isMoving ? -Mathf.Sin(t * strideSpeed) * 0.55f : 0f;
                    entity.Legs[0].Rotation = new Vector3(legAngleL, 0, 0);
                    entity.Legs[1].Rotation = new Vector3(legAngleR, 0, 0);
                }

                // Arm weapon ready sway
                if (entity.LeftAntennaNode != null && entity.RightAntennaNode != null)
                {
                    var armSwing = isMoving ? Mathf.Sin(t * 14.0f) * 0.35f : Mathf.Sin(t * 2.5f) * 0.08f;
                    entity.LeftAntennaNode.Rotation = new Vector3(armSwing, 0, 0);
                    entity.RightAntennaNode.Rotation = new Vector3(-armSwing, 0, 0);
                }
                break;
            }

            case CreatureTokenType.Demon:
            {
                var breath = Mathf.Sin(t * 4.5f);
                if (entity.BodyNode != null)
                {
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f - 0.02f * breath, 1.0f + 0.04f * breath, 1.0f - 0.02f * breath);
                    if (isMoving)
                    {
                        var bounce = Mathf.Abs(Mathf.Sin(t * 12.0f)) * 0.06f;
                        entity.BodyNode.Position = new Vector3(0, (entity.TokenType == CreatureTokenType.Demon ? (entity.CharacterNode.Name.ToString().Contains("Major") ? 0.65f : 0.32f) : 0.32f) + bounce, 0);
                    }
                }
                if (entity.LeftWingNode != null && entity.RightWingNode != null)
                {
                    var flap = Mathf.Sin(t * (isMoving ? 14.0f : 4.0f)) * 0.35f;
                    entity.LeftWingNode.Rotation = new Vector3(0, 0, flap);
                    entity.RightWingNode.Rotation = new Vector3(0, 0, -flap);
                }
                if (entity.TailNode != null)
                {
                    var tailSpeed = isMoving ? 14.0f : 5.0f;
                    entity.TailNode.Rotation = new Vector3(0.1f, Mathf.Sin(t * tailSpeed) * 0.45f, 0);
                }
                if (entity.Legs.Count >= 2)
                {
                    var stride = isMoving ? 12.0f : 0f;
                    entity.Legs[0].Rotation = new Vector3(Mathf.Sin(t * stride) * 0.45f, 0, 0);
                    entity.Legs[1].Rotation = new Vector3(-Mathf.Sin(t * stride) * 0.45f, 0, 0);
                }
                break;
            }

            case CreatureTokenType.Yeek:
            {
                var shriek = Mathf.Sin(t * 16.0f);
                if (entity.BodyNode != null)
                {
                    var hop = isMoving ? Mathf.Abs(Mathf.Sin(t * 18.0f)) * 0.09f : 0f;
                    entity.BodyNode.Position = new Vector3(0, 0.26f + hop, 0);
                    entity.BodyNode.Scale = entity.InitialBodyScale * new Vector3(1.0f + shriek * 0.06f, 1.0f - shriek * 0.06f, 1.0f);
                }
                if (entity.LeftAntennaNode != null && entity.RightAntennaNode != null)
                {
                    var twitch = Mathf.Sin(t * 22.0f) * 0.25f;
                    entity.LeftAntennaNode.Rotation = new Vector3(0, 0, twitch);
                    entity.RightAntennaNode.Rotation = new Vector3(0, 0, -twitch);
                }
                break;
            }

            case CreatureTokenType.Yeti:
            {
                var swaySpeed = isMoving ? 10.0f : 2.5f;
                if (entity.BodyNode != null)
                {
                    var roll = isMoving ? Mathf.Sin(t * swaySpeed) * 0.12f : 0f;
                    entity.BodyNode.Rotation = new Vector3(Mathf.DegToRad(15), 0, roll);
                }
                if (entity.LeftAntennaNode != null && entity.RightAntennaNode != null)
                {
                    var swing = isMoving ? Mathf.Sin(t * swaySpeed) * 0.50f : Mathf.Sin(t * 2.0f) * 0.08f;
                    entity.LeftAntennaNode.Rotation = new Vector3(swing, 0, 0);
                    entity.RightAntennaNode.Rotation = new Vector3(-swing, 0, 0);
                }
                if (entity.Legs.Count >= 2)
                {
                    var stride = isMoving ? 10.0f : 0f;
                    entity.Legs[0].Rotation = new Vector3(Mathf.Sin(t * stride) * 0.40f, 0, 0);
                    entity.Legs[1].Rotation = new Vector3(-Mathf.Sin(t * stride) * 0.40f, 0, 0);
                }
                break;
            }

            case CreatureTokenType.Louse:
            {
                var crawl = isMoving ? 20.0f : 5.0f;
                if (entity.BodyNode != null)
                {
                    var wiggle = Mathf.Sin(t * crawl) * 0.08f;
                    entity.BodyNode.Rotation = new Vector3(0, wiggle, 0);
                }
                if (entity.Legs.Count >= 6)
                {
                    for (int i = 0; i < entity.Legs.Count; i++)
                    {
                        var phase = (i % 2 == 0) ? 0f : Mathf.Pi;
                        entity.Legs[i].Rotation = new Vector3(0, Mathf.Sin(t * crawl + phase) * 0.35f, 0);
                    }
                }
                break;
            }

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
        if (string.IsNullOrEmpty(path)) return null;

        if (_modelCache.TryGetValue(path, out var scene))
        {
            return scene;
        }

        try
        {
            if (ResourceLoader.Exists(path))
            {
                scene = GD.Load<PackedScene>(path);
                if (scene != null)
                {
                    _modelCache[path] = scene;
                    return scene;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MonsterModelResolver] Failed to load model '{path}': {ex.Message}");
        }
        return null;
    }

    private static Texture2D GetCachedTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        if (_textureCache.TryGetValue(path, out var tex))
        {
            return tex;
        }

        try
        {
            if (ResourceLoader.Exists(path))
            {
                tex = GD.Load<Texture2D>(path);
                if (tex != null)
                {
                    _textureCache[path] = tex;
                    return tex;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MonsterModelResolver] Failed to load texture '{path}': {ex.Message}");
        }
        return null;
    }
}
