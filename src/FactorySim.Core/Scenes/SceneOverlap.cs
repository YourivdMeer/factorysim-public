namespace FactorySim.Core.Scenes;

/// <summary>
/// Finds transport parts that stand through each other. Only conveyors, curves, sorters and
/// turntables are compared: sensors, emitters, removers, rails and stops are meant to stand
/// on or over a conveyor, and stations take conveyors inside their frame.
/// </summary>
public static class SceneOverlap
{
    /// <summary>Parts may share this much, in metres, before it counts: they are meant to touch end to end.</summary>
    public const double Tolerance = 0.15;

    public static IReadOnlyList<(SceneItem First, SceneItem Second)> Find(Scene scene, AssetCatalog catalog)
    {
        var boxes = new List<(SceneItem Item, double[] Min, double[] Max)>();
        foreach (SceneItem item in scene.Items)
        {
            if (catalog.Find(item.Asset) is { } asset && IsTransport(asset))
            {
                (double[] min, double[] max) = Bounds(item, asset);
                boxes.Add((item, min, max));
            }
        }

        var pairs = new List<(SceneItem, SceneItem)>();
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (Enumerable.Range(0, 3).All(axis =>
                        Math.Min(boxes[i].Max[axis], boxes[j].Max[axis]) - Math.Max(boxes[i].Min[axis], boxes[j].Min[axis]) > Tolerance))
                {
                    pairs.Add((boxes[i].Item, boxes[j].Item));
                }
            }
        }

        return pairs;
    }

    /// <summary>The items that stand through another one: what the editor marks. Each item once, in the order of the scene.</summary>
    public static IReadOnlyList<SceneItem> Involved(Scene scene, AssetCatalog catalog)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach ((SceneItem first, SceneItem second) in Find(scene, catalog))
        {
            ids.Add(first.Id);
            ids.Add(second.Id);
        }

        return scene.Items.Where(item => ids.Contains(item.Id)).ToArray();
    }

    private static bool IsTransport(CatalogAsset asset) => asset.Behaviour
        is AssetBehaviours.Conveyor or AssetBehaviours.Curve or AssetBehaviours.WheelSorter or AssetBehaviours.Turntable;

    /// <summary>Axis-aligned box around the item in scene space (x, y, z), after its yaw.</summary>
    private static (double[] Min, double[] Max) Bounds(SceneItem item, CatalogAsset asset)
    {
        double yaw = item.Yaw * Math.PI / 180.0;
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (double x in new[] { asset.BoundsMin[0], asset.BoundsMax[0] })
        {
            foreach (double z in new[] { asset.BoundsMin[2], asset.BoundsMax[2] })
            {
                // A positive yaw turns local +X towards -Z.
                double worldX = item.X + x * cos + z * sin;
                double worldZ = item.Z - x * sin + z * cos;
                minX = Math.Min(minX, worldX);
                maxX = Math.Max(maxX, worldX);
                minZ = Math.Min(minZ, worldZ);
                maxZ = Math.Max(maxZ, worldZ);
            }
        }

        return (new[] { minX, item.Y + asset.BoundsMin[1], minZ }, new[] { maxX, item.Y + asset.BoundsMax[1], maxZ });
    }
}
