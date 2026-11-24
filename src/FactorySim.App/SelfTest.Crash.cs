using System;
using System.Collections.Generic;
using System.IO;
using FactorySim.Core;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 31, part of the "ui" self-test. (a) A crash report with a made exception: the crash file is written in the
/// log folder of <i>Over</i> and the message box (recorded, never shown) names it. (b) An exception thrown in a
/// deferred call, which Godot catches and logs: a frame later it is in the status line, and Details opens the log.
/// </summary>
public partial class SelfTest
{
    private const string ForcedError = "zelftest: geforceerde fout";

    private void RunUiCrash()
    {
        var alerts = new List<(string Text, string Title)>();
        Action<string, string> realAlert = Main.CrashAlert;
        Main.CrashAlert = (text, title) => alerts.Add((text, title));
        string? file = Main.ReportCrash(new InvalidOperationException(ForcedError));
        Main.CrashAlert = realAlert;

        Main.About.Refresh();
        string logFolder = Main.About.GetNode<Label>("Column/Folders/Path2").Text;
        bool written = file is not null && File.Exists(file) && Path.GetDirectoryName(file) == logFolder
            && File.ReadAllText(file).Contains(ForcedError) && File.ReadAllText(file).Contains(AppInfo.Version);
        bool alerted = alerts.Count == 1 && file is not null && alerts[0].Text.Contains(file) && alerts[0].Text.Contains(logFolder)
            && alerts[0].Text.Contains(AppInfo.Version);
        GD.Print($"SELFTEST ui crash: file={file} folder={logFolder} alerts={alerts.Count}");
        SortersCheck("a crash writes the crash file in the log folder and names it", written && alerted);
        if (file is not null)
        {
            File.Delete(file);
        }

        // Godot catches this one and logs it; the logger passes it to the status line.
        Callable.From(() => throw new InvalidOperationException(ForcedError)).CallDeferred();
    }

    private void CheckUiCrash()
    {
        string? status = Main.LastErrorStatus;
        bool shown = status is not null && status.Contains(ForcedError) && Main.Shell.StatusMessage.Text == status
            && Main.Shell.StatusDetails.Visible;

        var opened = new List<string>();
        Action<string> real = Main.OpenLog;
        Main.OpenLog = opened.Add;
        Main.Shell.StatusDetails.EmitSignal(BaseButton.SignalName.Pressed);
        Main.OpenLog = real;
        string logFile = Main.LogFile().Replace('/', Path.DirectorySeparatorChar);
        bool details = opened.Count == 1 && (opened[0] == logFile || opened[0] == Path.GetDirectoryName(logFile));
        GD.Print($"SELFTEST ui crash: status='{status}' details={string.Join(" | ", opened)}");
        SortersCheck("an error in a callback comes in the status line, Details opens the log", shown && details);
    }
}
