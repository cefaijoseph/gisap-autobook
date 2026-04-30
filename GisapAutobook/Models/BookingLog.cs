namespace GisapAutobook.Models;

public class BookingLog
{
    public int Id { get; set; }
    public int? ScheduleId { get; set; }
    public Schedule? Schedule { get; set; }
    public DateTime RunAt { get; set; }

    // Denormalized so the log survives schedule deletion
    public string ScheduleName { get; set; } = string.Empty;
    public DateTime BookingDate { get; set; }
    public int StartHour { get; set; }
    public int EndHour { get; set; }

    /// <summary>Pending | Success | Taken | NotOpened | Failed</summary>
    public string Status { get; set; } = "Pending";

    public string? ErrorMessage { get; set; }
    public string? ScreenshotPath { get; set; }
}
