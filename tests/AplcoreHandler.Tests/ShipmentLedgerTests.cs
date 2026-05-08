using AplcoreHandler.Models;
using AplcoreHandler.Services;

namespace AplcoreHandler.Tests;

public class ShipmentLedgerTests
{
    [Fact]
    public void IsShipped_ReturnsFalse_WhenNotRecorded()
    {
        var db = new AplcoreDb();
        Assert.False(DatabaseService.IsShipped(db, @"C:\target\test.zip", 1000, DateTime.UtcNow));
    }

    [Fact]
    public void IsShipped_ReturnsTrue_AfterRecordShipped()
    {
        var db = new AplcoreDb();
        var path = @"C:\target\test.zip";
        var size = 1234L;
        var modified = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        DatabaseService.RecordShipped(db, path, size, modified);
        Assert.True(DatabaseService.IsShipped(db, path, size, modified));
    }

    [Fact]
    public void IsShipped_ReturnsFalse_WhenSizeDiffers()
    {
        var db = new AplcoreDb();
        var path = @"C:\target\test.zip";
        var modified = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        DatabaseService.RecordShipped(db, path, 1234, modified);
        Assert.False(DatabaseService.IsShipped(db, path, 5678, modified));
    }

    [Fact]
    public void IsShipped_ReturnsFalse_WhenTimestampDiffers()
    {
        var db = new AplcoreDb();
        var path = @"C:\target\test.zip";
        var modified1 = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var modified2 = new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc);

        DatabaseService.RecordShipped(db, path, 1234, modified1);
        Assert.False(DatabaseService.IsShipped(db, path, 1234, modified2));
    }

    [Fact]
    public void RecordShipped_SetsShippedAtUtc()
    {
        var db = new AplcoreDb();
        var path = @"C:\target\test.zip";
        var before = DateTime.UtcNow;

        DatabaseService.RecordShipped(db, path, 1234, new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));

        var entry = db.Shipments.Values.Single();
        Assert.True(entry.ShippedAtUtc >= before);
        Assert.True(entry.ShippedAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void BuildShipmentKey_IncludesPathSizeAndTimestamp()
    {
        var key = DatabaseService.BuildShipmentKey(@"C:\target\test.zip", 1234, new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        Assert.Contains("1234", key);
        Assert.Contains("2026", key);
        Assert.Contains("test.zip", key);
    }

    [Fact]
    public void RecordShipped_OverwritesPreviousEntry()
    {
        var db = new AplcoreDb();
        var path = @"C:\target\test.zip";
        var modified = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        DatabaseService.RecordShipped(db, path, 1234, modified);
        var count1 = db.Shipments.Count;

        DatabaseService.RecordShipped(db, path, 1234, modified);
        Assert.Equal(count1, db.Shipments.Count); // no duplicate
    }
}
