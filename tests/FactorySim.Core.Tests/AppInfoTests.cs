using System.Text.RegularExpressions;
using FactorySim.Core.Api;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 28: one version number, in Core, in project.godot, in the title bar and in the tester changelog.</summary>
public class AppInfoTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void The_version_is_a_semantic_version()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$"), AppInfo.Version);
        Assert.Equal("0.1.0-beta.1", AppInfo.Version);
    }

    [Fact]
    public void Project_godot_carries_the_same_version()
    {
        string project = File.ReadAllText(Path.Combine(RepoRoot, "src", "FactorySim.App", "project.godot"));

        Match line = Regex.Match(project, "^config/version=\"([^\"]*)\"", RegexOptions.Multiline);
        Assert.True(line.Success, "config/version missing in project.godot");
        Assert.Equal(AppInfo.Version, line.Groups[1].Value);
    }

    [Fact]
    public void The_tester_changelog_starts_with_this_version()
    {
        string changelog = File.ReadAllText(Path.Combine(RepoRoot, "WIJZIGINGEN.txt"));

        Match first = Regex.Match(changelog, @"^(\d+\.\d+\.\d+\S*)", RegexOptions.Multiline);
        Assert.True(first.Success, "no version heading in WIJZIGINGEN.txt");
        Assert.Equal(AppInfo.Version, first.Groups[1].Value);
    }

    [Fact]
    public void The_window_title_names_the_app_the_version_and_the_scene()
    {
        Assert.Equal("FactorySim 0.1.0-beta.1 — MVP sorteren", AppInfo.WindowTitle("MVP sorteren"));
        Assert.Equal("FactorySim 0.1.0-beta.1", AppInfo.WindowTitle("  "));
        Assert.Equal("FactorySim 0.1.0-beta.1", AppInfo.WindowTitle(null));
    }

    [Fact]
    public void The_about_texts_say_that_nothing_goes_to_the_internet_and_carry_the_trademark_sentence()
    {
        Assert.Contains("internet", AppInfo.NetworkNote);
        Assert.Contains("127.0.0.1", AppInfo.NetworkNote);
        Assert.Equal("CODESYS is een merk van CODESYS GmbH; FactorySim heeft geen band met CODESYS GmbH.", AppInfo.TrademarkNote);
        Assert.Contains("THIRD-PARTY-NOTICES.txt", AppInfo.LicenceNote);
    }

    [Fact]
    public void The_test_api_state_carries_the_version()
    {
        var state = new ApiState("stopped", "mvp", 0.0, 1.0);

        Assert.Equal(AppInfo.Version, state.Version);
        Assert.Contains("\"version\":\"0.1.0-beta.1\"", ApiResponse.Ok(state).Json);
        Assert.DoesNotContain("\"level\"", ApiResponse.Ok(state).Json);     // plan 33: the old name of "scene" is gone
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
