using AplcoreHandler.Services;

namespace AplcoreHandler.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void BuildSummaryBody_IncludesTotals()
    {
        var result = new TransferResult();
        result.Uploaded.Add(new TransferArchive("file1.zip", 1024));
        result.Uploaded.Add(new TransferArchive("file2.zip", 2 * 1024 * 1024));
        result.Failed.Add(new FailedTransferArchive("file3.zip", 1536, "connection lost"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Uploaded:  2", body);
        Assert.Contains("Failed:    1", body);
        Assert.Contains("file1.zip (1.0 KB)", body);
        Assert.Contains("file2.zip (2.0 MB)", body);
        Assert.Contains("file3.zip (1.5 KB)", body);
        Assert.Contains("connection lost", body);
    }

    [Fact]
    public void BuildSummaryBody_TruncatesShippedAt20()
    {
        var result = new TransferResult();
        for (int i = 1; i <= 25; i++)
            result.Uploaded.Add(new TransferArchive($"file{i}.zip", i));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("file20.zip", body);
        Assert.DoesNotContain("file21.zip", body);
        Assert.Contains("and 5 more", body);
    }

    [Fact]
    public void BuildSummaryBody_TruncatesFailedAt10()
    {
        var result = new TransferResult();
        result.Uploaded.Add(new TransferArchive("trigger.zip", 1024)); // need at least one upload
        for (int i = 1; i <= 15; i++)
            result.Failed.Add(new FailedTransferArchive($"fail{i}.zip", i, "error"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("fail10.zip", body);
        Assert.DoesNotContain("fail11.zip", body);
        Assert.Contains("and 5 more", body);
    }

    [Fact]
    public void BuildSummaryBody_NoShipped_OmitsShippedSection()
    {
        var result = new TransferResult();
        result.Failed.Add(new FailedTransferArchive("file1.zip", 1024, "error"));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.DoesNotContain("Shipped files:", body);
        Assert.Contains("Failed files:", body);
    }

    [Fact]
    public void BuildSummaryBody_NoFailed_OmitsFailedSection()
    {
        var result = new TransferResult();
        result.Uploaded.Add(new TransferArchive("file1.zip", 1024));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Shipped files:", body);
        Assert.DoesNotContain("Failed files:", body);
    }

    [Fact]
    public void BuildSummaryBody_IncludesTimestampButOmitsHost()
    {
        var result = new TransferResult();
        result.Uploaded.Add(new TransferArchive("file1.zip", 1024));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("Timestamp:", body);
        Assert.DoesNotContain("Host:", body);
    }

    [Fact]
    public void BuildSummaryBody_ExactlyAt20_NoTruncationMessage()
    {
        var result = new TransferResult();
        for (int i = 1; i <= 20; i++)
            result.Uploaded.Add(new TransferArchive($"file{i}.zip", i));

        var body = NotificationService.BuildSummaryBody(result);

        Assert.Contains("file20.zip", body);
        Assert.DoesNotContain("more", body);
    }

    [Theory]
    [InlineData(1, "AplcoreHandler: 1 file shipped")]
    [InlineData(3, "AplcoreHandler: 3 files shipped")]
    public void BuildSubject_UsesUploadedCount(int uploadedCount, string expected)
    {
        Assert.Equal(expected, NotificationService.BuildSubject(uploadedCount));
    }
}
