using System;

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
    public static RetryPolicy Aggressive { get; } = new()
    {
        MaxRetries = 10,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(60),
        BackoffMultiplier = 2.0
    };

    /// <summary>
    ///     Maximum number of retry attempts per chunk. Default is 5.
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    ///     Initial delay before the first retry. Default is 1 second.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Maximum delay between retries. Default is 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Multiplier for exponential backoff. Default is 2.0 (doubles each retry).
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    ///     Whether to add random jitter to delays to prevent thundering herd. Default is true.
    /// </summary>
    public bool UseJitter { get; init; } = true;

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
        var delayMs = InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt);

        // Cap at max delay
        delayMs = Math.Min(delayMs, MaxDelay.TotalMilliseconds);

        // Add jitter if enabled (±25%)
        if (UseJitter)
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
        bool useJitter = true) => new()
    {
        MaxRetries = maxRetries,
        InitialDelay = initialDelay ?? TimeSpan.FromSeconds(1),
        MaxDelay = maxDelay ?? TimeSpan.FromSeconds(30),
        BackoffMultiplier = backoffMultiplier,
        UseJitter = useJitter
    };
}
