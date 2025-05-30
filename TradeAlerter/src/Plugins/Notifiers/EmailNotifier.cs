using TradeAlerter.Domain.Models;
using TradeAlerter.Domain.Notification;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace TradeAlerter.Plugins.Notifiers;

public sealed class EmailOptions
{
    public string SmtpServer { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
}

public class EmailNotifier(IOptions<EmailOptions> options, ILogger<EmailNotifier> logger)
    : INotifier
{
    private readonly EmailOptions _options = options.Value;

    public async Task NotifyAsync(IReadOnlyList<INotice> relevantNotices)
    {
        if (!relevantNotices.Any())
        {
            logger.LogInformation("No relevant notices to send via email");
            return;
        }

        logger.LogTrace("Preparing to send email notification for {NoticeCount} relevant notice(s)", relevantNotices.Count);

        var subject = $"Trading Alert: {relevantNotices.Count} Pipeline Notice{(relevantNotices.Count > 1 ? "s" : "")} Detected";
        var body = BuildEmailBody(relevantNotices);

        try
        {
            using var smtpClient = new SmtpClient(_options.SmtpServer, _options.SmtpPort);
            smtpClient.EnableSsl = _options.EnableSsl;
            smtpClient.Credentials = new NetworkCredential(_options.Username, _options.Password);

            using var message = new MailMessage(_options.FromEmail, _options.ToEmail, subject, body);
            message.IsBodyHtml = false;

            logger.LogTrace("Sending email from {FromEmail} to {ToEmail} via {SmtpServer}:{SmtpPort}", 
                _options.FromEmail, _options.ToEmail, _options.SmtpServer, _options.SmtpPort);

            await smtpClient.SendMailAsync(message);
            
            logger.LogInformation("Email notification sent successfully for {NoticeCount} notice(s)", relevantNotices.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email notification for {NoticeCount} notice(s). SMTP Server: {SmtpServer}, From: {FromEmail}, To: {ToEmail}", 
                relevantNotices.Count, _options.SmtpServer, _options.FromEmail, _options.ToEmail);
            throw;
        }
    }

    private string BuildEmailBody(IReadOnlyList<INotice> notices)
    {
        logger.LogTrace("Building email body for {NoticeCount} notice(s)", notices.Count);
        
        var sb = new StringBuilder();
        sb.AppendLine("TRADING ALERT - Pipeline Curtailment Notices");
        sb.AppendLine("============================================");
        sb.AppendLine();
        sb.AppendLine($"Detected {notices.Count} relevant notice{(notices.Count > 1 ? "s" : "")} requiring trader attention:");
        sb.AppendLine();

        foreach (var notice in notices.OrderByDescending(n => n.TimeStamp))
        {
            sb.AppendLine($"Pipeline: {notice.Pipeline}");
            sb.AppendLine($"Type: {notice.Type}");
            sb.AppendLine($"Summary: {notice.Summary}");
            sb.AppendLine($"Location: {notice.Location}");
            sb.AppendLine($"Timestamp: {notice.TimeStamp:yyyy-MM-dd HH:mm:ss zzz}");
            
            if (notice.CurtailmentVolumeDth.HasValue)
                sb.AppendLine($"Curtailment Volume: {notice.CurtailmentVolumeDth:N0} Dth");
            
            sb.AppendLine($"Link: {notice.Link}");
            sb.AppendLine();
            sb.AppendLine("----------------------------------------");
            sb.AppendLine();
        }

        sb.AppendLine("This is an automated alert from the TradeAlerter system.");
        return sb.ToString();
    }
}
