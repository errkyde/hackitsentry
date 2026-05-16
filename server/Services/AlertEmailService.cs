using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HITSight.Server.Services;

public class AlertEmailService
{
    private readonly RuntimeSettings _settings;
    private readonly ILogger<AlertEmailService> _logger;

    public AlertEmailService(RuntimeSettings settings, ILogger<AlertEmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    // Used by background services that already have a RuntimeSettings instance from scope
    internal static AlertEmailService FromSettings(RuntimeSettings settings, ILogger<AlertEmailService> logger)
        => new(settings, logger);

    public async Task<string?> SendAsync(string subject, string htmlBody)
    {
        if (!_settings.IsEmailConfigured) return "E-Mail nicht konfiguriert.";

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_settings.EmailFrom));
            foreach (var addr in _settings.EmailToList)
                message.To.Add(MailboxAddress.Parse(addr));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            var secureSocketOptions = _settings.EmailUseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_settings.EmailHost, _settings.EmailPort, secureSocketOptions);

            if (!string.IsNullOrEmpty(_settings.EmailUsername))
                await client.AuthenticateAsync(_settings.EmailUsername, _settings.EmailPassword);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return null; // null = success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert email to {To}", _settings.EmailTo);
            return ex.Message;
        }
    }

    public async Task<string?> SendToAsync(string toEmail, string subject, string htmlBody)
    {
        if (!_settings.IsEmailConfigured) return "E-Mail nicht konfiguriert.";

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_settings.EmailFrom));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            var secureSocketOptions = _settings.EmailUseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_settings.EmailHost, _settings.EmailPort, secureSocketOptions);

            if (!string.IsNullOrEmpty(_settings.EmailUsername))
                await client.AuthenticateAsync(_settings.EmailUsername, _settings.EmailPassword);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invite email to {To}", toEmail);
            return ex.Message;
        }
    }

    /// <summary>Builds a styled HTML email body.</summary>
    /// <param name="color">#dc2626 red | #ea580c orange | #16a34a green</param>
    public static string BuildHtml(string color, string badge, string heading, string bodyHtml, string? footerNote = null)
    {
        var footer = footerNote != null
            ? $"<p style='margin:16px 0 0;font-size:12px;color:#a1a1aa;'>{footerNote}</p>"
            : "";

        return $"""
            <!DOCTYPE html>
            <html lang="de">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:24px 16px;background:#f4f4f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;">
              <div style="max-width:580px;margin:0 auto;">
                <div style="background:#18181b;border-radius:8px 8px 0 0;padding:20px 28px;">
                  <span style="font-size:13px;font-weight:600;color:#a1a1aa;letter-spacing:.05em;text-transform:uppercase;">HITSight</span>
                </div>
                <div style="background:#ffffff;border:1px solid #e4e4e7;border-top:none;border-radius:0 0 8px 8px;padding:28px;">
                  <span style="display:inline-block;padding:4px 12px;border-radius:99px;font-size:12px;font-weight:600;background:{color}1a;color:{color};margin-bottom:16px;">{badge}</span>
                  <h2 style="margin:0 0 20px;font-size:17px;font-weight:600;color:#18181b;line-height:1.4;">{heading}</h2>
                  {bodyHtml}
                  {footer}
                </div>
                <p style="margin:16px 0 0;text-align:center;font-size:11px;color:#a1a1aa;">
                  HITSight &middot; {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC
                </p>
              </div>
            </body>
            </html>
            """;
    }

    public static string DeviceRows(IEnumerable<(string Name, string? Sub, string? Extra)> rows) =>
        $"<table style=\"width:100%;border-collapse:collapse;border:1px solid #e4e4e7;border-radius:6px;overflow:hidden;font-size:14px;\">" +
        string.Join("", rows.Select((d, i) =>
            $"<tr style=\"{(i > 0 ? "border-top:1px solid #e4e4e7;" : "")}\">" +
            $"<td style=\"padding:11px 16px;\"><div style=\"font-weight:600;color:#18181b;\">{d.Name}</div>" +
            (d.Sub != null ? $"<div style=\"font-size:12px;color:#71717a;margin-top:2px;\">{d.Sub}</div>" : "") +
            $"</td>" +
            (d.Extra != null ? $"<td style=\"padding:11px 16px;text-align:right;font-size:12px;color:#71717a;white-space:nowrap;\">{d.Extra}</td>" : "") +
            $"</tr>")) +
        "</table>";
}
