using System.Collections.Concurrent;

namespace HackITSentry.Server.Services;

/// <summary>
/// Singleton that lets the server instantly wake a waiting agent when a command is queued.
/// Each device can have at most one long-poll connection at a time.
/// </summary>
public sealed class AgentCommandNotifier
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _waiting = new();

    /// <summary>
    /// Called by the agent's long-poll endpoint. Blocks until NotifyDevice is called
    /// or the cancellation token fires (timeout / client disconnect).
    /// Returns true if a command was signalled, false on timeout.
    /// </summary>
    public async Task<bool> WaitAsync(Guid deviceId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Replace any existing waiter for this device (reconnect case)
        if (_waiting.TryRemove(deviceId, out var old))
            old.TrySetResult(false);

        _waiting[deviceId] = tcs;

        try
        {
            using var reg = ct.Register(() => tcs.TrySetResult(false));
            return await tcs.Task;
        }
        finally
        {
            _waiting.TryRemove(deviceId, out _);
        }
    }

    /// <summary>Called after a command is saved to DB to wake the waiting agent immediately.</summary>
    public void NotifyDevice(Guid deviceId)
    {
        if (_waiting.TryRemove(deviceId, out var tcs))
            tcs.TrySetResult(true);
    }
}
