using GisapAutobook.Data;
using GisapAutobook.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisapAutobook.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public BookingLogsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var logs = await _db.BookingLogs
            .OrderByDescending(l => l.RunAt)
            .Take(200)
            .ToListAsync();
        return Ok(logs);
    }

    [HttpGet("schedule/{scheduleId:int}")]
    public async Task<IActionResult> GetBySchedule(int scheduleId)
    {
        var logs = await _db.BookingLogs
            .Where(l => l.ScheduleId == scheduleId)
            .OrderByDescending(l => l.RunAt)
            .ToListAsync();
        return Ok(logs);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var log = await _db.BookingLogs.FindAsync(id);
        if (log == null) return NotFound();
        _db.BookingLogs.Remove(log);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
