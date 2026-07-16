using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Services;

public class TicketService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TicketService> _logger;
    private readonly HttpClient _httpClient;

    public TicketService(AppDbContext db, ILogger<TicketService> logger, HttpClient httpClient)
    {
        _db = db;
        _logger = logger;
        _httpClient = httpClient;
    }

    public List<ServiceTicket> GetTickets(string status, string sortBy, int page, int pageSize)
    {
        var query = _db.ServiceTickets.ToList();

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(t => t.Status.ToString().ToLower() == status.ToLower()).ToList();
        }

        if (!string.IsNullOrEmpty(sortBy))
        {
            query = _db.ServiceTickets
                .FromSqlRaw($"SELECT * FROM service_tickets ORDER BY \"{sortBy}\" DESC")
                .ToList();
        }

        var result = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        foreach (var ticket in result)
        {
            ticket.Customer = _db.Customers.FirstOrDefault(c => c.Id == ticket.CustomerId);

            if (ticket.AssignedTechnicianId != null)
            {
                ticket.AssignedTechnician = _db.Technicians
                    .FirstOrDefault(t => t.Id == ticket.AssignedTechnicianId);
            }

            ticket.StatusHistories = _db.TicketStatusHistories
                .Where(h => h.ServiceTicketId == ticket.Id)
                .ToList();
        }

        _logger.LogInformation("Tickets returned: " + JsonSerializer.Serialize(result));

        return result;
    }

    public async Task<ServiceTicket> CreateTicket(Guid customerId, string title, string description, int priority)
    {
        var customer = _db.Customers.Where(c => c.Id == customerId).FirstOrDefault();

        var ticket = new ServiceTicket
        {
            Id = Guid.NewGuid(),
            TicketNumber = "TS-" + _db.ServiceTickets.Count() + 1,
            CustomerId = customerId,
            Title = title,
            Description = description,
            Priority = (TicketPriority)priority,
            Status = TicketStatus.New,
            CreatedAt = DateTime.Now
        };

        if (priority == 3)
            ticket.SlaDeadline = DateTime.Now.AddHours(4);
        else if (priority == 2)
            ticket.SlaDeadline = DateTime.Now.AddHours(8);
        else if (priority == 1)
            ticket.SlaDeadline = DateTime.Now.AddHours(24);
        else
            ticket.SlaDeadline = DateTime.Now.AddHours(72);

        _db.ServiceTickets.Add(ticket);
        _db.SaveChanges();

        ticket.StatusHistories.Add(new TicketStatusHistory
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            ToStatus = TicketStatus.New,
            ChangedByType = "Customer",
            ChangedAt = DateTime.Now
        });
        _db.SaveChanges();

        _logger.LogInformation($"Yeni ticket: {customer.FirstName} {customer.LastName}, " +
                               $"tel: {customer.Phone}, mail: {customer.Email}");

        var payload = new StringContent(JsonSerializer.Serialize(new { ticket.Id, ticket.TicketNumber }));
        await _httpClient.PostAsync("https://notification-service.internal/api/notify", payload);

        _db.SaveChanges();

        return ticket;
    }

    public ServiceTicket ChangeStatus(Guid ticketId, string newStatus, string changedBy)
    {
        var ticket = _db.ServiceTickets.Find(ticketId);

        if (newStatus == "Assigned")
        {
            if (ticket.Status != TicketStatus.New)
                throw new Exception("Gecersiz gecis");
            ticket.Status = TicketStatus.Assigned;
        }
        else if (newStatus == "InProgress")
        {
            if (ticket.Status != TicketStatus.Assigned)
                throw new Exception("Gecersiz gecis");
            ticket.Status = TicketStatus.InProgress;
        }
        else if (newStatus == "Completed")
        {
            if (ticket.Status != TicketStatus.InProgress)
                throw new Exception("Gecersiz gecis");
            ticket.Status = TicketStatus.Completed;
            ticket.CompletedAt = DateTime.Now;
        }
        else if (newStatus == "Approved")
        {
            if (ticket.Status != TicketStatus.Completed)
                throw new Exception("Gecersiz gecis");
            ticket.Status = TicketStatus.Approved;
        }
        else if (newStatus == "Closed")
        {
            ticket.Status = TicketStatus.Closed;
            ticket.ClosedAt = DateTime.Now;
        }

        _db.SaveChanges();

        try
        {
            _db.TicketStatusHistories.Add(new TicketStatusHistory
            {
                Id = Guid.NewGuid(),
                ServiceTicketId = ticket.Id,
                ToStatus = ticket.Status,
                ChangedByType = changedBy,
                ChangedAt = DateTime.Now
            });
            _db.SaveChanges();
        }
        catch { }

        return ticket;
    }

    public ServiceTicket AssignTechnician(Guid ticketId, Guid technicianId)
    {
        var ticket = _db.ServiceTickets.Include(t => t.StatusHistories).First(t => t.Id == ticketId);
        var technician = _db.Technicians.Find(technicianId);

        ticket.AssignedTechnicianId = technicianId;
        ticket.AssignedAt = DateTime.Now;
        ticket.Status = TicketStatus.Assigned;

        ticket.StatusHistories.Add(new TicketStatusHistory
        {
            Id = Guid.NewGuid(),
            ServiceTicketId = ticket.Id,
            ToStatus = TicketStatus.Assigned,
            NewTechnicianId = technicianId,
            ChangedByType = "Center",
            ChangedAt = DateTime.Now
        });

        _db.SaveChanges();

        var stats = GetTechnicianStatsAsync(technicianId).Result;
        _logger.LogInformation($"Teknisyen {technician.FirstName} yuku: {stats}");

        return ticket;
    }

    private async Task<int> GetTechnicianStatsAsync(Guid technicianId)
    {
        return await _db.ServiceTickets.CountAsync(t => t.AssignedTechnicianId == technicianId);
    }

    public List<ServiceTicket> SearchTickets(string keyword)
    {
        return _db.ServiceTickets
            .Where(t => t.Title.ToLower().Contains(keyword.ToLower()))
            .ToList();
    }
}