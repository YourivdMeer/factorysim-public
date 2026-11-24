using System.Text.RegularExpressions;

namespace FactorySim.Core.Tests;

/// <summary>
/// Checks <c>THIRD-PARTY-NOTICES.txt</c> in the repo root (plan 27): it must name every component that goes
/// into an export, so a new NuGet package without a notice fails the unit tests.
/// </summary>
public class ThirdPartyNoticesTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string Notices => File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.txt"));

    [Fact]
    public void Notices_cover_godot_with_its_bundled_components()
    {
        string text = Notices;
        Assert.Contains("Godot Engine 4.7.2", text);
        Assert.Contains("Juan Linietsky, Ariel Manzur", text);
        Assert.Contains("Component: Jolt Physics", text);
        Assert.Contains("Component: The FreeType Project", text);
        Assert.Contains("Permission is hereby granted, free of charge", text);
    }

    [Fact]
    public void Notices_cover_the_dotnet_runtime()
    {
        string text = Notices;
        Assert.Contains(".NET runtime", text);
        Assert.Contains("Copyright (c) .NET Foundation and Contributors", text);
    }

    [Fact]
    public void Notices_name_every_package_the_app_and_core_reference()
    {
        string text = Notices;
        var packages = Directory.GetFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => Regex.Matches(File.ReadAllText(project),
                    "<PackageReference\\s+Include=\"([^\"]+)\"\\s+Version=\"([^\"]+)\"")
                .Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}"))
            .Distinct()
            .ToArray();

        Assert.NotEmpty(packages);
        Assert.All(packages, package => Assert.Contains(package, text));
        Assert.Contains("Copyright (c) 2006 Scott Alexander, 2015 Dmitry Turin", text);   // NModbus
    }

    [Fact]
    public void Godot_version_in_the_notices_matches_the_sdk_of_the_app()
    {
        string project = File.ReadAllText(Path.Combine(RepoRoot, "src", "FactorySim.App", "FactorySim.App.csproj"));
        string version = Regex.Match(project, "Godot\\.NET\\.Sdk/([0-9.]+)").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(version));
        Assert.Contains($"Godot Engine {version}", Notices);
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
