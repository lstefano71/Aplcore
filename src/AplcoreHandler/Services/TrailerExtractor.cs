using System.Text;

namespace AplcoreHandler.Services;

public static class TrailerExtractor
{
  private const int BufferSize = 1024 * 1024; // 1MB
  private const int OverlapSize = 128; // > marker length

  private static readonly byte[] Marker =
      Encoding.UTF8.GetBytes("========================== Interesting Information");

  /// <summary>
  /// Extracts the text trailer from the end of an aplcore binary file.
  /// Returns null if the marker is not found.
  /// </summary>
  public static string? Extract(string filePath)
  {
    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var fileLength = fs.Length;

    if (fileLength == 0)
      return null;

    // Try up to 2 buffer reads (2MB total coverage with overlap)
    for (int pass = 0; pass < 2; pass++) {
      var seekOffset = (long)(pass + 1) * BufferSize;
      var startPos = Math.Max(0, fileLength - seekOffset);
      var readSize = (int)Math.Min(fileLength - startPos, BufferSize + (pass > 0 ? OverlapSize : 0));

      var buffer = new byte[readSize];
      fs.Seek(startPos, SeekOrigin.Begin);
      var bytesRead = fs.Read(buffer, 0, readSize);

      var markerIndex = FindMarker(buffer.AsSpan(0, bytesRead));
      if (markerIndex >= 0) {
        // Read from marker position to end of file
        var absoluteMarkerPos = startPos + markerIndex;
        var trailerSize = (int)(fileLength - absoluteMarkerPos);
        var trailerBytes = new byte[trailerSize];

        fs.Seek(absoluteMarkerPos, SeekOrigin.Begin);
        fs.ReadExactly(trailerBytes);

        return Encoding.UTF8.GetString(trailerBytes);
      }

      // If we've already read from the start, no point trying again
      if (startPos == 0)
        break;
    }

    return null;
  }

  private static int FindMarker(ReadOnlySpan<byte> buffer)
  {
    // Search backwards for better performance (marker is near the end)
    for (int i = buffer.Length - Marker.Length; i >= 0; i--) {
      if (buffer.Slice(i, Marker.Length).SequenceEqual(Marker))
        return i;
    }
    return -1;
  }
}
