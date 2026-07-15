using Microsoft.AspNetCore.Mvc;
using TeknikServis.Application.Common.Pagination;
using TeknikServis.Application.DTOs;
using TeknikServis.Application.Interfaces;

namespace TeknikServis.Api.Controllers;

/// <summary>
/// Ariza kayitlarinin yonetimi: olusturma, teknisyen atama, durum gecisleri,
/// yorum ve ek (attachment) islemleri.
/// </summary>
[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;
    private readonly ITicketQueryService _queryService;
    private readonly ITicketInteractionService _interactionService;

    public TicketsController(
        ITicketService ticketService,
        ITicketQueryService queryService,
        ITicketInteractionService interactionService)
    {
        _ticketService = ticketService;
        _queryService = queryService;
        _interactionService = interactionService;
    }

    /// <summary>
    /// Ariza kayitlarini sayfalanmis olarak listeler.
    /// </summary>
    /// <remarks>
    /// Filtreler:
    /// status, priority, customerId, technicianId,
    /// slaViolated (true = ihlal edenler, false = uyanlar),
    /// createdFrom (bu tarihten sonrakiler, UTC),
    /// search (kayit numarasi, baslik, musteri adi veya teknisyen adi icinde arar).
    ///
    /// Siralama: sortBy = createdAt | priority | status | slaDeadline | ticketNumber, sortDir = asc | desc.
    /// Sayfalama: page (min 1), pageSize (min 1, max 100). Sinir disi degerler sessizce duzeltilir.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TicketListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketListItemResponse>>> GetAll(
        [FromQuery] TicketQueryParameters parameters, CancellationToken ct)
        => Ok(await _queryService.GetPagedAsync(parameters, ct));

    /// <summary>
    /// Tek bir ariza kaydini tum detaylariyla getirir.
    /// </summary>
    /// <remarks>
    /// Yanit, kaydin tam durum gecmisini (statusHistories) ve o an gecerli olan
    /// sonraki durumlari (allowedNextStatuses) icerir. Istemci, durum butonlarini
    /// bu alana gore cizebilir; is kurallarini kendi tarafinda tekrarlamasi gerekmez.
    /// </remarks>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetById(Guid id, CancellationToken ct)
        => Ok(await _queryService.GetByIdAsync(id, ct));

    /// <summary>
    /// Yeni ariza kaydi olusturur.
    /// </summary>
    /// <remarks>
    /// Kayit New durumunda acilir. SLA bitis tarihi, oncelige gore appsettings.json
    /// icindeki Sla bolumunden hesaplanir. Olusturma ani ilk durum gecmisi kaydi olarak yazilir.
    /// </remarks>
    /// <response code="400">Dogrulama hatasi (bos/uzun alanlar, gecersiz enum).</response>
    /// <response code="404">NOT_FOUND: Belirtilen musteri bulunamadi.</response>
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

    /// <summary>
    /// Ariza kaydinin icerigini (baslik, aciklama, oncelik) gunceller.
    /// </summary>
    /// <remarks>
    /// Oncelik degisirse SLA bitis tarihi yeniden hesaplanir.
    /// Approved ve Closed durumundaki kayitlar duzenlenemez.
    /// </remarks>
    /// <response code="400">Dogrulama hatasi.</response>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    /// <response code="409">TICKET_LOCKED: Approved/Closed kayitlar duzenlenemez.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Update(
        Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        await _ticketService.UpdateAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }

    /// <summary>
    /// Kayda teknisyen atar veya mevcut teknisyeni degistirir.
    /// </summary>
    /// <remarks>
    /// Kayit New durumundaysa otomatik olarak Assigned durumuna gecer.
    /// Her atama ve degisiklik, onceki/yeni teknisyen bilgisiyle birlikte
    /// durum gecmisine yazilir (denetim izi).
    /// </remarks>
    /// <response code="400">Dogrulama hatasi (changedByType: Customer | Technician | Center).</response>
    /// <response code="404">NOT_FOUND: Kayit veya teknisyen bulunamadi.</response>
    /// <response code="409">
    /// TICKET_LOCKED: Approved/Closed kayda atama yapilamaz.
    /// TECHNICIAN_INACTIVE: Teknisyen aktif degil.
    /// TECHNICIAN_ALREADY_ASSIGNED: Ayni teknisyen zaten atanmis.
    /// </response>
    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> AssignTechnician(
        Guid id, [FromBody] AssignTechnicianRequest request, CancellationToken ct)
    {
        await _ticketService.AssignTechnicianAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }

    /// <summary>
    /// Ariza kaydinin durumunu bir sonraki adima gecirir.
    /// </summary>
    /// <remarks>
    /// Gecerli akis: New -> Assigned -> InProgress -> Completed -> Approved -> Closed.
    /// Geriye donus ve adim atlama engellidir. Gecerli hedefleri gormek icin
    /// kaydin allowedNextStatuses alanina bakilabilir.
    /// </remarks>
    /// <response code="400">Dogrulama hatasi (gecersiz enum veya changedByType).</response>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    /// <response code="409">
    /// INVALID_STATUS_TRANSITION: Gecersiz durum gecisi (geriye donus/adim atlama).
    /// TICKET_LOCKED: Approved/Closed kayit uzerinde degisiklik yapilamaz.
    /// TECHNICIAN_REQUIRED: Teknisyen atanmadan Assigned durumuna gecilemez.
    /// </response>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> ChangeStatus(
        Guid id, [FromBody] ChangeStatusRequest request, CancellationToken ct)
    {
        await _ticketService.ChangeStatusAsync(id, request, ct);
        return Ok(await _queryService.GetByIdAsync(id, ct));
    }

    /// <summary>
    /// Kayda ait yorumlari kronolojik sirayla listeler.
    /// </summary>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    [HttpGet("{id:guid}/comments")]
    [ProducesResponseType(typeof(IReadOnlyList<CommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CommentResponse>>> GetComments(Guid id, CancellationToken ct)
        => Ok(await _interactionService.GetCommentsAsync(id, ct));

    /// <summary>
    /// Kayda yorum ekler.
    /// </summary>
    /// <remarks>
    /// Yorumlar Closed disindaki tum durumlarda eklenebilir; onay surecinde
    /// musteri-merkez iletisiminin devam etmesi gerektigi icin Approved durumu engellenmez.
    /// </remarks>
    /// <response code="400">Dogrulama hatasi (authorType: Customer | Technician | Center).</response>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    /// <response code="409">TICKET_LOCKED: Kapatilmis kayda yorum eklenemez.</response>
    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CommentResponse>> AddComment(
        Guid id, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var comment = await _interactionService.AddCommentAsync(id, request, ct);
        return CreatedAtAction(nameof(GetComments), new { id }, comment);
    }

    /// <summary>
    /// Kayda ait ek (dosya/fotograf) metadata listesini getirir.
    /// </summary>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    [HttpGet("{id:guid}/attachments")]
    [ProducesResponseType(typeof(IReadOnlyList<AttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AttachmentResponse>>> GetAttachments(Guid id, CancellationToken ct)
        => Ok(await _interactionService.GetAttachmentsAsync(id, ct));

    /// <summary>
    /// Kayda ek (dosya/fotograf) metadata'si kaydeder.
    /// </summary>
    /// <remarks>
    /// Dosyanin kendisi bu API uzerinden yuklenmez. Istemci dosyayi dogrudan
    /// nesne depolamaya (S3/blob) yukler, bu endpoint yalnizca erisim adresini
    /// (storagePath) ve metadata'yi saklar. Boylece API sunucusu dosya trafigi
    /// ve disk yonetimi yukunden ayrisir.
    ///
    /// Ekler Approved ve Closed durumunda eklenemez: kanit niteligindeki dosyalar
    /// onay adimindan once sisteme girmelidir.
    /// </remarks>
    /// <response code="400">Dogrulama hatasi (uploadedByType: Customer | Technician; max 25 MB).</response>
    /// <response code="404">NOT_FOUND: Kayit bulunamadi.</response>
    /// <response code="409">TICKET_LOCKED: Approved/Closed kayda ek yuklenemez.</response>
    [HttpPost("{id:guid}/attachments")]
    [ProducesResponseType(typeof(AttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AttachmentResponse>> AddAttachment(
        Guid id, [FromBody] CreateAttachmentRequest request, CancellationToken ct)
    {
        var attachment = await _interactionService.AddAttachmentAsync(id, request, ct);
        return CreatedAtAction(nameof(GetAttachments), new { id }, attachment);
    }
}

