using FactorySim.Core.Settings;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 30: the welcome window at the first start, and the short manual for testers.</summary>
public class WelcomeTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void A_first_start_without_a_settings_file_shows_the_welcome()
    {
        Assert.True(Welcome.ShowAtStart(settingsFileExists: false, new AppSettings()));
    }

    [Fact]
    public void An_existing_settings_file_without_the_key_does_not_show_it()
    {
        AppSettings read = AppSettings.FromPairs(new Dictionary<string, string> { ["plc.port"] = "5020" });

        Assert.False(read.ShowWelcome);
        Assert.False(Welcome.ShowAtStart(settingsFileExists: true, read));
    }

    [Fact]
    public void Closing_after_a_first_start_shows_it_again_next_time()
    {
        AppSettings after = Welcome.Shown(new AppSettings());
        AppSettings read = AppSettings.FromPairs(after.ToPairs());

        Assert.True(read.ShowWelcome);
        Assert.True(Welcome.ShowAtStart(settingsFileExists: true, read));
    }

    [Fact]
    public void Niet_meer_tonen_keeps_it_away()
    {
        AppSettings dismissed = Welcome.Dismissed(Welcome.Shown(new AppSettings()));
        AppSettings read = AppSettings.FromPairs(dismissed.ToPairs());

        Assert.False(read.ShowWelcome);
        Assert.False(Welcome.ShowAtStart(settingsFileExists: true, read));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("ja", false)]
    public void The_key_is_read_strictly(string value, bool expected)
    {
        AppSettings read = AppSettings.FromPairs(new Dictionary<string, string> { ["welcome.show"] = value });

        Assert.Equal(expected, read.ShowWelcome);
    }

    [Fact]
    public void The_steps_name_the_real_buttons()
    {
        string steps = string.Join("\n", Welcome.Steps);

        Assert.Equal(3, Welcome.Steps.Count);
        Assert.Contains("PLC: niet verbonden", steps);
        Assert.Contains("Ingebouwde test-PLC", steps);
        Assert.Contains("▶ Run", steps);
        Assert.Contains("Start", steps);
        Assert.Contains("Voorbeelden", steps);
    }

    [Fact]
    public void The_outro_points_to_the_help_menu()
    {
        // Plan 35: the buttons "?" and "Over" became the menu "Help ▾".
        Assert.Contains("Help ▾", Welcome.Outro);
        Assert.Contains("Aan de slag", Welcome.Outro);
        Assert.DoesNotContain("knop ?", Welcome.Outro);
    }

    [Fact]
    public void The_manual_points_to_the_help_menu()
    {
        string manual = File.ReadAllText(Path.Combine(RepoRoot, "HANDLEIDING.txt"));

        Assert.Contains("Help ▾", manual);
        Assert.DoesNotContain("\"?\"", manual);
    }

    [Fact]
    public void The_manual_is_there_and_covers_the_five_topics()
    {
        string manual = File.ReadAllText(Path.Combine(RepoRoot, "HANDLEIDING.txt"));

        Assert.Contains(AppInfo.Version, manual);
        foreach (string heading in new[] { "Starten", "Een scène bouwen", "Een PLC koppelen", "Waar je bestanden staan", "Bekende beperkingen" })
        {
            Assert.Contains(heading, manual);
        }

        Assert.Contains("Ingebouwde test-PLC", manual);
        Assert.Contains("Aan de slag", manual);
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "FactorySim.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("FactorySim.sln not found");
    }
}
