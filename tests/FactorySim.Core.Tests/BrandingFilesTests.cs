using System.Buffers.Binary;

namespace FactorySim.Core.Tests;

/// <summary>
/// Checks the generated app icon and splash screen (plan 25) against the files in the repo,
/// so a stale copy or a broken <c>.ico</c> fails the unit tests.
/// </summary>
public class BrandingFilesTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly string RepoRoot = FindRepoRoot();

    private static string AppPath(params string[] parts) =>
        Path.Combine(new[] { RepoRoot, "src", "FactorySim.App" }.Concat(parts).ToArray());

    [Fact]
    public void Icon_ico_holds_seven_png_sizes_with_matching_headers()
    {
        byte[] ico = File.ReadAllBytes(AppPath("branding", "icon.ico"));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        Assert.Equal(7, count);

        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (16 * i);
            int width = ico[entry] == 0 ? 256 : ico[entry];
            int height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 8));
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 12));
            Assert.Equal(width, height);
            Assert.True(offset + length <= ico.Length, $"entry {i} points past the end of the file");

            var (pngWidth, pngHeight, _) = ReadPngHeader(ico.AsSpan(offset, length));
            Assert.Equal(width, pngWidth);
            Assert.Equal(height, pngHeight);
            sizes.Add(width);
        }

        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, sizes.OrderBy(s => s));
    }

    [Fact]
    public void Splash_png_is_720_by_200_rgba()
    {
        byte[] png = File.ReadAllBytes(AppPath("branding", "splash.png"));

        var (width, height, colorType) = ReadPngHeader(png);

        Assert.Equal(720, width);
        Assert.Equal(200, height);
        Assert.Equal(6, colorType);
    }

    [Fact]
    public void Project_godot_points_at_the_branding_files()
    {
        string project = File.ReadAllText(AppPath("project.godot"));

        foreach (var (key, file) in new[]
        {
            ("config/icon", "icon.svg"),
            ("config/windows_native_icon", "icon.ico"),
            ("boot_splash/image", "splash.png"),
        })
        {
            Assert.Contains($"{key}=\"res://branding/{file}\"", project);
            Assert.True(File.Exists(AppPath("branding", file)), $"{file} is missing");
        }
    }

    [Fact]
    public void App_icon_svg_is_an_exact_copy_of_the_source()
    {
        byte[] source = File.ReadAllBytes(Path.Combine(RepoRoot, "assets", "branding", "factorysim-icon.svg"));
        byte[] copy = File.ReadAllBytes(AppPath("branding", "icon.svg"));

        Assert.Equal(source, copy);
    }

    private static (int Width, int Height, int ColorType) ReadPngHeader(ReadOnlySpan<byte> png)
    {
        Assert.True(png.Length > 33, "PNG too short");
        Assert.True(png[..8].SequenceEqual(PngSignature), "missing PNG signature");
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png.Slice(12, 4)));
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return (width, height, png[25]);
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
