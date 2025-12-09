using System;
using System.Diagnostics.CodeAnalysis;

namespace DepotDownloader.Lib;

/// <summary>
///     Configuration for retry behavior during downloads.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    ///     Default retry policy with sensible defaults.
    /// </summary>
    public static RetryPolicy Default { get; } = new();

    /// <summary>
    ///     No retries - fail immediately on first error.
    /// </summary>
    public static RetryPolicy None { get; } = new() { MaxRetries = 0 };

    /// <summary>
    ///     Aggressive retry policy for unreliable connections.
    /// </summary>
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members")]
    public static RetryPolicy Aggressive { get; } = new()
    {
        MaxRetries = 10,
        InitialDelayValue = TimeSpan.FromMilliseconds(500),
        MaxDelayValue = TimeSpan.FromSeconds(60),
        BackoffMultiplierValue = 2.0
    };

    /// <summary>
    ///     Maximum number of retry attempts per chunk. Default is 5.
    /// </summary>
    public int MaxRetries { get; private init; } = 5;

    // Internal configuration - used by GetDelay()
    private TimeSpan InitialDelayValue { get; init; } = TimeSpan.FromSeconds(1);
    private TimeSpan MaxDelayValue { get; init; } = TimeSpan.FromSeconds(30);
    private double BackoffMultiplierValue { get; init; } = 2.0;
    private bool UseJitterValue { get; init; } = true;

    /// <summary>
    ///     Calculates the delay for a given retry attempt.
    /// </summary>
    /// <param name="attempt">The retry attempt number (0-based).</param>
    /// <returns>The delay to wait before retrying.</returns>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 0)
            return TimeSpan.Zero;

        // Calculate exponential backoff
        var delayMs = InitialDelayValue.TotalMilliseconds * Math.Pow(BackoffMultiplierValue, attempt);

        // Cap at max delay
        delayMs = Math.Min(delayMs, MaxDelayValue.TotalMilliseconds);

        // Add jitter if enabled (±25%)
        if (UseJitterValue)
        {
            var jitter = delayMs * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
            delayMs += jitter;
        }

        return TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
    }

    /// <summary>
    ///     Creates a retry policy with custom settings.
    /// </summary>
    public static RetryPolicy Create(
        int maxRetries = 5,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true)
    {
        return new RetryPolicy
        {
            MaxRetries = maxRetries,
            InitialDelayValue = initialDelay ?? TimeSpan.FromSeconds(1),
            MaxDelayValue = maxDelay ?? TimeSpan.FromSeconds(30),
            BackoffMultiplierValue = backoffMultiplier,
            UseJitterValue = useJitter
        };
    }
}