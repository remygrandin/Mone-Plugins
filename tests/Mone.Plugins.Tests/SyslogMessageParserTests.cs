using Mone.Plugins.Probes.Syslog;
using Xunit;

namespace Mone.Plugins.Tests;

public class SyslogMessageParserTests
{
    [Fact]
    public void TryParse_ValidRfc5424_ParsesAllFields()
    {
        var raw = "<134>1 2025-05-24T12:00:00.000Z myhost myapp 1234 ID47 - Hello world";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(SyslogFormat.Rfc5424, msg.Format);
        Assert.Equal(134, msg.Priority);
        Assert.Equal(SyslogFacility.Local0, msg.Facility); // 134 / 8 = 16
        Assert.Equal(SyslogSeverity.Informational, msg.Severity); // 134 % 8 = 6
        Assert.Equal("myhost", msg.Hostname);
        Assert.Equal("myapp", msg.AppName);
        Assert.Equal("1234", msg.ProcessId);
        Assert.Equal("ID47", msg.MsgId);
        Assert.Null(msg.StructuredData); // "-" maps to null
        Assert.Equal("Hello world", msg.Message);
    }

    [Fact]
    public void TryParse_ValidRfc5424_WithStructuredData()
    {
        var raw = "<165>1 2025-05-24T12:00:00Z myhost myapp - - [exampleSDID@32473 iut=\"3\" eventSource=\"Application\"] An application event";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(SyslogFormat.Rfc5424, msg.Format);
        Assert.Equal(165, msg.Priority);
        Assert.Equal(SyslogFacility.Local4, msg.Facility); // 165 / 8 = 20
        Assert.Equal(SyslogSeverity.Notice, msg.Severity); // 165 % 8 = 5
        Assert.Contains("exampleSDID@32473", msg.StructuredData);
        Assert.Equal("An application event", msg.Message);
    }

    [Fact]
    public void TryParse_ValidRfc5424_NilFields()
    {
        var raw = "<14>1 - - - - - - No hostname or app";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(SyslogFormat.Rfc5424, msg.Format);
        Assert.Null(msg.Hostname);
        Assert.Null(msg.AppName);
        Assert.Null(msg.ProcessId);
        Assert.Null(msg.MsgId);
        Assert.Null(msg.StructuredData);
        Assert.Equal("No hostname or app", msg.Message);
    }

    [Fact]
    public void TryParse_ValidRfc3164_ParsesAllFields()
    {
        var raw = "<134>May 24 12:00:00 myhost myapp[1234]: Hello world";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(SyslogFormat.Rfc3164, msg.Format);
        Assert.Equal(134, msg.Priority);
        Assert.Equal(SyslogFacility.Local0, msg.Facility);
        Assert.Equal(SyslogSeverity.Informational, msg.Severity);
        Assert.Equal("myhost", msg.Hostname);
        Assert.Equal("myapp", msg.AppName);
        Assert.Equal("1234", msg.ProcessId);
        Assert.Null(msg.MsgId);
        Assert.Null(msg.StructuredData);
        Assert.Equal("Hello world", msg.Message);
    }

    [Fact]
    public void TryParse_ValidRfc3164_NoPid()
    {
        var raw = "<13>Jan  5 08:30:00 server1 kernel: Boot completed";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(SyslogFormat.Rfc3164, msg.Format);
        Assert.Equal(13, msg.Priority);
        Assert.Equal(SyslogFacility.User, msg.Facility); // 13 / 8 = 1
        Assert.Equal(SyslogSeverity.Notice, msg.Severity); // 13 % 8 = 5
        Assert.Equal("server1", msg.Hostname);
        Assert.Equal("kernel", msg.AppName);
        Assert.Null(msg.ProcessId);
        Assert.Equal("Boot completed", msg.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrWhitespace_ReturnsFalse(string? input)
    {
        var ok = SyslogMessageParser.TryParse(input!, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("garbage data no pri")]
    [InlineData("just some text")]
    [InlineData("<>1 2025-05-24T12:00:00Z host app - - - msg")]
    [InlineData("<999>1 2025-05-24T12:00:00Z host app - - - msg")] // PRI > 191
    public void TryParse_MalformedInput_ReturnsFalse(string input)
    {
        var ok = SyslogMessageParser.TryParse(input, out _);
        Assert.False(ok);
    }

    [Theory]
    [InlineData(0, SyslogFacility.Kernel, SyslogSeverity.Emergency)]
    [InlineData(11, SyslogFacility.User, SyslogSeverity.Error)] // 11 / 8 = 1, 11 % 8 = 3
    [InlineData(134, SyslogFacility.Local0, SyslogSeverity.Informational)]
    [InlineData(191, SyslogFacility.Local7, SyslogSeverity.Debug)] // 191 / 8 = 23, 191 % 8 = 7
    public void TryParse_FacilityAndSeverityExtraction(int pri, SyslogFacility expectedFacility, SyslogSeverity expectedSeverity)
    {
        var raw = $"<{pri}>1 2025-05-24T12:00:00Z host app - - - test";

        var ok = SyslogMessageParser.TryParse(raw, out var msg);

        Assert.True(ok);
        Assert.Equal(expectedFacility, msg.Facility);
        Assert.Equal(expectedSeverity, msg.Severity);
    }
}
