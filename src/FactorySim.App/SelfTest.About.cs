using System.Collections.Generic;
using System.IO;
using FactorySim.Core;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 28, part of the "ui" self-test: the version in the title bar and the window Over. "Map openen" is pressed
/// with a recording opener, so that no Explorer window opens during an unattended run.
/// </summary>
public partial class SelfTest
{
    private const string RenamedForTitle = "Titeltest";

    private void RunUiAbout()
    {
        string name = Main.Scene.Name;
        SortersCheck("title bar has the version and the scene", Main.WindowTitle == AppInfo.WindowTitle(name)
            && Main.WindowTitle.Contains(AppInfo.Version) && (string.IsNullOrWhiteSpace(name) || Main.WindowTitle.Contains(name.Trim())));

        var about = Main.About;
        about.Refresh();
        var opened = new List<string>();
        System.Action<string> real = about.OpenFolder;
        about.OpenFolder = opened.Add;
        string version = about.GetNode<Label>("Column/Version").Text;
        bool texts = about.GetNode<Label>("Column/Network").Text == AppInfo.NetworkNote
            && about.GetNode<Label>("Column/Trademark").Text == AppInfo.TrademarkNote
            && about.GetNode<Label>("Column/Licences").Text.Contains("THIRD-PARTY-NOTICES.txt");
        SortersCheck("Over shows the version and the notes", version == $"FactorySim {AppInfo.Version}" && texts);

        var paths = new List<string>();
        for (int index = 0; index < about.FolderCount; index++)
        {
            paths.Add(about.GetNode<Label>($"Column/Folders/Path{index}").Text);
            var open = about.GetNode<Button>($"Column/Folders/Open{index}");
            if (!open.Disabled)
            {
                open.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }

        about.OpenFolder = real;
        GD.Print($"SELFTEST ui: Over folders {string.Join(" | ", paths)}");
        bool rooted = paths.Count == 3 && paths[0] != "uit" && Path.IsPathRooted(paths[0]) && Path.IsPathRooted(paths[2])
            && (paths[1] == "uit" || Path.IsPathRooted(paths[1]));
        SortersCheck("Over lists three folders as full paths", rooted && paths[0].EndsWith("scenes") && paths[2].EndsWith("logs"));
        SortersCheck("Map openen opens the folder of its row", opened.Count == paths.FindAll(path => path != "uit").Count
            && opened.TrueForAll(paths.Contains));

        Editor.SetSceneName(RenamedForTitle);       // the title follows on the next frame
    }

    private void CheckUiAboutTitle() =>
        SortersCheck("title bar follows a new scene name", Main.WindowTitle == AppInfo.WindowTitle(RenamedForTitle));
}
