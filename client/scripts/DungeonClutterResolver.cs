using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Resolves and procedurally places authentic CC0 3D dungeon clutter, wall torches,
/// sconces with dynamic point lights, and environmental dressing based on dungeon topology.
/// </summary>
public static class DungeonClutterResolver
{
    private static readonly Dictionary<string, PackedScene> _modelCache = new();

    private static readonly string[] ClutterModels =
    {
        "res://assets/models/props/Barrel.gltf",
        "res://assets/models/props/Barrel_Apples.gltf",
        "res://assets/models/props/Crate_Wooden.gltf",
        "res://assets/models/props/Crate_Metal.gltf",
        "res://assets/models/props/Chest_Wood.gltf",
        "res://assets/models/props/Cauldron.gltf",
        "res://assets/models/props/Bookcase_2.gltf",
        "res://assets/models/props/Anvil.gltf",
        "res://assets/models/props/WeaponStand.gltf",
        "res://assets/models/props/Table_Large.gltf",
        "res://assets/models/props/Chair_1.gltf",
        "res://assets/models/props/Bench.gltf",
        "res://assets/models/props/Stool.gltf",
        "res://assets/models/props/skull_candle.gltf",
        "res://assets/models/props/ribcage.gltf",
        "res://assets/models/props/Vase_Rubble_Medium.gltf"
    };

    private static readonly string[] TownProps =
    {
        "res://assets/models/props/Stall_Empty.gltf",
        "res://assets/models/props/Stall_Cart_Empty.gltf",
        "res://assets/models/props/Barrel.gltf",
        "res://assets/models/props/Crate_Wooden.gltf",
        "res://assets/models/props/bench.gltf",
        "res://assets/models/props/post_lantern.gltf",
        "res://assets/models/props/fence.gltf",
        "res://assets/models/props/tree_pine_orange_large.gltf"
    };

    private static PackedScene GetModel(string path)
    {
        if (_modelCache.TryGetValue(path, out var scene))
        {
            return scene;
        }

        if (ResourceLoader.Exists(path))
        {
            scene = GD.Load<PackedScene>(path);
            _modelCache[path] = scene;
            return scene;
        }

        return null;
    }

    /// <summary>
    /// Populates deterministic wall torches, sconces with point lights, and environmental clutter.
    /// </summary>
    public static void PopulateClutter(
        Node3D clutterRoot,
        JsonElement map,
        int h,
        int w,
        int px,
        int py,
        bool outdoors,
        Func<JsonElement, int, int, int, int, bool> isWallOrVoid,
        Func<int, bool> isWalkable,
        Func<JsonElement, int, int, int> featAt)
    {
        if (clutterRoot == null) return;

        // Clear previous clutter nodes
        foreach (var child in clutterRoot.GetChildren())
        {
            child.QueueFree();
        }

        if (map.ValueKind != JsonValueKind.Object || h <= 0 || w <= 0) return;

        if (outdoors)
        {
            PopulateTownDressing(clutterRoot, map, h, w, px, py, isWallOrVoid, isWalkable);
        }
        else
        {
            PopulateDungeonDressing(clutterRoot, map, h, w, px, py, isWallOrVoid, isWalkable, featAt);
        }
    }

    private static void PopulateDungeonDressing(
        Node3D clutterRoot,
        JsonElement map,
        int h,
        int w,
        int px,
        int py,
        Func<JsonElement, int, int, int, int, bool> isWallOrVoid,
        Func<int, bool> isWalkable,
        Func<JsonElement, int, int, int> featAt)
    {
        var torchScene = GetModel("res://assets/models/props/Torch_Metal.gltf") 
                         ?? GetModel("res://assets/models/props/Torch_Metal.fbx");
        var bannerScene = GetModel("res://assets/models/props/Banner_1.gltf")
                          ?? GetModel("res://assets/models/props/Banner_2.gltf");

        const float cell = DungeonWorld.Cell;
        int torchInterval = 0;

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                // Skip player cell
                if (x == px && y == py) continue;

                int feat = featAt(map, x, y);
                bool walkable = isWalkable(feat);

                if (!walkable) continue;

                // Check neighboring walls for wall sconces / torches
                bool nWall = isWallOrVoid(map, x, y - 1, w, h);
                bool sWall = isWallOrVoid(map, x, y + 1, w, h);
                bool wWall = isWallOrVoid(map, x - 1, y, w, h);
                bool eWall = isWallOrVoid(map, x + 1, y, w, h);

                int wallCount = (nWall ? 1 : 0) + (sWall ? 1 : 0) + (wWall ? 1 : 0) + (eWall ? 1 : 0);

                uint cellHash = Hash(x, y, 101);

                // 1. Wall Torches with Dynamic Point Lights (placed every 6-8 tiles along walls)
                if (wallCount >= 1 && torchScene != null)
                {
                    torchInterval++;
                    if (torchInterval % 7 == 0 || (cellHash % 100 < 8 && wallCount == 1))
                    {
                        var torchNode = new Node3D();
                        var inst = torchScene.Instantiate<Node3D>();
                        inst.Scale = new Vector3(0.55f, 0.55f, 0.55f);
                        torchNode.AddChild(inst);

                        // Position and orientation towards room/corridor
                        Vector3 torchPos = new Vector3(x * cell, 1.4f, y * cell);
                        float yaw = 0f;
                        if (nWall) { torchPos.Z -= 0.85f; yaw = 0f; }
                        else if (sWall) { torchPos.Z += 0.85f; yaw = Mathf.Pi; }
                        else if (wWall) { torchPos.X -= 0.85f; yaw = Mathf.Pi * 0.5f; }
                        else if (eWall) { torchPos.X += 0.85f; yaw = -Mathf.Pi * 0.5f; }

                        torchNode.Position = torchPos;
                        torchNode.Rotation = new Vector3(0, yaw, 0);

                        // Warm point light attached to torch
                        var light = new OmniLight3D
                        {
                            LightColor = new Color(1.0f, 0.78f, 0.45f),
                            LightEnergy = 1.35f,
                            OmniRange = 5.2f,
                            OmniAttenuation = 1.25f,
                            ShadowEnabled = false,
                            Position = new Vector3(0, 0.15f, 0.1f)
                        };
                        torchNode.AddChild(light);
                        clutterRoot.AddChild(torchNode);
                    }
                }

                // 2. Wall Banners on long flat walls
                if (wallCount == 1 && bannerScene != null && cellHash % 100 is >= 10 and < 14)
                {
                    var bannerInst = bannerScene.Instantiate<Node3D>();
                    bannerInst.Scale = new Vector3(0.6f, 0.6f, 0.6f);
                    Vector3 bPos = new Vector3(x * cell, 1.5f, y * cell);
                    float yaw = 0f;
                    if (nWall) { bPos.Z -= 0.90f; yaw = 0f; }
                    else if (sWall) { bPos.Z += 0.90f; yaw = Mathf.Pi; }
                    else if (wWall) { bPos.X -= 0.90f; yaw = Mathf.Pi * 0.5f; }
                    else if (eWall) { bPos.X += 0.90f; yaw = -Mathf.Pi * 0.5f; }

                    bannerInst.Position = bPos;
                    bannerInst.Rotation = new Vector3(0, yaw, 0);
                    clutterRoot.AddChild(bannerInst);
                }

                // 3. Alcove & Corner Props (Barrels, Crates, Chests, Anvils, Tables, Bones)
                if (wallCount >= 2 && cellHash % 100 < 22)
                {
                    int propIdx = (int)(cellHash % (uint)ClutterModels.Length);
                    var propPath = ClutterModels[propIdx];
                    var propScene = GetModel(propPath);
                    if (propScene != null)
                    {
                        var propInst = propScene.Instantiate<Node3D>();
                        float scale = 0.55f;
                        if (propPath.Contains("skull") || propPath.Contains("ribcage")) scale = 0.45f;
                        if (propPath.Contains("Table") || propPath.Contains("Bookcase")) scale = 0.50f;

                        propInst.Scale = new Vector3(scale, scale, scale);

                        // Offset slightly toward corner
                        float offX = (wWall ? -0.45f : (eWall ? 0.45f : 0f));
                        float offZ = (nWall ? -0.45f : (sWall ? 0.45f : 0f));
                        propInst.Position = new Vector3(x * cell + offX, 0.0f, y * cell + offZ);
                        propInst.Rotation = new Vector3(0, (float)(cellHash % 8) * Mathf.Pi * 0.25f, 0);

                        clutterRoot.AddChild(propInst);
                    }
                }
            }
        }
    }

    private static void PopulateTownDressing(
        Node3D clutterRoot,
        JsonElement map,
        int h,
        int w,
        int px,
        int py,
        Func<JsonElement, int, int, int, int, bool> isWallOrVoid,
        Func<int, bool> isWalkable)
    {
        const float cell = DungeonWorld.Cell;
        for (int y = 2; y < h - 2; y += 2)
        {
            for (int x = 2; x < w - 2; x += 2)
            {
                if (x == px && y == py) continue;

                uint cellHash = Hash(x, y, 77);
                if (cellHash % 100 < 12)
                {
                    int propIdx = (int)(cellHash % (uint)TownProps.Length);
                    var propPath = TownProps[propIdx];
                    var propScene = GetModel(propPath);
                    if (propScene != null)
                    {
                        var propInst = propScene.Instantiate<Node3D>();
                        float scale = propPath.Contains("tree") ? 0.8f : 0.6f;
                        propInst.Scale = new Vector3(scale, scale, scale);
                        propInst.Position = new Vector3(x * cell, 0.0f, y * cell);
                        propInst.Rotation = new Vector3(0, (float)(cellHash % 4) * Mathf.Pi * 0.5f, 0);
                        clutterRoot.AddChild(propInst);
                    }
                }
            }
        }
    }

    private static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695040888963407L);
        h = (h ^ (h >> 13)) * 1274126177;
        return h ^ (h >> 16);
    }
}
