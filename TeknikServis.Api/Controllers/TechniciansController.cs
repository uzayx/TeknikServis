using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Api.Controllers;

[ApiController]
[Route("api/technicians")]
public class TechniciansController : ControllerBase
{
    private readonly IAppDbContext _db;

    public TechniciansController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TechnicianResponse>>> GetAll(CancellationToken ct)
    {
        var technicians = await _db.Technicians
            .AsNoTracking()
            .OrderBy(t => t.LastName).ThenBy(t => t.FirstName)
            .Select(t => new TechnicianResponse(t.Id, t.FirstName, t.LastName, t.Email, t.Phone, t.Specialty, t.IsActive, t.CreatedAt))
            .ToListAsync(ct);
        return Ok(technicians);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TechnicianResponse>> GetById(Guid id, CancellationToken ct)
    {
        var t = await _db.Technicians.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Technician), id);
        return Ok(new TechnicianResponse(t.Id, t.FirstName, t.LastName, t.Email, t.Phone, t.Specialty, t.IsActive, t.CreatedAt));
    }

    [HttpPost]
    public async Task<ActionResult<TechnicianResponse>> Create(
        [FromBody] CreateTechnicianRequest request, CancellationToken ct)
    {
        var technician = new Technician
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Specialty = request.Specialty,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Technicians.Add(technician);
        await _db.SaveChangesAsync(ct);

        var response = new TechnicianResponse(
            technician.Id, technician.FirstName, technician.LastName, technician.Email,
            technician.Phone, technician.Specialty, technician.IsActive, technician.CreatedAt);
        return CreatedAtAction(nameof(GetById), new { id = technician.Id }, response);
    }
}
