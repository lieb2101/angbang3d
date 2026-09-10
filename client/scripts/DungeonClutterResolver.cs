using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Clutter resolver - disabled to ensure only authentic game-relevant items and features
/// are rendered without distracting visual noise or non-game props.
/// </summary>
public static class DungeonClutterResolver
{
    /// <summary>
    /// Cleans up any existing clutter nodes so only authentic game items appear.
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
        if (clutterRoot != null && clutterRoot.GetChildCount() > 0)
        {
            foreach (var child in clutterRoot.GetChildren())
            {
                child.QueueFree();
            }
        }
    }
}
