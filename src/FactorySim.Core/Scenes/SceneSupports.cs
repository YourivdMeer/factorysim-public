namespace FactorySim.Core.Scenes;

/// <summary>
/// One beam of a support frame, in the item's own frame (x along travel, y up, z across).
/// It starts at (X, Y, Z) and runs <see cref="Length"/> metres along <see cref="Axis"/>.
/// </summary>
public sealed record SupportBeam(double X, double Y, double Z, double Length, char Axis);

/// <summary>
/// The frame that carries a conveyor, emitter or remover that was placed higher than its
/// model was made for. The legs of the model end above the floor then; the frame fills the
/// gap: four posts from the floor and a ring of beams for the legs to stand on.
/// </summary>
public static class SceneSupports
{
    /// <summary>Side of the square beams, in metres. Must match the support_beam model.</summary>
    public const double BeamSize = 0.06;

    /// <summary>Below this gap, in metres, no frame is drawn.</summary>
    public const double MinimumGap = 0.02;

    public static IReadOnlyList<SupportBeam> For(SceneItem item, CatalogAsset asset)
    {
        if (asset.Behaviour is not (AssetBehaviours.Conveyor or AssetBehaviours.Curve or AssetBehaviours.WheelSorter
            or AssetBehaviours.Turntable or AssetBehaviours.Emitter or AssetBehaviours.Remover))
        {
            return Array.Empty<SupportBeam>();
        }

        // Height of the underside of the model above the floor.
        double gap = item.Y + asset.BoundsMin[1];
        if (gap < MinimumGap)
        {
            return Array.Empty<SupportBeam>();
        }

        double half = BeamSize / 2.0;
        double minX = asset.BoundsMin[0] + half;
        double maxX = asset.BoundsMax[0] - half;
        double minZ = asset.BoundsMin[2] + half;
        double maxZ = asset.BoundsMax[2] - half;
        double floor = -item.Y;
        double top = asset.BoundsMin[1];            // where the model's own legs end
        double ring = top - half;                   // centre line of the ring, just under the legs

        var beams = new List<SupportBeam>();
        double postLength = Math.Max(0.0, gap - BeamSize);
        if (postLength > 0.0)
        {
            foreach (double x in new[] { minX, maxX })
            {
                foreach (double z in new[] { minZ, maxZ })
                {
                    beams.Add(new SupportBeam(x, floor, z, postLength, 'Y'));
                }
            }
        }

        foreach (double z in new[] { minZ, maxZ })
        {
            beams.Add(new SupportBeam(minX - half, ring, z, maxX - minX + BeamSize, 'X'));
        }

        foreach (double x in new[] { minX, maxX })
        {
            beams.Add(new SupportBeam(x, ring, minZ + half, maxZ - minZ - BeamSize, 'Z'));
        }

        return beams;
    }
}
