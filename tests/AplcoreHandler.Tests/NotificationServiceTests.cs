using AplcoreHandler.Services;

namespace AplcoreHandler.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void BuildSummaryBody_IncludesTotals()
    {
        var result = new TransferResult();
        result.Uploaded.Add("file1.zip");
        result.Uploaded.Add("file2.zip");
        result.Failed.Add(("file3.zip", "connection lost"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Uploaded:  2", body);
        Assert.Contains("Failed:    1", body);
        Assert.Contains("file1.zip", body);
        Assert.Contains("file3.zip", body);
        Assert.Contains("connection lost", body);
    }

    [Fact]
    public void BuildSummaryBody_TruncatesShippedAt20()
    {
        var result = new TransferResult();
        for (int i = 1; i <= 25; i++)
            result.Uploaded.Add($"file{i}.zip");

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("file20.zip", body);
        Assert.DoesNotContain("file21.zip", body);
        Assert.Contains("and 5 more", body);
    }

    [Fact]
    public void BuildSummaryBody_TruncatesFailedAt10()
    {
        var result = new TransferResult();
        result.Uploaded.Add("trigger.zip"); // need at least one upload
        for (int i = 1; i <= 15; i++)
            result.Failed.Add(($"fail{i}.zip", "error"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("fail10.zip", body);
        Assert.DoesNotContain("fail11.zip", body);
        Assert.Contains("and 5 more", body);
    }

    [Fact]
    public void BuildSummaryBody_NoShipped_OmitsShippedSection()
    {
        var result = new TransferResult();
        result.Failed.Add(("file1.zip", "error"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.DoesNotContain("Shipped files:", body);
        Assert.Contains("Failed files:", body);
    }

    [Fact]
    public void BuildSummaryBody_NoFailed_OmitsFailedSection()
    {
        var result = new TransferResult();
        result.Uploaded.Add("file1.zip");

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Shipped files:", body);
        Assert.DoesNotContain("Failed files:", body);
    }

    [Fact]
    public void BuildSummaryBody_IncludesTimestampAndHost()
    {
        var result = new TransferResult();
        result.Uploaded.Add("file1.zip");

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Timestamp:", body);
        Assert.Contains("Host:", body);
    }

    [Fact]
    public void BuildSummaryBody_ExactlyAt20_NoTruncationMessage()
    {
        var result = new TransferResult();
        for (int i = 1; i <= 20; i++)
            result.Uploaded.Add($"file{i}.zip");

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("file20.zip", body);
        Assert.DoesNotContain("more", body);
    }
}
