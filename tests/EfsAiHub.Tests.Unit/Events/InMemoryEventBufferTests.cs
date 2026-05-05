using System.Diagnostics;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Infra.Messaging.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Events;

public sealed class InMemoryEventBufferTests
{
    private static InMemoryEventBuffer Make(int maxLen = 5000)
    {
        var opts = Options.Create(new InMemoryEventBufferOptions
        {
            MaxLengthPerStream = maxLen,
            RetentionAfterTerminalMinutes = 30,
            IdleEvictionAfterMinutes = 120,
        });
        return new InMemoryEventBuffer(opts, NullLogger<InMemoryEventBuffer>.Instance);
    }

    private static BufferEvent Ev(string type, string payload = "{}")
        => new(type, payload, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Append_Read_Basic()
    {
        var buf = Make();
        await buf.AppendAsync("k1", Ev("a"));
        await buf.AppendAsync("k1", Ev("b"));

        var page = await buf.ReadSinceAsync("k1", since: 0, limit: 10, waitFor: TimeSpan.Zero);

        page.Events.Should().HaveCount(2);
        page.Events[0].Type.Should().Be("a");
        page.Events[1].Type.Should().Be("b");
        page.NextSince.Should().Be(2);
        page.Terminal.Should().BeFalse();
    }

    [Fact]
    public async Task Read_Cursor_Filters_By_Since()
    {
        var buf = Make();
        await buf.AppendAsync("k", Ev("a"));
        await buf.AppendAsync("k", Ev("b"));
        await buf.AppendAsync("k", Ev("c"));

        var page = await buf.ReadSinceAsync("k", since: 1, limit: 10, waitFor: TimeSpan.Zero);

        page.Events.Should().HaveCount(2);
        page.Events[0].Seq.Should().Be(2);
        page.Events[1].Seq.Should().Be(3);
        page.NextSince.Should().Be(3);
    }

    [Fact]
    public async Task DropOldest_When_Exceeds_MaxLen()
    {
        var buf = Make(maxLen: 3);
        await buf.AppendAsync("k", Ev("a"));
        await buf.AppendAsync("k", Ev("b"));
        await buf.AppendAsync("k", Ev("c"));
        await buf.AppendAsync("k", Ev("d"));

        var page = await buf.ReadSinceAsync("k", since: 0, limit: 10, waitFor: TimeSpan.Zero);

        page.Events.Should().HaveCount(3);
        page.Events[0].Type.Should().Be("b");
        page.Events[2].Type.Should().Be("d");
        page.NextSince.Should().Be(4);
        buf.OverflowCount.Should().Be(1);
    }

    [Fact]
    public async Task LongPoll_Wakeup_When_Append_During_Wait()
    {
        var buf = Make();
        var sw = Stopwatch.StartNew();

        var readTask = buf.ReadSinceAsync("k", since: 0, limit: 10, waitFor: TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        await buf.AppendAsync("k", Ev("hi"));

        var page = await readTask;
        sw.Stop();

        page.Events.Should().HaveCount(1);
        page.Events[0].Type.Should().Be("hi");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LongPoll_Timeout_Returns_Empty()
    {
        var buf = Make();
        var sw = Stopwatch.StartNew();

        var page = await buf.ReadSinceAsync("k", since: 0, limit: 10, waitFor: TimeSpan.FromMilliseconds(200));
        sw.Stop();

        page.Events.Should().BeEmpty();
        page.NextSince.Should().Be(0);
        page.Terminal.Should().BeFalse();
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task MarkTerminal_Reflects_In_Subsequent_Reads_And_Rejects_Append()
    {
        var buf = Make();
        await buf.AppendAsync("k", Ev("a"));
        await buf.MarkTerminalAsync("k");

        var page = await buf.ReadSinceAsync("k", since: 0, limit: 10, waitFor: TimeSpan.Zero);
        page.Terminal.Should().BeTrue();
        page.Events.Should().HaveCount(1);

        Func<Task> appendAfterTerminal = () => buf.AppendAsync("k", Ev("late"));
        await appendAfterTerminal.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LongPoll_Wakeup_On_MarkTerminal()
    {
        var buf = Make();
        var sw = Stopwatch.StartNew();

        var readTask = buf.ReadSinceAsync("k", since: 0, limit: 10, waitFor: TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        await buf.MarkTerminalAsync("k");

        var page = await readTask;
        sw.Stop();

        page.Terminal.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
