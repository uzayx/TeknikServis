using Microsoft.AspNetCore.Mvc;
using TeknikServis.Application.Common.Pagination;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly ITicketQueryService _queryService;

    public TicketsController(ITicketService ticketService, ITicketQueryService queryService)
    {
        _ticketService = ticketService;
        _queryService = queryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TicketListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketListItemResponse>>> GetAll(
        [FromQuery] TicketQueryParameters parameters, CancellationToken ct)
        => Ok(await _queryService.GetPagedAsync(parameters, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetById(Guid id, CancellationToken ct)
        => Ok(await _queryService.GetByIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> Create(
        [FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _ticketService.CreateAsync(request, ct);
        var response = await _queryService.GetByIdAsync(ticket.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Update(
        Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        await _ticketService.UpdateAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }

    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> AssignTechnician(
        Guid id, [FromBody] AssignTechnicianRequest request, CancellationToken ct)
    {
        await _ticketService.AssignTechnicianAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }

    [HttpPost("{id:guid}/status")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> ChangeStatus(
        Guid id, [FromBody] ChangeStatusRequest request, CancellationToken ct)
    {
        await _ticketService.ChangeStatusAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }
}
