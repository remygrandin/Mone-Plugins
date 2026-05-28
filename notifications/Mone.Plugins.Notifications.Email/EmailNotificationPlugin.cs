using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Mone.Contracts.Models;
using Mone.Contracts.Plugins;
using Mone.Contracts.Plugins.Attributes;

namespace Mone.Plugins.Notifications.Email;

[NotificationPlugin]
public sealed class EmailNotificationPlugin : INotificationPlugin, IConfigurablePlugin
{
    public ConfigManifest GetConfigManifest() => new()
    {
        Fields =
        [
            new ConfigField { Key = "SmtpHost", DisplayName = "SMTP Host", Description = "SMTP server hostname", FieldType = ConfigFieldType.String, Required = true, IsGlobal = true },
            new ConfigField { Key = "SmtpPort", DisplayName = "SMTP Port", Description = "SMTP server port", FieldType = ConfigFieldType.Integer, DefaultValue = "587", Required = false, IsGlobal = true },
            new ConfigField { Key = "SmtpUser", DisplayName = "SMTP User", Description = "SMTP authentication username", FieldType = ConfigFieldType.String, Required = false, IsGlobal = true },
            new ConfigField { Key = "SmtpPassword", DisplayName = "SMTP Password", Description = "SMTP authentication password", FieldType = ConfigFieldType.Secret, Required = false, IsGlobal = true },
            new ConfigField { Key = "FromAddress", DisplayName = "From Address", Description = "Sender email address", FieldType = ConfigFieldType.String, Required = true, IsGlobal = true },
            new ConfigField { Key = "ToAddresses", DisplayName = "To Addresses", Description = "Comma-separated recipient email addresses", FieldType = ConfigFieldType.String, Required = true, IsGlobal = false },
            new ConfigField { Key = "UseSsl", DisplayName = "Use SSL/TLS", Description = "Whether to use StartTLS for SMTP connection", FieldType = ConfigFieldType.Boolean, DefaultValue = "true", Required = false, IsGlobal = true },
        ]
    };
    public string Name => "EmailNotifier";
    public Version Version => new(1, 0, 0);
    public string Description => "Sends email notifications via SMTP using MailKit";

    private string _smtpHost = string.Empty;
    private int _smtpPort = 587;
    private string _smtpUser = string.Empty;
    private string _smtpPassword = string.Empty;
    private string _fromAddress = string.Empty;
    private string[] _toAddresses = [];
    private bool _useSsl = true;
    private bool _configured;

    public Task InitializeAsync(IPluginContext context)
    {
        var config = context.Configuration;

        if (!config.TryGetValue("SmtpHost", out var host) || string.IsNullOrWhiteSpace(host))
            return Task.CompletedTask;

        if (!config.TryGetValue("FromAddress", out var from) || string.IsNullOrWhiteSpace(from))
            return Task.CompletedTask;

        if (!config.TryGetValue("ToAddresses", out var to) || string.IsNullOrWhiteSpace(to))
            return Task.CompletedTask;

        _smtpHost = host;
        _fromAddress = from;
        _toAddresses = to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (config.TryGetValue("SmtpPort", out var portStr) && int.TryParse(portStr, out var port))
            _smtpPort = port;

        if (config.TryGetValue("SmtpUser", out var user))
            _smtpUser = user;

        if (config.TryGetValue("SmtpPassword", out var password))
            _smtpPassword = password;

        if (config.TryGetValue("UseSsl", out var sslStr) && bool.TryParse(sslStr, out var ssl))
            _useSsl = ssl;

        _configured = true;
        return Task.CompletedTask;
    }

    public async Task<DeliveryResult> SendAsync(StatusChange statusChange, CancellationToken cancellationToken)
    {
        if (!_configured)
            return new DeliveryResult(false, "Missing SMTP configuration");

        try
        {
            var message = BuildMessage(statusChange);

            using var client = new SmtpClient();
            var secureSocketOptions = _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_smtpHost, _smtpPort, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrEmpty(_smtpUser))
                await client.AuthenticateAsync(_smtpUser, _smtpPassword, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return new DeliveryResult(true);
        }
        catch (Exception ex) when (ex is MailKit.CommandException or MailKit.ProtocolException or System.Net.Sockets.SocketException)
        {
            return new DeliveryResult(false, ex.Message);
        }
    }

    private MimeMessage BuildMessage(StatusChange statusChange)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_fromAddress));
        foreach (var addr in _toAddresses)
            message.To.Add(MailboxAddress.Parse(addr));

        message.Subject = $"[Mone Alert] Host {statusChange.TargetId}: {statusChange.PreviousStatus} → {statusChange.CurrentStatus}";

        var body = $"""
            Status Change Alert
            ====================

            Target:    {statusChange.TargetId}
            Previous:  {statusChange.PreviousStatus}
            Current:   {statusChange.CurrentStatus}
            Changed:   {statusChange.ChangedAt:O}
            Status:    {statusChange.LatestResult.Status}
            Summary:   {statusChange.LatestResult.Summary}
            Duration:  {statusChange.LatestResult.Duration}
            """;

        message.Body = new TextPart("plain") { Text = body };
        return message;
    }
}
