using FactorySim.Core.Robot;

namespace FactorySim.Core.Scenes;

/// <summary>A position taught to a robot arm: its number for the PLC (1 to 15), a name and the six axis angles.</summary>
public sealed record RobotPose(int Number, string Name, RobotAngles Angles);
