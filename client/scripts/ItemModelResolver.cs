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
        { '$', ("res://assets/models/dungeon/chest_gold.glb", 0.40f) },
        { '!', ("res://assets/models/dungeon/bottle_A_green.gltf.glb", 0.60f) },
        { '?', ("res://assets/models/characters/spellbook_closed.gltf", 0.60f) },
        { ')', ("res://assets/models/characters/sword_1handed.gltf", 0.65f) },
        { '[', ("res://assets/models/characters/shield_badge.gltf", 0.65f) },
        { ']', ("res://assets/models/characters/shield_badge.gltf", 0.65f) },
        { '(', ("res://assets/models/characters/shield_badge.gltf", 0.65f) },
        { '/', ("res://assets/models/characters/wand.gltf", 0.60f) },
        { '_', ("res://assets/models/characters/wand.gltf", 0.60f) },
        { '|', ("res://assets/models/characters/wand.gltf", 0.60f) },
        { ',', ("res://assets/models/dungeon/plate_food_A.gltf.glb", 0.50f) },
        { '"', ("res://assets/models/dungeon/key.gltf.glb", 0.50f) },
        { '=', ("res://assets/models/dungeon/key.gltf.glb", 0.50f) },
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
                return ("res://assets/models/characters/dagger.gltf", 0.65f);
            }
            // 2H Swords
            if (lower.Contains("two-handed") || lower.Contains("great sword") || lower.Contains("claymore") || lower.Contains("zweihander") || lower.Contains("bastard"))
            {
                return ("res://assets/models/characters/sword_2handed.gltf", 0.65f);
            }
            // 2H Battle Axes & Polearms
            if (lower.Contains("battle axe") || lower.Contains("great axe") || lower.Contains("broad axe") || lower.Contains("halberd") || lower.Contains("poleaxe"))
            {
                return ("res://assets/models/characters/axe_2handed.gltf", 0.65f);
            }
            // 1H Axes
            if (lower.Contains("axe") || lower.Contains("cleaver") || lower.Contains("hatchet"))
            {
                return ("res://assets/models/characters/axe_1handed.gltf", 0.65f);
            }
            // War Hammers & Mattocks
            if (lower.Contains("hammer") || lower.Contains("mattock"))
            {
                return ("res://assets/models/characters/hammer.gltf", 0.65f);
            }
            // Maces, Flails, Morning Stars, Clubs, Cudgels, Whips
            if (lower.Contains("mace") || lower.Contains("flail") || lower.Contains("star") ||
                lower.Contains("club") || lower.Contains("cudgel") || lower.Contains("whip") ||
                lower.Contains("ball-and-chain"))
            {
                return ("res://assets/models/characters/mace.gltf", 0.65f);
            }
            // Staves & Polearms
            if (lower.Contains("staff") || lower.Contains("spear") || lower.Contains("pike") || lower.Contains("lance") || lower.Contains("trident"))
            {
                return ("res://assets/models/characters/staff.gltf", 0.55f);
            }
            // Crossbows & Bows
            if (lower.Contains("crossbow") || lower.Contains("arbalest") || lower.Contains("bow"))
            {
                return ("res://assets/models/characters/crossbow_1handed.gltf", 0.60f);
            }
            // Shields
            if (lower.Contains("spiked"))
            {
                return ("res://assets/models/characters/shield_spikes.gltf", 0.65f);
            }
            if (lower.Contains("round") || lower.Contains("buckler"))
            {
                return ("res://assets/models/characters/shield_round.gltf", 0.65f);
            }
            // Torches & Lanterns
            if (lower.Contains("torch") || lower.Contains("lantern"))
            {
                return ("res://assets/models/dungeon/torch_lit.gltf.glb", 0.60f);
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
