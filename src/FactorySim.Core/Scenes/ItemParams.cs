using FactorySim.Core.Robot;

namespace FactorySim.Core.Scenes;

/// <summary>A parameter of a scene item that the editor lets the user change.</summary>
/// <param name="Name">Key in the item's params, e.g. "speed".</param>
/// <param name="IsToggle">True for an on/off setting stored as 0 or 1.</param>
/// <param name="Default">Value when the item does not override it.</param>
public sealed record ParamField(string Name, double Min, double Max, double Step, bool IsToggle, double Default);

/// <summary>
/// Which parameters of an asset are safe to change per item. Sizes are left out: they must
/// match the model.
/// </summary>
public static class ItemParams
{
    private static readonly string[] Speeds =
    {
        "speed", "rollSpeed", "feedSpeed", "pushSpeed", "tableSpeed", "leafSpeed", "turnSpeed", "swingSpeed", "speedZ",
        "deckSpeed", "driveSpeed", "hoistSpeed", "forkSpeed",
    };

    public static IReadOnlyList<ParamField> Editable(CatalogAsset asset)
    {
        var fields = new List<ParamField>();
        if (!asset.IsFunctional)
        {
            return fields;
        }

        // The speed of a robot is a percentage of the top speed of its axes; it gets its own field below.
        foreach (string name in asset.Behaviour == AssetBehaviours.Robot ? Array.Empty<string>() : Speeds)
        {
            if (asset.Params.TryGetValue(name, out double value))
            {
                // Turn and swing speeds are in degrees per second, the others in metres per second.
                bool degrees = name is "turnSpeed" or "swingSpeed";
                fields.Add(new ParamField(name, degrees ? 5.0 : 0.05, degrees ? 360.0 : 3.0, degrees ? 5.0 : 0.05, false, value));
            }
        }

        switch (asset.Behaviour)
        {
            case AssetBehaviours.Conveyor:
            case AssetBehaviours.Curve:
                fields.Add(new ParamField(SceneTags.AlwaysOn, 0.0, 1.0, 1.0, true, 0.0));
                if (asset.Behaviour == AssetBehaviours.Conveyor)
                {
                    fields.Add(new ParamField(SceneTags.TwoWay, 0.0, 1.0, 1.0, true, 0.0));
                }

                if (asset.Param("rise", 0.0) > 0.0)
                {
                    fields.Add(new ParamField(SceneTags.Reverse, 0.0, 1.0, 1.0, true, 0.0));
                }

                break;
            case AssetBehaviours.Pusher:
                fields.Add(new ParamField(SceneTags.Bistable, 0.0, 1.0, 1.0, true, 0.0));
                break;
            case AssetBehaviours.Emitter:
                fields.Add(new ParamField("interval", 0.5, 120.0, 0.5, false, asset.Param("interval", 2.5)));
                if (asset.Emits is null)
                {
                    fields.Add(new ParamField("highRatio", 0.0, 1.0, 0.05, false, 0.5));
                }

                fields.Add(new ParamField("seed", 1.0, 999.0, 1.0, false, 1.0));
                break;
            case AssetBehaviours.Machining:
                fields.Add(new ParamField("machineTime", 0.5, 60.0, 0.5, false, asset.Param("machineTime", 4.0)));
                break;
            case AssetBehaviours.Robot:
                fields.Add(new ParamField("speed", RobotMotion.MinSpeedPercent, RobotMotion.MaxSpeedPercent, 5.0, false, asset.Param("speed", 40.0)));
                fields.Add(new ParamField("pedestal", 0.0, 1.5, 0.05, false, asset.Param("pedestal", 0.75)));
                break;
            case AssetBehaviours.StackerCrane:
                fields.Add(new ParamField(SceneTags.StackerJog, 0.0, 1.0, 1.0, true, 0.0));
                break;
            case AssetBehaviours.PhotoEye:
            case AssetBehaviours.Retroreflective:
            case AssetBehaviours.LightArray:
                fields.Add(new ParamField("range", 0.1, 10.0, 0.05, false, asset.Param("range", 1.0)));
                fields.Add(new ParamField("post", 0.0, 1.0, 1.0, true, 1.0));
                break;
            case AssetBehaviours.Encoder:
                fields.Add(new ParamField("pulseDistance", 0.01, 0.5, 0.01, false, asset.Param("pulseDistance", 0.05)));
                break;
            case AssetBehaviours.Button:
            case AssetBehaviours.Lamp:
            case AssetBehaviours.Siren:
                fields.Add(new ParamField("post", 0.0, 1.0, 1.0, true, 1.0));
                break;
            case AssetBehaviours.Potentiometer:
                fields.Add(new ParamField("start", 0.0, KnobLogic.Maximum, KnobLogic.StepSize, false, 0.0));
                fields.Add(new ParamField("post", 0.0, 1.0, 1.0, true, 1.0));
                break;
            case AssetBehaviours.Display:
                fields.Add(new ParamField("decimals", 0.0, DisplayText.MaxDecimals, 1.0, false, 0.0));
                fields.Add(new ParamField("post", 0.0, 1.0, 1.0, true, 1.0));
                break;
            case AssetBehaviours.Tank:
                fields.Add(new ParamField("maxInflow", 0.01, 2.0, 0.01, false, 0.2));
                fields.Add(new ParamField("maxOutflow", 0.01, 2.0, 0.01, false, 0.2));
                break;
            case AssetBehaviours.Sign:
                fields.Add(new ParamField(SignText.SizeParam, 0.0, 2.0, 1.0, false, (double)SignSize.Medium));
                break;
            case AssetBehaviours.Vision:
                fields.Add(new ParamField("range", 0.1, 10.0, 0.05, false, asset.Param("range", 2.0)));
                fields.Add(new ParamField("post", 0.0, 1.0, 1.0, true, 1.0));
                break;
        }

        return fields;
    }
}
