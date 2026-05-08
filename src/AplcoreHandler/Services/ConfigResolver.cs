using AplcoreHandler.Models;

namespace AplcoreHandler.Services;

public static class ConfigResolver
{
    public const string SftpPasswordEnvVar = "APLCORE_SFTP_PASSWORD";
    public const string SmtpPasswordEnvVar = "APLCORE_SMTP_PASSWORD";

    /// <summary>Returns the effective SFTP password: env var takes precedence over config.</summary>
    public static string ResolveEffectiveSftpPassword(SftpConfig config)
    {
        var envVal = Environment.GetEnvironmentVariable(SftpPasswordEnvVar);
        return !string.IsNullOrEmpty(envVal) ? envVal : config.Password;
    }

    /// <summary>Returns the effective SMTP password: env var takes precedence over config.</summary>
    public static string ResolveEffectiveSmtpPassword(SmtpConfig config)
    {
        var envVal = Environment.GetEnvironmentVariable(SmtpPasswordEnvVar);
        return !string.IsNullOrEmpty(envVal) ? envVal : config.Password;
    }

    /// <summary>Validates SftpConfig. Returns list of error messages (empty = valid).</summary>
    public static List<string> ValidateSftpConfig(SftpConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Host))
            errors.Add("SFTP host is required.");
        if (config.Port is <= 0 or > 65535)
            errors.Add($"SFTP port must be 1-65535, got {config.Port}.");
        if (string.IsNullOrWhiteSpace(config.Username))
            errors.Add("SFTP username is required.");
        if (string.IsNullOrWhiteSpace(config.RemotePath))
            errors.Add("SFTP remote path is required.");
        return errors;
    }

    /// <summary>Validates SmtpConfig. Returns list of error messages (empty = valid).</summary>
    public static List<string> ValidateSmtpConfig(SmtpConfig config)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Host))
            errors.Add("SMTP host is required.");
        if (config.Port is <= 0 or > 65535)
            errors.Add($"SMTP port must be 1-65535, got {config.Port}.");
        if (string.IsNullOrWhiteSpace(config.FromAddress))
            errors.Add("SMTP fromAddress is required.");
        if (config.To is not { Length: > 0 })
            errors.Add("SMTP must have at least one 'to' recipient.");
        return errors;
    }
}
