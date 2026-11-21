namespace Applada.Modules.Matches.Domain;

public enum AttendanceStatus
{
    Present,
    Absent,
}

public static class AttendanceStatusText
{
    public const string Present = "present";
    public const string Absent = "absent";

    public static AttendanceStatus? Parse(string? text) => text switch
    {
        Present => AttendanceStatus.Present,
        Absent => AttendanceStatus.Absent,
        _ => null,
    };

    public static string ToContractText(this AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => Present,
        AttendanceStatus.Absent => Absent,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status de presença desconhecido."),
    };
}
