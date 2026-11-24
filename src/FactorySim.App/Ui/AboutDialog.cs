using System;
using System.Collections.Generic;
using FactorySim.Core;
using Godot;

namespace FactorySim.App.Ui;

/// <summary>
/// Plan 28: the window <i>Over</i>. Version, licences, the folders with the own files (each with
/// <i>Map openen</i>), that nothing goes to the internet, and the trademark sentence about CODESYS.
/// </summary>
public partial class AboutDialog : AcceptDialog
{
    /// <summary>One folder row: what it holds and its full path, or null when it is switched off.</summary>
    public readonly record struct Folder(string Label, string? Path);

    private readonly Func<IReadOnlyList<Folder>> _folders;
    private readonly GridContainer _grid = new() { Name = "Folders", Columns = 3 };

    /// <param name="folders">The folders to show, asked again each time the window opens.</param>
    public AboutDialog(Func<IReadOnlyList<Folder>> folders)
    {
        _folders = folders;
        Name = "AboutDialog";
        Title = "Over FactorySim";
        OkButtonText = "Sluiten";
        MinSize = new Vector2I(620, 0);

        var column = new VBoxContainer { Name = "Column" };
        column.AddChild(new Label { Name = "Version", Text = $"{AppInfo.Name} {AppInfo.Version}" });
        column.AddChild(Wrapped("Licences", AppInfo.LicenceNote));
        column.AddChild(new HSeparator());
        column.AddChild(Shell.Heading("Je bestanden"));
        column.AddChild(_grid);
        column.AddChild(new HSeparator());
        column.AddChild(Wrapped("Network", AppInfo.NetworkNote));
        column.AddChild(Wrapped("Trademark", AppInfo.TrademarkNote));
        AddChild(column);
        AboutToPopup += Refresh;
    }

    /// <summary>Opens a folder in Explorer. A self-test replaces it, so that no window opens.</summary>
    public Action<string> OpenFolder { get; set; } = path =>
    {
        System.IO.Directory.CreateDirectory(path);
        OS.ShellOpen(path);
    };

    /// <summary>Number of folder rows after the last <see cref="Refresh"/>.</summary>
    public int FolderCount { get; private set; }

    /// <summary>Rebuilds the folder rows.</summary>
    public void Refresh()
    {
        foreach (Node child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        IReadOnlyList<Folder> folders = _folders();
        for (int index = 0; index < folders.Count; index++)
        {
            Folder folder = folders[index];
            _grid.AddChild(new Label { Text = folder.Label });
            _grid.AddChild(new Label
            {
                Name = $"Path{index}",
                Text = folder.Path ?? "uit",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TooltipText = folder.Path ?? string.Empty,
                MouseFilter = Control.MouseFilterEnum.Pass,
            });
            Button open = Shell.MakeButton("Map openen", null, folder.Path is null ? "Staat uit" : "Open de map in de Verkenner");
            open.Name = $"Open{index}";
            open.Disabled = folder.Path is null;
            if (folder.Path is { } path)
            {
                open.Pressed += () => OpenFolder(path);
            }

            _grid.AddChild(open);
        }

        FolderCount = folders.Count;
    }

    private static Label Wrapped(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(580.0f, 0.0f),
    };
}
