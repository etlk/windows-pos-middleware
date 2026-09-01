using System.Threading;

namespace MiddlewareApp.Services;

/// <summary>
/// Ensures only one middleware process listens on Pusher. A second launch signals
/// the running instance to open its window, then exits immediately.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\ET.CloudPOS.Middleware.SingleInstance";
    private const string ShowEventName = @"Local\ET.CloudPOS.Middleware.Show";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static CancellationTokenSource? _watchCts;

    /// <summary>Returns true when this process should start normally.</summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>Ask the already-running instance to show its main window.</summary>
    public static void NotifyExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (Exception)
        {
            // First instance still starting, or named event not created yet.
        }
    }

    /// <summary>Listen for show requests from subsequent launches (first instance only).</summary>
    public static void StartWatching(Action onShowRequested)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _watchCts = new CancellationTokenSource();
        var token = _watchCts.Token;

        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_showEvent.WaitOne(500))
                        onShowRequested();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, token);
    }

    public static void Dispose()
    {
        try
        {
            _watchCts?.Cancel();
            _watchCts?.Dispose();
            _watchCts = null;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _showEvent?.Dispose();
            _showEvent = null;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch
        {
            /* ignore */
        }
    }
}
