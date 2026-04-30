using GisapAutobook.Bot;
using GisapAutobook.Data;
using GisapAutobook.Models;
using Microsoft.EntityFrameworkCore;

namespace GisapAutobook.Services;

public class BookingSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingSchedulerService> _logger;
    private readonly ITelegramNotifier _notifier;
    private readonly IConfiguration _config;
    private readonly SchedulerWakeService _wake;

    public BookingSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingSchedulerService> logger,
        ITelegramNotifier notifier,
        IConfiguration config,
        SchedulerWakeService wake)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _notifier = notifier;
        _config = config;
        _wake = wake;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Booking scheduler service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in scheduler tick");
            }

            try
            {
                // Sleep precisely until 2s before the next scheduled job, capped at 5 minutes.
                // This ensures we wake up right on time for competitive booking slots.
                var sleepDuration = await GetSleepDurationAsync(stoppingToken);
                _logger.LogDebug("Next tick in {Seconds:F1}s", sleepDuration.TotalSeconds);
                await _wake.WaitAsync(sleepDuration, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Booking scheduler service stopped");
    }

    private async Task<TimeSpan> GetSleepDurationAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var headStartSeconds = _config.GetValue("Bot:TriggerHeadStartSeconds", 2);
            var maxSleepSeconds = _config.GetValue("Bot:MaxSchedulerSleepSeconds", 300);

            var nextRun = await db.Schedules
                .Where(s => s.IsActive && s.NextRunAt != null)
                .MinAsync(s => (DateTime?)s.NextRunAt, ct);

            if (nextRun == null)
                return TimeSpan.FromSeconds(maxSleepSeconds);

            var sleepUntil = nextRun.Value.AddSeconds(-headStartSeconds);
            var sleep = sleepUntil - DateTime.UtcNow;

            if (sleep <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return sleep < TimeSpan.FromSeconds(maxSleepSeconds)
                ? sleep
                : TimeSpan.FromSeconds(maxSleepSeconds);
        }
        catch
        {
            return TimeSpan.FromSeconds(60);
        }
    }

    private async Task ProcessDueSchedulesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var dueSchedules = await db.Schedules
            .Where(s => s.IsActive && s.NextRunAt != null && s.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var schedule in dueSchedules)
        {
            // Re-scope per schedule so DB context is not shared across async bot calls
            using var scheduleScope = _scopeFactory.CreateScope();
            var scheduleDb = scheduleScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var config = scheduleScope.ServiceProvider.GetRequiredService<IConfiguration>();
            var botLogger = scheduleScope.ServiceProvider.GetRequiredService<ILogger<GisapBot>>();

            await ProcessScheduleAsync(scheduleDb, schedule, config, botLogger, ct);
        }
    }

    private async Task ProcessScheduleAsync(
        AppDbContext db,
        Schedule schedule,
        IConfiguration config,
        ILogger<GisapBot> botLogger,
        CancellationToken ct)
    {
        _logger.LogInformation("Running schedule '{Name}' (ID: {Id})", schedule.Name, schedule.Id);

        // Re-fetch to ensure we have a fresh tracked entity in this scope
        var trackedSchedule = await db.Schedules.FindAsync(new object[] { schedule.Id }, ct);
        if (trackedSchedule == null) return;

        var log = new BookingLog
        {
            ScheduleId = trackedSchedule.Id,
            RunAt = DateTime.UtcNow,
            Status = "Pending",
            ScheduleName = trackedSchedule.Name,
            BookingDate = trackedSchedule.BookingDate.Date,
            StartHour = trackedSchedule.StartHour,
            EndHour = trackedSchedule.EndHour,
        };
        db.BookingLogs.Add(log);
        await db.SaveChangesAsync(ct);

        var request = new BookingRequest(
            ScheduleId: trackedSchedule.Id,
            ResourceId: trackedSchedule.ResourceId,
            BookingDate: trackedSchedule.BookingDate.Date,
            StartHour: trackedSchedule.StartHour,
            EndHour: trackedSchedule.EndHour,
            NumberOfPersons: trackedSchedule.NumberOfPersons);

        var bot = new GisapBot(config, botLogger);
        var result = BookingResult.Failure("Not started", null);
        int retryCount = _config.GetValue("Bot:RetryCount", 5);
        int maxAttempts = Math.Max(1, retryCount + 1);
        var bookingDate = request.BookingDate.ToString("dd MMM yyyy");

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Check if the schedule was deleted mid-run
            var stillExists = await db.Schedules.AsNoTracking()
                .AnyAsync(s => s.Id == trackedSchedule.Id, ct);
            if (!stillExists)
            {
                _logger.LogWarning("Schedule '{Name}' (ID: {Id}) was deleted — stopping retries.", trackedSchedule.Name, trackedSchedule.Id);
                return;
            }

            _logger.LogInformation("Attempt {Attempt}/{Max} for schedule '{Name}'",
                attempt, maxAttempts, trackedSchedule.Name);

            result = await bot.BookAsync(request, ct);

            if (result.IsSuccess || result.IsAlreadyBooked || result.IsNotOpenYet) break;

            _logger.LogWarning("Attempt {Attempt} failed: {Error}", attempt, result.ErrorMessage);

            if (attempt < maxAttempts)
            {
                var failRetryDelaySec = _config.GetValue("Bot:RetryDelaySeconds", 30);
                await Task.Delay(TimeSpan.FromSeconds(failRetryDelaySec), ct);
            }
        }

        log.Status = result.IsSuccess ? "Success" : result.IsAlreadyBooked ? "Taken" : result.IsNotOpenYet ? "NotOpened" : "Failed";
        log.ErrorMessage = result.ErrorMessage;
        log.ScreenshotPath = result.ScreenshotPath;

        trackedSchedule.LastRunAt = DateTime.UtcNow;
        trackedSchedule.LastRunStatus = log.Status;
        await db.SaveChangesAsync(ct);

        // One-shot: delete the schedule now that it has run
        db.Schedules.Remove(trackedSchedule);
        await db.SaveChangesAsync(ct);

        // Telegram notification
        string telegramMsg;
        var clickedAtStr = result.AddToCartClickedAt.HasValue
            ? $"\n⏱ Add to cart clicked at: <code>{result.AddToCartClickedAt.Value:HH:mm:ss.fff} UTC</code>"
            : "";
        if (result.IsSuccess)
            telegramMsg = $"✅ <b>Booking succeeded!</b>\n" +
                          $"<b>{trackedSchedule.Name}</b>\n" +
                          $"Court <code>{request.ResourceId}</code> · {bookingDate}\n" +
                          $"Slot: <b>{request.StartHour}:00–{request.EndHour}:00</b> · {request.NumberOfPersons} persons" +
                          clickedAtStr;
        else if (result.IsAlreadyBooked)
            telegramMsg = $"⚠️ <b>Slot already taken!</b>\n" +
                          $"<b>{trackedSchedule.Name}</b>\n" +
                          $"{bookingDate} {request.StartHour}:00–{request.EndHour}:00\n" +
                          $"Someone else already booked this slot." +
                          clickedAtStr;
        else if (result.IsNotOpenYet)
            telegramMsg = $"⏳ <b>Booking window never opened</b> after {maxAttempts} attempt(s)\n" +
                          $"<b>{trackedSchedule.Name}</b>\n" +
                          $"{bookingDate} {request.StartHour}:00–{request.EndHour}:00\n" +
                          $"The slot may not be available yet. Try again with /runnow {trackedSchedule.Id}.";
        else
            telegramMsg = $"❌ <b>Booking failed!</b>\n" +
                          $"<b>{trackedSchedule.Name}</b>\n" +
                          $"Court <code>{request.ResourceId}</code> · {bookingDate}\n" +
                          $"<i>{result.ErrorMessage}</i>";

        await _notifier.NotifyAsync(telegramMsg, ct);

        _logger.LogInformation("Schedule '{Name}' completed with status {Status} (disabled — one-shot).",
            trackedSchedule.Name, log.Status);
    }

    /// <summary>
    /// Computes NextRunAt for a one-shot schedule based on the booking date.
    /// The slot time is in Malta local time (Europe/Malta = UTC+1 winter, UTC+2 summer).
    /// We convert it to UTC then subtract exactly 14 days to get the trigger moment.
    /// If that trigger moment is already in the past, run immediately.
    /// </summary>
    public static DateTime ComputeNextRunAt(Schedule schedule)
    {
        var maltaTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Malta");

        // Treat BookingDate + StartHour as Malta local time
        var bookingLocalDt = DateTime.SpecifyKind(
            schedule.BookingDate.Date.AddHours(schedule.StartHour),
            DateTimeKind.Unspecified);

        var bookingUtc = TimeZoneInfo.ConvertTimeToUtc(bookingLocalDt, maltaTz);
        var triggerAt = bookingUtc.AddDays(-14);

        if (triggerAt <= DateTime.UtcNow)
            return DateTime.UtcNow.AddSeconds(-2);

        return triggerAt;
    }
}
