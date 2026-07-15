using TeknikServis.Application.Common.Pagination;
using TeknikServis.Application.DTOs;

namespace TeknikServis.Application.Interfaces;

public interface ITicketQueryService
{
    Task<TicketResponse> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<TicketListItemResponse>> GetPagedAsync(TicketQueryParameters parameters, CancellationToken ct = default);
}
