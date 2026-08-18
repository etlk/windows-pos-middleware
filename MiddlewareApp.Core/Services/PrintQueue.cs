namespace MiddlewareApp.Core.Services;

/// <summary>
/// Per-printer serial queue (spec §5.3): jobs targeting the same host:port run strictly
/// one after another with a 400 ms gap after each job; different printers print in
/// parallel; a failed job must not block subsequent jobs on that printer.
/// </summary>
public class PrintQueue
{
    private readonly Dictionary<string, Task> _tails = new();
    private readonly object _lock = new();
    private readonly Func<Task> _gapDelay;

    public PrintQueue(Func<Task>? gapDelay = null)
    {
        _gapDelay = gapDelay ?? (() => Task.Delay(400));
    }

    /// <summary>Returned task reflects the job's own outcome (faults propagate to the caller, not the chain).</summary>
    public Task Enqueue(string printerKey, Func<Task> job)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            var tail = _tails.TryGetValue(printerKey, out var t) ? t : Task.CompletedTask;
            _tails[printerKey] = RunAfter(tail, job, done);
        }
        return done.Task;
    }

    private async Task RunAfter(Task tail, Func<Task> job, TaskCompletionSource done)
    {
        await tail.ConfigureAwait(false); // chain tasks never fault
        try
        {
            await job().ConfigureAwait(false);
            done.TrySetResult();
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
        await _gapDelay().ConfigureAwait(false); // let the cutter finish before the next job
    }
}
