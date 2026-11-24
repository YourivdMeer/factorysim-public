using System;
using System.Threading.Tasks;
using FactorySim.Core.Diagnostics;
using Godot;

namespace FactorySim.App;

/// <summary>
/// Plan 31: errors do not stay hidden in the console. Every error Godot logs (also a C# exception in a callback,
/// which Godot catches) comes in the status line with <i>Details</i>, which opens the log. An exception that ends
/// the process (on a thread of our own, such as the PLC link) writes a crash file next to the log and shows a
/// message box with its path. A self-test and a headless run never show a message box: <see cref="CrashAlert"/> only prints.
/// </summary>
public partial class Main
{
    private readonly ErrorFeed _errors = new();
    private ErrorLogger? _errorLogger;

    /// <summary>The errors on their way to the status line.</summary>
    public ErrorFeed Errors => _errors;

    /// <summary>Shows a message box (text, title). In a self-test or headless it only prints, so an unattended run never hangs.</summary>
    public Action<string, string> CrashAlert { get; set; } = (text, title) => OS.Alert(text, title);

    /// <summary>Opens the log file. A self-test replaces it, so that no window opens.</summary>
    public Action<string> OpenLog { get; set; } = path => OS.ShellOpen(path);

    /// <summary>The text in the status line of the last error shown; null before the first.</summary>
    public string? LastErrorStatus { get; private set; }

    /// <summary>Hooks the logger and the two .NET events; first thing in <see cref="_Ready"/>.</summary>
    private void InstallCrashHandlers(bool quiet)
    {
        if (quiet)
        {
            CrashAlert = (text, title) => GD.Print($"Crash alert (not shown): {title}: {text.Replace('\n', ' ')}");
        }

        _errorLogger = new ErrorLogger(_errors);
        OS.AddLogger(_errorLogger);
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void RemoveCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        if (_errorLogger is not null)
        {
            OS.RemoveLogger(_errorLogger);
            _errorLogger = null;
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        ReportCrash(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "onbekende fout"));

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();                 // not fatal: in the log and the status line
        GD.PrintErr($"Unobserved task exception: {args.Exception}");
        _errors.Report(args.Exception.InnerException?.ToString() ?? args.Exception.ToString());
    }

    /// <summary>
    /// The process stops on <paramref name="error"/>: writes the crash file in the log folder, prints it, and shows the
    /// message box with the paths. Returns the crash file, or null when it could not be written.
    /// </summary>
    public string? ReportCrash(Exception error)
    {
        string folder = Native(LogFolder());
        string? file = null;
        try
        {
            file = CrashReport.Write(folder, error, DateTime.Now);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Cannot write the crash file in {folder}: {e.Message}");
        }

        GD.PrintErr($"FactorySim stopt door een fout: {error}");
        CrashAlert(CrashReport.AlertText(file, folder), CrashReport.AlertTitle);
        return file;
    }

    /// <summary>The app cannot start: the reason, in the log and (outside a self-test) in a message box.</summary>
    private void ReportStartupFailure(string reason)
    {
        GD.PushError(reason);
        CrashAlert(CrashReport.StartupText(reason, Native(LogFolder())), CrashReport.StartupTitle);
    }

    /// <summary>Once a frame on the main thread: a new error goes to the status line with Details.</summary>
    private void ShowNewErrors()
    {
        if (_errors.TakeChanged() is not { } notice)
        {
            return;
        }

        LastErrorStatus = ErrorFeed.StatusText(notice);
        _editor.ShowMessage(LastErrorStatus, isError: true);
        Shell.StatusDetails.Visible = true;
    }

    /// <summary>Details: the log file of this session, or its folder when the file is not there.</summary>
    private void OpenLogFile()
    {
        string file = Native(LogFile());
        OpenLog(System.IO.File.Exists(file) ? file : Native(LogFolder()));
    }

    /// <summary>The log file of Godot (debug/file_logging/log_path), as a full path.</summary>
    public static string LogFile() =>
        ProjectSettings.GlobalizePath(ProjectSettings.GetSetting("debug/file_logging/log_path", "user://logs/godot.log").AsString());
}
