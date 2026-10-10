namespace CentralPackageMigrator.TestUtilities;

/// <summary>
/// A <see cref="TimeProvider"/> that always returns the same instant, for deterministic plans.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset DefaultInstant = new(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset? now = null)
    {
        _now = now ?? DefaultInstant;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}
