using System;
using System.Text.Json;
using Godot;

namespace Angband3D;

/// <summary>
/// Clutter and non-item object resolver.
/// Procedural decorative clutter and fake non-item props are disabled to keep the dungeon
/// and town uncluttered and clear of confusing non-interactable objects.
/// </summary>
public static class DungeonClutterResolver
{
    /// <summary>
    /// Clears any residual clutter nodes. Procedural clutter generation is disabled.
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

        // Clear any previous clutter nodes
        foreach (var child in clutterRoot.GetChildren())
        {
            child.QueueFree();
        }
    }
}

