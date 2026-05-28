using System.Text.Json;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Xunit;
using Mone.Plugins.Notifications.Email;
using Mone.Plugins.Probes.Https;
using Mone.Plugins.Probes.Ping;
using Mone.Plugins.Notifications.Slack;
using Mone.Plugins.Probes.SnmpTrap;
using Mone.Plugins.Probes.Syslog;
using Mone.Plugins.Notifications.Teams;
using Mone.Plugins.Checkers.ThresholdChecker;
using Mone.Plugins.Probes.Webhook;
using Mone.Plugins.Notifications.Webhook;

namespace Mone.Plugins.Tests;

public class ConfigManifestTests
{
    private static ConfigManifest GetManifest(IConfigurablePlugin plugin) => plugin.GetConfigManifest();

    [Fact]
    public void PingProbe_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new PingProbePlugin();
        var manifest = GetManifest(plugin);

        Assert.Equal(3, manifest.Fields.Count);
        AssertField(manifest, "Timeout", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "BufferSize", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "Ttl", ConfigFieldType.Integer, required: false);
    }

    [Fact]
    public void HttpsProbe_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new HttpsProbePlugin();
        var manifest = GetManifest(plugin);

        Assert.Equal(6, manifest.Fields.Count);
        AssertField(manifest, "Timeout", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "ExpectedStatusCode", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "CertExpiryWarningDays", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "FollowRedirects", ConfigFieldType.Boolean, required: false);
        AssertField(manifest, "ValidateCertificate", ConfigFieldType.Boolean, required: false);
        AssertField(manifest, "Path", ConfigFieldType.String, required: false);
    }

    [Fact]
    public void WebhookProbe_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new WebhookProbePlugin();
        var manifest = GetManifest(plugin);

        Assert.Single(manifest.Fields);
        AssertField(manifest, "MaxPayloadSize", ConfigFieldType.Integer, required: false);
    }

    [Fact]
    public void ThresholdChecker_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new ThresholdCheckerPlugin();
        var manifest = GetManifest(plugin);

        Assert.Equal(4, manifest.Fields.Count);
        AssertField(manifest, "MetricKey", ConfigFieldType.String, required: true);
        AssertField(manifest, "WarningThreshold", ConfigFieldType.Double, required: false);
        AssertField(manifest, "CriticalThreshold", ConfigFieldType.Double, required: false);
        AssertField(manifest, "ComparisonMode", ConfigFieldType.Choice, required: false);
    }

    [Fact]
    public void EmailNotification_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new EmailNotificationPlugin();
        var manifest = GetManifest(plugin);

        Assert.Equal(7, manifest.Fields.Count);
        AssertField(manifest, "SmtpHost", ConfigFieldType.String, required: true);
        AssertField(manifest, "SmtpPort", ConfigFieldType.Integer, required: false);
        AssertField(manifest, "SmtpUser", ConfigFieldType.String, required: false);
        AssertField(manifest, "SmtpPassword", ConfigFieldType.Secret, required: false);
        AssertField(manifest, "FromAddress", ConfigFieldType.String, required: true);
        AssertField(manifest, "ToAddresses", ConfigFieldType.String, required: true);
        AssertField(manifest, "UseSsl", ConfigFieldType.Boolean, required: false);
    }

    [Fact]
    public void SlackNotification_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new SlackNotificationPlugin();
        var manifest = GetManifest(plugin);

        Assert.Single(manifest.Fields);
        AssertField(manifest, "WebhookUrl", ConfigFieldType.Secret, required: true);
    }

    [Fact]
    public void TeamsNotification_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new TeamsNotificationPlugin();
        var manifest = GetManifest(plugin);

        Assert.Single(manifest.Fields);
        AssertField(manifest, "WebhookUrl", ConfigFieldType.Secret, required: true);
    }

    [Fact]
    public void WebhookNotification_ManifestFieldsMatchConfigKeys()
    {
        var plugin = new WebhookNotificationPlugin();
        var manifest = GetManifest(plugin);

        Assert.Equal(3, manifest.Fields.Count);
        AssertField(manifest, "Url", ConfigFieldType.String, required: true);
        AssertField(manifest, "Method", ConfigFieldType.Choice, required: false);
        AssertField(manifest, "Headers", ConfigFieldType.String, required: false);
    }

    [Fact]
    public void SyslogProbe_ManifestHasNoFields()
    {
        var plugin = new SyslogProbePlugin();
        var manifest = GetManifest(plugin);

        Assert.Empty(manifest.Fields);
    }

    [Fact]
    public void SnmpTrapProbe_ManifestHasNoFields()
    {
        var plugin = new SnmpTrapProbePlugin();
        var manifest = GetManifest(plugin);

        Assert.Empty(manifest.Fields);
    }

    [Theory]
    [MemberData(nameof(AllConfigurablePlugins))]
    public void AllManifests_RoundTripThroughJson(IConfigurablePlugin plugin)
    {
        var manifest = GetManifest(plugin);

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<ConfigManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(manifest.Fields.Count, deserialized.Fields.Count);

        for (var i = 0; i < manifest.Fields.Count; i++)
        {
            Assert.Equal(manifest.Fields[i].Key, deserialized.Fields[i].Key);
            Assert.Equal(manifest.Fields[i].FieldType, deserialized.Fields[i].FieldType);
            Assert.Equal(manifest.Fields[i].Required, deserialized.Fields[i].Required);
            Assert.Equal(manifest.Fields[i].DefaultValue, deserialized.Fields[i].DefaultValue);
        }
    }

    [Theory]
    [MemberData(nameof(AllConfigurablePlugins))]
    public void AllManifests_FieldKeysAreUnique(IConfigurablePlugin plugin)
    {
        var manifest = GetManifest(plugin);
        var keys = manifest.Fields.Select(f => f.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllConfigurablePlugins))]
    public void AllManifests_FieldsHaveDisplayNames(IConfigurablePlugin plugin)
    {
        var manifest = GetManifest(plugin);
        foreach (var field in manifest.Fields)
        {
            Assert.False(string.IsNullOrWhiteSpace(field.DisplayName),
                $"Field '{field.Key}' has empty DisplayName");
        }
    }

    [Theory]
    [MemberData(nameof(AllConfigurablePlugins))]
    public void AllManifests_ChoiceFieldsHaveChoices(IConfigurablePlugin plugin)
    {
        var manifest = GetManifest(plugin);
        foreach (var field in manifest.Fields.Where(f => f.FieldType == ConfigFieldType.Choice))
        {
            Assert.NotNull(field.Choices);
            Assert.NotEmpty(field.Choices);
        }
    }

    public static TheoryData<IConfigurablePlugin> AllConfigurablePlugins => new()
    {
        new PingProbePlugin(),
        new HttpsProbePlugin(),
        new WebhookProbePlugin(),
        new ThresholdCheckerPlugin(),
        new EmailNotificationPlugin(),
        new SlackNotificationPlugin(),
        new TeamsNotificationPlugin(),
        new WebhookNotificationPlugin(),
        new SyslogProbePlugin(),
        new SnmpTrapProbePlugin(),
    };

    private static void AssertField(ConfigManifest manifest, string key, ConfigFieldType expectedType, bool required)
    {
        var field = manifest.Fields.SingleOrDefault(f => f.Key == key);
        Assert.NotNull(field);
        Assert.Equal(expectedType, field.FieldType);
        Assert.Equal(required, field.Required);
    }
}
