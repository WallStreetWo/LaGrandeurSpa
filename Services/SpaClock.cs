namespace LaGrandeurSpa.Web.Services;

public sealed class SpaClock : ISpaClock
{
    private static readonly string[] FallbackZoneIds =
    [
        "Africa/Johannesburg",
        "South Africa Standard Time"
    ];

    private readonly TimeZoneInfo _zone;

    public SpaClock(IConfiguration configuration)
    {
        var requested = configuration["Business:TimeZone"]?.Trim();
        _zone = ResolveZone(requested);
    }

    public DateTime NowLocal => ToLocal(DateTime.UtcNow);
    public DateTime TodayLocal => NowLocal.Date;

    public DateTime ToLocal(DateTime utc)
    {
        var utcValue = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(utcValue, _zone),
            DateTimeKind.Unspecified);
    }

    public DateTime ToUtc(DateTime local)
    {
        var localValue = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localValue, _zone);
    }

    private static TimeZoneInfo ResolveZone(string? requested)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(requested))
            candidates.Add(requested);

        candidates.AddRange(FallbackZoneIds.Where(x =>
            !candidates.Contains(x, StringComparer.OrdinalIgnoreCase)));

        foreach (var id in candidates)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next cross-platform identifier.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the next cross-platform identifier.
            }
        }

        throw new InvalidOperationException(
            "The configured La Grandeur Spa timezone could not be resolved. " +
            "Use 'Africa/Johannesburg' (Linux/macOS) or 'South Africa Standard Time' (Windows)." );
    }
}
