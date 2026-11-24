using FactorySim.Core.Diagnostics;
using Xunit;

namespace FactorySim.Core.Tests;

/// <summary>Plan 31: errors in the status line, and the crash report with the path to the log.</summary>
public class CrashReportTests
{
    private static readonly DateTime When = new(2026, 11, 23, 21, 5, 9);

    [Fact]
    public void An_empty_feed_has_nothing_to_show()
    {
        var feed = new ErrorFeed();

        Assert.Null(feed.TakeChanged());
        Assert.Equal(0, feed.Total);
    }

    [Fact]
    public void The_newest_error_is_shown_once()
    {
        var feed = new ErrorFeed();
        feed.Report("Eerste fout");
        feed.Report("Tweede fout");

        ErrorNotice? notice = feed.TakeChanged();

        Assert.Equal(new ErrorNotice("Tweede fout", 1), notice);
        Assert.Null(feed.TakeChanged());
        Assert.Equal(2, feed.Total);
    }

    [Fact]
    public void A_repeated_error_is_counted_not_queued()
    {
        var feed = new ErrorFeed();
        for (int i = 0; i < 12; i++)
        {
            feed.Report("Elke frame dezelfde fout");
        }

        Assert.Equal(new ErrorNotice("Elke frame dezelfde fout", 12), feed.TakeChanged());
        feed.Report("Elke frame dezelfde fout");
        Assert.Equal(new ErrorNotice("Elke frame dezelfde fout", 13), feed.TakeChanged());
    }

    [Fact]
    public void Reports_from_many_threads_are_all_counted()
    {
        var feed = new ErrorFeed();

        Parallel.For(0, 1000, i => feed.Report(i % 2 == 0 ? "a" : "b"));

        Assert.Equal(1000, feed.Total);
        Assert.NotNull(feed.TakeChanged());
    }

    [Fact]
    public void Blank_reports_are_ignored()
    {
        var feed = new ErrorFeed();
        feed.Report("  ");
        feed.Report(null);

        Assert.Null(feed.TakeChanged());
    }

    [Fact]
    public void The_status_text_is_one_short_line_with_the_count_and_details()
    {
        string text = ErrorFeed.StatusText(new ErrorNotice("System.InvalidOperationException: kapot\n   at Iets.Doe()", 3));

        Assert.Equal("Fout: System.InvalidOperationException: kapot (×3) — Details opent het log", text);
        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("FAILED", text);
    }

    [Fact]
    public void A_long_message_is_cut()
    {
        string text = ErrorFeed.StatusText(new ErrorNotice(new string('x', 500), 1));

        Assert.True(text.Length < 220, text);
        Assert.Contains("…", text);
        Assert.DoesNotContain("(×", text);
    }

    [Fact]
    public void The_crash_file_holds_the_version_time_and_exception()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fs-crash-" + Guid.NewGuid().ToString("N"));
        try
        {
            Exception error;
            try
            {
                throw new InvalidOperationException("geforceerde fout");
            }
            catch (InvalidOperationException e)
            {
                error = e;
            }

            string path = CrashReport.Write(folder, error, When);

            Assert.Equal(Path.Combine(folder, "crash-20261123-210509.txt"), path);
            string content = File.ReadAllText(path);
            Assert.Contains(AppInfo.Version, content);
            Assert.Contains("2026-11-23 21:05:09", content);
            Assert.Contains("geforceerde fout", content);
            Assert.Contains(nameof(The_crash_file_holds_the_version_time_and_exception), content);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_alert_names_the_crash_file_the_log_folder_and_asks_to_send_them()
    {
        string text = CrashReport.AlertText(@"C:\logs\crash-20261123-210509.txt", @"C:\logs");

        Assert.Contains(@"C:\logs\crash-20261123-210509.txt", text);
        Assert.Contains(@"C:\logs", text);
        Assert.Contains("mee", text);
        Assert.Contains(AppInfo.Version, text);
    }

    [Fact]
    public void Without_a_crash_file_the_alert_names_the_log_folder()
    {
        string text = CrashReport.AlertText(null, @"C:\logs");

        Assert.Contains(@"C:\logs", text);
        Assert.DoesNotContain("crash-", text);
    }

    [Fact]
    public void A_startup_failure_names_the_reason_and_the_log_folder()
    {
        string text = CrashReport.StartupText("Cannot load scene res://scenes/x.fsscene.json: kapot", @"C:\logs");

        Assert.Contains("kapot", text);
        Assert.Contains(@"C:\logs", text);
    }
}
