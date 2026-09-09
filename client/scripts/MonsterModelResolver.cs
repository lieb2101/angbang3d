using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

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
}

/// <summary>
/// Resolves Angband monsters to rich, accurate CC0 3D character models or
/// specialized 3D procedural creature tokens with smooth animations and dynamic lighting.
/// </summary>
public static class MonsterModelResolver
{
    private static readonly Dictionary<string, PackedScene> _modelCache = new();
    private static CylinderMesh _tokenBaseMesh;

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
        // 1. Ghosts, Spectres, Wraiths, Phantoms, Shadows (G, W)
        if (glyph == 'G' || lowerName.Contains("ghost") || lowerName.Contains("spectre") ||
            lowerName.Contains("wraith") || lowerName.Contains("phantom") || lowerName.Contains("shade") ||
            lowerName.Contains("poltergeist") || lowerName.Contains("shadow"))
        {
            return ("res://assets/models/characters/Rogue_Hooded.glb", 0.90f, true, true, 0.8f);
        }

        // 2. Liches and Undead Spellcasters (L, W)
        if (glyph == 'L' || lowerName.Contains("lich") || lowerName.Contains("arch-lich") ||
            lowerName.Contains("wight") || lowerName.Contains("necromancer"))
        {
            return ("res://assets/models/characters/Skeleton_Mage.glb", 1.05f, false, false, 0.9f);
        }

        // 3. Skeletons (s)
        if (glyph == 's' || lowerName.Contains("skeleton"))
        {
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
        }

        // 4. Zombies, Mummies, Ghouls (z)
        if (glyph == 'z' || lowerName.Contains("zombie") || lowerName.Contains("mummy") ||
            lowerName.Contains("ghoul") || lowerName.Contains("flesh golem"))
        {
            if (lowerName.Contains("mummy") || lowerName.Contains("greater"))
                return ("res://assets/models/characters/Skeleton_Warrior.glb", 1.05f, false, false, 0.75f);
            return ("res://assets/models/characters/Skeleton_Minion.glb", 0.85f, false, false, 0.70f);
        }

        // 5. Vampires (V)
        if (glyph == 'V' || lowerName.Contains("vampire"))
        {
            return ("res://assets/models/characters/Rogue_Hooded.glb", 1.0f, false, false, 1.05f);
        }

        // 6. Ents & Trees (l)
        if (glyph == 'l' || lowerName.Contains("tree") || lowerName.Contains("ent") || lowerName.Contains("huorn"))
        {
            return ("res://assets/models/props/tree_dead_large.gltf", 1.10f, false, false, 0.5f);
        }

        // 7. Mimics & Creeping Coins (?, $)
        if (glyph == '?' || lowerName.Contains("mimic"))
        {
            return ("res://assets/models/dungeon/chest.glb", 0.85f, false, false, 1.0f);
        }
        if (glyph == '$' || lowerName.Contains("creeping coins") || lowerName.Contains("copper coins") ||
            lowerName.Contains("silver coins") || lowerName.Contains("gold coins") || lowerName.Contains("mithril coins"))
        {
            return ("res://assets/models/dungeon/coin_stack_large.gltf.glb", 0.90f, false, false, 1.0f);
        }

        // 8. Demons (u, U)
        if (glyph == 'u' || lowerName.Contains("minor demon") || lowerName.Contains("imp") ||
            lowerName.Contains("quasit") || lowerName.Contains("lemure") || lowerName.Contains("homunculus"))
        {
            return ("res://assets/models/characters/Skeleton_Minion.glb", 0.65f, false, false, 1.2f);
        }
        if (glyph == 'U' || lowerName.Contains("major demon") || lowerName.Contains("balrog") ||
            lowerName.Contains("pit fiend") || lowerName.Contains("demon lord") || lowerName.Contains("marilith") ||
            lowerName.Contains("vrock") || lowerName.Contains("hezrou") || lowerName.Contains("glabrezu"))
        {
            return ("res://assets/models/characters/Barbarian.glb", 1.65f, false, false, 1.0f);
        }

        // 9. Giants (P)
        if (glyph == 'P' || lowerName.Contains("giant") || lowerName.Contains("titan") || lowerName.Contains("cyclops"))
        {
            return ("res://assets/models/characters/Barbarian.glb", 1.75f, false, false, 0.85f);
        }

        // 10. Trolls (T)
        if (glyph == 'T' || lowerName.Contains("troll"))
        {
            return ("res://assets/models/characters/Barbarian.glb", 1.45f, false, false, 0.90f);
        }

        // 11. Ogres (O)
        if (glyph == 'O' || lowerName.Contains("ogre"))
        {
            return ("res://assets/models/characters/Barbarian.glb", 1.30f, false, false, 0.95f);
        }

        // 12. Orcs (o)
        if (glyph == 'o' || lowerName.Contains("orc") || lowerName.Contains("uruk") || lowerName.Contains("snaga"))
        {
            if (lowerName.Contains("shaman") || lowerName.Contains("mage") || lowerName.Contains("curse"))
                return ("res://assets/models/characters/Skeleton_Mage.glb", 0.85f, false, false, 1.0f);
            if (lowerName.Contains("archer") || lowerName.Contains("scout") || lowerName.Contains("tracker"))
                return ("res://assets/models/characters/Skeleton_Rogue.glb", 0.85f, false, false, 1.05f);
            return ("res://assets/models/characters/Barbarian.glb", 0.90f, false, false, 1.0f);
        }

        // 13. Kobolds (k) and Yeeks (y)
        if (glyph == 'k' || glyph == 'y' || lowerName.Contains("kobold") || lowerName.Contains("yeek"))
        {
            return ("res://assets/models/characters/Skeleton_Minion.glb", 0.60f, false, false, 1.15f);
        }

        // 14. Humanoids, Adventurers, Warriors, Townspeople (p, h, t)
        if (glyph is 'p' or 'h' or 't' || lowerName.Contains("human") || lowerName.Contains("person") ||
            lowerName.Contains("elf") || lowerName.Contains("dwarf") || lowerName.Contains("hobbit") ||
            lowerName.Contains("gnome") || lowerName.Contains("halfling"))
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

            if (glyph == 't')
            {
                var townModel = SelectTownspersonModel(raceName);
                return (townModel, scale, false, false, 1.0f);
            }

            return ("res://assets/models/characters/Rogue.glb", scale, false, false, 1.0f);
        }

        // Non-humanoids will use procedural 3D tokens
        return (null, 1.0f, false, false, 1.0f);
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
        entity.CharacterNode.AddChild(CreateTokenBase(color));

        Node3D tokenBody;
        float height = 1.2f;
        bool isFloating = false;
        bool isSpinning = false;

        switch (glyph)
        {
            case 'e': // Floating Eye
                tokenBody = CreateEyeToken(color);
                height = 1.15f;
                isFloating = true;
                break;

            case 'j' or 'i' or 'w': // Jelly, Slime, Ooze, Icky Thing, Worm Mass
                tokenBody = CreateSlimeToken(color);
                height = 0.75f;
                break;

            case 'm' or ',': // Mold, Mushroom
                tokenBody = CreateMushroomToken(color);
                height = 0.80f;
                break;

            case 'S' or 'K' or 'a' or 'c' or 'I': // Spider, Scorpion, Killer Beetle, Ant, Centipede, Insect
                var archScale = glyph == 'K' ? 1.3f : 0.9f;
                tokenBody = CreateArachnidToken(color, archScale);
                height = 0.55f + 0.50f * archScale;
                break;

            case 'C' or 'f' or 'q' or 'r' or 'Z': // Canine, Wolf, Feline, Quadruped, Rodent, Zephyr Hound
                var beastScale = glyph == 'r' ? 0.65f : 1.0f;
                tokenBody = CreateBeastToken(color, beastScale);
                height = 0.50f + 0.45f * beastScale;
                break;

            case 'd' or 'D' or 'M': // Dragon, Ancient Dragon, Wyrm, Hydra
                var dragonScale = glyph == 'D' ? 1.75f : glyph == 'M' ? 1.4f : 1.25f;
                tokenBody = CreateDragonToken(color, dragonScale);
                height = 0.25f + 1.10f * dragonScale;
                break;

            case 'E' or 'v': // Elemental, Vortex
                tokenBody = CreateElementalToken(color);
                height = 1.25f;
                isFloating = true;
                isSpinning = true;
                break;

            case 'g' or 'X' or 'x': // Golem, Xorn, Lurker
                tokenBody = CreateGolemToken(color);
                height = 1.10f;
                break;

            case 'Q': // Quylthulg
                tokenBody = CreateQuylthulgToken(color);
                height = 1.05f;
                isFloating = true;
                break;

            default:
                tokenBody = CreateFloatingRunicGem(color, glyph.ToString());
                height = 1.00f;
                break;
        }

        entity.IsFloating = isFloating;
        entity.IsSpinning = isSpinning;
        entity.BaseY = isFloating ? 0.25f : 0.0f;
        entity.ModelHeight = height;

        entity.CharacterNode.AddChild(tokenBody);
    }

    private static Node3D CreateEyeToken(Color glowColor)
    {
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

        // Iris
        var irisMesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.05f, RadialSegments = 12 };
        var irisMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.2f,
        };
        var irisInst = new MeshInstance3D
        {
            Mesh = irisMesh,
            MaterialOverride = irisMat,
            Position = new Vector3(0, 0, 0.30f),
            Rotation = new Vector3(Mathf.DegToRad(90), 0, 0),
        };
        container.AddChild(irisInst);

        // Pupil
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
            Position = new Vector3(0, 0, 0.31f),
            Rotation = new Vector3(Mathf.DegToRad(90), 0, 0),
        };
        container.AddChild(pupilInst);

        return container;
    }

    private static Node3D CreateSlimeToken(Color glowColor)
    {
        var container = new Node3D { Position = new Vector3(0, 0.20f, 0) };

        // Translucent outer dome
        var domeMesh = new SphereMesh { Radius = 0.45f, Height = 0.50f, RadialSegments = 16, Rings = 8 };
        var domeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(glowColor.R, glowColor.G, glowColor.B, 0.65f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.1f,
            EmissionEnabled = true,
            Emission = glowColor * 0.6f,
            EmissionEnergyMultiplier = 0.9f,
        };
        container.AddChild(new MeshInstance3D { Mesh = domeMesh, MaterialOverride = domeMat, Position = new Vector3(0, 0.10f, 0) });

        // Inner glowing nucleus
        var coreMesh = new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 12, Rings = 6 };
        var coreMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.5f,
        };
        container.AddChild(new MeshInstance3D { Mesh = coreMesh, MaterialOverride = coreMat, Position = new Vector3(0, 0.15f, 0) });

        return container;
    }

    private static Node3D CreateMushroomToken(Color glowColor)
    {
        var container = new Node3D { Position = new Vector3(0, 0.10f, 0) };

        var stemMesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.09f, Height = 0.40f, RadialSegments = 8 };
        var stemMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.70f, 0.62f), Roughness = 0.8f };

        var capMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.24f, Height = 0.18f, RadialSegments = 12 };
        var capMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 0.9f,
        };

        // Main central mushroom
        container.AddChild(new MeshInstance3D { Mesh = stemMesh, MaterialOverride = stemMat, Position = new Vector3(0, 0.20f, 0) });
        container.AddChild(new MeshInstance3D { Mesh = capMesh, MaterialOverride = capMat, Position = new Vector3(0, 0.44f, 0) });

        // Side smaller spores
        var sideStemMesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.06f, Height = 0.25f, RadialSegments = 8 };
        var sideCapMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.14f, Height = 0.12f, RadialSegments = 10 };

        container.AddChild(new MeshInstance3D { Mesh = sideStemMesh, MaterialOverride = stemMat, Position = new Vector3(0.20f, 0.12f, 0.10f) });
        container.AddChild(new MeshInstance3D { Mesh = sideCapMesh, MaterialOverride = capMat, Position = new Vector3(0.20f, 0.26f, 0.10f) });

        container.AddChild(new MeshInstance3D { Mesh = sideStemMesh, MaterialOverride = stemMat, Position = new Vector3(-0.18f, 0.10f, -0.08f) });
        container.AddChild(new MeshInstance3D { Mesh = sideCapMesh, MaterialOverride = capMat, Position = new Vector3(-0.18f, 0.22f, -0.08f) });

        return container;
    }

    private static Node3D CreateArachnidToken(Color glowColor, float scale)
    {
        var container = new Node3D { Position = new Vector3(0, 0.15f, 0), Scale = new Vector3(scale, scale, scale) };

        // Abdomen
        var bodyMesh = new SphereMesh { Radius = 0.28f, Height = 0.38f, RadialSegments = 12, Rings = 6 };
        var bodyMat = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.14f, 0.16f), Roughness = 0.4f, Metallic = 0.3f };
        container.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.18f, -0.12f) });

        // Cephalothorax (head)
        var headMesh = new SphereMesh { Radius = 0.20f, Height = 0.25f, RadialSegments = 12, Rings = 6 };
        container.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.16f, 0.18f) });

        // Glowing eyes
        var eyeMesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 8, Rings = 4 };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.5f };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.08f, 0.22f, 0.32f) });
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.08f, 0.22f, 0.32f) });

        return container;
    }

    private static Node3D CreateBeastToken(Color glowColor, float scale)
    {
        var container = new Node3D { Position = new Vector3(0, 0.15f, 0), Scale = new Vector3(scale, scale, scale) };

        // Beast body block
        var bodyMesh = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.65f) };
        var bodyMat = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.18f, 0.15f), Roughness = 0.7f };
        container.AddChild(new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.24f, 0) });

        // Head with muzzle
        var headMesh = new BoxMesh { Size = new Vector3(0.28f, 0.25f, 0.32f) };
        container.AddChild(new MeshInstance3D { Mesh = headMesh, MaterialOverride = bodyMat, Position = new Vector3(0, 0.38f, 0.38f) });

        // Glowing eyes
        var eyeMesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.05f) };
        var eyeMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.6f };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(0.09f, 0.42f, 0.52f) });
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = eyeMat, Position = new Vector3(-0.09f, 0.42f, 0.52f) });

        return container;
    }

    private static Node3D CreateDragonToken(Color glowColor, float scale)
    {
        var container = new Node3D { Position = new Vector3(0, 0.15f, 0), Scale = new Vector3(scale, scale, scale) };

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
        container.AddChild(new MeshInstance3D { Mesh = spireMesh, MaterialOverride = spireMat, Position = new Vector3(0, 0.48f, 0) });

        // Horn Crest
        var hornMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.08f, Height = 0.35f, RadialSegments = 8 };
        var hornMat = new StandardMaterial3D { AlbedoColor = glowColor, EmissionEnabled = true, Emission = glowColor, EmissionEnergyMultiplier = 1.4f };

        var hornL = new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(0.18f, 0.85f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(-25)) };
        var hornR = new MeshInstance3D { Mesh = hornMesh, MaterialOverride = hornMat, Position = new Vector3(-0.18f, 0.85f, -0.05f), Rotation = new Vector3(0, 0, Mathf.DegToRad(25)) };
        container.AddChild(hornL);
        container.AddChild(hornR);

        // Blazing Draconic Eyes
        var eyeMesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 };
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = hornMat, Position = new Vector3(0.10f, 0.70f, 0.20f) });
        container.AddChild(new MeshInstance3D { Mesh = eyeMesh, MaterialOverride = hornMat, Position = new Vector3(-0.10f, 0.70f, 0.20f) });

        return container;
    }

    private static Node3D CreateElementalToken(Color glowColor)
    {
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

    private static Node3D CreateGolemToken(Color glowColor)
    {
        var container = new Node3D { Position = new Vector3(0, 0.15f, 0) };

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
        container.AddChild(new MeshInstance3D { Mesh = colMesh, MaterialOverride = colMat, Position = new Vector3(0, 0.42f, 0) });

        // Glowing Rune Visor
        var visorMesh = new BoxMesh { Size = new Vector3(0.38f, 0.08f, 0.06f) };
        var visorMat = new StandardMaterial3D
        {
            AlbedoColor = glowColor,
            EmissionEnabled = true,
            Emission = glowColor,
            EmissionEnergyMultiplier = 1.6f,
        };
        container.AddChild(new MeshInstance3D { Mesh = visorMesh, MaterialOverride = visorMat, Position = new Vector3(0, 0.62f, 0.23f) });

        return container;
    }

    private static Node3D CreateQuylthulgToken(Color glowColor)
    {
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
        container.AddChild(new MeshInstance3D { Mesh = orbMesh, MaterialOverride = orbMat });

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

    private static void ApplyEtherealMaterial(Node node, Color spectralColor)
    {
        if (node is MeshInstance3D mi)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoColor = new Color(spectralColor.R, spectralColor.G, spectralColor.B, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.2f,
                Metallic = 0.1f,
                EmissionEnabled = true,
                Emission = spectralColor,
                EmissionEnergyMultiplier = 1.25f,
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

    private static Node3D CreateTokenBase(Color glowColor)
    {
        _tokenBaseMesh ??= new CylinderMesh
        {
            TopRadius = 0.40f,
            BottomRadius = 0.50f,
            Height = 0.12f,
            RadialSegments = 16,
        };

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.12f, 0.14f),
            Roughness = 0.70f,
            Metallic = 0.2f,
            EmissionEnabled = true,
            Emission = glowColor * 0.5f,
            EmissionEnergyMultiplier = 1.0f,
        };

        return new MeshInstance3D
        {
            Mesh = _tokenBaseMesh,
            MaterialOverride = mat,
            Position = new Vector3(0, 0.06f, 0),
        };
    }

    private static Node3D CreateFloatingRunicGem(Color glowColor, string glyphStr)
    {
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
