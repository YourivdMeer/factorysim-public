using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace FactorySim.Core.Tests.Integration;

/// <summary>Runs tools/fake_plc/fake_plc.py as a child process for the duration of a test.</summary>
public sealed class FakePlcProcess : IDisposable
{
    private readonly Process _process;

    private FakePlcProcess(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    public int Port { get; }

    /// <param name="port">0 lets the fake PLC pick a free port.</param>
    public static FakePlcProcess Start(string mode, int port = 0, double scanMs = 10.0)
    {
        string script = Path.Combine(FindRepoRoot(), "tools", "fake_plc", "fake_plc.py");
        var start = new ProcessStartInfo("py")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "-u", script, "--mode", mode, "--port", port.ToString(),
            "--scan-ms", scanMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        {
            start.ArgumentList.Add(argument);
        }

        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start 'py'.");
        Task<string?> ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result is null)
        {
            string error = process.HasExited ? process.StandardError.ReadToEnd() : "(no output within 10 s)";
            Kill(process);
            throw new InvalidOperationException($"Fake PLC did not start: {error}");
        }

        Match match = Regex.Match(ready.Result, @"READY port=(\d+)");
        if (!match.Success)
        {
            Kill(process);
            throw new InvalidOperationException($"Unexpected fake PLC output: {ready.Result}");
        }

        return new FakePlcProcess(process, int.Parse(match.Groups[1].Value));
    }

    /// <summary>A TCP port on 127.0.0.1 that was free a moment ago.</summary>
    public static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => Kill(_process);

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // The 'py' launcher starts python.exe as a child; take both down.
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FactorySim.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("FactorySim.sln not found above the test directory.");
    }
}
