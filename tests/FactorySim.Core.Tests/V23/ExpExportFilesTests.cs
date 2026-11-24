using System.Text;
using FactorySim.Core.V23;

namespace FactorySim.Core.Tests.V23;

/// <summary>
/// The B0 acceptance test: the container parser splits the synthetic test exports and
/// the real exports into objects, with no unknown block left unnamed. The real exports live in
/// <c>testdata/v23-exp/echt/</c>, which is not in git; without them those tests pass without checking
/// anything (category <c>RealExports</c>).
/// </summary>
public class ExpExportFilesTests
{
    private static readonly string TestData = Path.Combine(FindRepoRoot(), "testdata", "v23-exp");

    private static readonly string RealExports = Path.Combine(TestData, "echt");

    private static string ReadExport(string path) => File.ReadAllText(path, Encoding.Latin1);

    /// <summary>Every non-blank line lies in exactly one object, and every unknown object has a warning.</summary>
    private static void AssertFullySplit(string text, ExpContainer container)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int line = 1; line <= lines.Length; line++)
        {
            int owners = container.Objects.Count(o => o.FirstLine <= line && line <= o.LastLine);
            if (lines[line - 1].Trim().Length > 0)
            {
                Assert.True(owners == 1, $"line {line} lies in {owners} objects: {lines[line - 1]}");
            }
        }

        foreach (ExpObject unknown in container.Objects.Where(o => o.Kind == ExpObjectKind.Unknown))
        {
            Assert.Contains(container.Warnings, w => w.Line == unknown.FirstLine);
        }
    }

    [Theory]
    [InlineData("01_st_basis.exp", new[] { ExpObjectKind.Program, ExpObjectKind.GlobalVariableList })]
    [InlineData("02_timers_tellers.exp", new[] { ExpObjectKind.Program, ExpObjectKind.GlobalVariableList, ExpObjectKind.Library })]
    [InlineData("03_fb_types.exp", new[]
    {
        ExpObjectKind.Type, ExpObjectKind.Type, ExpObjectKind.Function, ExpObjectKind.FunctionBlock,
        ExpObjectKind.Program, ExpObjectKind.GlobalVariableList,
    })]
    [InlineData("04_sorteerscene.exp", new[]
    {
        ExpObjectKind.Program, ExpObjectKind.GlobalVariableList, ExpObjectKind.VariableConfiguration,
        ExpObjectKind.Library, ExpObjectKind.PlcConfiguration,
    })]
    public void A_synthetic_export_splits_into_its_objects_without_warnings(string file, ExpObjectKind[] kinds)
    {
        string text = ReadExport(Path.Combine(TestData, file));

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal(kinds, container.Objects.Select(o => o.Kind));
        Assert.Empty(container.Warnings);
        AssertFullySplit(text, container);
    }

    [Fact]
    public void The_synthetic_export_with_an_unknown_block_names_it()
    {
        string text = ReadExport(Path.Combine(TestData, "05_niet_ondersteund.exp"));

        ExpContainer container = ExpContainer.Parse(text);

        Assert.Equal(
            new[] { "FB_Ongebruikt", "PLC_PRG", "Globale_Variablen", "_ONBEKEND_BLOK", "VendorIO.lib" },
            container.Objects.Select(o => o.Name));
        Assert.Equal("/Niet_bereikbaar", container.Objects[0].Path);
        ExpWarning warning = Assert.Single(container.Warnings);
        Assert.Equal(76, warning.Line);
        AssertFullySplit(text, container);
    }

    public static IEnumerable<object?[]> RealExportFiles()
    {
        string[] files = Directory.Exists(RealExports)
            ? Directory.GetFiles(RealExports, "*.exp", SearchOption.AllDirectories)
            : Array.Empty<string>();
        if (files.Length == 0)
        {
            yield return new object?[] { null };       // a theory needs at least one row
            yield break;
        }

        foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return new object?[] { Path.GetRelativePath(RealExports, file) };
        }
    }

    [Theory]
    [Trait("Category", "RealExports")]
    [MemberData(nameof(RealExportFiles))]
    public void A_real_export_splits_completely_and_names_every_unknown_block(string? file)
    {
        if (file is null)
        {
            return;                                     // no real exports on this machine
        }

        string text = ReadExport(Path.Combine(RealExports, file));

        AssertFullySplit(text, ExpContainer.Parse(text));
    }

    [Theory]
    [Trait("Category", "RealExports")]
    [InlineData("ST", "Deur_ST", ExpBodyKind.Text)]
    [InlineData("LD", "A_Deur_LD", ExpBodyKind.Ld)]
    [InlineData("FBD", "A_Deur_FBD", ExpBodyKind.Fbd)]
    [InlineData("SFC", "Deur_SFC", ExpBodyKind.Sfc)]
    public void The_door_project_has_the_expected_objects_and_body_kind(string language, string program, ExpBodyKind bodyKind)
    {
        if (!Directory.Exists(RealExports))
        {
            return;                                     // no real exports on this machine
        }

        // Found anywhere under echt/, so moving the folders does not silently skip the test.
        string[] found = Directory.GetFiles(RealExports, language + ".EXP", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        });
        string path = Assert.Single(found);

        ExpContainer container = ExpContainer.Parse(ReadExport(path));

        Assert.Empty(container.Warnings);
        Assert.Equal(
            new[] { ExpObjectKind.Program, ExpObjectKind.Program, ExpObjectKind.GlobalVariableList, ExpObjectKind.VariableConfiguration, ExpObjectKind.AlarmConfiguration }
                .Concat(Enumerable.Repeat(ExpObjectKind.Library, 6))
                .Concat(new[] { ExpObjectKind.PlcConfiguration, ExpObjectKind.Resource, ExpObjectKind.Workspace }),
            container.Objects.Select(o => o.Kind));
        Assert.Equal(new[] { program, "PLC_PRG" }, container.Objects.Take(2).Select(o => o.Name));
        Assert.Equal(bodyKind, container.Objects[0].BodyKind);
        Assert.Equal(ExpBodyKind.Text, container.Objects[1].BodyKind);
        Assert.Equal(
            new[] { "IecSfc.lib", "Standard.lib", "SYSLIBCALLBACK.LIB", "SysLibTime.lib", "SysTaskInfo.lib", "Util.lib" },
            container.Objects.Where(o => o.Kind == ExpObjectKind.Library).Select(o => o.Name));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FactorySim.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("FactorySim.sln not found above the test directory.");
    }
}
