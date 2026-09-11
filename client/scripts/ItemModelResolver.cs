using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Resolves Angband items to 3D pickups with continuous hover animation,
/// slow rotation, and emissive color accents.
/// </summary>
public static class ItemModelResolver
{
    private static readonly Dictionary<string, PackedScene> _modelCache = new();
    private static readonly Dictionary<char, Mesh> _meshCache = new();
    private static readonly Dictionary<(char Glyph, Color Color), StandardMaterial3D> _materialCache = new();

    /// <summary>
    /// Declarative mapping of item glyphs to CC0 3D mesh assets and scaling factors.
    /// Contributors can add new 3D item models here or via RegisterItemModel.
    /// </summary>
    public static readonly Dictionary<char, (string ModelPath, float Scale)> ModelMappings = new()
    {
        { '$', ("res://assets/models/items/Chest_Closed.fbx", 0.45f) },
        { '!', ("res://assets/models/items/Potion1_Filled.fbx", 0.45f) },
        { '?', ("res://assets/models/items/Parchment.fbx", 0.45f) },
        { ')', ("res://assets/models/weapons/Sword.fbx", 0.55f) },
        { '[', ("res://assets/models/items/Armor_Metal.fbx", 0.50f) },
        { ']', ("res://assets/models/items/Armor_Metal2.fbx", 0.50f) },
        { '(', ("res://assets/models/weapons/Shield_Heater.fbx", 0.55f) },
        { '/', ("res://assets/models/weapons/Spear.fbx", 0.55f) },
        { '_', ("res://assets/models/weapons/Spear.fbx", 0.55f) },
        { '|', ("res://assets/models/weapons/Spear.fbx", 0.50f) },
        { ',', ("res://assets/models/items/ChickenLeg.fbx", 0.45f) },
        { '"', ("res://assets/models/items/Necklace1.fbx", 0.45f) },
        { '=', ("res://assets/models/items/Ring1.fbx", 0.45f) },
        { '*', ("res://assets/models/items/Crystal1.fbx", 0.40f) },
        { '~', ("res://assets/models/props/Torch_Metal.fbx", 0.45f) },
        { '{', ("res://assets/models/weapons/Arrow.fbx", 0.50f) },
        { '}', ("res://assets/models/weapons/Bow_Wooden.fbx", 0.55f) },
    };

    /// <summary>
    /// Register or override a 3D model mapping for an item glyph.
    /// </summary>
    public static void RegisterItemModel(char glyph, string modelPath, float scale = 0.50f)
    {
        ModelMappings[glyph] = (modelPath, scale);
    }

    public static Node3D CreateItemNode(JsonElement item, Vector3 worldPos)
    {
        var root = new Node3D { Position = worldPos };

        var glyphStr = item.TryGetProperty("glyph", out var gProp) ? gProp.GetString() ?? "?" : "?";
        var glyph = glyphStr.Length > 0 ? glyphStr[0] : '?';
        var attr = item.TryGetProperty("attr", out var aProp) ? aProp.GetInt32() : 1;
        var color = AngbandColors.Get(attr);

        string itemName = item.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
        if (item.TryGetProperty("pile", out var pProp) && pProp.GetBoolean())
        {
            itemName += " (pile)";
        }

        var visual = CreateVisual(glyph, color, itemName);
        var rotator = new ItemRotator();
        rotator.Position = new Vector3(0, 0.25f, 0);
        rotator.AddChild(visual);
        root.AddChild(rotator);

        if (!string.IsNullOrEmpty(itemName))
        {
            var caption = CreateCaption(itemName, new Vector3(0, 0.85f, 0),
                new Color(0.88f, 0.88f, 0.82f), 38);
            root.AddChild(caption);
        }

        return root;
    }

    private static Node3D CreateVisual(char glyph, Color color, string itemName = null)
    {
        var modelConfig = ResolveSpecificItemModel(glyph, itemName);
        if (modelConfig.ModelPath != null)
        {
            var scene = GetModel(modelConfig.ModelPath);
            if (scene != null)
            {
                var inst = scene.Instantiate<Node3D>();
                inst.Scale = new Vector3(modelConfig.Scale, modelConfig.Scale, modelConfig.Scale);
                return inst;
            }
        }

        // Procedural stylized 3D pickups fallback
        var mesh = GetPickupMesh(glyph);
        var mat = GetPickupMaterial(glyph, color);

        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
        };
    }

    private static (string ModelPath, float Scale) ResolveSpecificItemModel(char glyph, string itemName)
    {
        if (!string.IsNullOrEmpty(itemName))
        {
            var lower = itemName.ToLowerInvariant();

            // Daggers & Knives
            if (lower.Contains("dagger") || lower.Contains("knife") || lower.Contains("rapier") || lower.Contains("stiletto") || lower.Contains("main gauche"))
            {
                return ("res://assets/models/weapons/Dagger.fbx", 0.50f);
            }
            // 2H Swords & Greatswords
            if (lower.Contains("two-handed") || lower.Contains("great sword") || lower.Contains("claymore") || lower.Contains("zweihander") || lower.Contains("bastard") || lower.Contains("executioner"))
            {
                return ("res://assets/models/weapons/Claymore.fbx", 0.60f);
            }
            // 1H Swords
            if (lower.Contains("sword") || lower.Contains("blade") || lower.Contains("scimitar") || lower.Contains("sabre") || lower.Contains("katana") || lower.Contains("cutlass"))
            {
                if (lower.Contains("golden") || lower.Contains("holy") || lower.Contains("radiant"))
                {
                    return ("res://assets/models/weapons/Sword_Golden.fbx", 0.55f);
                }
                return ("res://assets/models/weapons/Sword.fbx", 0.55f);
            }
            // 2H Battle Axes & Polearms
            if (lower.Contains("battle axe") || lower.Contains("great axe") || lower.Contains("broad axe") || lower.Contains("halberd") || lower.Contains("poleaxe"))
            {
                return ("res://assets/models/weapons/Axe_Double.fbx", 0.55f);
            }
            // 1H Axes
            if (lower.Contains("axe") || lower.Contains("cleaver") || lower.Contains("hatchet"))
            {
                return ("res://assets/models/weapons/Axe.fbx", 0.50f);
            }
            // War Hammers & Mattocks
            if (lower.Contains("war hammer") || lower.Contains("great hammer") || lower.Contains("mattock"))
            {
                return ("res://assets/models/weapons/Hammer_Double.fbx", 0.55f);
            }
            // Maces, Flails, Morning Stars, Clubs, Cudgels, Whips
            if (lower.Contains("hammer") || lower.Contains("mace") || lower.Contains("flail") || lower.Contains("star") ||
                lower.Contains("club") || lower.Contains("cudgel") || lower.Contains("whip") ||
                lower.Contains("ball-and-chain"))
            {
                return ("res://assets/models/weapons/Hammer_Small.fbx", 0.50f);
            }
            // Staves & Polearms
            if (lower.Contains("staff") || lower.Contains("spear") || lower.Contains("pike") || lower.Contains("lance") || lower.Contains("trident"))
            {
                return ("res://assets/models/weapons/Spear.fbx", 0.55f);
            }
            // Scythes
            if (lower.Contains("scythe"))
            {
                return ("res://assets/models/weapons/Scythe.fbx", 0.55f);
            }
            // Bows & Crossbows
            if (lower.Contains("crossbow") || lower.Contains("arbalest") || lower.Contains("bow") || lower.Contains("sling"))
            {
                if (lower.Contains("golden")) return ("res://assets/models/weapons/Bow_Golden.fbx", 0.55f);
                if (lower.Contains("evil") || lower.Contains("dark")) return ("res://assets/models/weapons/Bow_Evil.fbx", 0.55f);
                return ("res://assets/models/weapons/Bow_Wooden.fbx", 0.55f);
            }
            // Arrows & Missiles
            if (lower.Contains("arrow") || lower.Contains("bolt") || lower.Contains("shot") || lower.Contains("pebble"))
            {
                return ("res://assets/models/weapons/Arrow.fbx", 0.50f);
            }
            // Shields
            if (lower.Contains("shield") || lower.Contains("buckler") || lower.Contains("targe"))
            {
                if (lower.Contains("golden") || lower.Contains("celtic"))
                {
                    return ("res://assets/models/weapons/Shield_Celtic_Golden.fbx", 0.55f);
                }
                if (lower.Contains("round") || lower.Contains("small"))
                {
                    return ("res://assets/models/weapons/Shield_Round.fbx", 0.55f);
                }
                return ("res://assets/models/weapons/Shield_Heater.fbx", 0.55f);
            }
            // Body Armor & Clothes
            if (lower.Contains("plate") || lower.Contains("chain") || lower.Contains("mail") || lower.Contains("armor") || lower.Contains("cuirass") || lower.Contains("corselet"))
            {
                if (lower.Contains("leather") || lower.Contains("soft") || lower.Contains("padded") || lower.Contains("robe") || lower.Contains("cloak"))
                {
                    return ("res://assets/models/items/Armor_Leather.fbx", 0.50f);
                }
                if (lower.Contains("gold") || lower.Contains("shining") || lower.Contains("mithril"))
                {
                    return ("res://assets/models/items/Armor_Golden.fbx", 0.50f);
                }
                if (lower.Contains("black") || lower.Contains("dark") || lower.Contains("shadow"))
                {
                    return ("res://assets/models/items/Armor_Black.fbx", 0.50f);
                }
                return ("res://assets/models/items/Armor_Metal.fbx", 0.50f);
            }
            // Helmets & Crowns
            if (lower.Contains("crown") || lower.Contains("coronet") || lower.Contains("helm") || lower.Contains("cap") || lower.Contains("hat"))
            {
                return ("res://assets/models/items/Crown.fbx", 0.50f);
            }
            // Gloves & Gauntlets
            if (lower.Contains("glove") || lower.Contains("gauntlet") || lower.Contains("cesta") || lower.Contains("bracer"))
            {
                return ("res://assets/models/items/Glove.fbx", 0.50f);
            }
            // Potions
            if (lower.Contains("potion") || lower.Contains("draught") || lower.Contains("flask") || lower.Contains("elixir"))
            {
                return ("res://assets/models/items/Potion1_Filled.fbx", 0.45f);
            }
            // Books & Spellbooks
            if (lower.Contains("book") || lower.Contains("tome") || lower.Contains("grimoire") || lower.Contains("incantations") || lower.Contains("prayers") || lower.Contains("sorcery") || lower.Contains("magic"))
            {
                return ("res://assets/models/items/Book1_Closed.fbx", 0.45f);
            }
            // Scrolls
            if (lower.Contains("scroll") || lower.Contains("parchment") || lower.Contains("paper"))
            {
                return ("res://assets/models/items/Scroll.fbx", 0.45f);
            }
            // Rings
            if (lower.Contains("ring") || lower.Contains("band"))
            {
                return ("res://assets/models/items/Ring1.fbx", 0.45f);
            }
            // Amulets & Necklaces
            if (lower.Contains("amulet") || lower.Contains("necklace") || lower.Contains("pendant") || lower.Contains("medallion") || lower.Contains("periapt") || lower.Contains("talisman"))
            {
                return ("res://assets/models/items/Necklace1.fbx", 0.45f);
            }
            // Gems & Crystals
            if (lower.Contains("gem") || lower.Contains("crystal") || lower.Contains("diamond") || lower.Contains("ruby") || lower.Contains("emerald") || lower.Contains("sapphire") || lower.Contains("mineral") || lower.Contains("opal") || lower.Contains("garnet"))
            {
                return ("res://assets/models/items/Crystal1.fbx", 0.40f);
            }
            // Chests & Gold
            if (lower.Contains("chest") || lower.Contains("coffer") || lower.Contains("box"))
            {
                return ("res://assets/models/items/Chest_Closed.fbx", 0.45f);
            }
            if (lower.Contains("gold") || lower.Contains("coin") || lower.Contains("copper") || lower.Contains("silver") || lower.Contains("mithril coin"))
            {
                return ("res://assets/models/items/Gold_Ingots.fbx", 0.45f);
            }
            // Food
            if (lower.Contains("ration") || lower.Contains("food") || lower.Contains("meat") || lower.Contains("bread") || lower.Contains("mushroom") || lower.Contains("apple") || lower.Contains("slime mold"))
            {
                return ("res://assets/models/items/ChickenLeg.fbx", 0.45f);
            }
            // Remains & Skulls
            if (lower.Contains("skull") || lower.Contains("bone") || lower.Contains("skeleton"))
            {
                return ("res://assets/models/items/Skull.fbx", 0.45f);
            }
            // Torches & Lanterns
            if (lower.Contains("torch") || lower.Contains("lantern"))
            {
                return ("res://assets/models/props/Torch_Metal.fbx", 0.45f);
            }
            // Bags & Backpacks
            if (lower.Contains("bag") || lower.Contains("pouch") || lower.Contains("pack") || lower.Contains("sack"))
            {
                return ("res://assets/models/items/Backpack.fbx", 0.45f);
            }
            // Keys & Lockpicks
            if (lower.Contains("key") || lower.Contains("lockpick") || lower.Contains("padlock"))
            {
                return ("res://assets/models/items/Key1.fbx", 0.45f);
            }
        }

        if (ModelMappings.TryGetValue(glyph, out var config))
        {
            return config;
        }

        return (null, 0.50f);
    }

    private static StandardMaterial3D GetPickupMaterial(char glyph, Color color)
    {
        var key = (glyph, color);
        if (_materialCache.TryGetValue(key, out var mat))
        {
            return mat;
        }

        mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = glyph is ')' or '[' or ']' or '(' or '=' ? 0.28f : 0.45f,
            Metallic = glyph is ')' or '[' or ']' or '(' or '=' ? 0.92f : 0.05f,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.85f,
            RimEnabled = true,
            Rim = 0.5f,
            RimTint = 0.5f,
        };
        _materialCache[key] = mat;
        return mat;
    }

    private static Mesh GetPickupMesh(char glyph)
    {
        if (_meshCache.TryGetValue(glyph, out var cached))
        {
            return cached;
        }

        Mesh mesh = glyph switch
        {
            '!' => new CylinderMesh { TopRadius = 0.10f, BottomRadius = 0.18f, Height = 0.38f, RadialSegments = 12 }, // Potion
            '?' => new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.45f, RadialSegments = 8 },  // Scroll
            '=' => new TorusMesh { InnerRadius = 0.10f, OuterRadius = 0.18f, Rings = 12, RingSegments = 8 },       // Ring
            '"' => new SphereMesh { Radius = 0.15f, Height = 0.30f, RadialSegments = 12, Rings = 6 },              // Amulet
            ')' => new BoxMesh { Size = new Vector3(0.08f, 0.55f, 0.14f) },                                        // Weapon
            '[' or ']' or '(' => new BoxMesh { Size = new Vector3(0.35f, 0.42f, 0.12f) },                          // Armor / Shield
            '/' or '_' or '|' => new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.65f },     // Wand / Staff
            _ => new BoxMesh { Size = new Vector3(0.24f, 0.24f, 0.24f) },                                          // Default
        };

        _meshCache[glyph] = mesh;
        return mesh;
    }

    private static Label3D CreateCaption(string text, Vector3 pos, Color colour, int size = 48)
    {
        return new Label3D
        {
            Text = text,
            Modulate = colour,
            OutlineModulate = new Color(0, 0, 0, 0.95f),
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

/// <summary>
/// Continuous gentle hover and rotation for 3D item pickups on the floor.
/// </summary>
public partial class ItemRotator : Node3D
{
    private double _time;
    private float _baseY;

    public override void _Ready()
    {
        _baseY = Position.Y;
        _time = GD.Randf() * 10.0;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        Rotation = new Vector3(0, (float)(_time * 1.8), 0);
        Position = new Vector3(Position.X, _baseY + Mathf.Sin((float)_time * 3.2f) * 0.06f, Position.Z);
    }
}
