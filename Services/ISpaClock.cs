namespace LaGrandeurSpa.Web.Services;

public interface ISpaClock
{
    DateTime NowLocal { get; }
    DateTime TodayLocal { get; }
    DateTime ToLocal(DateTime utc);
    DateTime ToUtc(DateTime local);
}
