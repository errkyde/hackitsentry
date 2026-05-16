using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HITSight.Server.Services;

/// <summary>
/// Singleton email service for platform-level emails (Stripe events, cleanup notifications).
/// Reads SMTP settings directly from IConfiguration, not from tenant AppSettings.
/// </summary>
public class PlatformEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformEmailService> _logger;

    public PlatformEmailService(IConfiguration config, ILogger<PlatformEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Email:Host"]) &&
        !string.IsNullOrWhiteSpace(_config["Email:From"]);

    public async Task<string?> SendAsync(string to, string subject, string htmlBody)
    {
        if (!IsConfigured) return "E-Mail nicht konfiguriert.";

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_config["Email:From"]!));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            var useSsl = _config.GetValue<bool>("Email:UseSsl", false);
            var port = _config.GetValue<int>("Email:Port", 587);
            var host = _config["Email:Host"]!;

            await client.ConnectAsync(host, port,
                useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);

            var username = _config["Email:Username"];
            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, _config["Email:Password"]);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send platform email to {To}", to);
            return ex.Message;
        }
    }
}
