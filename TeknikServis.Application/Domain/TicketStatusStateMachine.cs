using TeknikServis.Application.Domain.Enums;

namespace TeknikServis.Application.Domain;

/// <summary>
/// Ariza kaydi durum gecislerinin tek yetkili kaynagi.
/// </summary>
public static class TicketStatusStateMachine
{
    // Gecis kurallari tek bir tabloda tutuluyor: kural degisirse yalnizca burasi degisir,
    // servis kodu ve testler etkilenmez. Bu sinif hicbir altyapiya (EF, DB, HTTP) bagimli
    // olmadigi icin milisaniyeler icinde birim testi yazilabiliyor.
    //
    // Her durumun yalnizca BIR sonraki adima gecmesine izin veriliyor:
    // - Geriye donus yasagi case gereksinimi.
    // - Adim atlama da bilincli olarak yasak: her adimin is anlami var
    //   (teknisyen atanmadan is baslayamaz, is bitmeden onaylanamaz).
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> AllowedTransitions =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = new[] { TicketStatus.Assigned },
            [TicketStatus.Assigned] = new[] { TicketStatus.InProgress },
            [TicketStatus.InProgress] = new[] { TicketStatus.Completed },
            [TicketStatus.Completed] = new[] { TicketStatus.Approved },
            [TicketStatus.Approved] = new[] { TicketStatus.Closed },
            [TicketStatus.Closed] = Array.Empty<TicketStatus>()
        };

    /// <summary>Verilen gecisin is kurallarina uygun olup olmadigini soyler.</summary>
    public static bool CanTransition(TicketStatus from, TicketStatus to)
        => AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    // Approved ve Closed "kilitli" kabul ediliyor: bu asamadan sonra kaydin icerigi,
    // onceligi veya teknisyeni degistirilemez. Aksi halde onaylanmis bir isin
    // kapsami sonradan degistirilebilir ve denetim izi anlamini yitirir.
    /// <summary>Kaydin icerik/atama degisikligine kapali olup olmadigini soyler.</summary>
    public static bool IsTerminalOrLocked(TicketStatus status)
        => status is TicketStatus.Approved or TicketStatus.Closed;

    // API yanitinda istemciye donuluyor: mobil taraf durum butonlarini buna gore cizer,
    // is kurallarini kendi icinde tekrar etmek zorunda kalmaz.
    /// <summary>Verilen durumdan gecis yapilabilecek durumlari dondurur.</summary>
    public static IReadOnlyList<TicketStatus> GetAllowedTargets(TicketStatus from)
        => AllowedTransitions.TryGetValue(from, out var targets) ? targets : Array.Empty<TicketStatus>();
}
