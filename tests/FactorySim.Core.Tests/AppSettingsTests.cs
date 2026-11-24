using System.Globalization;
using FactorySim.Core.Settings;

namespace FactorySim.Core.Tests;

/// <summary>Plan 15: the settings the app remembers between sessions.</summary>
public class AppSettingsTests
{
    private static AppSettings Full() => new()
    {
        ScenePath = "user://scenes/mijn_lijn.fsscene.json",
        Host = "192.168.0.10",
        Port = 5020,
        UnitId = 1,
        CameraScene = "user://scenes/mijn_lijn.fsscene.json",
        Camera = new CameraState(1.5, 0.8, -2.25, 12.5, -90.0, -45.5),
        BottomCollapsed = true,
        BottomTab = 2,
        Window = new WindowState(100, 50, 1600, 900, Maximized: true),
        Recent = new[] { "user://scenes/mijn_lijn.fsscene.json", "C:/export/oud.fsscene.json" },
    };

    private static void AssertSame(AppSettings expected, AppSettings actual)
    {
        Assert.Equal(expected.ScenePath, actual.ScenePath);
        Assert.Equal(expected.Host, actual.Host);
        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.UnitId, actual.UnitId);
        Assert.Equal(expected.CameraScene, actual.CameraScene);
        Assert.Equal(expected.Camera, actual.Camera);
        Assert.Equal(expected.BottomCollapsed, actual.BottomCollapsed);
        Assert.Equal(expected.BottomTab, actual.BottomTab);
        Assert.Equal(expected.Window, actual.Window);
        Assert.Equal(expected.Recent, actual.Recent);
    }

    [Fact]
    public void Defaults_are_the_start_of_the_app_today()
    {
        var settings = new AppSettings();

        Assert.Null(settings.ScenePath);
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal(502, settings.Port);
        Assert.Equal(255, settings.UnitId);
        Assert.Null(settings.Camera);
        Assert.False(settings.BottomCollapsed);
        Assert.Equal(0, settings.BottomTab);
        Assert.Null(settings.Window);
        Assert.Empty(settings.Recent);
    }

    [Fact]
    public void Round_trip_keeps_everything()
    {
        AppSettings settings = Full();

        AssertSame(settings, AppSettings.FromPairs(settings.ToPairs()));
    }

    [Fact]
    public void Round_trip_uses_a_point_whatever_the_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            AppSettings settings = Full();

            IReadOnlyDictionary<string, string> pairs = settings.ToPairs();

            Assert.Equal("12.5", pairs["camera.distance"]);
            AssertSame(settings, AppSettings.FromPairs(pairs));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Empty_gives_the_defaults()
    {
        AssertSame(new AppSettings(), AppSettings.FromPairs(new Dictionary<string, string>()));
    }

    [Fact]
    public void Defaults_write_no_optional_keys()
    {
        IReadOnlyDictionary<string, string> pairs = new AppSettings().ToPairs();

        Assert.DoesNotContain("level", pairs.Keys);
        Assert.DoesNotContain("camera.distance", pairs.Keys);
        Assert.DoesNotContain("window.width", pairs.Keys);
        Assert.DoesNotContain(pairs.Keys, key => key.StartsWith("recent.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void A_port_out_of_range_falls_back_to_502_and_the_rest_stays(string port)
    {
        var pairs = new Dictionary<string, string>(Full().ToPairs()) { ["plc.port"] = port };

        AppSettings settings = AppSettings.FromPairs(pairs);

        Assert.Equal(502, settings.Port);
        Assert.Equal("192.168.0.10", settings.Host);
        Assert.Equal(1, settings.UnitId);
        Assert.Equal(Full().Camera, settings.Camera);
    }

    [Theory]
    [InlineData("plc.unit", "256")]
    [InlineData("plc.unit", "x")]
    [InlineData("plc.host", "   ")]
    [InlineData("panel.tab", "4")]
    [InlineData("panel.tab", "-1")]
    [InlineData("panel.collapsed", "misschien")]
    public void Each_broken_key_falls_back_on_its_own(string key, string value)
    {
        var pairs = new Dictionary<string, string>(Full().ToPairs()) { [key] = value };

        AppSettings settings = AppSettings.FromPairs(pairs);
        AppSettings defaults = new();

        switch (key)
        {
            case "plc.unit":
                Assert.Equal(defaults.UnitId, settings.UnitId);
                break;
            case "plc.host":
                Assert.Equal(defaults.Host, settings.Host);
                break;
            case "panel.tab":
                Assert.Equal(defaults.BottomTab, settings.BottomTab);
                break;
            default:
                Assert.Equal(defaults.BottomCollapsed, settings.BottomCollapsed);
                break;
        }

        Assert.Equal(5020, settings.Port);
        Assert.Equal(Full().ScenePath, settings.ScenePath);
    }

    [Theory]
    [InlineData("camera.distance", "0.5")]
    [InlineData("camera.distance", "41")]
    [InlineData("camera.distance", "NaN")]
    [InlineData("camera.pitch", "10")]
    [InlineData("camera.x", "twee")]
    public void A_broken_camera_is_dropped_as_a_whole(string key, string value)
    {
        var pairs = new Dictionary<string, string>(Full().ToPairs()) { [key] = value };

        AppSettings settings = AppSettings.FromPairs(pairs);

        Assert.Null(settings.Camera);
        Assert.Equal(5020, settings.Port);
    }

    [Fact]
    public void A_window_too_small_is_dropped()
    {
        var pairs = new Dictionary<string, string>(Full().ToPairs()) { ["window.width"] = "10" };

        Assert.Null(AppSettings.FromPairs(pairs).Window);
    }

    [Fact]
    public void An_unknown_key_is_ignored()
    {
        var pairs = new Dictionary<string, string>(Full().ToPairs()) { ["toekomst.iets"] = "42" };

        AssertSame(Full(), AppSettings.FromPairs(pairs));
    }

    [Fact]
    public void Recent_puts_a_new_path_in_front()
    {
        AppSettings settings = Full().WithRecent("user://scenes/nieuw.fsscene.json");

        Assert.Equal(
            new[] { "user://scenes/nieuw.fsscene.json", "user://scenes/mijn_lijn.fsscene.json", "C:/export/oud.fsscene.json" },
            settings.Recent);
    }

    [Fact]
    public void Recent_moves_a_double_to_the_front_without_a_copy()
    {
        AppSettings settings = Full().WithRecent("c:/EXPORT/oud.fsscene.json");

        Assert.Equal(new[] { "c:/EXPORT/oud.fsscene.json", "user://scenes/mijn_lijn.fsscene.json" }, settings.Recent);
    }

    [Fact]
    public void Recent_is_cut_at_ten()
    {
        AppSettings settings = new();
        for (int i = 0; i < 12; i++)
        {
            settings = settings.WithRecent($"user://scenes/scene{i}.fsscene.json");
        }

        Assert.Equal(AppSettings.MaxRecent, settings.Recent.Count);
        Assert.Equal("user://scenes/scene11.fsscene.json", settings.Recent[0]);
        Assert.Equal("user://scenes/scene2.fsscene.json", settings.Recent[^1]);
    }

    [Fact]
    public void Recent_read_from_a_file_drops_doubles_and_blanks_and_is_cut_at_ten()
    {
        var pairs = new Dictionary<string, string> { ["recent.0"] = "a", ["recent.1"] = "A", ["recent.2"] = " ", ["recent.3"] = "b" };
        for (int i = 4; i < 15; i++)
        {
            pairs[$"recent.{i}"] = $"p{i}";
        }

        AppSettings settings = AppSettings.FromPairs(pairs);

        Assert.Equal(AppSettings.MaxRecent, settings.Recent.Count);
        Assert.Equal(new[] { "a", "b", "p4" }, settings.Recent.Take(3));
    }

    [Fact]
    public void Same_as_compares_by_content()
    {
        Assert.True(Full().SameAs(Full()));
        Assert.False(Full().SameAs(Full() with { Port = 502 }));
        Assert.False(Full().SameAs(Full().WithRecent("x")));
    }

    [Fact]
    public void Settings_of_before_plan_22_keep_working_with_the_paths_moved_to_scenes()
    {
        var pairs = new Dictionary<string, string>
        {
            ["level"] = "user://levels/mijn_lijn.fslevel.json",
            ["camera.level"] = "user://levels/mijn_lijn.fslevel.json",
            ["camera.x"] = "1", ["camera.y"] = "0", ["camera.z"] = "0", ["camera.distance"] = "12", ["camera.yaw"] = "0", ["camera.pitch"] = "-45",
            ["recent.0"] = "res://levels/mvp_sorting.fslevel.json",
            ["recent.1"] = "res://scenes/mvp_sorting.fsscene.json",
            ["recent.2"] = "C:/export/oud.fslevel.json",
        };

        AppSettings settings = AppSettings.FromPairs(pairs);

        Assert.Equal("user://scenes/mijn_lijn.fsscene.json", settings.ScenePath);
        Assert.Equal("user://scenes/mijn_lijn.fsscene.json", settings.CameraScene);
        Assert.NotNull(settings.Camera);
        Assert.Equal(new[] { "res://scenes/mvp_sorting.fsscene.json", "C:/export/oud.fslevel.json" }, settings.Recent);

        IReadOnlyDictionary<string, string> written = settings.ToPairs();
        Assert.Equal("user://scenes/mijn_lijn.fsscene.json", written["scene"]);
        Assert.Equal("user://scenes/mijn_lijn.fsscene.json", written["camera.scene"]);
        Assert.False(written.ContainsKey("level"));
        Assert.False(written.ContainsKey("camera.level"));
    }

    [Fact]
    public void The_new_key_wins_over_the_old_one()
    {
        AppSettings settings = AppSettings.FromPairs(new Dictionary<string, string>
        {
            ["level"] = "user://levels/oud.fslevel.json",
            ["scene"] = "user://scenes/nieuw.fsscene.json",
        });

        Assert.Equal("user://scenes/nieuw.fsscene.json", settings.ScenePath);
    }
}
