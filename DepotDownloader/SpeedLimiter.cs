using System;
using System.Threading;
using System.Threading.Tasks;

namespace DepotDownloader.Lib;

/// <summary>
///     Token bucket rate limiter for controlling download speed.
/// </summary>
internal sealed class SpeedLimiter : IDisposable
{
    private readonly long _bucketSize;
    private readonly long _bytesPerSecond;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _availableTokens;
    private bool _disposed;
    private DateTime _lastRefill;

    /// <summary>
    ///     Creates a new speed limiter.
    /// </summary>
    /// <param name="bytesPerSecond">Maximum bytes per second. Must be positive.</param>
    public SpeedLimiter(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), "Speed limit must be positive.");

        _bytesPerSecond = bytesPerSecond;
        // Allow bursts of up to 1 second worth of data
        _bucketSize = bytesPerSecond;
        _availableTokens = _bucketSize;
        _lastRefill = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lock.Dispose();
    }

    /// <summary>
    ///     Waits until the specified number of bytes can be consumed without exceeding the rate limit.
    /// </summary>
    /// <param name="bytes">Number of bytes to consume.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WaitAsync(int bytes, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            ObjectDisposedException.ThrowIf(_disposed, nameof(SpeedLimiter));

        if (bytes <= 0)
            return;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RefillTokens();

                if (_availableTokens >= bytes)
                {
                    _availableTokens -= bytes;
                    return;
                }

                // Calculate wait time for required tokens
                var tokensNeeded = bytes - _availableTokens;
                var waitMs = (int)Math.Ceiling(tokensNeeded * 1000.0 / _bytesPerSecond);

                // Release lock while waiting
                _lock.Release();
                try
                {
                    await Task.Delay(Math.Max(1, waitMs), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (_lock.CurrentCount == 0)
                _lock.Release();
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;

        if (elapsed > 0)
        {
            var newTokens = (long)(elapsed * _bytesPerSecond);
            _availableTokens = Math.Min(_bucketSize, _availableTokens + newTokens);
            _lastRefill = now;
        }
    }
}