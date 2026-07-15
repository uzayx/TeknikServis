using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Common.Pagination;

public class TicketQueryParameters
{
    private const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>Sayfa numarasi (min 1). Sinir disi deger sessizce duzeltilir.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Sayfa boyutu (1-100). Sinir disi deger sessizce duzeltilir.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : (value > MaxPageSize ? MaxPageSize : value);
    }

    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TechnicianId { get; set; }

    /// <summary>Kayit numarasi, baslik, musteri adi veya teknisyen adi icinde arar.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// true: SLA ihlali olanlar, false: SLA'ya uyanlar, null: tumu.
    /// Tamamlanmis kayitlarda CompletedAt, tamamlanmamislarda su an ile karsilastirilir.
    /// </summary>
    public bool? SlaViolated { get; set; }

    /// <summary>Bu tarihten sonra olusturulan kayitlar (UTC).</summary>
    public DateTime? CreatedFrom { get; set; }

    public string? SortBy { get; set; }
    public string SortDir { get; set; } = "desc";
}
