using AplcoreHandler.Models;
using AplcoreHandler.Services;

namespace AplcoreHandler.Tests;

public class ConfigResolverTests
{
    [Fact]
    public void SftpPassword_EnvVarOverridesConfig()
    {
        var config = new SftpConfig("host") { Password = "config-pw" };
        Environment.SetEnvironmentVariable(ConfigResolver.SftpPasswordEnvVar, "env-pw");
        try {
            Assert.Equal("env-pw", ConfigResolver.ResolveEffectiveSftpPassword(config));
        } finally {
            Environment.SetEnvironmentVariable(ConfigResolver.SftpPasswordEnvVar, null);
        }
    }

    [Fact]
    public void SftpPassword_FallsBackToConfig_WhenNoEnvVar()
    {
        var config = new SftpConfig("host") { Password = "config-pw" };
        Environment.SetEnvironmentVariable(ConfigResolver.SftpPasswordEnvVar, null);
        Assert.Equal("config-pw", ConfigResolver.ResolveEffectiveSftpPassword(config));
    }

    [Fact]
    public void SmtpPassword_EnvVarOverridesConfig()
    {
        var config = new SmtpConfig("host") { Password = "config-pw" };
        Environment.SetEnvironmentVariable(ConfigResolver.SmtpPasswordEnvVar, "env-pw");
        try {
            Assert.Equal("env-pw", ConfigResolver.ResolveEffectiveSmtpPassword(config));
        } finally {
            Environment.SetEnvironmentVariable(ConfigResolver.SmtpPasswordEnvVar, null);
        }
    }

    [Fact]
    public void SmtpPassword_FallsBackToConfig_WhenNoEnvVar()
    {
        var config = new SmtpConfig("host") { Password = "config-pw" };
        Environment.SetEnvironmentVariable(ConfigResolver.SmtpPasswordEnvVar, null);
        Assert.Equal("config-pw", ConfigResolver.ResolveEffectiveSmtpPassword(config));
    }

    [Fact]
    public void SftpPassword_EmptyEnvVar_FallsBackToConfig()
    {
        var config = new SftpConfig("host") { Password = "config-pw" };
        Environment.SetEnvironmentVariable(ConfigResolver.SftpPasswordEnvVar, "");
        try {
            Assert.Equal("config-pw", ConfigResolver.ResolveEffectiveSftpPassword(config));
        } finally {
            Environment.SetEnvironmentVariable(ConfigResolver.SftpPasswordEnvVar, null);
        }
    }

    [Fact]
    public void ValidateSftpConfig_ValidConfig_ReturnsNoErrors()
    {
        var config = new SftpConfig("sftp.example.com", Username: "user", RemotePath: "/uploads");
        Assert.Empty(ConfigResolver.ValidateSftpConfig(config));
    }

    [Fact]
    public void ValidateSftpConfig_MissingHost_ReturnsError()
    {
        var config = new SftpConfig("", Username: "user");
        var errors = ConfigResolver.ValidateSftpConfig(config);
        Assert.Contains(errors, e => e.Contains("host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSftpConfig_MissingUsername_ReturnsError()
    {
        var config = new SftpConfig("host");
        var errors = ConfigResolver.ValidateSftpConfig(config);
        Assert.Contains(errors, e => e.Contains("username", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSftpConfig_InvalidPort_ReturnsError()
    {
        var config = new SftpConfig("host", Port: 0, Username: "user");
        var errors = ConfigResolver.ValidateSftpConfig(config);
        Assert.Contains(errors, e => e.Contains("port", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSmtpConfig_ValidConfig_ReturnsNoErrors()
    {
        var config = new SmtpConfig("smtp.example.com", FromAddress: "from@test.com") { To = ["to@test.com"] };
        Assert.Empty(ConfigResolver.ValidateSmtpConfig(config));
    }

    [Fact]
    public void ValidateSmtpConfig_MissingHost_ReturnsError()
    {
        var config = new SmtpConfig("", FromAddress: "from@test.com") { To = ["to@test.com"] };
        var errors = ConfigResolver.ValidateSmtpConfig(config);
        Assert.Contains(errors, e => e.Contains("host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSmtpConfig_MissingFromAddress_ReturnsError()
    {
        var config = new SmtpConfig("host") { To = ["to@test.com"] };
        var errors = ConfigResolver.ValidateSmtpConfig(config);
        Assert.Contains(errors, e => e.Contains("fromAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSmtpConfig_EmptyToList_ReturnsError()
    {
        var config = new SmtpConfig("host", FromAddress: "from@test.com");
        var errors = ConfigResolver.ValidateSmtpConfig(config);
        Assert.Contains(errors, e => e.Contains("recipient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSmtpConfig_InvalidPort_ReturnsError()
    {
        var config = new SmtpConfig("host", Port: 99999, FromAddress: "from@test.com") { To = ["to@test.com"] };
        var errors = ConfigResolver.ValidateSmtpConfig(config);
        Assert.Contains(errors, e => e.Contains("port", StringComparison.OrdinalIgnoreCase));
    }
}
