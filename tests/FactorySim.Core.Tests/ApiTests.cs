using FactorySim.Core.Api;

namespace FactorySim.Core.Tests;

public class ApiTests
{
    [Theory]
    [InlineData("mvp_sorting")]
    [InlineData("assembly_line")]
    [InlineData("Mijn scène 2")]
    [InlineData("v1.2-test")]
    public void Plain_scene_names_are_valid(string name) => Assert.True(SceneName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:x")]
    [InlineData("user://scenes/x")]
    [InlineData(".hidden")]
    [InlineData(" space")]
    [InlineData("a..b")]
    [InlineData("tab\t")]
    public void Paths_and_odd_names_are_not(string? name) => Assert.False(SceneName.IsValid(name));

    [Fact]
    public void A_token_has_32_characters_and_differs_each_time()
    {
        string first = ApiToken.New();
        string second = ApiToken.New();
        Assert.Equal(ApiToken.Length, first.Length);
        Assert.All(first, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        Assert.NotEqual(first, second);
        Assert.True(ApiToken.Matches(first, first));
        Assert.False(ApiToken.Matches(first, second));
        Assert.False(ApiToken.Matches(first, null));
        Assert.False(ApiToken.Matches(first, first[..31]));
    }

    [Fact]
    public void The_path_is_split_before_it_is_unescaped()
    {
        Assert.Equal(new[] { "api", "tags", "Pusher back" }, HttpWire.Segments("/api/tags/Pusher%20back?x=1"));
        Assert.Equal(new[] { "api", "tags", "a/b", "force" }, HttpWire.Segments("/api/tags/a%2Fb/force"));
        Assert.Empty(HttpWire.Segments("/"));
    }

    [Fact]
    public void The_queue_runs_work_on_the_pumping_thread_and_passes_exceptions_back()
    {
        var queue = new MainThreadQueue(TimeSpan.FromSeconds(5));
        int pumpThread = 0;
        var pump = new Thread(() =>
        {
            pumpThread = Environment.CurrentManagedThreadId;
            for (int i = 0; i < 400 && queue.Pump() < 2; i++)
            {
                Thread.Sleep(5);
            }
        });
        pump.Start();

        Assert.True(queue.TryInvoke(() => Environment.CurrentManagedThreadId, out int ranOn));
        Assert.Throws<InvalidOperationException>(() => queue.TryInvoke<int>(() => throw new InvalidOperationException(), out _));
        pump.Join();
        Assert.Equal(pumpThread, ranOn);
    }

    [Fact]
    public void Work_that_times_out_never_runs_later()
    {
        var queue = new MainThreadQueue(TimeSpan.FromMilliseconds(50));
        bool ran = false;
        Assert.False(queue.TryInvoke(() => ran = true, out _));
        Assert.Equal(0, queue.Pump());
        Assert.False(ran);
        Assert.Equal(0, queue.Pending);
    }
}
