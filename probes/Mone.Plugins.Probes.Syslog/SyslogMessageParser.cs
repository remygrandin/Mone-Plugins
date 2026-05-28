using System.Globalization;
using System.Text.RegularExpressions;

namespace Mone.Plugins.Probes.Syslog;

/// <summary>
/// Parses syslog messages in RFC 3164 (BSD) and RFC 5424 (structured) formats.
/// </summary>
public static partial class SyslogMessageParser
{
    // RFC 5424: <PRI>VERSION SP TIMESTAMP SP HOSTNAME SP APP-NAME SP PROCID SP MSGID SP STRUCTURED-DATA [SP MSG]
    // Example:  <134>1 2025-05-24T12:00:00.000Z myhost myapp 1234 ID47 - Hello world
    [GeneratedRegex(
        @"^<(\d{1,3})>(\d+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(-|(?:\[.*?\])+)\s*(.*)?$",
        RegexOptions.Singleline)]
    private static partial Regex Rfc5424Pattern();

    // RFC 3164: <PRI>TIMESTAMP HOSTNAME APP-NAME[PID]: MSG  (or without PID)
    // Timestamp format: "Mmm dd HH:mm:ss" (e.g., "May 24 12:00:00")
    // Example: <134>May 24 12:00:00 myhost myapp[1234]: Hello world
    [GeneratedRegex(
        @"^<(\d{1,3})>([A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(\S+)\s+(\S+?)(?:\[(\d+)\])?:\s*(.*)?$",
        RegexOptions.Singleline)]
    private static partial Regex Rfc3164Pattern();

    /// <summary>
    /// Attempts to parse a raw syslog message string into a <see cref="SyslogMessage"/>.
    /// Tries RFC 5424 first, then falls back to RFC 3164.
    /// </summary>
    public static bool TryParse(string rawMessage, out SyslogMessage result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(rawMessage))
            return false;

        // Try RFC 5424 first (has a version digit after the PRI)
        var match5424 = Rfc5424Pattern().Match(rawMessage);
        if (match5424.Success)
            return TryParseRfc5424(match5424, out result);

        // Fall back to RFC 3164
        var match3164 = Rfc3164Pattern().Match(rawMessage);
        if (match3164.Success)
            return TryParseRfc3164(match3164, out result);

        return false;
    }

    private static bool TryParseRfc5424(Match match, out SyslogMessage result)
    {
        result = default;

        if (!int.TryParse(match.Groups[1].Value, out var pri) || pri < 0 || pri > 191)
            return false;

        var (facility, severity) = DecodePriority(pri);

        var timestampStr = match.Groups[3].Value;
        DateTimeOffset timestamp;
        if (timestampStr == "-")
        {
            timestamp = DateTimeOffset.UtcNow;
        }
        else if (!DateTimeOffset.TryParse(timestampStr, CultureInfo.InvariantCulture,
                     DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out timestamp))
        {
            timestamp = DateTimeOffset.UtcNow;
        }

        var hostname = NilToNull(match.Groups[4].Value);
        var appName = NilToNull(match.Groups[5].Value);
        var procId = NilToNull(match.Groups[6].Value);
        var msgId = NilToNull(match.Groups[7].Value);
        var structuredData = NilToNull(match.Groups[8].Value);
        var message = match.Groups[9].Success ? match.Groups[9].Value : null;

        result = new SyslogMessage(
            SyslogFormat.Rfc5424,
            pri,
            facility,
            severity,
            timestamp,
            hostname,
            appName,
            procId,
            msgId,
            structuredData,
            message?.TrimEnd());

        return true;
    }

    private static bool TryParseRfc3164(Match match, out SyslogMessage result)
    {
        result = default;

        if (!int.TryParse(match.Groups[1].Value, out var pri) || pri < 0 || pri > 191)
            return false;

        var (facility, severity) = DecodePriority(pri);

        var timestampStr = match.Groups[2].Value;
        // RFC 3164 timestamps have no year — assume current year
        DateTimeOffset timestamp;
        if (!DateTimeOffset.TryParseExact(
                timestampStr,
                "MMM  d HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out timestamp) &&
            !DateTimeOffset.TryParseExact(
                timestampStr,
                "MMM dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out timestamp))
        {
            timestamp = DateTimeOffset.UtcNow;
        }
        else
        {
            // Set the year to the current year since RFC 3164 doesn't include it
            timestamp = new DateTimeOffset(
                DateTimeOffset.UtcNow.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour,
                timestamp.Minute,
                timestamp.Second,
                TimeSpan.Zero);
        }

        var hostname = match.Groups[3].Value;
        var appName = match.Groups[4].Value;
        var procId = match.Groups[5].Success && match.Groups[5].Length > 0 ? match.Groups[5].Value : null;
        var message = match.Groups[6].Success ? match.Groups[6].Value : null;

        result = new SyslogMessage(
            SyslogFormat.Rfc3164,
            pri,
            facility,
            severity,
            timestamp,
            hostname,
            appName,
            procId,
            MsgId: null,
            StructuredData: null,
            message?.TrimEnd());

        return true;
    }

    private static (SyslogFacility Facility, SyslogSeverity Severity) DecodePriority(int pri)
    {
        var facility = (SyslogFacility)(pri / 8);
        var severity = (SyslogSeverity)(pri % 8);
        return (facility, severity);
    }

    private static string? NilToNull(string value) => value == "-" ? null : value;
}

public enum SyslogFormat
{
    Rfc3164,
    Rfc5424
}

public enum SyslogSeverity
{
    Emergency = 0,
    Alert = 1,
    Critical = 2,
    Error = 3,
    Warning = 4,
    Notice = 5,
    Informational = 6,
    Debug = 7
}

public enum SyslogFacility
{
    Kernel = 0,
    User = 1,
    Mail = 2,
    Daemon = 3,
    Auth = 4,
    Syslog = 5,
    Lpr = 6,
    News = 7,
    Uucp = 8,
    Cron = 9,
    AuthPriv = 10,
    Ftp = 11,
    Ntp = 12,
    Audit = 13,
    Alert = 14,
    Clock = 15,
    Local0 = 16,
    Local1 = 17,
    Local2 = 18,
    Local3 = 19,
    Local4 = 20,
    Local5 = 21,
    Local6 = 22,
    Local7 = 23
}

public readonly record struct SyslogMessage(
    SyslogFormat Format,
    int Priority,
    SyslogFacility Facility,
    SyslogSeverity Severity,
    DateTimeOffset Timestamp,
    string? Hostname,
    string? AppName,
    string? ProcessId,
    string? MsgId,
    string? StructuredData,
    string? Message);
