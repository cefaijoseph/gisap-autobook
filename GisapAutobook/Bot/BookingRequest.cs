namespace GisapAutobook.Bot;

public record BookingRequest(
    int ScheduleId,
    string ResourceId,
    DateTime BookingDate,
    int StartHour,
    int EndHour,
    int NumberOfPersons);

public record BookingResult(bool IsSuccess, bool IsAlreadyBooked, bool IsNotOpenYet, string? ErrorMessage, string? ScreenshotPath, DateTime? AddToCartClickedAt = null)
{
    public static BookingResult Success(DateTime clickedAt) => new(true, false, false, null, null, clickedAt);
    public static BookingResult AlreadyBooked(DateTime clickedAt) => new(false, true, false, "Slot already booked by someone else", null, clickedAt);
    public static BookingResult NotOpenYet() => new(false, false, true, "Booking window not open yet", null);
    public static BookingResult Failure(string error, string? screenshot) => new(false, false, false, error, screenshot);
}
