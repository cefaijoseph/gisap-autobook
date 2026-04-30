using System.Collections.Concurrent;
using System.Text;
using GisapAutobook.Data;
using GisapAutobook.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace GisapAutobook.Services;

public class TelegramBotService : IHostedService, ITelegramNotifier
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly SchedulerWakeService _wake;

    private TelegramBotClient? _bot;
    private long _allowedChatId;
    private CancellationTokenSource? _cts;

    // Per-user wizard state keyed by Telegram user ID
    private readonly ConcurrentDictionary<long, ScheduleWizard> _wizards = new();

    private static readonly string[] DayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] DayShort =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public TelegramBotService(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBotService> logger,
        SchedulerWakeService wake)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _wake = wake;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Telegram:BotToken is not configured. Telegram bot is disabled.");
            return;
        }

        long.TryParse(_config["Telegram:AllowedChatId"], out _allowedChatId);

        _bot = new TelegramBotClient(token);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            },
            cancellationToken: _cts.Token);

        var me = await _bot.GetMeAsync(ct);
        _logger.LogInformation("Telegram bot @{Username} is running. Allowed chat ID: {ChatId}",
            me.Username, _allowedChatId == 0 ? "any" : _allowedChatId.ToString());

        if (_allowedChatId != 0)
        {
            try
            {
                await SendHtmlAsync(_allowedChatId,
                    "🤖 <b>GISAP Padel Autobook</b> bot started.\nSend /help for commands.", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send startup message to chat {ChatId}", _allowedChatId);
            }
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    // ── ITelegramNotifier ─────────────────────────────────────────────────────

    public async Task NotifyAsync(string htmlMessage, CancellationToken ct = default)
    {
        if (_bot == null || _allowedChatId == 0) return;
        try
        {
            await SendHtmlAsync(_allowedChatId, htmlMessage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram notification");
        }
    }

    // ── Update handler ────────────────────────────────────────────────────────

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } cbq)
        {
            await HandleCallbackQueryAsync(cbq, ct);
            return;
        }

        if (update.Message is not { Text: { } text } msg) return;

        var chatId = msg.Chat.Id;
        var userId = msg.From?.Id ?? chatId;

        // Security: ignore chats not in the allowed list (if configured)
        if (_allowedChatId != 0 && chatId != _allowedChatId)
        {
            _logger.LogWarning("Rejected message from unauthorized chat {ChatId}", chatId);
            return;
        }

        var trimmed = text.Trim();

        // Active wizard: all selections happen via inline buttons; only /cancel is accepted as text
        if (_wizards.TryGetValue(userId, out _) &&
            !trimmed.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
        {
            await SendTextAsync(chatId, "Please use the buttons, or send /cancel to abort.", ct);
            return;
        }

        // Parse command + optional argument
        var space = trimmed.IndexOf(' ');
        var cmd = (space >= 0 ? trimmed[..space] : trimmed)
            .ToLowerInvariant()
            .Split('@')[0]; // strip @botname suffix
        var arg = space >= 0 ? trimmed[(space + 1)..].Trim() : null;

        switch (cmd)
        {
            case "/start":
            case "/help":
                await SendHelpAsync(chatId, ct);
                break;

            case "/schedules":
            case "/list":
                await SendSchedulesListAsync(chatId, ct);
                break;

            case "/newschedule":
                await StartWizardAsync(chatId, userId, ct);
                break;

            case "/cancel":
                _wizards.TryRemove(userId, out _);
                await SendTextAsync(chatId, "Cancelled.", ct);
                break;

            case "/delete":
                if (int.TryParse(arg, out var delId))
                    await DeleteScheduleAsync(chatId, delId, ct);
                else
                    await SendHtmlAsync(chatId, "Usage: /delete &lt;id&gt;", ct);
                break;

            case "/bookings":
                await SendConfirmedBookingsAsync(chatId, ct);
                break;

            case "/runnow":
                if (int.TryParse(arg, out var runId))
                    await TriggerNowAsync(chatId, runId, ct);
                else
                    await SendHtmlAsync(chatId, "Usage: /runnow &lt;id&gt;", ct);
                break;

            default:
                await SendTextAsync(chatId, "Unknown command. Send /help.", ct);
                break;
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram polling error");
        return Task.CompletedTask;
    }

    // ── Help ──────────────────────────────────────────────────────────────────

    private Task SendHelpAsync(long chatId, CancellationToken ct) =>
        SendHtmlAsync(chatId, """
            🎾 <b>GISAP Padel Autobook</b>

            <b>Commands:</b>
            /schedules — List pending bookings
            /bookings — List confirmed bookings
            /newschedule — Schedule a new padel booking
            /delete &lt;id&gt; — Cancel a pending booking
            /runnow &lt;id&gt; — Trigger a booking immediately (test)
            /cancel — Cancel the current wizard
            """, ct);

    // ── Schedules list ────────────────────────────────────────────────────────

    private async Task SendSchedulesListAsync(long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = await db.Schedules.Where(s => s.IsActive).OrderBy(s => s.BookingDate).ToListAsync(ct);

        if (schedules.Count == 0)
        {
            await SendTextAsync(chatId, "No pending bookings. Use /newschedule to schedule one.", ct);
            return;
        }

        var sb = new StringBuilder("<b>📋 Pending bookings:</b>\n\n");
        foreach (var s in schedules)
        {
            var nextRun = s.NextRunAt.HasValue
                ? s.NextRunAt.Value.ToString("dd MMM HH:mm") + " UTC"
                : "not scheduled";
            sb.AppendLine($"🕑 <b>[{s.Id}] {s.Name}</b>");
            sb.AppendLine($"   Slot: {s.StartHour}:00–{s.EndHour}:00 · {s.BookingDate:dd MMM yyyy}");
            sb.AppendLine($"   Will run: {nextRun}");
            sb.AppendLine();
        }

        await SendHtmlAsync(chatId, sb.ToString(), ct);
    }

    private async Task SendConfirmedBookingsAsync(long chatId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var confirmed = await db.BookingLogs
            .Where(l => l.Status == "Success")
            .OrderByDescending(l => l.RunAt)
            .Take(20)
            .ToListAsync(ct);

        if (confirmed.Count == 0)
        {
            await SendTextAsync(chatId, "No confirmed bookings yet.", ct);
            return;
        }

        var sb = new StringBuilder("✅ <b>Confirmed bookings:</b>\n\n");
        foreach (var l in confirmed)
        {
            sb.AppendLine($"✅ <b>{l.BookingDate:dd MMM yyyy}</b> · {l.StartHour}:00–{l.EndHour}:00");
            sb.AppendLine($"   {l.ScheduleName}");
            sb.AppendLine($"   Booked on {l.RunAt:dd MMM yyyy HH:mm} UTC");
            sb.AppendLine();
        }

        await SendHtmlAsync(chatId, sb.ToString(), ct);
    }

    // ── Creation wizard ───────────────────────────────────────────────────────

    private async Task StartWizardAsync(long chatId, long userId, CancellationToken ct)
    {
        _wizards[userId] = new ScheduleWizard();
        await SendDatePickerAsync(chatId, 0, ct);
    }

    // page: 0 = days 1-14, page: 1 = days 15-28, page: 2 = days 29-42
    private Task SendDatePickerAsync(long chatId, int page, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        int startOffset = page * 14 + 1;
        int endOffset = startOffset + 13;

        var dates = Enumerable.Range(startOffset, 14)
            .Select(d => today.AddDays(d))
            .ToList();

        // 7 dates per row
        var dateRows = dates
            .Chunk(7)
            .Select(row => row
                .Select(d => InlineKeyboardButton.WithCallbackData(
                    d.ToString("dd MMM"),
                    $"date:{d:yyyy-MM-dd}"))
                .ToArray())
            .ToList();

        // Navigation row
        var navRow = new List<InlineKeyboardButton>();
        if (page > 0)
            navRow.Add(InlineKeyboardButton.WithCallbackData("\u25c0 Earlier", $"datepage:{page - 1}"));
        if (endOffset < 42)
            navRow.Add(InlineKeyboardButton.WithCallbackData("Later \u25b6", $"datepage:{page + 1}"));

        var allRows = dateRows.Cast<IEnumerable<InlineKeyboardButton>>().ToList();
        if (navRow.Count > 0) allRows.Add(navRow);

        return _bot!.SendTextMessageAsync(chatId,
            $"🎾 <b>New Booking</b>\n\nChoose the date you want to play padel:\n<i>Showing {today.AddDays(startOffset):dd MMM} – {today.AddDays(endOffset):dd MMM}</i>\n/cancel to abort.",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(allRows),
            cancellationToken: ct);
    }

    private Task SendStartHourPickerAsync(long chatId, CancellationToken ct)
    {
        var rows = Enumerable.Range(6, 16)
            .Chunk(4)
            .Select(row => row.Select(h => InlineKeyboardButton.WithCallbackData($"{h}:00", $"start:{h}")).ToArray())
            .ToArray();
        return _bot!.SendTextMessageAsync(chatId, "What <b>start time</b> do you want?",
            parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private Task SendEndHourPickerAsync(long chatId, int startHour, CancellationToken ct)
    {
        var rows = Enumerable.Range(startHour + 1, 21 - startHour)
            .Chunk(4)
            .Select(row => row.Select(h => InlineKeyboardButton.WithCallbackData($"{h}:00", $"end:{h}")).ToArray())
            .ToArray();
        return _bot!.SendTextMessageAsync(chatId, "What <b>end time</b> do you want?",
            parseMode: ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message!.Chat.Id;
        var userId = query.From.Id;

        if (_allowedChatId != 0 && chatId != _allowedChatId)
        {
            await _bot!.AnswerCallbackQueryAsync(query.Id, cancellationToken: ct);
            return;
        }

        await _bot!.AnswerCallbackQueryAsync(query.Id, cancellationToken: ct);

        if (!_wizards.TryGetValue(userId, out var wizard))
        {
            await SendTextAsync(chatId, "No active wizard. Send /newschedule.", ct);
            return;
        }

        var data = query.Data ?? "";

        switch (wizard.Step)
        {
            case WizardStep.BookingDate when data.StartsWith("date:"):
                wizard.Draft.BookingDate = DateTime.Parse(data[5..]);
                wizard.Step = WizardStep.StartHour;
                await SendHtmlAsync(chatId, $"Date: <b>{wizard.Draft.BookingDate:dd MMM yyyy}</b> ✅", ct);
                await SendStartHourPickerAsync(chatId, ct);
                break;

            case WizardStep.BookingDate when data.StartsWith("datepage:"):
                var pg = int.Parse(data[9..]);
                await SendDatePickerAsync(chatId, pg, ct);
                break;

            case WizardStep.StartHour when data.StartsWith("start:"):
                var startH = int.Parse(data[6..]);
                wizard.Draft.StartHour = startH;
                wizard.Step = WizardStep.EndHour;
                await SendHtmlAsync(chatId, $"Start: <b>{startH}:00</b> ✅", ct);
                await SendEndHourPickerAsync(chatId, startH, ct);
                break;

            case WizardStep.EndHour when data.StartsWith("end:"):
                var endH = int.Parse(data[4..]);
                wizard.Draft.EndHour = endH;
                _wizards.TryRemove(userId, out _);
                await SendHtmlAsync(chatId, $"End: <b>{endH}:00</b> ✅", ct);
                await FinalizeScheduleAsync(chatId, wizard.Draft, ct);
                break;
        }
    }



    private async Task FinalizeScheduleAsync(long chatId, Schedule draft, CancellationToken ct)
    {
        draft.Name = $"Padel {draft.BookingDate:dd MMM yyyy} {draft.StartHour}:00–{draft.EndHour}:00";
        draft.ResourceId = "241563";
        draft.NumberOfPersons = 4;
        draft.DaysInAdvance = 14; // kept for reference
        draft.TriggerDayOfWeek = draft.BookingDate.DayOfWeek; // kept for reference
        draft.TriggerTime = new TimeSpan(8, 0, 0);
        draft.IsActive = true;
        draft.NextRunAt = BookingSchedulerService.ComputeNextRunAt(draft);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Schedules.Add(draft);
        await db.SaveChangesAsync(ct);
        _wake.Wake();

        var daysUntil = (draft.BookingDate.Date - DateTime.UtcNow.Date).TotalDays;
        var runMsg = daysUntil < 14
            ? "Running now — you’ll get a result notification shortly."
            : $"Will attempt booking on <b>{draft.BookingDate.Date.AddDays(-14):dd MMM yyyy}</b> (14 days before)";

        await SendHtmlAsync(chatId,
            $"✅ <b>Booking scheduled!</b>\n\n" +
            $"<b>{draft.Name}</b> [ID: {draft.Id}]\n" +
            $"Date: {draft.BookingDate:dd MMM yyyy} ({draft.BookingDate.DayOfWeek})\n" +
            $"Slot: {draft.StartHour}:00–{draft.EndHour}:00 · 4 persons\n\n" +
            $"{runMsg}\n\n" +
            $"Use /delete {draft.Id} to cancel it.", ct);
    }

    // ── Toggle / Delete ───────────────────────────────────────────────────────

    private async Task DeleteScheduleAsync(long chatId, int id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = await db.Schedules.FindAsync(new object[] { id }, ct);
        if (s == null) { await SendTextAsync(chatId, $"Booking #{id} not found.", ct); return; }

        db.Schedules.Remove(s);
        await db.SaveChangesAsync(ct);
        await SendHtmlAsync(chatId, $"🗑 Booking <b>{s.Name}</b> cancelled.", ct);
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    // ── Run now ───────────────────────────────────────────────────────────────

    private async Task TriggerNowAsync(long chatId, int id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = await db.Schedules.FindAsync(new object[] { id }, ct);
        if (s == null) { await SendTextAsync(chatId, $"Schedule #{id} not found.", ct); return; }

        // Set NextRunAt to the past so the scheduler fires it on the next tick
        s.NextRunAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync(ct);
        _wake.Wake();

        await SendHtmlAsync(chatId,
            $"⏱ <b>{s.Name}</b> will run within the next minute.\nYou'll get a notification with the result.", ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task SendHtmlAsync(long chatId, string html, CancellationToken ct) =>
        _bot!.SendTextMessageAsync(chatId, html, parseMode: ParseMode.Html, cancellationToken: ct);

    private Task SendTextAsync(long chatId, string text, CancellationToken ct) =>
        _bot!.SendTextMessageAsync(chatId, text, cancellationToken: ct);

    // ── Inner types ───────────────────────────────────────────────────────────

    private enum WizardStep { BookingDate, StartHour, EndHour }

    private sealed class ScheduleWizard
    {
        public WizardStep Step { get; set; } = WizardStep.BookingDate;
        public Schedule Draft { get; } = new();
    }
}
