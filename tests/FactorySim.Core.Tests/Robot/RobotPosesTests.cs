using FactorySim.Core.Scenes;
using FactorySim.Core.Robot;
using FactorySim.Core.Tags;

namespace FactorySim.Core.Tests.Robot;

public class RobotPosesTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static readonly RobotAngles Above = new(90, 10, 20, 0, -30, 0);

    private static readonly RobotAngles Down = new(90, 30, 10, 0, -40, 0);

    // ---- the list of positions

    [Fact]
    public void Add_takes_the_lowest_free_number()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Add(null, "Boven", Above);
        poses = RobotPoses.Add(poses, "Op", Down);
        poses = RobotPoses.Remove(poses, 1);
        poses = RobotPoses.Add(poses, "Weer", Above);

        Assert.Equal(new[] { 1, 2 }, poses.Select(pose => pose.Number));
        Assert.Equal("Weer", poses[0].Name);
    }

    [Fact]
    public void Removing_a_position_does_not_renumber_the_others()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Add(RobotPoses.Add(RobotPoses.Add(null, "a", Above), "b", Down), "c", Above);

        poses = RobotPoses.Remove(poses, 2);

        Assert.Equal(new[] { 1, 3 }, poses.Select(pose => pose.Number));
    }

    [Fact]
    public void Only_15_positions_fit()
    {
        IReadOnlyList<RobotPose>? poses = null;
        for (int i = 0; i < RobotController.MaxPosition; i++)
        {
            poses = RobotPoses.Add(poses, "p" + i, Above);
        }

        Assert.Throws<SceneException>(() => RobotPoses.Add(poses, "te veel", Above));
    }

    [Fact]
    public void Overwrite_changes_the_angles_and_keeps_the_name()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Add(null, "Boven", Above);

        poses = RobotPoses.Overwrite(poses, 1, Down);

        Assert.Equal(Down, poses.Single().Angles);
        Assert.Equal("Boven", poses.Single().Name);
        Assert.Throws<SceneException>(() => RobotPoses.Overwrite(poses, 4, Down));
    }

    [Fact]
    public void Renumber_moves_a_position_to_a_free_number()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Add(RobotPoses.Add(null, "a", Above), "b", Down);

        poses = RobotPoses.Renumber(poses, 1, 7);

        Assert.Equal(new[] { 2, 7 }, poses.Select(pose => pose.Number));
        Assert.Throws<SceneException>(() => RobotPoses.Renumber(poses, 7, 2));
        Assert.Throws<SceneException>(() => RobotPoses.Renumber(poses, 7, 0));
        Assert.Throws<SceneException>(() => RobotPoses.Renumber(poses, 7, 16));
    }

    [Fact]
    public void Rename_changes_the_name_and_refuses_an_empty_one()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Add(null, "a", Above);

        poses = RobotPoses.Rename(poses, 1, "  Boven de band ");

        Assert.Equal("Boven de band", poses.Single().Name);
        Assert.Throws<SceneException>(() => RobotPoses.Rename(poses, 1, " "));
    }

    [Fact]
    public void The_positions_become_a_table_for_the_controller()
    {
        IReadOnlyList<RobotPose> poses = RobotPoses.Renumber(RobotPoses.Add(null, "a", Above), 1, 5);

        IReadOnlyDictionary<int, RobotAngles> table = RobotPoses.ToTable(poses);

        Assert.Equal(Above, table[5]);
        Assert.Single(table);
        Assert.Empty(RobotPoses.ToTable(null));
    }

    // ---- in a scene

    private static (Scene Scene, SceneItem Robot) SceneWithRobot()
    {
        var scene = new Scene();
        SceneItem robot = scene.Add(Catalog.Get("robot_6axis"), 0, 0);
        robot = scene.SetPoses(robot.Id, RobotPoses.Add(RobotPoses.Add(null, "Boven", Above), "Op", Down));
        return (scene, robot);
    }

    [Fact]
    public void A_robot_is_named_robot_and_has_no_positions_of_its_own()
    {
        var scene = new Scene();
        SceneItem robot = scene.Add(Catalog.Get("robot_6axis"), 0, 0);

        Assert.Equal("Robot", robot.Name);
        Assert.Null(robot.Poses);
    }

    [Fact]
    public void Save_and_open_keep_the_positions()
    {
        (Scene scene, SceneItem robot) = SceneWithRobot();

        Scene again = Scene.FromJson(scene.ToJson());

        SceneItem loaded = again.Find(robot.Id)!;
        Assert.NotNull(loaded.Poses);
        Assert.Equal(robot.Poses!, loaded.Poses!);
    }

    [Fact]
    public void A_scene_without_positions_writes_none()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("robot_6axis"), 0, 0);

        Assert.DoesNotContain("poses", scene.ToJson());
    }

    [Fact]
    public void A_position_with_a_bad_number_or_too_few_angles_is_refused_on_open()
    {
        (Scene scene, SceneItem _) = SceneWithRobot();
        string json = scene.ToJson();

        Assert.Throws<SceneException>(() => Scene.FromJson(json.Replace("\"number\": 2", "\"number\": 16")));
        Assert.Throws<SceneException>(() => Scene.FromJson(json.Replace("\"number\": 2", "\"number\": 1")));
    }

    [Fact]
    public void Duplicate_and_paste_take_the_positions_along()
    {
        (Scene scene, SceneItem robot) = SceneWithRobot();

        SceneItem copy = scene.Duplicate(robot.Id, 2, 0);
        SceneItem pasted = scene.PasteMany(new[] { robot }, 4, 0).Single();

        Assert.Equal(robot.Poses!, copy.Poses!);
        Assert.Equal(robot.Poses!, pasted.Poses!);
        Assert.NotSame(robot.Poses, copy.Poses);
    }

    [Fact]
    public void Undo_brings_the_old_positions_back()
    {
        (Scene scene, SceneItem robot) = SceneWithRobot();
        var history = new SceneHistory();

        history.Record(scene);
        scene.SetPoses(robot.Id, RobotPoses.Remove(robot.Poses, 1));
        Scene undone = history.Undo(scene)!;

        Assert.Equal(2, undone.Find(robot.Id)!.Poses!.Count);
    }

    [Fact]
    public void Setting_no_positions_clears_them()
    {
        (Scene scene, SceneItem robot) = SceneWithRobot();

        SceneItem cleared = scene.SetPoses(robot.Id, Array.Empty<RobotPose>());

        Assert.Null(cleared.Poses);
    }

    // ---- tags and settings

    [Fact]
    public void A_robot_gives_six_commands_and_four_sensors()
    {
        var scene = new Scene();
        scene.Add(Catalog.Get("robot_6axis"), 0, 0);

        IReadOnlyList<TagMapEntry> tags = SceneTags.Build(scene, Catalog);

        foreach (string suffix in new[] { "pos 1", "pos 2", "pos 4", "pos 8", "go", "grip" })
        {
            Assert.Equal(TagDirection.Actuator, tags.Single(tag => tag.Name == "Robot " + suffix).Direction);
        }

        foreach (string suffix in new[] { "busy", "at pos", "item", "error" })
        {
            Assert.Equal(TagDirection.Sensor, tags.Single(tag => tag.Name == "Robot " + suffix).Direction);
        }

        Assert.Equal(10, tags.Count(tag => tag.Name.StartsWith("Robot ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Speed_in_percent_and_the_pedestal_are_settings_and_nothing_in_metres_per_second()
    {
        IReadOnlyList<ParamField> fields = ItemParams.Editable(Catalog.Get("robot_6axis"));

        ParamField speed = fields.Single(field => field.Name == "speed");
        Assert.Equal(5.0, speed.Min);
        Assert.Equal(100.0, speed.Max);
        Assert.Equal(40.0, speed.Default);
        ParamField pedestal = fields.Single(field => field.Name == "pedestal");
        Assert.Equal(0.0, pedestal.Min);
        Assert.Equal(0.75, pedestal.Default);
        Assert.Equal(2, fields.Count);
    }
}
