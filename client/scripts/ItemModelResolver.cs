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

        var visual = CreateVisual(glyph, color);
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

    private static Node3D CreateVisual(char glyph, Color color)
    {
        string modelPath = glyph switch
        {
            '$' => "res://assets/models/dungeon/chest_gold.glb",
            '!' => "res://assets/models/dungeon/bottle_A_green.gltf.glb",
            '?' => "res://assets/models/characters/spellbook_closed.gltf",
            ')' => "res://assets/models/characters/sword_1handed.gltf",
            '[' or ']' or '(' => "res://assets/models/characters/shield_badge.gltf",
            '/' or '_' or '|' => "res://assets/models/characters/wand.gltf",
            ',' => "res://assets/models/dungeon/plate_food_A.gltf.glb",
            '"' or '=' => "res://assets/models/dungeon/key.gltf.glb",
            _ => null
        };

        if (modelPath != null)
        {
            var scene = GetModel(modelPath);
            if (scene != null)
            {
                var inst = scene.Instantiate<Node3D>();
                var sc = glyph switch
                {
                    '$' => 0.40f,
                    '!' => 0.60f,
                    '?' => 0.60f,
                    ')' => 0.65f,
                    '[' or ']' or '(' => 0.65f,
                    '/' or '_' or '|' => 0.60f,
                    ',' => 0.50f,
                    _ => 0.50f
                };
                inst.Scale = new Vector3(sc, sc, sc);
                return inst;
            }
        }

        // Procedural stylized 3D pickups fallback
        var mesh = GetPickupMesh(glyph);
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = 0.35f,
            Metallic = glyph is ')' or '[' or ']' or '(' or '=' ? 0.85f : 0.1f,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.65f,
        };

        return new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
        };
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
