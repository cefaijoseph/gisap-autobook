namespace GisapAutobook.Models;

public class Schedule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ResourceId { get; set; } = "241563";
    public bool IsActive { get; set; } = true;

    /// <summary>Day of week on which the booking attempt is triggered.</summary>
    public DayOfWeek TriggerDayOfWeek { get; set; }

    /// <summary>UTC time of day at which the booking attempt fires.</summary>
    public TimeSpan TriggerTime { get; set; }

    /// <summary>How many days ahead to book (default 14 — GISAP opens slots 2 weeks in advance).</summary>
    public int DaysInAdvance { get; set; } = 14;

    /// <summary>The specific date to book the padel court slot on.</summary>
    public DateTime BookingDate { get; set; }

    /// <summary>Desired booking start hour (6–21).</summary>
    public int StartHour { get; set; } = 8;

    /// <summary>Desired booking end hour (6–21).</summary>
    public int EndHour { get; set; } = 10;

    public int NumberOfPersons { get; set; } = 4;

    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public string? LastRunStatus { get; set; }

    public ICollection<BookingLog> BookingLogs { get; set; } = new List<BookingLog>();
}
