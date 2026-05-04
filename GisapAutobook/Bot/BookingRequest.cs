namespace GisapAutobook.Bot;

public record BookingRequest(
    int ScheduleId,
    string ResourceId,
    DateTime BookingDate,
    int StartHour,
    int EndHour,
    int NumberOfPersons);

public record BookingResult(
    bool IsSuccess,
    bool IsAlreadyBooked,
    bool IsNotOpenYet,
    string? ErrorMessage,
    string? ScreenshotPath,
    DateTime? AddToCartClickedAt = null,
    DateTime? BotStartedAt = null,
    DateTime? FormReadyAt = null,
    DateTime? WindowOpensUtc = null,
    int AttemptNumber = 1)
{
    public static BookingResult Success(DateTime clickedAt, DateTime botStarted, DateTime formReady, DateTime windowOpens, int attempt)
        => new(true, false, false, null, null, clickedAt, botStarted, formReady, windowOpens, attempt);
    public static BookingResult AlreadyBooked(DateTime clickedAt, DateTime botStarted, DateTime formReady, DateTime windowOpens, int attempt)
        => new(false, true, false, "Slot already booked by someone else", null, clickedAt, botStarted, formReady, windowOpens, attempt);
    public static BookingResult NotOpenYet(DateTime botStarted, DateTime formReady, DateTime windowOpens)
        => new(false, false, true, "Booking window not open yet", null, null, botStarted, formReady, windowOpens);
    public static BookingResult Failure(string error, string? screenshot)
       
    DateTime? AddToCartClickedAt = null,
    DateTime? BotStartedAt = null,
    DateTime? FormReadyAt = null,
    DateTime? WindowOpensUtc = null,
    int AttemptNumber = 1)
{
    public static BookingResult Success(DateTime clickedAt, DateTime botStarted, DateTime formReady, DateTime windowOpens, int attempt)
        => new(true, false, false, null, null, clickedAt, botStarted, formReady, windowOpens, attempt);
    public static BookingResult AlreadyBooked(DateTime clickedAt, DateTime botStarted, DateTime formReady, DateTime windowOpens, int attempt)
        => new(false, true, false, "Slot already booked by someone else", null, clickedAt, botStarted, formReady, windowOpens, attempt);
    public static BookingResult NotOpenYet(DateTime botStarted, DateTime formReady, DateTime windowOpens)
        => new(false, false, true, "Booking window not open yet", null, null, botStarted, formReady, windowOpens);
    public static BookingResult Failure(string error, string? screenshot)
        => new(false, false, false, error, screenshot);
}
