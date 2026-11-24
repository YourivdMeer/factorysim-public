using System.Text.RegularExpressions;
using FactorySim.Core.Scenes;
using FactorySim.Core.Tags;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 05, part 6: the I/O list as a Global Variable List in Structured Text.</summary>
public class GvlExportTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static IReadOnlyList<IoPoint> MvpPoints(out Scene scene)
    {
        scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mvp_sorting.fsscene.json")));
        return IoList.Build(scene, Catalog, SceneTags.Build(scene, Catalog));
    }

    /// <summary>The variables of a GVL text: name, area ("Coil" or "Discrete Input") and offset, read from the comment.</summary>
    private static Dictionary<string, (string Area, int Offset)> Variables(string text)
    {
        var found = new Dictionary<string, (string, int)>();
        foreach (Match line in Regex.Matches(text, @"^\s*(\w+)\s*:\s*BOOL;\s*\(\*\s*(Coil|Discrete Input)\s+(\d+)", RegexOptions.Multiline))
        {
            found[line.Groups[1].Value] = (line.Groups[2].Value, int.Parse(line.Groups[3].Value));
        }

        return found;
    }

    [Fact]
    public void The_export_is_a_global_variable_list_with_one_bool_per_tag_sensors_first_then_actuators()
    {
        IReadOnlyList<IoPoint> points = MvpPoints(out Scene scene);

        string text = GvlExport.ToSt(points, scene.Name);

        Assert.Contains("VAR_GLOBAL", text);
        Assert.Contains("END_VAR", text);
        Assert.True(text.IndexOf("VAR_GLOBAL", StringComparison.Ordinal) < text.IndexOf("END_VAR", StringComparison.Ordinal));
        Dictionary<string, (string Area, int Offset)> variables = Variables(text);
        Assert.Equal(points.Count, variables.Count);
        Assert.Equal(7, variables.Count(pair => pair.Value.Area == "Coil"));
        Assert.Equal(4, variables.Count(pair => pair.Value.Area == "Discrete Input"));
        // Inputs of the PLC (coils) come before outputs (discrete inputs), each by address.
        string[] order = Regex.Matches(text, @"^\s*(\w+)\s*:\s*BOOL;", RegexOptions.Multiline).Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "iStart", "iStop", "iAtPusher", "iHighBox", "iPusherBack", "iPusherFront", "iSimRunning",
            "oConveyor", "oPusher", "oEmitter", "oRunLamp" }, order);
    }

    [Fact]
    public void The_header_names_the_version_of_factorysim()
    {
        IReadOnlyList<IoPoint> points = MvpPoints(out Scene scene);

        string text = GvlExport.ToSt(points, scene.Name);

        string header = text[..text.IndexOf("VAR_GLOBAL", StringComparison.Ordinal)];
        Assert.Contains($"FactorySim {AppInfo.Version}", header);
    }

    [Fact]
    public void Each_variable_carries_its_modbus_address_its_iec_address_and_what_it_is_wired_to_as_a_comment()
    {
        IReadOnlyList<IoPoint> points = MvpPoints(out Scene scene);

        string text = GvlExport.ToSt(points, scene.Name);

        Assert.Contains("(* Coil 2, %IX0.2:", text);
        Assert.Contains("(* Discrete Input 1, %QX0.1:", text);
        Assert.Contains("At pusher", text);
        Assert.Contains(scene.Name, text);                                  // the scene it was made from
        Assert.DoesNotContain("IO.", text.Split('\n').First(line => line.Contains("iStart")));    // the list is called IO; the variables are not prefixed
    }

    [Fact]
    public void The_export_of_the_mvp_scene_matches_the_acceptance_project_apart_from_the_spares()
    {
        IReadOnlyList<IoPoint> points = MvpPoints(out Scene scene);
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "IO.gvl.st"));

        Dictionary<string, (string Area, int Offset)> exported = Variables(GvlExport.ToSt(points, scene.Name));
        Dictionary<string, (string Area, int Offset)> hand = Variables(source);

        // The source also lists a spare variable for each unused address (iSpare, oSpare1..oSpare4). They are the
        // only difference: the app writes what the scene has, the source keeps room for more.
        Dictionary<string, (string Area, int Offset)> handWithoutSpares = hand.Where(pair => !pair.Key.Contains("Spare")).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(handWithoutSpares.OrderBy(pair => pair.Key), exported.OrderBy(pair => pair.Key));
        Assert.Equal(5, Regex.Matches(source, @"^\s*\w*Spare\w*\s*:\s*BOOL;", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void The_variable_names_are_valid_and_unique_for_a_scene_with_many_parts()
    {
        Scene scene = Scene.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assembly_line.fsscene.json")));
        IReadOnlyList<IoPoint> points = IoList.Build(scene, Catalog, SceneTags.Build(scene, Catalog));

        string text = GvlExport.ToSt(points, scene.Name);

        string[] names = Regex.Matches(text, @"^\s*(\w+)\s*:\s*BOOL;", RegexOptions.Multiline).Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(points.Count, names.Length);
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name => Assert.Matches("^[A-Za-z][A-Za-z0-9_]*$", name));
        Assert.Contains("iClampClosed", names);
        Assert.Contains("oPickAndPlaceGrab", names);
    }

    [Fact]
    public void A_tag_without_a_variable_gets_one_made_from_its_name()
    {
        var tag = new TagMapEntry("Door 1 closed", TagDirection.Sensor, ModbusArea.Coil, 0, "%IX0.0");
        var points = new[] { new IoPoint(tag, "door-1", "Door 1", "closed", "Safety door") };

        string text = GvlExport.ToSt(points, "Test");

        Assert.Contains("iDoor1Closed", text);
    }

    [Fact]
    public void An_empty_scene_gives_an_empty_but_valid_list()
    {
        string text = GvlExport.ToSt(Array.Empty<IoPoint>(), "Leeg");

        Assert.Contains("VAR_GLOBAL", text);
        Assert.Contains("END_VAR", text);
        Assert.Empty(Variables(text));
    }
}
