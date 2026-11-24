using System.Collections.Generic;
using FactorySim.App.Ui;
using FactorySim.Core;
using FactorySim.Core.Scenes;
using Godot;

namespace FactorySim.App;

/// <summary>Plan 28: the version in the title bar, and the folders of the window Over.</summary>
public partial class Main
{
    /// <summary>The title the window has now: "FactorySim &lt;version&gt; — &lt;scene&gt;".</summary>
    public string WindowTitle { get; private set; } = string.Empty;

    /// <summary>The window Over.</summary>
    public AboutDialog About => _hud.About;

    /// <summary>Follows the scene name (load, gallery, typing in the toolbar); sets the title only when it changes.</summary>
    private void UpdateWindowTitle()
    {
        string title = AppInfo.WindowTitle(_scene.Name);
        if (title != WindowTitle)
        {
            WindowTitle = title;
            GetWindow().Title = title;
        }
    }

    /// <summary>Own scenes, automatic copies (or "uit") and the log of Godot, as full paths.</summary>
    private IReadOnlyList<AboutDialog.Folder> AboutFolders() => new AboutDialog.Folder[]
    {
        new("Eigen scènes", Native(ProjectSettings.GlobalizePath(ScenePaths.OwnFolder))),
        new("Automatisch opgeslagen", _editor.AutosaveFolderPath() is { } autosave ? Native(autosave) : null),
        new("Logbestanden", Native(LogFolder())),
    };

    /// <summary>Backslashes on Windows, as Explorer writes them.</summary>
    private static string Native(string path) => OS.GetName() == "Windows" ? path.Replace('/', '\\') : path;

    private static string LogFolder() => LogFile().GetBaseDir();
}
