using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HackITSentry.Server.Services;

public class AlertEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<AlertEmailService> _logger;

    public AlertEmailService(IConfiguration config, ILogger<AlertEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Email:Host"]) &&
        !string.IsNullOrWhiteSpace(_config["Email:To"]);

    public async Task SendAsync(string subject, string body)
    {
        if (!IsConfigured) return;

        var host = _config["Email:Host"]!;
        var port = _config.GetValue<int>("Email:Port", 587);
        var username = _config["Email:Username"] ?? "";
        var password = _config["Email:Password"] ?? "";
        var from = _config["Email:From"] ?? "sentry@localhost";
        var to = _config["Email:To"]!;
        var useSsl = _config.GetValue<bool>("Email:UseSsl", false);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            var secureSocketOptions = useSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(host, port, secureSocketOptions);

            if (!string.IsNullOrEmpty(username))
                await client.AuthenticateAsync(username, password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert email to {To}", to);
        }
    }
}
