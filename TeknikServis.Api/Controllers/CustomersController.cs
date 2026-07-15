using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeknikServis.Application.Common.Exceptions;
using TeknikServis.Application.Domain.Entities;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly IAppDbContext _db;

    public CustomersController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAll(CancellationToken ct)
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.FullName)
            .Select(c => new CustomerResponse(c.Id, c.FullName, c.Email, c.Phone, c.Address, c.CreatedAt))
            .ToListAsync(ct);
        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);
        return Ok(new CustomerResponse(c.Id, c.FullName, c.Email, c.Phone, c.Address, c.CreatedAt));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            CreatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        var response = new CustomerResponse(
            customer.Id, customer.FullName, customer.Email,
            customer.Phone, customer.Address, customer.CreatedAt);
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, response);
    }
}
