namespace Ravensguard.Sim.Core;

public enum Season { Spring, Summer, Autumn, Winter }

public enum SimSpeed { Paused = 0, X1 = 1, X2 = 2, X4 = 4, X8 = 8 }

/// <summary>Calendar constants from plan §1.</summary>
public static class TimeConst
{
    public const int GameSecondsPerRealSecond = 20;
    public const long SecondsPerMinute = 60;
    public const long SecondsPerHour = 3600;
    public const long SecondsPerDay = 86400;
    public const int DaysPerSeason = 15;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
    public const long SecondsPerSeason = SecondsPerDay * DaysPerSeason;
    public const long SecondsPerYear = SecondsPerDay * DaysPerYear;
}

/// <summary>A point in time as game seconds since year 0, day 1, 00:00.</summary>
public readonly record struct GameTime(long Seconds) : IComparable<GameTime>
{
    public static GameTime FromGameSeconds(long s) => new(s);
    public static GameTime FromGameMinutes(long m) => new(m * TimeConst.SecondsPerMinute);
    public static GameTime FromGameHours(long h) => new(h * TimeConst.SecondsPerHour);
    public static GameTime FromGameDays(long d) => new(d * TimeConst.SecondsPerDay);
    public static GameTime FromRealSeconds(long s) => new(s * TimeConst.GameSecondsPerRealSecond);
    public static GameTime FromCalendar(int year, Season season, int dayOfSeason, int hour = 0, int minute = 0)
        => new(year * TimeConst.SecondsPerYear + (int)season * TimeConst.SecondsPerSeason
               + (dayOfSeason - 1) * TimeConst.SecondsPerDay + hour * TimeConst.SecondsPerHour + minute * TimeConst.SecondsPerMinute);

    public long TotalDays => Math.DivRem(Seconds, TimeConst.SecondsPerDay, out _);
    public long TotalHours => Seconds / TimeConst.SecondsPerHour;
    public int Year => (int)(Seconds / TimeConst.SecondsPerYear);
    public int DayOfYear => (int)(TotalDays % TimeConst.DaysPerYear) + 1;       // 1..60
    public Season Season => (Season)((DayOfYear - 1) / TimeConst.DaysPerSeason);
    public int DayOfSeason => (DayOfYear - 1) % TimeConst.DaysPerSeason + 1;    // 1..15
    public int Hour => (int)(Seconds % TimeConst.SecondsPerDay / TimeConst.SecondsPerHour);
    public int Minute => (int)(Seconds % TimeConst.SecondsPerHour / TimeConst.SecondsPerMinute);

    public int CompareTo(GameTime other) => Seconds.CompareTo(other.Seconds);
    public static GameTime operator +(GameTime t, Duration d) => new(t.Seconds + d.GameSeconds);
    public static GameTime operator -(GameTime t, Duration d) => new(t.Seconds - d.GameSeconds);
    public static Duration operator -(GameTime a, GameTime b) => new(a.Seconds - b.Seconds);
    public static bool operator <(GameTime a, GameTime b) => a.Seconds < b.Seconds;
    public static bool operator >(GameTime a, GameTime b) => a.Seconds > b.Seconds;
    public static bool operator <=(GameTime a, GameTime b) => a.Seconds <= b.Seconds;
    public static bool operator >=(GameTime a, GameTime b) => a.Seconds >= b.Seconds;
    public override string ToString() => $"Y{Year} {Season} {DayOfSeason:00} {Hour:00}:{Minute:00}";
}

/// <summary>A span of game time with explicit-unit constructors.</summary>
public readonly record struct Duration(long GameSeconds)
{
    public static Duration FromGameSeconds(long s) => new(s);
    public static Duration FromGameMinutes(long m) => new(m * TimeConst.SecondsPerMinute);
    public static Duration FromGameHours(long h) => new(h * TimeConst.SecondsPerHour);
    public static Duration FromGameDays(long d) => new(d * TimeConst.SecondsPerDay);
    public static Duration FromRealSeconds(long s) => new(s * TimeConst.GameSecondsPerRealSecond);
    public static Duration FromRealMinutes(long m) => FromRealSeconds(m * 60);

    public long ToGameHours() => GameSeconds / TimeConst.SecondsPerHour;
    public long ToGameDays() => GameSeconds / TimeConst.SecondsPerDay;
    public long ToRealSeconds() => GameSeconds / TimeConst.GameSecondsPerRealSecond;
    public static Duration operator +(Duration a, Duration b) => new(a.GameSeconds + b.GameSeconds);
    public static Duration operator -(Duration a, Duration b) => new(a.GameSeconds - b.GameSeconds);
}

/// <summary>Advances game time from real time × speed; reports hour boundaries crossed.</summary>
public sealed class SimClock
{
    // Sub-second remainder kept in game milliseconds so no time is lost between frames.
    private long _gameMs;

    public GameTime Now { get; private set; }
    public SimSpeed Speed { get; set; } = SimSpeed.X1;

    public SimClock(GameTime start = default) { Now = start; }

    /// <summary>Restores a saved clock including the sub-second remainder (game milliseconds, 0..999).</summary>
    public SimClock(GameTime start, long gameMsRemainder) { Now = start; _gameMs = gameMsRemainder; }

    /// <summary>Sub-second remainder in game milliseconds; saved so a reload loses no time.</summary>
    public long GameMsRemainder => _gameMs;

    /// <summary>Advance by realSeconds × speed. Returns the number of game-hour boundaries crossed.</summary>
    public int Advance(double realSeconds)
    {
        if (Speed == SimSpeed.Paused || realSeconds <= 0) return 0;
        _gameMs += (long)Math.Round(realSeconds * TimeConst.GameSecondsPerRealSecond * (int)Speed * 1000.0);
        long wholeSeconds = _gameMs / 1000;
        _gameMs %= 1000;
        long before = Now.TotalHours;
        Now = new GameTime(Now.Seconds + wholeSeconds);
        return (int)(Now.TotalHours - before);
    }
}
