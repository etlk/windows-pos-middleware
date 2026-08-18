using MiddlewareApp.Core.Services;
using Xunit;

namespace MiddlewareApp.Core.Tests;

public class PrintQueueTests
{
    [Fact]
    public async Task SamePrinter_JobsRunStrictlySerially()
    {
        var queue = new PrintQueue(gapDelay: () => Task.CompletedTask);
        var active = 0;
        var maxActive = 0;
        var gate = new object();

        async Task Job()
        {
            lock (gate) { active++; maxActive = Math.Max(maxActive, active); }
            await Task.Delay(30);
            lock (gate) { active--; }
        }

        var tasks = Enumerable.Range(0, 5).Select(_ => queue.Enqueue("10.0.0.5:9100", Job)).ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task DifferentPrinters_RunInParallel()
    {
        var queue = new PrintQueue(gapDelay: () => Task.CompletedTask);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task Job()
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.TrySetResult();
            // Each job waits until BOTH have started — only possible when parallel.
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var a = queue.Enqueue("10.0.0.1:9100", Job);
        var b = queue.Enqueue("10.0.0.2:9100", Job);
        await Task.WhenAll(a, b);
    }

    [Fact]
    public async Task FailedJob_DoesNotBlockNextJobOnSamePrinter()
    {
        var queue = new PrintQueue(gapDelay: () => Task.CompletedTask);

        var first = queue.Enqueue("k", () => throw new InvalidOperationException("boom"));
        var ranSecond = false;
        var second = queue.Enqueue("k", () => { ranSecond = true; return Task.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;
        Assert.True(ranSecond);
    }

    [Fact]
    public async Task GapDelay_RunsBetweenJobsOnSamePrinter()
    {
        var gaps = 0;
        var queue = new PrintQueue(gapDelay: () => { Interlocked.Increment(ref gaps); return Task.CompletedTask; });

        var order = new List<int>();
        await queue.Enqueue("k", () => { lock (order) order.Add(1); return Task.CompletedTask; });
        await queue.Enqueue("k", () => { lock (order) order.Add(2); return Task.CompletedTask; });

        Assert.Equal(new[] { 1, 2 }, order);
        Assert.True(gaps >= 1); // a gap ran after the first job before the second's completion was observed
    }
}
