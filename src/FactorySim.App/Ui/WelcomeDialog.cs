using System;
using FactorySim.Core.Settings;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Plan 30: the welcome window. Three steps to a running sorting line, a button <i>Niet meer tonen</i>,
/// and <i>Sluiten</i>. The texts come from <see cref="Welcome"/> in Core; the button "Aan de slag" in the window of "?" opens it again.
/// </summary>
public partial class WelcomeDialog : AcceptDialog
{
    public const string DontShowAction = "dont_show";

    public WelcomeDialog()
    {
        Name = "WelcomeDialog";
        Title = Welcome.Title;
        OkButtonText = "Sluiten";
        MinSize = new Vector2I(640, 0);

        var column = new VBoxContainer { Name = "Column" };
        column.AddChild(Wrapped("Intro", Welcome.Intro));
        for (int index = 0; index < Welcome.Steps.Count; index++)
        {
            column.AddChild(Wrapped($"Step{index + 1}", Welcome.Steps[index]));
        }

        column.AddChild(new HSeparator());
        column.AddChild(Wrapped("Outro", Welcome.Outro));
        AddChild(column);

        DontShowButton = AddButton("Niet meer tonen", right: false, DontShowAction);
        DontShowButton.Name = "DontShow";
        CustomAction += action =>
        {
            if (action == DontShowAction)
            {
                DontShowAgain?.Invoke();
                Hide();
            }
        };
    }

    /// <summary>The button "Niet meer tonen".</summary>
    public Button DontShowButton { get; }

    /// <summary>"Niet meer tonen" was clicked.</summary>
    public event Action? DontShowAgain;

    private static Label Wrapped(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(600.0f, 0.0f),
    };
}
