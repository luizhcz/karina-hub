using System.ComponentModel;
using System.Globalization;

namespace EfsAiHub.Platform.Runtime.Tools;

public static class DateTimeFunctions
{
    // ConvertTimeBySystemTimeZoneId tenta múltiplos backends (TZDB, Windows IDs,
    // ICU). Lazy avoid type initializer issues caso a primeira chamada falhe.
    // Fallback manual pra UTC-3 quando o ID não existe (containers Alpine sem
    // tzdata, ex.).
    private const string TzId = "America/Sao_Paulo";
    private static readonly CultureInfo PtBr = new("pt-BR");

    [Description("Retorna data, hora, data+hora ISO-8601, dia da semana e timezone para America/Sao_Paulo (UTC-3).")]
    public static DateTimeResult GetDateTime()
    {
        DateTime now;
        TimeSpan offset;
        try
        {
            now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, TzId);
            offset = TimeZoneInfo.FindSystemTimeZoneById(TzId).GetUtcOffset(now);
        }
        catch (TimeZoneNotFoundException)
        {
            offset = TimeSpan.FromHours(-3);
            now = DateTime.UtcNow + offset;
        }

        return new DateTimeResult(
            Date: now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Time: now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            DateTime: new DateTimeOffset(now, offset).ToString("o", CultureInfo.InvariantCulture),
            DayOfWeek: PtBr.DateTimeFormat.GetDayName(now.DayOfWeek),
            Timezone: TzId);
    }

    public record DateTimeResult(string Date, string Time, string DateTime, string DayOfWeek, string Timezone);
}
