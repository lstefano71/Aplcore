using AplcoreHandler.Models;
using AplcoreHandler.Output;
using AplcoreHandler.Formatting;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AplcoreHandler.Services;

public static class NotificationService
{
    private const int MaxShippedItems = 20;
    private const int MaxFailedItems = 10;

    /// <summary>
    /// Sends a shipment summary email if at least one upload succeeded.
    /// In dry-run mode, reports what would happen without sending.
    /// </summary>
    public static bool SendIfNeeded(
        SmtpConfig config,
        string effectivePassword,
        TransferResult transferResult,
        IOutputRenderer output,
        bool dryRun)
    {
        var allRecipients = GetAllRecipients(config);
        bool wouldSend = transferResult.Uploaded.Count > 0;

        if (dryRun) {
            output.ReportDryRunNotification(wouldSend, allRecipients);
            return false;
        }

        if (!wouldSend)
            return false;

        try {
            var body = BuildSummaryBody(transferResult);
            SendEmail(config, effectivePassword, BuildSubject(transferResult.Uploaded.Count), body);
            output.ReportNotificationSent(allRecipients);
            return true;
        } catch (Exception ex) {
            output.ReportError($"Email notification failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds a bounded plain-text summary body.
    /// Shows first 20 shipped items + first 10 failed items, then truncates.
    /// </summary>
    public static string BuildSubject(int uploadedCount)
    {
        var noun = uploadedCount == 1 ? "file" : "files";
        return $"AplcoreHandler: {uploadedCount} {noun} shipped";
    }

    public static string BuildSummaryBody(TransferResult result)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("AplcoreHandler Shipment Summary");
        sb.AppendLine(new string('=', 40));
        sb.AppendLine();

        // Totals
        sb.AppendLine($"Uploaded:  {result.Uploaded.Count}");
        sb.AppendLine($"Skipped:   {result.Skipped.Count}");
        sb.AppendLine($"Failed:    {result.Failed.Count}");
        sb.AppendLine();

        // Shipped details (bounded)
        if (result.Uploaded.Count > 0) {
            sb.AppendLine("Shipped files:");
            var shownCount = Math.Min(result.Uploaded.Count, MaxShippedItems);
            for (int i = 0; i < shownCount; i++)
                sb.AppendLine($"  ✓ {result.Uploaded[i].Name} ({ByteSizeFormatter.Format(result.Uploaded[i].SizeBytes)})");
            if (result.Uploaded.Count > MaxShippedItems)
                sb.AppendLine($"  ... and {result.Uploaded.Count - MaxShippedItems} more");
            sb.AppendLine();
        }

        // Failed details (bounded)
        if (result.Failed.Count > 0) {
            sb.AppendLine("Failed files:");
            var shownCount = Math.Min(result.Failed.Count, MaxFailedItems);
            for (int i = 0; i < shownCount; i++)
                sb.AppendLine($"  ✗ {result.Failed[i].Name} ({ByteSizeFormatter.Format(result.Failed[i].SizeBytes)}): {result.Failed[i].Error}");
            if (result.Failed.Count > MaxFailedItems)
                sb.AppendLine($"  ... and {result.Failed.Count - MaxFailedItems} more");
            sb.AppendLine();
        }

        sb.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

        return sb.ToString();
    }

    private static void SendEmail(SmtpConfig config, string effectivePassword, string subject, string body)
    {
        var message = new MimeMessage();

        // From
        message.From.Add(new MailboxAddress(
            config.FromDisplayName ?? config.FromAddress,
            config.FromAddress));

        // To
        foreach (var to in config.To)
            message.To.Add(MailboxAddress.Parse(to));

        // Cc
        if (config.Cc is { Length: > 0 }) {
            foreach (var cc in config.Cc)
                message.Cc.Add(MailboxAddress.Parse(cc));
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();

        // Connect with appropriate security
        var secureOption = config.UseSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        client.Connect(config.Host, config.Port, secureOption);

        // Authenticate if credentials provided
        if (!string.IsNullOrEmpty(config.Username))
            client.Authenticate(config.Username, effectivePassword);

        client.Send(message);
        client.Disconnect(quit: true);
    }

    private static string[] GetAllRecipients(SmtpConfig config)
    {
        if (config.Cc is not { Length: > 0 })
            return config.To;
        return [.. config.To, .. config.Cc];
    }
}
