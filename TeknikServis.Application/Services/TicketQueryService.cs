using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Common.Pagination;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;
using TeknikServis.Application.Mapping;

namespace TeknikServis.Application.Services;

public class TicketQueryService : ITicketQueryService
{
    private readonly IAppDbContext _db;

    public TicketQueryService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<TicketResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await _db.ServiceTickets
            .AsNoTracking()
            .Include(t => t.Customer)
            .Include(t => t.AssignedTechnician)
            .Include(t => t.StatusHistories)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(nameof(ServiceTicket), id);

        return TicketMapper.ToResponse(ticket);
    }

    public async Task<PagedResult<TicketListItemResponse>> GetPagedAsync(
        TicketQueryParameters p, CancellationToken ct = default)
    {
        var query = _db.ServiceTickets.AsNoTracking();

        if (p.Status.HasValue)
            query = query.Where(t => t.Status == p.Status.Value);

        if (p.Priority.HasValue)
            query = query.Where(t => t.Priority == p.Priority.Value);

        if (p.CustomerId.HasValue)
            query = query.Where(t => t.CustomerId == p.CustomerId.Value);

        if (p.TechnicianId.HasValue)
            query = query.Where(t => t.AssignedTechnicianId == p.TechnicianId.Value);

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var s = p.Search.Trim().ToLower();
            query = query.Where(t =>
                t.TicketNumber.ToLower().Contains(s) ||
                t.Title.ToLower().Contains(s));
        }

        query = ApplySorting(query, p);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(t => new TicketListItemResponse(
                t.Id,
                t.TicketNumber,
                t.Title,
                t.Status,
                t.Priority,
                t.Customer.FullName,
                t.AssignedTechnician != null ? t.AssignedTechnician.FullName : null,
                t.SlaDeadline,
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<TicketListItemResponse>(items, p.Page, p.PageSize, totalCount);
    }

    private static IQueryable<ServiceTicket> ApplySorting(
        IQueryable<ServiceTicket> query, TicketQueryParameters p)
    {
        var desc = string.Equals(p.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return (p.SortBy?.ToLowerInvariant()) switch
        {
            "priority" => desc ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            "status" => desc ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "sladeadline" => desc ? query.OrderByDescending(t => t.SlaDeadline) : query.OrderBy(t => t.SlaDeadline),
            "ticketnumber" => desc ? query.OrderByDescending(t => t.TicketNumber) : query.OrderBy(t => t.TicketNumber),
            _ => desc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt)
        };
    }
}
