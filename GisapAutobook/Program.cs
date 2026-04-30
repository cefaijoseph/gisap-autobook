using GisapAutobook.Data;
using GisapAutobook.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "autobook.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Telegram bot: singleton so it can act as both IHostedService and ITelegramNotifier
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton<ITelegramNotifier>(sp => sp.GetRequiredService<TelegramBotService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());

builder.Services.AddSingleton<SchedulerWakeService>();
builder.Services.AddHostedService<BookingSchedulerService>();

// ── App ───────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Auto-apply EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// // ── TEST: seed a schedule that fires at 13:42 Malta time today ────────────────
// using (var scope = app.Services.CreateScope())
// {
//     var db = scope.ServiceProvider.GetRequiredService<GisapAutobook.Data.AppDbContext>();
//     var maltaTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Malta");
//     var triggerMalta = DateTime.SpecifyKind(DateTime.Today.AddHours(14).AddMinutes(14), DateTimeKind.Unspecified);
//     var triggerUtc = TimeZoneInfo.ConvertTimeToUtc(triggerMalta, maltaTz);
//     if (!db.Schedules.Any(s => s.Name == "TEST 14:20"))
//     {
//         db.Schedules.Add(new GisapAutobook.Models.Schedule
//         {
//             Name        = "TEST 13:42",
//             ResourceId  = "241563",
//             BookingDate = DateTime.Today.AddDays(1),
//             StartHour   = 10,
//             EndHour     = 12,
//             NumberOfPersons = 4,
//             IsActive    = true,
//             NextRunAt   = triggerUtc,
//         });
//         db.SaveChanges();
//     }
// }
// // ── END TEST ─────────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();

app.Run();
