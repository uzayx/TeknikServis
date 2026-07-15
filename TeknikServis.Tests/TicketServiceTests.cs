using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Common.Options;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Services;
using TeknikServis.Infrastructure.Persistence;
using Xunit;

namespace TeknikServis.Tests;

public class TicketServiceTests
{
    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TicketService CreateService(AppDbContext db, SlaOptions? sla = null)
        => new(db, Options.Create(sla ?? new SlaOptions()));

    private static async Task<Customer> SeedCustomerAsync(AppDbContext db)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = "Ahmet",
            LastName = "Yilmaz",
            Email = $"{Guid.NewGuid():N}@test.com",
            Phone = "05550000000",
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<Technician> SeedTechnicianAsync(AppDbContext db, bool isActive = true)
    {
        var technician = new Technician
        {
            Id = Guid.NewGuid(),
            FirstName = "Can",
            LastName = "Demir",
            Email = $"{Guid.NewGuid():N}@test.com",
            Phone = "05551111111",
            Specialty = "Klima",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Technicians.Add(technician);
        await db.SaveChangesAsync();
        return technician;
    }

    [Fact]
    public async Task CreateAsync_ShouldUseSlaHoursFromOptions()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var customSla = new SlaOptions { CriticalHours = 2, HighHours = 6, MediumHours = 12, LowHours = 48 };
        var service = CreateService(db, customSla);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Critical));

        var expected = ticket.CreatedAt.AddHours(2);
        Assert.Equal(expected, ticket.SlaDeadline, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public async Task CreateAsync_DefaultSla_ShouldMatchPriority(TicketPriority priority, int expectedHours)
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", priority));

        Assert.Equal(ticket.CreatedAt.AddHours(expectedHours), ticket.SlaDeadline, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAsync_MissingCustomer_ShouldThrowNotFound()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(new CreateTicketRequest(Guid.NewGuid(), "Test", "Aciklama", TicketPriority.Low)));
    }

    [Fact]
    public async Task CreateAsync_ShouldWriteInitialHistoryRecord()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        var history = db.TicketStatusHistories.Where(h => h.ServiceTicketId == ticket.Id).ToList();
        Assert.Single(history);
        Assert.Null(history[0].FromStatus);
        Assert.Equal(TicketStatus.New, history[0].ToStatus);
    }

    [Fact]
    public async Task AssignTechnician_FirstAssignment_ShouldMoveToAssignedAndWriteHistory()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var technician = await SeedTechnicianAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        await service.AssignTechnicianAsync(ticket.Id,
            new AssignTechnicianRequest(technician.Id, "Center", null));

        var updated = db.ServiceTickets.First(t => t.Id == ticket.Id);
        Assert.Equal(TicketStatus.Assigned, updated.Status);
        Assert.Equal(technician.Id, updated.AssignedTechnicianId);
        Assert.NotNull(updated.AssignedAt);

        var histories = db.TicketStatusHistories
            .Where(h => h.ServiceTicketId == ticket.Id)
            .OrderBy(h => h.ChangedAt).ToList();
        Assert.Equal(2, histories.Count);
        Assert.Null(histories[1].PreviousTechnicianId);
        Assert.Equal(technician.Id, histories[1].NewTechnicianId);
        Assert.Contains("Can Demir", histories[1].Note);
    }

    [Fact]
    public async Task AssignTechnician_Reassignment_ShouldTrackPreviousTechnician()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var tech1 = await SeedTechnicianAsync(db);
        var tech2 = await SeedTechnicianAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        await service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(tech1.Id, "Center", null));
        await service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(tech2.Id, "Center", "Degisiklik"));

        var lastHistory = db.TicketStatusHistories
            .Where(h => h.ServiceTicketId == ticket.Id)
            .OrderBy(h => h.ChangedAt).Last();
        Assert.Equal(tech1.Id, lastHistory.PreviousTechnicianId);
        Assert.Equal(tech2.Id, lastHistory.NewTechnicianId);
    }

    [Fact]
    public async Task AssignTechnician_SameTechnicianTwice_ShouldThrow()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var technician = await SeedTechnicianAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        await service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null)));
        Assert.Equal("TECHNICIAN_ALREADY_ASSIGNED", ex.ErrorCode);
    }

    [Fact]
    public async Task AssignTechnician_InactiveTechnician_ShouldThrow()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var technician = await SeedTechnicianAsync(db, isActive: false);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null)));
        Assert.Equal("TECHNICIAN_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task ChangeStatus_SkippingStep_ShouldThrow()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ChangeStatusAsync(ticket.Id,
                new ChangeStatusRequest(TicketStatus.InProgress, "Center", null)));
        Assert.Equal("INVALID_STATUS_TRANSITION", ex.ErrorCode);
    }

    [Fact]
    public async Task FullLifecycle_ShouldReachClosed_AndLockTicket()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var technician = await SeedTechnicianAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.High));

        await service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.InProgress, "Technician", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.Completed, "Technician", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.Approved, "Center", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.Closed, "Center", null));

        var updated = db.ServiceTickets.First(t => t.Id == ticket.Id);
        Assert.Equal(TicketStatus.Closed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.NotNull(updated.ClosedAt);
        Assert.Equal(6, db.TicketStatusHistories.Count(h => h.ServiceTicketId == ticket.Id));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(ticket.Id, new UpdateTicketRequest("Yeni", "Yeni", TicketPriority.Low)));
        Assert.Equal("TICKET_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task Update_ApprovedTicket_ShouldThrowLocked()
    {
        using var db = CreateDb();
        var customer = await SeedCustomerAsync(db);
        var technician = await SeedTechnicianAsync(db);
        var service = CreateService(db);

        var ticket = await service.CreateAsync(
            new CreateTicketRequest(customer.Id, "Test", "Aciklama", TicketPriority.Medium));
        await service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.InProgress, "Technician", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.Completed, "Technician", null));
        await service.ChangeStatusAsync(ticket.Id, new ChangeStatusRequest(TicketStatus.Approved, "Center", null));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssignTechnicianAsync(ticket.Id, new AssignTechnicianRequest(technician.Id, "Center", null)));
        Assert.Equal("TICKET_LOCKED", ex.ErrorCode);
    }
}
