namespace FactorySim.App;

/// <summary>
/// Names of the tags the MVP scene binds its parts to. Addresses and PLC variable names
/// live in tagmap.json; these names are the only link between the scene and that file.
/// </summary>
public static class TagNames
{
    public const string StartButton = "Start button";
    public const string StopButton = "Stop button";
    public const string AtPusher = "At pusher";
    public const string HighBox = "High box";
    public const string PusherBack = "Pusher back";
    public const string PusherFront = "Pusher front";
    public const string SimRunning = "Sim running";

    public const string Conveyor = "Conveyor";
    public const string Pusher = "Pusher";
    public const string Emitter = "Emitter";
    public const string RunLamp = "Run lamp";

    public static readonly string[] All =
    {
        StartButton, StopButton, AtPusher, HighBox, PusherBack, PusherFront, SimRunning,
        Conveyor, Pusher, Emitter, RunLamp,
    };
}
