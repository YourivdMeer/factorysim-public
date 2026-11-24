namespace FactorySim.Core.Scenes;

/// <summary>
/// Plan 17: the ends of straight belts and curves, and clicking one onto another while dragging.
/// <list type="bullet">
/// <item>A straight belt takes in at local (-length/2, 0) and puts out at (+length/2, 0), travelling +X; an inclined belt is
/// rise/2 lower at the in and higher at the out. <c>reverse</c> swaps in and out; <c>twoWay</c> gives each end both.</item>
/// <item>A curve takes in at local (radius, 0) travelling -Z and puts out at (0, -radius) travelling -X.</item>
/// </list>
/// Only an out clicks onto an in, with the travel in line, at the same height (± 1 cm), within 0.25 m, and onto an end
/// nothing is attached to yet. A curve may be turned in quarter turns to fit; a straight belt is never turned, so a belt
/// that runs the wrong way does not click.
/// </summary>
public static class ScenePorts
{
    /// <summary>How close the ends must be to click, in metres.</summary>
    public const double Reach = 0.25;

    /// <summary>Largest height difference between two ends that still click, in metres.</summary>
    public const double HeightTolerance = 0.01;

    /// <summary>Two ends closer than this are attached to each other.</summary>
    public const double SamePlace = 0.01;

    /// <summary>Only items with their origin this close to the dragged one are looked at (longest belt plus margin).</summary>
    public const double SearchRadius = 10.0;

    private const string NewItemId = "\u0000new";

    /// <summary>The ends of an item in world coordinates; empty for anything but a straight belt or a curve.</summary>
    public static IReadOnlyList<ScenePort> Ports(SceneItem item, CatalogAsset asset)
    {
        var local = new List<(double X, double Z, double Y, double DirX, double DirZ, bool IsIn)>();
        switch (asset.Behaviour)
        {
            case AssetBehaviours.Conveyor:
            {
                double half = asset.Param("length", 0.0) / 2.0;
                double rise = asset.Param("rise", 0.0);
                bool reverse = item.Param(SceneTags.Reverse, asset, 0.0) >= 0.5;
                bool twoWay = item.Param(SceneTags.TwoWay, asset, 0.0) >= 0.5;
                double low = item.Y - rise / 2.0;
                double high = item.Y + rise / 2.0;
                if (!reverse || twoWay)
                {
                    local.Add((-half, 0.0, low, 1.0, 0.0, true));
                    local.Add((half, 0.0, high, 1.0, 0.0, false));
                }

                if (reverse || twoWay)
                {
                    local.Add((half, 0.0, high, -1.0, 0.0, true));
                    local.Add((-half, 0.0, low, -1.0, 0.0, false));
                }

                break;
            }

            case AssetBehaviours.Curve:
            {
                double radius = asset.Param("radius", 0.0);
                local.Add((radius, 0.0, item.Y, 0.0, -1.0, true));
                local.Add((0.0, -radius, item.Y, -1.0, 0.0, false));
                break;
            }

            default:
                return Array.Empty<ScenePort>();
        }

        // A positive yaw turns local +X towards -Z (as everywhere in the scene).
        double yaw = item.Yaw * Math.PI / 180.0;
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        return local.Select(p => new ScenePort(
            item.Id,
            Clean(item.X + p.X * cos + p.Z * sin),
            p.Y,
            Clean(item.Z - p.X * sin + p.Z * cos),
            Clean(p.DirX * cos + p.DirZ * sin),
            Clean(-p.DirX * sin + p.DirZ * cos),
            p.IsIn)).ToList();
    }

    /// <summary>
    /// Where the item <paramref name="anchorId"/> clicks when dragged to (x, z), or null. The items in
    /// <paramref name="movingIds"/> (its group) move along and are not something to click onto.
    /// </summary>
    public static PortSnap? Snap(Scene scene, AssetCatalog catalog, IEnumerable<string> movingIds, string anchorId, double x, double z)
    {
        if (scene.Find(anchorId) is not { } anchor || catalog.Find(anchor.Asset) is not { } asset)
        {
            return null;
        }

        var moving = new HashSet<string>(movingIds, StringComparer.Ordinal) { anchorId };
        return SnapProbe(scene, catalog, anchor, asset, x, z, moving);
    }

    /// <summary>Where a new item from the library, turned to <paramref name="yaw"/> and held at (x, z), clicks; or null.</summary>
    public static PortSnap? SnapNew(Scene scene, AssetCatalog catalog, CatalogAsset asset, double yaw, double x, double z)
    {
        var probe = new SceneItem(NewItemId, asset.Id, string.Empty, x, asset.OriginHeight, z, yaw);
        return SnapProbe(scene, catalog, probe, asset, x, z, new HashSet<string>(StringComparer.Ordinal));
    }

    private static PortSnap? SnapProbe(Scene scene, AssetCatalog catalog, SceneItem item, CatalogAsset asset, double x, double z,
        HashSet<string> moving)
    {
        if (asset.Behaviour is not (AssetBehaviours.Conveyor or AssetBehaviours.Curve) || !OnQuarterTurn(item.Yaw))
        {
            return null;
        }

        var others = new List<ScenePort>();
        foreach (SceneItem other in scene.Items)
        {
            if (moving.Contains(other.Id) || catalog.Find(other.Asset) is not { } otherAsset
                || Math.Abs(other.X - x) > SearchRadius || Math.Abs(other.Z - z) > SearchRadius)
            {
                continue;
            }

            others.AddRange(Ports(other, otherAsset));
        }

        double[] turns = asset.Behaviour == AssetBehaviours.Curve ? new[] { 0.0, 90.0, 180.0, 270.0 } : new[] { 0.0 };
        PortSnap? best = null;
        double bestDistance = double.MaxValue;
        foreach (double turn in turns)
        {
            SceneItem probe = item with { X = x, Z = z, Yaw = Normalize(item.Yaw + turn) };
            foreach (ScenePort mine in Ports(probe, asset))
            {
                foreach (ScenePort theirs in others)
                {
                    double distance = Math.Sqrt((mine.X - theirs.X) * (mine.X - theirs.X) + (mine.Z - theirs.Z) * (mine.Z - theirs.Z));
                    if (theirs.IsIn == mine.IsIn
                        || Math.Abs(theirs.Y - mine.Y) > HeightTolerance
                        || mine.DirX * theirs.DirX + mine.DirZ * theirs.DirZ < 0.999
                        || distance > Reach + 1e-9
                        || distance >= bestDistance - 1e-9                        // the nearest wins; on a tie, the smaller turn
                        || !IsFree(theirs, others))
                    {
                        continue;
                    }

                    bestDistance = distance;
                    best = new PortSnap(x + theirs.X - mine.X, z + theirs.Z - mine.Z, probe.Yaw, turn, theirs.X, theirs.Y, theirs.Z, theirs.ItemId);
                }
            }
        }

        return best;
    }

    /// <summary>True when no other item has an end at the same place.</summary>
    private static bool IsFree(ScenePort port, List<ScenePort> all) =>
        !all.Any(other => other.ItemId != port.ItemId
            && Math.Abs(other.X - port.X) < SamePlace && Math.Abs(other.Z - port.Z) < SamePlace
            && Math.Abs(other.Y - port.Y) < HeightTolerance);

    private static bool OnQuarterTurn(double yaw)
    {
        double rest = Math.Abs(yaw % 90.0);
        return rest < 0.01 || rest > 89.99;
    }

    private static double Normalize(double yaw)
    {
        double value = yaw % 360.0;
        return value < 0.0 ? value + 360.0 : value;
    }

    private static double Clean(double value) => Math.Round(value, 9) + 0.0;
}
