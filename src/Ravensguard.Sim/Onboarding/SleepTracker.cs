namespace Ravensguard.Sim.Onboarding;

/// <summary>
/// Notices the first night (onboarding step 8) until the sleep verb of the camp spec exists: the hero counts as asleep after standing still, at night (20:00 to 08:00) under a roof or at a camp, for six game hours in a row.
/// The game and the bot feed it once a game hour; any movement of more than five metres in that hour starts the count again.
/// </summary>
public sealed class SleepTracker
{
    private int _hours;
    private long _lastX = long.MinValue, _lastY;
    public int RestedHours => _hours;

    /// <summary>One game hour has passed with the hero at (<paramref name="xMm"/>, <paramref name="yMm"/>). Returns the length of the rest when it reaches <paramref name="needHours"/>, else 0.</summary>
    public int Feed(long nowSec, long xMm, long yMm, bool sheltered, int needHours = 6)
    {
        int hod = (int)(nowSec / 3600 % 24);
        bool night = hod >= 20 || hod < 8;
        bool still = _lastX != long.MinValue && Math.Abs(xMm - _lastX) <= 5000 && Math.Abs(yMm - _lastY) <= 5000;
        _lastX = xMm; _lastY = yMm;
        if (night && still && sheltered) _hours++; else _hours = 0;
        return _hours >= needHours ? _hours : 0;
    }
}
