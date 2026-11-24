using FactorySim.Core.Robot;

namespace FactorySim.Core.Scenes;

/// <summary>
/// Editing the list of taught positions of a robot. Every operation returns a new list sorted by
/// number and leaves the old one alone, so a scene item can share it safely. Numbers stay put
/// when a position is removed: the PLC program refers to them.
/// </summary>
public static class RobotPoses
{
    public static IReadOnlyList<RobotPose> Add(IReadOnlyList<RobotPose>? poses, string name, RobotAngles angles)
    {
        IReadOnlyList<RobotPose> list = poses ?? Array.Empty<RobotPose>();
        for (int number = 1; number <= RobotController.MaxPosition; number++)
        {
            if (list.All(pose => pose.Number != number))
            {
                return Sorted(list.Append(new RobotPose(number, CheckName(name), angles)));
            }
        }

        throw new SceneException($"Een robot kent hoogstens {RobotController.MaxPosition} posities.");
    }

    public static IReadOnlyList<RobotPose> Overwrite(IReadOnlyList<RobotPose>? poses, int number, RobotAngles angles) =>
        Change(poses, number, pose => pose with { Angles = angles });

    public static IReadOnlyList<RobotPose> Rename(IReadOnlyList<RobotPose>? poses, int number, string name) =>
        Change(poses, number, pose => pose with { Name = CheckName(name) });

    public static IReadOnlyList<RobotPose> Remove(IReadOnlyList<RobotPose>? poses, int number) =>
        Sorted((poses ?? Array.Empty<RobotPose>()).Where(pose => pose.Number != number));

    /// <summary>Gives a position another, free number from 1 to 15.</summary>
    public static IReadOnlyList<RobotPose> Renumber(IReadOnlyList<RobotPose>? poses, int number, int newNumber)
    {
        if (newNumber is < 1 or > RobotController.MaxPosition)
        {
            throw new SceneException($"Een positie heeft een nummer van 1 tot {RobotController.MaxPosition}.");
        }

        if (newNumber != number && (poses ?? Array.Empty<RobotPose>()).Any(pose => pose.Number == newNumber))
        {
            throw new SceneException($"Positie {newNumber} bestaat al.");
        }

        return Change(poses, number, pose => pose with { Number = newNumber });
    }

    /// <summary>The positions by number, for <see cref="RobotController.SetPoses"/>.</summary>
    public static IReadOnlyDictionary<int, RobotAngles> ToTable(IReadOnlyList<RobotPose>? poses) =>
        (poses ?? Array.Empty<RobotPose>()).ToDictionary(pose => pose.Number, pose => pose.Angles);

    /// <summary>Checks a list read from a file: numbers 1 to 15, each once.</summary>
    public static IReadOnlyList<RobotPose> Validate(IEnumerable<RobotPose> poses, string itemId)
    {
        RobotPose[] list = poses.ToArray();
        foreach (RobotPose pose in list)
        {
            if (pose.Number is < 1 or > RobotController.MaxPosition)
            {
                throw new SceneException($"Robot '{itemId}' has a position with number {pose.Number}; numbers run from 1 to {RobotController.MaxPosition}.");
            }
        }

        if (list.Select(pose => pose.Number).Distinct().Count() != list.Length)
        {
            throw new SceneException($"Robot '{itemId}' has two positions with the same number.");
        }

        return Sorted(list);
    }

    private static IReadOnlyList<RobotPose> Change(IReadOnlyList<RobotPose>? poses, int number, Func<RobotPose, RobotPose> change)
    {
        IReadOnlyList<RobotPose> list = poses ?? Array.Empty<RobotPose>();
        if (list.All(pose => pose.Number != number))
        {
            throw new SceneException($"Positie {number} bestaat niet.");
        }

        return Sorted(list.Select(pose => pose.Number == number ? change(pose) : pose));
    }

    private static string CheckName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new SceneException("Een positie heeft een naam nodig.");
        }

        return name;
    }

    private static RobotPose[] Sorted(IEnumerable<RobotPose> poses) => poses.OrderBy(pose => pose.Number).ToArray();
}
