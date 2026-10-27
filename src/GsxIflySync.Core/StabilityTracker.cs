namespace GsxIflySync.Core;

/// <summary>Une valeur est stable si elle n'a pas changé depuis au moins la durée demandée.</summary>
public sealed class StabilityTracker(TimeSpan required)
{
    private double? _value;
    private DateTimeOffset _since;

    public void Observe(double? value, DateTimeOffset now)
    {
        if (value != _value)
        {
            _value = value;
            _since = now;
        }
    }

    public bool IsStable(DateTimeOffset now) => _value.HasValue && now - _since >= required;

    public TimeSpan StableFor(DateTimeOffset now) => _value.HasValue ? now - _since : TimeSpan.Zero;

    public void Reset()
    {
        _value = null;
    }
}
