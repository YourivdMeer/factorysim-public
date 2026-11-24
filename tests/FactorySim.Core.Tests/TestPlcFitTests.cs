using FactorySim.Core.Plc.TestPlc;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests;

public class TestPlcFitTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static IReadOnlyList<TagMapEntry> TagsOf(string file) =>
        SceneTags.Build(Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file))), Catalog);

    [Fact]
    public void The_mvp_scene_fits_the_sorter()
    {
        TestPlcFit fit = TestPlcFit.Check(TagsOf("mvp_sorting.fsscene.json"));

        Assert.True(fit.Fits);
        Assert.Empty(fit.Problems);
    }

    [Fact]
    public void The_assembly_line_does_not_fit()
    {
        TestPlcFit fit = TestPlcFit.Check(TagsOf("assembly_line.fsscene.json"));

        Assert.False(fit.Fits);
        Assert.NotEmpty(fit.Problems);
        Assert.False(string.IsNullOrWhiteSpace(fit.Reason));
    }

    [Fact]
    public void A_renamed_tag_on_the_same_address_does_not_fit()
    {
        var tags = TagsOf("mvp_sorting.fsscene.json")
            .Select(t => t.Name == "High box" ? t with { Name = "Tall box" } : t)
            .ToArray();

        TestPlcFit fit = TestPlcFit.Check(tags);

        Assert.False(fit.Fits);
        Assert.Contains(fit.Problems, p => p.Contains("High box"));
    }

    [Fact]
    public void A_tag_on_another_address_does_not_fit()
    {
        var tags = TagsOf("mvp_sorting.fsscene.json")
            .Select(t => t.Name == "Conveyor" ? t with { Offset = 7 } : t)
            .ToArray();

        TestPlcFit fit = TestPlcFit.Check(tags);

        Assert.False(fit.Fits);
        Assert.Contains(fit.Problems, p => p.Contains("Conveyor") && p.Contains('7'));
    }

    [Fact]
    public void Extra_tags_do_not_matter()
    {
        var tags = TagsOf("mvp_sorting.fsscene.json")
            .Append(new TagMapEntry("Extra lamp", TagDirection.Actuator, ModbusArea.DiscreteInput, 4))
            .ToArray();

        Assert.True(TestPlcFit.Check(tags).Fits);
    }

    [Fact]
    public void Names_are_compared_without_case()
    {
        var tags = TagsOf("mvp_sorting.fsscene.json")
            .Select(t => t with { Name = t.Name.ToUpperInvariant() })
            .ToArray();

        Assert.True(TestPlcFit.Check(tags).Fits);
    }
}
