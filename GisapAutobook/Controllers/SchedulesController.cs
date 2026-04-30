using GisapAutobook.Data;
using GisapAutobook.Models;
using GisapAutobook.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisapAutobook.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchedulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SchedulerWakeService _wake;

    public SchedulesController(AppDbContext db, SchedulerWakeService wake)
    {
        _db = db;
        _wake = wake;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var schedules = await _db.Schedules
            .OrderBy(s => s.Name)
            .ToListAsync();
        return Ok(schedules);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var schedule = await _db.Schedules.FindAsync(id);
        return schedule == null ? NotFound() : Ok(schedule);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Schedule schedule)
    {
        schedule.Id = 0;
        schedule.NextRunAt = BookingSchedulerService.ComputeNextRunAt(schedule);
        schedule.LastRunAt = null;
        schedule.LastRunStatus = null;

        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync();

        _wake.Wake();
        return CreatedAtAction(nameof(GetById), new { id = schedule.Id }, schedule);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] Schedule schedule)
    {
        if (id != schedule.Id) return BadRequest("ID mismatch");

        var existing = await _db.Schedules.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = schedule.Name;
        existing.ResourceId = schedule.ResourceId;
        existing.IsActive = schedule.IsActive;
        existing.TriggerDayOfWeek = schedule.TriggerDayOfWeek;
        existing.TriggerTime = schedule.TriggerTime;
        existing.DaysInAdvance = schedule.DaysInAdvance;
        existing.StartHour = schedule.StartHour;
        existing.EndHour = schedule.EndHour;
        existing.NumberOfPersons = schedule.NumberOfPersons;
        existing.NextRunAt = BookingSchedulerService.ComputeNextRunAt(existing);

        await _db.SaveChangesAsync();
        _wake.Wake();
        return Ok(existing);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var schedule = await _db.Schedules.FindAsync(id);
        if (schedule == null) return NotFound();

        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var schedule = await _db.Schedules.FindAsync(id);
        if (schedule == null) return NotFound();

        schedule.IsActive = !schedule.IsActive;
        await _db.SaveChangesAsync();
        return Ok(schedule);
    }

    [HttpPost("{id:int}/runnow")]
    public async Task<IActionResult> RunNow(int id)
    {
        var schedule = await _db.Schedules.FindAsync(id);
        if (schedule == null) return NotFound();

        schedule.NextRunAt = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();
        _wake.Wake();
        return Ok(new { message = "Schedule queued — the booking bot will start immediately." });
    }
}
