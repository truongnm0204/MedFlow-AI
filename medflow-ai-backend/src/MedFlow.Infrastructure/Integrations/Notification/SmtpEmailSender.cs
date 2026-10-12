using MailKit.Net.Smtp;
using MailKit.Security;
using MedFlow.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace MedFlow.Infrastructure.Integrations.Notification;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var host = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
        var port = int.TryParse(_configuration["Smtp:Port"], out var p) ? p : 587;
        var username = _configuration["Smtp:Username"]
            ?? throw new InvalidOperationException("Smtp:Username not configured.");
        var password = _configuration["Smtp:Password"]
            ?? throw new InvalidOperationException("Smtp:Password not configured. Set via user-secrets.");
        var fromName = _configuration["Smtp:FromName"] ?? "MedFlow AI";

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromName, username));
        mime.To.Add(MailboxAddress.Parse(toEmail));
        mime.Subject = subject;
        mime.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.SendAsync(mime, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            throw;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }
    }
}
