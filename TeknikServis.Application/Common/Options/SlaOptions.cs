namespace TeknikServis.Application.Common.Options;

public class SlaOptions
{
    public const string SectionName = "Sla";

    public int CriticalHours { get; set; } = 4;
    public int HighHours { get; set; } = 8;
    public int MediumHours { get; set; } = 24;
    public int LowHours { get; set; } = 72;
}
