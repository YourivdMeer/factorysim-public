using System.Linq;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 32, part of the "ui" self-test: every palette item's thumbnail and every catalog model load as resources.
/// In an exported app these exist only as imported resources in the pck, so this catches files the
/// export left out (run the "ui" self-test on the exported FactorySim.exe).
/// </summary>
public partial class SelfTest
{
    private void RunUiResources()
    {
        CatalogAsset[] assets = Main.Catalog?.Assets.ToArray() ?? System.Array.Empty<CatalogAsset>();
        // Internal items (support beam, rail bracket, ...) are not in the palette and need no thumbnail.
        CatalogAsset[] inPalette = assets.Where(asset => !asset.Internal).ToArray();
        string[] noThumbnail = inPalette
            .Where(asset => !(ResourceLoader.Exists("res://" + asset.Thumbnail) && GD.Load<Texture2D>("res://" + asset.Thumbnail) is not null))
            .Select(asset => asset.Id).ToArray();
        string[] noModel = assets
            .Where(asset => !(ResourceLoader.Exists("res://" + asset.File) && GD.Load<PackedScene>("res://" + asset.File) is not null))
            .Select(asset => asset.Id).ToArray();
        GD.Print($"SELFTEST ui resources: assets={assets.Length} thumbnails={inPalette.Length - noThumbnail.Length}/{inPalette.Length} "
            + $"models={assets.Length - noModel.Length} missing=[{string.Join(", ", noThumbnail.Concat(noModel).Distinct())}]");
        SortersCheck("catalog thumbnails and models load", assets.Length >= 100 && noThumbnail.Length == 0 && noModel.Length == 0);
    }
}
