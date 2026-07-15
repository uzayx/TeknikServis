using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Common.Pagination;

public class TicketQueryParameters
{
    private const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : (value > MaxPageSize ? MaxPageSize : value);
    }

    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TechnicianId { get; set; }
    public string? Search { get; set; }

    public string? SortBy { get; set; }
    public string SortDir { get; set; } = "desc";
}
