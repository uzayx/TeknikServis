using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Services;
using TeknikServis.Infrastructure.Persistence;
using Xunit;

namespace TeknikServis.Tests;

public class TicketInteractionServiceTests
{
    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<ServiceTicket> SeedTicketAsync(AppDbContext db, TicketStatus status = TicketStatus.New)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FirstName = "Test", LastName = "Musteri",
            Email = $"{Guid.NewGuid():N}@test.com", Phone = "0555", CreatedAt = DateTime.UtcNow
        };
        var ticket = new ServiceTicket
        {
            Id = Guid.NewGuid(), TicketNumber = $"TS-{Guid.NewGuid().ToString("N")[..8]}",
            CustomerId = customer.Id, Title = "Test", Description = "Test",
            Status = status, Priority = TicketPriority.Medium,
            SlaDeadline = DateTime.UtcNow.AddHours(24), CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.ServiceTickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task AddComment_ShouldPersist()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db);
        var service = new TicketInteractionService(db);

        var result = await service.AddCommentAsync(ticket.Id,
            new CreateCommentRequest("Technician", Guid.NewGuid(), "Parca siparis edildi."));

        Assert.Equal("Parca siparis edildi.", result.Content);
        Assert.Single(db.Comments.Where(c => c.ServiceTicketId == ticket.Id));
    }

    [Fact]
    public async Task AddComment_ApprovedTicket_ShouldBeAllowed()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db, TicketStatus.Approved);
        var service = new TicketInteractionService(db);

        var result = await service.AddCommentAsync(ticket.Id,
            new CreateCommentRequest("Customer", null, "Onay surecinde soru."));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddComment_ClosedTicket_ShouldThrowLocked()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db, TicketStatus.Closed);
        var service = new TicketInteractionService(db);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AddCommentAsync(ticket.Id, new CreateCommentRequest("Center", null, "Calismamali")));
        Assert.Equal("TICKET_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task AddComment_MissingTicket_ShouldThrowNotFound()
    {
        using var db = CreateDb();
        var service = new TicketInteractionService(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AddCommentAsync(Guid.NewGuid(), new CreateCommentRequest("Center", null, "Test")));
    }

    [Fact]
    public async Task GetComments_ShouldOrderByCreatedAt()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db);
        var service = new TicketInteractionService(db);

        await service.AddCommentAsync(ticket.Id, new CreateCommentRequest("Customer", null, "Ilk"));
        await Task.Delay(10);
        await service.AddCommentAsync(ticket.Id, new CreateCommentRequest("Center", null, "Ikinci"));

        var list = await service.GetCommentsAsync(ticket.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal("Ilk", list[0].Content);
        Assert.Equal("Ikinci", list[1].Content);
    }

    [Fact]
    public async Task AddAttachment_ShouldPersist()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db, TicketStatus.InProgress);
        var service = new TicketInteractionService(db);

        var result = await service.AddAttachmentAsync(ticket.Id, new CreateAttachmentRequest(
            "ariza.jpg", "image/jpeg", 512_000,
            "https://storage.example.com/tickets/ariza.jpg", "Customer"));

        Assert.Equal("ariza.jpg", result.FileName);
        Assert.Single(db.Attachments.Where(a => a.ServiceTicketId == ticket.Id));
    }

    [Fact]
    public async Task AddAttachment_ApprovedTicket_ShouldThrowLocked()
    {
        using var db = CreateDb();
        var ticket = await SeedTicketAsync(db, TicketStatus.Approved);
        var service = new TicketInteractionService(db);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AddAttachmentAsync(ticket.Id, new CreateAttachmentRequest(
                "gec.jpg", "image/jpeg", 1000, "https://x.com/gec.jpg", "Technician")));
        Assert.Equal("TICKET_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task AddAttachment_MissingTicket_ShouldThrowNotFound()
    {
        using var db = CreateDb();
        var service = new TicketInteractionService(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AddAttachmentAsync(Guid.NewGuid(), new CreateAttachmentRequest(
                "x.jpg", "image/jpeg", 1000, "https://x.com/x.jpg", "Customer")));
    }
}
