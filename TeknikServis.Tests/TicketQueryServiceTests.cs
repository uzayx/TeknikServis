using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Pagination;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.Domain.Enums;
using TeknikServis.Application.Services;
using TeknikServis.Infrastructure.Persistence;
using Xunit;

namespace TeknikServis.Tests;

public class TicketQueryServiceTests
{
    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Senaryo: 2 musteri, 2 teknisyen, 4 ticket.
    // t1: SLA ihlali (tamamlanmamis, deadline gecmis), Ahmet Yilmaz / Can Demir
    // t2: SLA uyumlu (tamamlanmamis, deadline ileride), Ayse Kaya / Elif Yildiz
    // t3: SLA ihlali (tamamlanmis, gec bitmis), Ahmet Yilmaz / Elif Yildiz
    // t4: SLA uyumlu (tamamlanmis, zamaninda), Ayse Kaya / teknisyen yok
    private static async Task<AppDbContext> SeedAsync()
    {
        var db = CreateDb();
        var now = DateTime.UtcNow;

        var c1 = new Customer { Id = Guid.NewGuid(), FirstName = "Ahmet", LastName = "Yilmaz", Email = "a@t.com", Phone = "1", CreatedAt = now };
        var c2 = new Customer { Id = Guid.NewGuid(), FirstName = "Ayse", LastName = "Kaya", Email = "b@t.com", Phone = "2", CreatedAt = now };
        var tech1 = new Technician { Id = Guid.NewGuid(), FirstName = "Can", LastName = "Demir", Email = "c@t.com", Phone = "3", IsActive = true, CreatedAt = now };
        var tech2 = new Technician { Id = Guid.NewGuid(), FirstName = "Elif", LastName = "Yildiz", Email = "d@t.com", Phone = "4", IsActive = true, CreatedAt = now };
        db.Customers.AddRange(c1, c2);
        db.Technicians.AddRange(tech1, tech2);

        db.ServiceTickets.AddRange(
            new ServiceTicket
            {
                Id = Guid.NewGuid(), TicketNumber = "TS-001", CustomerId = c1.Id,
                AssignedTechnicianId = tech1.Id, Title = "Kombi arizasi", Description = "x",
                Status = TicketStatus.Assigned, Priority = TicketPriority.High,
                SlaDeadline = now.AddHours(-5), CreatedAt = now.AddHours(-10),
            },
            new ServiceTicket
            {
                Id = Guid.NewGuid(), TicketNumber = "TS-002", CustomerId = c2.Id,
                AssignedTechnicianId = tech2.Id, Title = "Klima arizasi", Description = "x",
                Status = TicketStatus.New, Priority = TicketPriority.Low,
                SlaDeadline = now.AddHours(20), CreatedAt = now.AddHours(-1),
            },
            new ServiceTicket
            {
                Id = Guid.NewGuid(), TicketNumber = "TS-003", CustomerId = c1.Id,
                AssignedTechnicianId = tech2.Id, Title = "Priz arizasi", Description = "x",
                Status = TicketStatus.Completed, Priority = TicketPriority.Medium,
                SlaDeadline = now.AddDays(-3), CompletedAt = now.AddDays(-2),
                CreatedAt = now.AddDays(-4),
            },
            new ServiceTicket
            {
                Id = Guid.NewGuid(), TicketNumber = "TS-004", CustomerId = c2.Id,
                Title = "Musluk arizasi", Description = "x",
                Status = TicketStatus.Completed, Priority = TicketPriority.Low,
                SlaDeadline = now.AddDays(-2), CompletedAt = now.AddDays(-3),
                CreatedAt = now.AddDays(-5),
            });

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Search_ShouldMatchCustomerName()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { Search = "ahmet" });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Ahmet Yilmaz", i.CustomerName));
    }

    [Fact]
    public async Task Search_ShouldMatchTechnicianName()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { Search = "elif" });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Search_ShouldStillMatchTicketNumberAndTitle()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var byNumber = await service.GetPagedAsync(new TicketQueryParameters { Search = "TS-002" });
        Assert.Equal(1, byNumber.TotalCount);

        var byTitle = await service.GetPagedAsync(new TicketQueryParameters { Search = "klima" });
        Assert.Equal(1, byTitle.TotalCount);
    }

    [Fact]
    public async Task Search_ShouldBeCaseInsensitive()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { Search = "AHMET YILMAZ" });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SlaViolated_True_ShouldReturnOnlyViolations()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { SlaViolated = true });

        // TS-001 (tamamlanmamis, deadline gecmis) + TS-003 (gec tamamlanmis)
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.TicketNumber == "TS-001");
        Assert.Contains(result.Items, i => i.TicketNumber == "TS-003");
    }

    [Fact]
    public async Task SlaViolated_False_ShouldReturnOnlyCompliant()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { SlaViolated = false });

        // TS-002 (deadline ileride) + TS-004 (zamaninda tamamlanmis)
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, i => i.TicketNumber == "TS-002");
        Assert.Contains(result.Items, i => i.TicketNumber == "TS-004");
    }

    [Fact]
    public async Task SlaViolated_Null_ShouldReturnAll()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters());

        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public async Task CreatedFrom_ShouldFilterByDate()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters
        {
            CreatedFrom = DateTime.UtcNow.AddHours(-24)
        });

        // TS-001 (-10 saat) ve TS-002 (-1 saat); TS-003/004 gunler once
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task CombinedFilters_ShouldWorkTogether()
    {
        using var db = await SeedAsync();
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters
        {
            Search = "ahmet",
            SlaViolated = true,
            CreatedFrom = DateTime.UtcNow.AddHours(-24),
        });

        // Ahmet'in ihlalleri: TS-001 ve TS-003; son 24 saat: sadece TS-001
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("TS-001", result.Items[0].TicketNumber);
    }

    [Fact]
    public async Task TechnicianIdFilter_ShouldWork()
    {
        using var db = await SeedAsync();
        var tech = db.Technicians.First(t => t.FirstName == "Elif");
        var service = new TicketQueryService(db);

        var result = await service.GetPagedAsync(new TicketQueryParameters { TechnicianId = tech.Id });

        Assert.Equal(2, result.TotalCount);
    }
}
