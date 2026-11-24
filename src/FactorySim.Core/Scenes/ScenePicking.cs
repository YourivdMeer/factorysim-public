namespace FactorySim.Core.Scenes;

/// <summary>Finds which item of a scene lies under a point on the floor.</summary>
public static class ScenePicking
{
    /// <summary>Extra room around a footprint, so thin parts such as sensors can be clicked.</summary>
    public const double Margin = 0.12;

    /// <summary>
    /// The item whose footprint contains the floor point (x, z). When footprints overlap,
    /// the smallest one wins, so a sensor standing next to a conveyor can still be picked.
    /// </summary>
    public static SceneItem? ItemAt(Scene scene, AssetCatalog catalog, double x, double z)
    {
        SceneItem? best = null;
        double bestArea = double.MaxValue;
        foreach (SceneItem item in scene.Items)
        {
            CatalogAsset? asset = catalog.Find(item.Asset);
            if (asset is null)
            {
                continue;
            }

            (double minX, double maxX, double minZ, double maxZ) = Footprint(item, asset);

            // Bring the point into the item's own frame (undo its position and yaw).
            double yaw = item.Yaw * Math.PI / 180.0;
            double dx = x - item.X;
            double dz = z - item.Z;
            double localX = dx * Math.Cos(yaw) - dz * Math.Sin(yaw);
            double localZ = dx * Math.Sin(yaw) + dz * Math.Cos(yaw);

            if (localX < minX - Margin || localX > maxX + Margin || localZ < minZ - Margin || localZ > maxZ + Margin)
            {
                continue;
            }

            double area = (maxX - minX + 2 * Margin) * (maxZ - minZ + 2 * Margin);
            if (area < bestArea)
            {
                best = item;
                bestArea = area;
            }
        }

        return best;
    }

    /// <summary>
    /// The items whose footprint, turned and placed on the floor, lies at least partly inside the frame
    /// from (minX, minZ) to (maxX, maxZ); in the order of the scene.
    /// </summary>
    public static IReadOnlyList<SceneItem> ItemsIn(Scene scene, AssetCatalog catalog, double minX, double minZ, double maxX, double maxZ)
    {
        var found = new List<SceneItem>();
        foreach (SceneItem item in scene.Items)
        {
            if (catalog.Find(item.Asset) is not { } asset)
            {
                continue;
            }

            (double fMinX, double fMaxX, double fMinZ, double fMaxZ) = Footprint(item, asset);
            double yaw = item.Yaw * Math.PI / 180.0;
            double cos = Math.Cos(yaw);
            double sin = Math.Sin(yaw);
            double low = double.MaxValue, high = double.MinValue, near = double.MaxValue, far = double.MinValue;
            foreach (double x in new[] { fMinX, fMaxX })
            {
                foreach (double z in new[] { fMinZ, fMaxZ })
                {
                    // A positive yaw turns local +X towards -Z.
                    double worldX = item.X + x * cos + z * sin;
                    double worldZ = item.Z - x * sin + z * cos;
                    low = Math.Min(low, worldX);
                    high = Math.Max(high, worldX);
                    near = Math.Min(near, worldZ);
                    far = Math.Max(far, worldZ);
                }
            }

            if (high > minX && low < maxX && far > minZ && near < maxZ)
            {
                found.Add(item);
            }
        }

        return found;
    }

    /// <summary>Extent of an item on the floor in its own frame: (minX, maxX, minZ, maxZ).</summary>
    public static (double MinX, double MaxX, double MinZ, double MaxZ) Footprint(SceneItem item, CatalogAsset asset)
    {
        double minX = asset.BoundsMin[0];
        double maxX = asset.BoundsMax[0];
        if (asset.Behaviour == AssetBehaviours.Rail)
        {
            // Rails are modelled 1 m long and stretched to the item's length.
            double length = item.Param("length", asset, 1.0);
            minX *= length;
            maxX *= length;
        }

        return (minX, maxX, asset.BoundsMin[2], asset.BoundsMax[2]);
    }
}
