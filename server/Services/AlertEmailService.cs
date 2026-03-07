using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HackITSentry.Server.Services;

public class AlertEmailService
{
    private readonly RuntimeSettings _settings;
    private readonly ILogger<AlertEmailService> _logger;

    public AlertEmailService(RuntimeSettings settings, ILogger<AlertEmailService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> SendAsync(string subject, string body)
    {
        if (!_settings.IsEmailConfigured) return "E-Mail nicht konfiguriert.";

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_settings.EmailFrom));
            message.To.Add(MailboxAddress.Parse(_settings.EmailTo));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

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
}
