using FactorySim.Core.Scenes;
using FactorySim.Core.Robot;

namespace FactorySim.Core.Tests.Robot;

public class RobotCatalogTests
{
    private static readonly AssetCatalog Catalog =
        AssetCatalog.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")));

    private static CatalogAsset Robot => Catalog.Get("robot_6axis");

    [Fact]
    public void The_robot_is_a_placeable_station()
    {
        Assert.Equal(AssetBehaviours.Robot, Robot.Behaviour);
        Assert.Equal("Robotarm 6 assen", Robot.Name);
        Assert.Equal("stations", Robot.Category);
        Assert.True(Robot.IsPlaceable);
        Assert.DoesNotContain("ABB", Robot.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_model_has_the_seven_parts_and_a_pedestal()
    {
        foreach (string part in RobotKinematics.PartNames.Append("Pedestal"))
        {
            Assert.Contains(part, Robot.Parts);
        }

        Assert.InRange(Robot.Triangles, 1, 4999);
    }

    [Fact]
    public void The_sizes_in_the_catalogue_are_those_of_the_kinematics()
    {
        RobotGeometry fromCatalog = RobotGeometry.FromParams(Robot.Param);
        RobotGeometry expected = RobotGeometry.Default;

        foreach (string name in RobotGeometry.ParamNames)
        {
            Assert.True(Robot.Params.ContainsKey(name), $"the catalogue has no '{name}'");
        }

        Assert.Equal(expected.BaseHeight, fromCatalog.BaseHeight, 6);
        Assert.Equal(expected.UpperArm, fromCatalog.UpperArm, 6);
        Assert.Equal(expected.ElbowOffset, fromCatalog.ElbowOffset, 6);
        Assert.Equal(expected.Forearm, fromCatalog.Forearm, 6);
        Assert.Equal(expected.WristToFlange, fromCatalog.WristToFlange, 6);
        Assert.Equal(expected.ToolLength, fromCatalog.ToolLength, 6);
    }

    [Fact]
    public void The_robot_starts_slow_on_a_pedestal_at_belt_height()
    {
        Assert.Equal(40.0, Robot.Param("speed", 0.0));
        Assert.Equal(0.75, Robot.Param("pedestal", 0.0));
    }
}
