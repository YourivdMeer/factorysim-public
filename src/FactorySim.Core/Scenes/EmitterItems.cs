namespace FactorySim.Core.Scenes;

/// <summary>
/// Which item an emitter puts on the line. A scene item can name one (<see cref="SceneItem.Emits"/>);
/// without a choice the catalogue decides: the two standard boxes for the gantry, a pallet for the
/// pallet emitter, raw material for the raw material emitters. The portal limits what fits: its size
/// is in the emitter's catalogue params as <c>maxLength</c> (along the line), <c>maxWidth</c> (across)
/// and <c>maxHeight</c>.
/// </summary>
public static class EmitterItems
{
    /// <summary>The two standard boxes are the default of the gantry, not something to choose.</summary>
    private static readonly string[] Standard = { "box_low", "box_high" };

    /// <summary>The spawn volume is this much larger than the item all round, in metres.</summary>
    public const double VolumeMargin = 0.1;

    /// <summary>True when the item goes through the portal of the emitter. Without limits in the catalogue everything does.</summary>
    public static bool Fits(CatalogAsset emitter, CatalogAsset item) =>
        item.Size[0] <= Limit(emitter, "maxLength") + 1e-6
        && item.Size[1] <= Limit(emitter, "maxHeight") + 1e-6
        && item.Size[2] <= Limit(emitter, "maxWidth") + 1e-6;

    /// <summary>The items an emitter can be set to, in catalogue order.</summary>
    public static IReadOnlyList<CatalogAsset> Choices(CatalogAsset emitter, AssetCatalog catalog) =>
        catalog.Assets
            .Where(asset => asset.Behaviour == AssetBehaviours.Item && !asset.Internal && !Standard.Contains(asset.Id) && Fits(emitter, asset))
            .ToArray();

    /// <summary>
    /// The item the emitter puts on the line: the scene item's choice, otherwise the catalogue's. Null means
    /// the two standard boxes, and also an item that the library does not have.
    /// </summary>
    public static CatalogAsset? Resolve(SceneItem item, CatalogAsset emitter, AssetCatalog catalog)
    {
        string? id = item.Emits ?? emitter.Emits;
        return id is null ? null : catalog.Find(id);
    }

    /// <summary>Length and width, in metres, of the volume that must be empty before the next item appears.</summary>
    public static (double Length, double Width) VolumeFor(CatalogAsset emitted) =>
        (emitted.Size[0] + VolumeMargin, emitted.Size[2] + VolumeMargin);

    /// <summary>
    /// Mass of what is emitted, in kilograms: what the user typed in for this emitter (<c>itemMass</c>), otherwise the
    /// mass of the item in the catalogue, otherwise what the emitter's own catalogue entry says.
    /// </summary>
    public static double MassFor(SceneItem item, CatalogAsset emitter, CatalogAsset emitted) =>
        item.Params is not null && item.Params.TryGetValue("itemMass", out double chosen)
            ? chosen
            : emitted.Param("mass", emitter.Param("itemMass", 2.0));

    private static double Limit(CatalogAsset emitter, string name) =>
        emitter.Params.TryGetValue(name, out double value) ? value : double.PositiveInfinity;
}
