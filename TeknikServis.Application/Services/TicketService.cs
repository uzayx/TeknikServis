using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Common.Options;
using TeknikServis.Application.Domain;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Application.Services;

public class TicketService : ITicketService
{
    private readonly IAppDbContext _db;
    private readonly SlaOptions _slaOptions;

    public TicketService(IAppDbContext db, IOptions<SlaOptions> slaOptions)
    {
        _db = db;
        _slaOptions = slaOptions.Value;
    }

    public async Task<ServiceTicket> CreateAsync(CreateTicketRequest request, CancellationToken ct = default)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            throw new NotFoundException(nameof(Customer), request.CustomerId);

        var now = DateTime.UtcNow;

        var ticket = new ServiceTicket
        {
            Id = Guid.NewGuid(),
            TicketNumber = GenerateTicketNumber(now),
            CustomerId = request.CustomerId,
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            Status = TicketStatus.New,
            SlaDeadline = now.Add(GetSlaDuration(request.Priority)),
            CreatedAt = now
        };

        _db.ServiceTickets.Add(ticket);

        _db.TicketStatusHistories.Add(new TicketStatusHistory
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            FromStatus = null,
            ToStatus = TicketStatus.New,
            ChangedByType = "Customer",
            Note = "Ariza kaydi olusturuldu.",
            ChangedAt = now
        });

        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<ServiceTicket> UpdateAsync(Guid ticketId, UpdateTicketRequest request, CancellationToken ct = default)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, ct);
        EnsureEditable(ticket);

        ticket.Title = request.Title;
        ticket.Description = request.Description;

        if (ticket.Priority != request.Priority)
        {
            ticket.Priority = request.Priority;
            ticket.SlaDeadline = ticket.CreatedAt.Add(GetSlaDuration(request.Priority));
        }

        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<ServiceTicket> AssignTechnicianAsync(Guid ticketId, AssignTechnicianRequest request, CancellationToken ct = default)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, ct);
        EnsureEditable(ticket);

        var technician = await _db.Technicians
            .FirstOrDefaultAsync(t => t.Id == request.TechnicianId, ct)
            ?? throw new NotFoundException(nameof(Technician), request.TechnicianId);

        var technicianFullName = $"{technician.FirstName} {technician.LastName}";

        if (!technician.IsActive)
            throw new BusinessRuleException(
                "TECHNICIAN_INACTIVE",
                $"Teknisyen aktif degil: {technicianFullName}");

        if (ticket.AssignedTechnicianId == request.TechnicianId)
            throw new BusinessRuleException(
                "TECHNICIAN_ALREADY_ASSIGNED",
                "Bu teknisyen zaten bu kayda atanmis durumda.");

        var now = DateTime.UtcNow;

        // Onceki teknisyen, degisiklik yazilmadan once yakalaniyor: case geregi
        // teknisyen degisikliklerinin kimden kime yapildigi izlenebilmeli.
        var previousTechnicianId = ticket.AssignedTechnicianId;
        var isFirstAssignment = previousTechnicianId is null;

        ticket.AssignedTechnicianId = technician.Id;
        ticket.AssignedAt = now;

        TicketStatus? fromStatus = null;

        // Ilk atama, kaydi New'den Assigned'a otomatik tasiyor: "teknisyen atandi ama
        // durum hala New" gibi tutarsiz bir ara durum olusmasini engelliyor.
        if (ticket.Status == TicketStatus.New)
        {
            fromStatus = ticket.Status;
            ticket.Status = TicketStatus.Assigned;
        }

        _db.TicketStatusHistories.Add(new TicketStatusHistory
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            FromStatus = fromStatus,
            ToStatus = ticket.Status,
            PreviousTechnicianId = previousTechnicianId,
            NewTechnicianId = technician.Id,
            ChangedByType = request.ChangedByType,
            Note = request.Note ?? (isFirstAssignment
                ? $"Teknisyen atandi: {technicianFullName}"
                : $"Teknisyen degistirildi: {technicianFullName}"),
            ChangedAt = now
        });

        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<ServiceTicket> ChangeStatusAsync(Guid ticketId, ChangeStatusRequest request, CancellationToken ct = default)
    {
        var ticket = await GetTicketOrThrowAsync(ticketId, ct);

        if (TicketStatusStateMachine.IsTerminalOrLocked(ticket.Status) && request.NewStatus != TicketStatus.Closed)
            throw new BusinessRuleException(
                "TICKET_LOCKED",
                $"'{ticket.Status}' durumundaki kayit uzerinde degisiklik yapilamaz.");

        if (!TicketStatusStateMachine.CanTransition(ticket.Status, request.NewStatus))
        {
            var allowed = TicketStatusStateMachine.GetAllowedTargets(ticket.Status);
            var allowedText = allowed.Count > 0 ? string.Join(", ", allowed) : "yok (son durum)";
            throw new BusinessRuleException(
                "INVALID_STATUS_TRANSITION",
                $"'{ticket.Status}' durumundan '{request.NewStatus}' durumuna gecis yapilamaz. Izin verilen gecisler: {allowedText}.");
        }

        if (request.NewStatus == TicketStatus.Assigned && ticket.AssignedTechnicianId is null)
            throw new BusinessRuleException(
                "TECHNICIAN_REQUIRED",
                "Teknisyen atanmadan kayit 'Assigned' durumuna gecirilemez. Once teknisyen atayin.");

        var now = DateTime.UtcNow;
        var fromStatus = ticket.Status;

        ticket.Status = request.NewStatus;

        switch (request.NewStatus)
        {
            case TicketStatus.Completed:
                ticket.CompletedAt = now;
                break;
            case TicketStatus.Closed:
                ticket.ClosedAt = now;
                break;
        }

        _db.TicketStatusHistories.Add(new TicketStatusHistory
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            FromStatus = fromStatus,
            ToStatus = request.NewStatus,
            ChangedByType = request.ChangedByType,
            Note = request.Note,
            ChangedAt = now
        });

        // Ticket guncellemesi ve gecmis kaydi tek SaveChanges cagrisinda gidiyor.
        // EF Core tek SaveChanges'i zaten tek transaction icinde calistirdigi icin
        // ayrica BeginTransaction acmaya gerek yok: ya ikisi de yazilir ya hicbiri.
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    private async Task<ServiceTicket> GetTicketOrThrowAsync(Guid ticketId, CancellationToken ct)
        => await _db.ServiceTickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct)
           ?? throw new NotFoundException(nameof(ServiceTicket), ticketId);

    private static void EnsureEditable(ServiceTicket ticket)
    {
        if (TicketStatusStateMachine.IsTerminalOrLocked(ticket.Status))
            throw new BusinessRuleException(
                "TICKET_LOCKED",
                $"'{ticket.Status}' durumundaki kayitlar duzenlenemez.");
    }

    private TimeSpan GetSlaDuration(TicketPriority priority) => priority switch
    {
        TicketPriority.Critical => TimeSpan.FromHours(_slaOptions.CriticalHours),
        TicketPriority.High => TimeSpan.FromHours(_slaOptions.HighHours),
        TicketPriority.Medium => TimeSpan.FromHours(_slaOptions.MediumHours),
        _ => TimeSpan.FromHours(_slaOptions.LowHours)
    };

    // Insan tarafindan okunabilir kayit numarasi: musteri telefonda GUID degil
    // bu numarayi soyler. Sirali sayac yerine tarih + rastgele son ek kullaniliyor;
    // sayac esZamanli isteklerde race condition yaratir, benzersizlik zaten
    // veritabanindaki unique index ile garanti altinda.
    private static string GenerateTicketNumber(DateTime utcNow)
        => $"TS-{utcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}

