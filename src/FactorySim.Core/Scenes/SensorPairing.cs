namespace FactorySim.Core.Scenes;

/// <summary>A sensor of a pair and the part it works with, with the distance between them in metres.</summary>
public sealed record SensorLink(string SensorId, string TargetId, double Distance);

/// <summary>
/// Sensors that need a partner: a reflection sensor works against a reflector and the receiver of a
/// light array against its emitter. The partner is the nearest part of the right kind in the
/// sensor's line of sight (a few degrees either way), within the sensor's range. A sensor without a
/// partner sees nothing.
/// </summary>
public static class SensorPairing
{
    /// <summary>How far, in degrees, the partner may lie off the sensor's line of sight.</summary>
    public const double MaxAngleDegrees = 5.0;

    /// <summary>Closer than this, in metres, is not a pair: it is the same spot.</summary>
    private const double MinimumDistance = 0.05;

    /// <summary>The behaviour of the part a sensor with this behaviour works against; null when it needs none.</summary>
    public static string? TargetBehaviourOf(string? sensorBehaviour) => sensorBehaviour switch
    {
        AssetBehaviours.Retroreflective => AssetBehaviours.Reflector,
        AssetBehaviours.LightArray => AssetBehaviours.LightEmitter,
        _ => null,
    };

    /// <summary>
    /// The nearest of the targets in the sensor's line of sight within the range; null when there is none.
    /// The line of sight is local +Z turned by the yaw of the sensor, scene with the ground.
    /// </summary>
    public static SensorLink? Find(SceneItem sensor, double range, IEnumerable<SceneItem> targets)
    {
        double yaw = sensor.Yaw * Math.PI / 180.0;
        double lookX = Math.Sin(yaw);
        double lookZ = Math.Cos(yaw);

        SensorLink? best = null;
        foreach (SceneItem target in targets)
        {
            double dx = target.X - sensor.X;
            double dy = target.Y - sensor.Y;
            double dz = target.Z - sensor.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < MinimumDistance || distance > range)
            {
                continue;
            }

            double cosine = Math.Clamp((dx * lookX + dz * lookZ) / distance, -1.0, 1.0);
            if (Math.Acos(cosine) * 180.0 / Math.PI > MaxAngleDegrees)
            {
                continue;
            }

            if (best is null || distance < best.Distance)
            {
                best = new SensorLink(sensor.Id, target.Id, distance);
            }
        }

        return best;
    }

    /// <summary>The pairs of a scene, in the order of their sensors. Sensors without a partner are left out.</summary>
    public static IReadOnlyList<SensorLink> Links(Scene scene, AssetCatalog catalog)
    {
        var links = new List<SensorLink>();
        foreach ((SceneItem sensor, SensorLink? link) in Resolve(scene, catalog))
        {
            if (link is not null)
            {
                links.Add(link);
            }
        }

        return links;
    }

    /// <summary>The sensors that need a partner and have none.</summary>
    public static IReadOnlyList<SceneItem> Unpaired(Scene scene, AssetCatalog catalog) =>
        Resolve(scene, catalog).Where(pair => pair.Link is null).Select(pair => pair.Sensor).ToArray();

    private static IEnumerable<(SceneItem Sensor, SensorLink? Link)> Resolve(Scene scene, AssetCatalog catalog)
    {
        foreach (SceneItem item in scene.Items)
        {
            if (catalog.Find(item.Asset) is not { } asset || TargetBehaviourOf(asset.Behaviour) is not { } targetBehaviour)
            {
                continue;
            }

            IEnumerable<SceneItem> targets = scene.Items.Where(other => catalog.Find(other.Asset)?.Behaviour == targetBehaviour);
            yield return (item, Find(item, item.Param("range", asset, 0.0), targets));
        }
    }
}
