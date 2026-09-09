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
    Slime,
    Worm,
    Mushroom,
    Eye,
    Dragon,
    Elemental,
    Golem,
    Quylthulg,
    GenericToken
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
}

/// <summary>
/// Resolves Angband monsters to rich, accurate CC0 3D character models or
/// specialized 3D procedural creature tokens with smooth animations and dynamic lighting.
/// </summary>
public static class MonsterModelResolver
{
    private static readonly Dictionary<string, PackedScene> _modelCache = new();

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

        return entity;
    }

    public static void UpdateMonsterVisual(MonsterEntity entity, JsonElement monster)
    {
        int hp = monster.TryGetProperty("hp", out var hProp) ? hProp.GetInt32() : entity.Hp;
        int hpMax = monster.TryGetProperty("hp_max", out var hmProp) ? hmProp.GetInt32() : entity.HpMax;
        entity.Hp = hp;
        entity.HpMax = hpMax;

        if (entity.Nameplate != null)
        {
            var glyphStr = entity.Glyph.ToString();
            entity.Nameplate.Text = FormatNameplateText(entity.RaceName, glyphStr, hp, hpMax);
            if (hpMax > 0)
            {
                entity.Nameplate.Modulate = HealthColour(hp, hpMax);
            }
        }
    }

    private static string FormatNameplateText(string raceName, string glyphStr, int hp, int hpMax)
    {
        var text = !string.IsNullOrEmpty(raceName) ? raceName : glyphStr;
        if (hpMax > 0)
        {
            var bar = FormatHealthBar(hp, hpMax);
            text += $"\n{bar}  {hp}/{hpMax}";
        }
        return text;
    }

    private static void BuildMonsterVisuals(MonsterEntity entity, char glyph, string raceName, Color color)
    {
        var lowerName = (raceName ?? "").ToLowerInvariant();

        // Check for specific 3D mesh model paths
        var (modelPath, scale, isEthereal, isFloating, customSpeed) = ResolveModelConfig(glyph, raceName, lowerName);

        if (modelPath != null)
        {
            var scene = GetModel(modelPath);
            if (scene != null)
            {
                var instance = scene.Instantiate<Node3D>();
                CleanEquipmentSlots(instance);

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
                if (modelPath.Contains("tree_dead_large"))
                {
                    entity.ModelHeight = 4.8f * scale;
                }
                else if (modelPath.Contains("chest"))
                {
                    entity.ModelHeight = 0.85f * scale;
                }
                else if (modelPath.Contains("coin_stack"))
                {
                    entity.ModelHeight = 1.2f * scale;
                }
                else
                {
                    // KayKit characters have native height ~ 2.3f
                    entity.ModelHeight = 2.35f * scale;
                }

                instance.Scale = new Vector3(scale, scale, scale);
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

        // Dedicated Procedural 3D Creature Tokens for Non-Humanoids
        BuildCreatureToken(entity, glyph, lowerName, color);
    }

    private static (string ModelPath, float Scale, bool IsEthereal, bool IsFloating, float Speed) ResolveModelConfig(
        char glyph, string raceName, string lowerName)
    {
        switch (glyph)
        {
            // 1. Ghosts, Spectres, Poltergeists (G)
            case 'G':
                return ("res://assets/models/characters/Rogue_Hooded.glb", 0.90f, true, true, 0.8f);

            // 2. Wights, Wraiths, Nazgul, Ringwraiths (W)
            case 'W':
                if (lowerName.Contains("wight"))
                    return ("res://assets/models/characters/Skeleton_Warrior.glb", 0.95f, true, false, 0.85f);
                return ("res://assets/models/characters/Rogue_Hooded.glb", 0.95f, true, true, 0.85f);

            // 3. Liches and Arch-Liches (L)
            case 'L':
                return ("res://assets/models/characters/Skeleton_Mage.glb", 1.10f, false, false, 0.9f);

            // 4. Skeletons (s)
            case 's':
                if (lowerName.Contains("archer") || lowerName.Contains("scout") || lowerName.Contains("sniper"))
                    return ("res://assets/models/characters/Skeleton_Rogue.glb", 0.85f, false, false, 1.0f);
                if (lowerName.Contains("mage") || lowerName.Contains("sorcerer") || lowerName.Contains("druj"))
                    return ("res://assets/models/characters/Skeleton_Mage.glb", 0.90f, false, false, 0.9f);
                if (lowerName.Contains("minion") || lowerName.Contains("decayed") || lowerName.Contains("small") ||
                    lowerName.Contains("crawler") || lowerName.Contains("broken"))
                    return ("res://assets/models/characters/Skeleton_Minion.glb", 0.70f, false, false, 1.1f);
                if (lowerName.Contains("lord") || lowerName.Contains("king") || lowerName.Contains("knight") ||
                    lowerName.Contains("champion"))
                    return ("res://assets/models/characters/Skeleton_Warrior.glb", 1.20f, false, false, 1.0f);
                return ("res://assets/models/characters/Skeleton_Warrior.glb", 0.95f, false, false, 1.0f);

            // 5. Zombies, Mummies, Ghouls (z)
            case 'z':
                if (lowerName.Contains("mummy") || lowerName.Contains("greater") || lowerName.Contains("pharaoh"))
                    return ("res://assets/models/characters/Skeleton_Warrior.glb", 1.05f, false, false, 0.75f);
                return ("res://assets/models/characters/Skeleton_Minion.glb", 0.85f, false, false, 0.70f);

            // 6. Vampires (V)
            case 'V':
                return ("res://assets/models/characters/Rogue_Hooded.glb", 1.0f, false, false, 1.05f);

            // 7. Ainur, Maiar (A)
            case 'A':
                return ("res://assets/models/characters/Mage.glb", 1.15f, false, false, 1.0f);

            // 8. Major Demons, Balrogs, Pit Fiends (U)
            case 'U':
                return ("res://assets/models/characters/Barbarian.glb", 1.65f, false, false, 1.0f);

            // 9. Minor Demons, Imps, Quasits, Lemures (u)
            case 'u':
                return ("res://assets/models/characters/Skeleton_Minion.glb", 0.65f, false, false, 1.2f);

            // 10. Giants, Titans, Cyclops, Morgoth (P)
            case 'P':
                if (lowerName.Contains("morgoth"))
                    return ("res://assets/models/characters/Barbarian.glb", 2.0f, false, false, 0.85f);
                return ("res://assets/models/characters/Barbarian.glb", 1.75f, false, false, 0.85f);

            // 11. Trolls (T)
            case 'T':
                return ("res://assets/models/characters/Barbarian.glb", 1.45f, false, false, 0.90f);

            // 12. Ogres (O)
            case 'O':
                return ("res://assets/models/characters/Barbarian.glb", 1.30f, false, false, 0.95f);

            // 13. Yetis (Y)
            case 'Y':
                return ("res://assets/models/characters/Barbarian.glb", 1.30f, false, false, 0.90f);

            // 14. Orcs, Goblins, Snagas, Uruks (o)
            case 'o':
                if (lowerName.Contains("shaman") || lowerName.Contains("mage") || lowerName.Contains("curse"))
                    return ("res://assets/models/characters/Skeleton_Mage.glb", 0.85f, false, false, 1.0f);
                if (lowerName.Contains("archer") || lowerName.Contains("scout") || lowerName.Contains("tracker") || lowerName.Contains("sniper"))
                    return ("res://assets/models/characters/Skeleton_Rogue.glb", 0.85f, false, false, 1.05f);
                return ("res://assets/models/characters/Barbarian.glb", 0.88f, false, false, 1.0f);

            // 15. Kobolds (k) and Yeeks (y)
            case 'k':
            case 'y':
                return ("res://assets/models/characters/Skeleton_Minion.glb", 0.60f, false, false, 1.15f);

            // 16. Ents & Trees (l)
            case 'l':
                return ("res://assets/models/props/tree_dead_large.gltf", 1.10f, false, false, 0.5f);

            // 17. Mimics (?) and Creeping Coins ($)
            case '?':
                return ("res://assets/models/dungeon/chest.glb", 0.85f, false, false, 1.0f);
            case '$':
                return ("res://assets/models/dungeon/coin_stack_large.gltf.glb", 0.90f, false, false, 1.0f);

            // 18. Humanoids, Elves, Dwarves, Hobbits, Gnomes (h)
            case 'h':
            {
                float scale = 0.90f;
                if (lowerName.Contains("dwarf") || lowerName.Contains("hobbit") || lowerName.Contains("gnome") ||
                    lowerName.Contains("halfling") || lowerName.Contains("leprechaun"))
                {
                    scale = 0.65f;
                }
                else if (lowerName.Contains("elf") || lowerName.Contains("ranger") || lowerName.Contains("dunedain"))
                {
                    scale = 0.98f;
                }

                if (lowerName.Contains("knight") || lowerName.Contains("paladin") || lowerName.Contains("veteran") ||
                    lowerName.Contains("warrior") || lowerName.Contains("soldier") || lowerName.Contains("guard") ||
                    lowerName.Contains("captain") || lowerName.Contains("fighter") || lowerName.Contains("champion") ||
                    lowerName.Contains("swordsman") || lowerName.Contains("centurion"))
                {
                    return ("res://assets/models/characters/Knight.glb", scale * 1.05f, false, false, 1.0f);
                }

                if (lowerName.Contains("barbarian") || lowerName.Contains("mercenary") || lowerName.Contains("gladiator") ||
                    lowerName.Contains("berserker") || lowerName.Contains("bouncer") || lowerName.Contains("ruffian") ||
                    lowerName.Contains("beastman"))
                {
                    return ("res://assets/models/characters/Barbarian.glb", scale * 1.10f, false, false, 1.0f);
                }

                if (lowerName.Contains("mage") || lowerName.Contains("wizard") || lowerName.Contains("warlock") ||
                    lowerName.Contains("sorcerer") || lowerName.Contains("alchemist") || lowerName.Contains("scholar") ||
                    lowerName.Contains("priest") || lowerName.Contains("cleric") || lowerName.Contains("sage") ||
                    lowerName.Contains("acolyte") || lowerName.Contains("cultist") || lowerName.Contains("druid") ||
                    lowerName.Contains("seer") || lowerName.Contains("shaman") || lowerName.Contains("enchanter"))
                {
                    return ("res://assets/models/characters/Mage.glb", scale * 0.95f, false, false, 0.95f);
                }

                if (lowerName.Contains("thief") || lowerName.Contains("rogue") || lowerName.Contains("burglar") ||
                    lowerName.Contains("assassin") || lowerName.Contains("cutpurse") || lowerName.Contains("beggar") ||
                    lowerName.Contains("scoundrel") || lowerName.Contains("bandit") || lowerName.Contains("brigand") ||
                    lowerName.Contains("ninja") || lowerName.Contains("scout") || lowerName.Contains("stalker"))
                {
                    return ("res://assets/models/characters/Rogue_Hooded.glb", scale * 0.95f, false, false, 1.05f);
                }

                return ("res://assets/models/characters/Rogue.glb", scale, false, false, 1.0f);
            }

            // 19. People, Adventurers, Mercenaries, Warriors, Mages (p)
            case 'p':
            {
                float scale = 0.95f;
                if (lowerName.Contains("knight") || lowerName.Contains("paladin") || lowerName.Contains("veteran") ||
                    lowerName.Contains("warrior") || lowerName.Contains("soldier") || lowerName.Contains("guard") ||
                    lowerName.Contains("captain") || lowerName.Contains("fighter") || lowerName.Contains("champion") ||
                    lowerName.Contains("swordsman") || lowerName.Contains("centurion"))
                {
                    return ("res://assets/models/characters/Knight.glb", scale * 1.05f, false, false, 1.0f);
                }

                if (lowerName.Contains("barbarian") || lowerName.Contains("mercenary") || lowerName.Contains("gladiator") ||
                    lowerName.Contains("berserker") || lowerName.Contains("bouncer") || lowerName.Contains("ruffian") ||
                    lowerName.Contains("beastman"))
                {
                    return ("res://assets/models/characters/Barbarian.glb", scale * 1.10f, false, false, 1.0f);
                }

                if (lowerName.Contains("mage") || lowerName.Contains("wizard") || lowerName.Contains("warlock") ||
                    lowerName.Contains("sorcerer") || lowerName.Contains("alchemist") || lowerName.Contains("scholar") ||
                    lowerName.Contains("priest") || lowerName.Contains("cleric") || lowerName.Contains("sage") ||
                    lowerName.Contains("acolyte") || lowerName.Contains("cultist") || lowerName.Contains("druid") ||
                    lowerName.Contains("seer") || lowerName.Contains("shaman") || lowerName.Contains("enchanter") ||
                    lowerName.Contains("necromancer"))
                {
                    return ("res://assets/models/characters/Mage.glb", scale * 0.95f, false, false, 0.95f);
                }

                if (lowerName.Contains("thief") || lowerName.Contains("rogue") || lowerName.Contains("burglar") ||
                    lowerName.Contains("assassin") || lowerName.Contains("cutpurse") || lowerName.Contains("beggar") ||
                    lowerName.Contains("scoundrel") || lowerName.Contains("bandit") || lowerName.Contains("brigand") ||
                    lowerName.Contains("ninja") || lowerName.Contains("scout") || lowerName.Contains("stalker"))
                {
                    return ("res://assets/models/characters/Rogue_Hooded.glb", scale * 0.95f, false, false, 1.05f);
                }

                return ("res://assets/models/characters/Rogue.glb", scale, false, false, 1.0f);
            }

            // 20. Townsfolk (t)
            case 't':
            {
                float scale = 0.85f;
                if (lowerName.Contains("dwarf") || lowerName.Contains("hobbit") || lowerName.Contains("gnome") ||
                    lowerName.Contains("halfling"))
                {
                    scale = 0.65f;
                }
                var townModel = SelectTownspersonModel(raceName);
                return (townModel, scale, false, false, 1.0f);
            }

            // 21. Nagas (n)
            case 'n':
                return ("res://assets/models/characters/Mage.glb", 0.90f, false, false, 0.95f);

            // All non-humanoid glyphs (r, C, f, q, Z, R, J, a, c, S, K, I, F, b, B, d, D, M, e, E, v, g, X, x, Q, j, w, i, m, ,, etc.)
            default:
                return (null, 1.0f, false, false, 1.0f);
        }
    }

    private static string SelectTownspersonModel(string raceName)
    {
        var hash = Math.Abs((raceName ?? "townsperson").GetHashCode());
        var options = new[]
        {
            "res://assets/models/characters/Knight.glb",
            "res://assets/models/characters/Rogue.glb",
            "res://assets/models/characters/Barbarian.glb",
            "res://assets/models/characters/Mage.glb",
            "res://assets/models/characters/Rogue_Hooded.glb",
        };
        return options[hash % options.Length];
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
                var insectScale = isBeetle ? 1.25f : (glyph == 'a' ? 0.95f : 0.85f);
                tokenBody = CreateInsectToken(entity, color, insectScale, isFly, isBeetle);
                height = 0.45f + 0.35f * insectScale;
                if (isFly) isFloating = true;
                break;

            case 'S': // Spider, Scorpion
                var spiderScale = 1.0f;
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
                tokenBody = CreateRodentToken(entity, color, 0.85f);
                height = 0.55f;
                break;

            case 'C' or 'f' or 'q' or 'Z' or 'R' or 'J' or 'H': // Canine, Feline, Quadruped, Zephyr Hound, Reptile, Snake, Hybrid
                var beastScale = (glyph == 'R' || glyph == 'J') ? 0.80f : 1.0f;
                tokenBody = CreateBeastToken(entity, color, beastScale);
                height = 0.50f + 0.45f * beastScale;
                break;

            case 'd' or 'D' or 'M': // Dragon, Ancient Dragon, Wyrm, Hydra
                var dragonScale = glyph == 'D' ? 1.75f : glyph == 'M' ? 1.4f : 1.25f;
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

        // Sclera (eyeball)
        var eyeMesh = new SphereMesh { Radius = 0.32f, Height = 0.64f, RadialSegments = 16, Rings = 8 };
        var eyeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.90f, 0.85f),
            Roughness = 0.15f,
            Metallic = 0.1f,
        };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat });

        // Iris & Pupil attached to an Iris pivot node
        var irisPivot = new Node3D { Position = new Vector3(0, 0, 0.30f) };
        container.AddChild(irisPivot);
        entity.IrisNode = irisPivot;

        var irisMesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.05f, RadialSegments = 12 };
        var irisMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.4f,
        };
        var irisInst = new MeshInstance3D
        {
            Mesh = irisMesh,
            MaterialOverride = irisMat,
            Rotation = new Vector3(Mathf.DegToRad(90), 0, 0),
        };
        irisPivot.AddChild(irisInst);

        var pupilMesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.06f, RadialSegments = 12 };
        var pupilMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.04f),
            Roughness = 0.1f,
        };
        var pupilInst = new MeshInstance3D
        {
            Mesh = pupilMesh,
            MaterialOverride = pupilMat,
            Position = new Vector3(0, 0, 0.01f),
            Rotation = new Vector3(Mathf.DegToRad(90), 0, 0),
        };
        irisPivot.AddChild(pupilInst);

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

    private static Node3D CreateBeastToken(MonsterEntity entity, Color glowColor, float scale)
    {
        entity.TokenType = CreatureTokenType.Beast;
        var container = new Node3D { Position = new Vector3(0, 0, 0), Scale = new Vector3(scale, scale, scale) };

        var furColor = new Color(0.22f, 0.20f, 0.18f).Lerp(glowColor, 0.45f);
        var bodyMat = new StandardMaterial3D { AlbedoColor = furColor, Roughness = 0.7f };

        var bodyNode = new Node3D { Position = new Vector3(0, 0.24f, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        var bodySize = new Vector3(0.38f, 0.34f, 0.65f);
        var bodyMesh = new BoxMesh { Size = bodySize };
        bodyNode.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat });

        var headNode = new Node3D { Position = new Vector3(0, 0.12f, 0.38f) };
        bodyNode.AddChild(headNode);
        entity.HeadNode = headNode;

        var headSize = new Vector3(0.28f, 0.25f, 0.32f);
        var headMesh = new BoxMesh { Size = headSize };
        headNode.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat });

        // Ears
        var earMesh = new BoxMesh { Size = new Vector3(0.07f, 0.12f, 0.04f) };
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(0.11f, 0.16f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(-15)) });
        headNode.AddChild(new MeshInstance3D { Mesh = earMesh, MaterialOverride = bodyMat, Position = new Vector3(-0.11f, 0.16f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(15)) });

        // Glowing Eyes
        var eyeMesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.8f };
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.10f, 0.05f, 0.12f) });
        headNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.10f, 0.05f, 0.12f) });

        // Tail
        var tailNode = new Node3D { Position = new Vector3(0, 0.08f, -0.32f) };
        bodyNode.AddChild(tailNode);
        entity.TailNode = tailNode;

        var tailMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 0.35f, RadialSegments = 6 };
        tailNode.AddChild(new MeshInstance3D { Mesh = tailMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.12f, -0.12f), Rotation = new Vector3(Mathf.DegToRad(-45), 0, 0) });

        // 4 Legs
        var legMesh = new BoxMesh { Size = new Vector3(0.10f, 0.22f, 0.12f) };
        var legPositions = new[]
        {
            new Vector3(0.15f, -0.16f, 0.20f),
            new Vector3(-0.15f, -0.16f, 0.20f),
            new Vector3(0.15f, -0.16f, -0.20f),
            new Vector3(-0.15f, -0.16f, -0.20f),
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

        var bodyNode = new Node3D { Position = new Vector3(0, 0, 0) };
        container.AddChild(bodyNode);
        entity.BodyNode = bodyNode;
        entity.InitialBodyScale = Vector3.One;

        // Draconic Spire
        var spireMesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.40f, Height = 0.95f, RadialSegments = 12 };
        var spireMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.12f, 0.14f),
            Roughness = 0.4f,
            Metallic = 0.5f,
            EmissionEnabled = true,
            Emission = glowColor * 0.4f,
            EmissionEnergyMultiplier = 0.8f,
        };
        bodyNode.AddChild(new MeshInstance3D { Mesh = spireMesh, MaterialOverride = spireMat, Position = new Vector3(0, 0.48f, 0) });

        // Horn Crest
        var hornMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.08f, Height = 0.35f, RadialSegments = 8 };
        var hornMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.4f };

        var hornL = new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(0.18f, 0.85f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(-25)) };
        var hornR = new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(-0.18f, 0.85f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(25)) };
        bodyNode.AddChild(hornL);
        bodyNode.AddChild(hornR);

        // Blazing Draconic Eyes
        var eyeMesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 };
        bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = hornMat, Position = new Vector3(0.10f, 0.70f, 0.20f) });
        bodyNode.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = hornMat, Position = new Vector3(-0.10f, 0.70f, 0.20f) });

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
            if (idle == null && s.Contains("idle")) idle = name;
            if (walk == null && (s.Contains("walk") || s.Contains("run"))) walk = name;
        }

        idle ??= anims[0];
        walk ??= idle;

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
    /// KayKit characters pack all weapons/shields into the scene graph simultaneously.
    /// Keep only the first weapon/shield visible in each hand slot to prevent overlapping glitches.
    /// </summary>
    private static void CleanEquipmentSlots(Node node)
    {
        if (node.Name == "handslot_l" || node.Name == "handslot_r")
        {
            var count = node.GetChildCount();
            for (var i = 1; i < count; i++)
            {
                var child = node.GetChild(i);
                if (child is Node3D n3d)
                {
                    n3d.Visible = false;
                }
            }
            return;
        }

        foreach (var child in node.GetChildren())
        {
            CleanEquipmentSlots(child);
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

            case CreatureTokenType.Beast:
            {
                // Breathing rise & fall
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
