using AplcoreHandler.Models;

using System.Text;

namespace AplcoreHandler.Services;

public static class MetadataSplitter
{
  public static TrailerData Split(string rawTrailer)
  {
    var metadata = new StringBuilder();
    var addressSpace = new StringBuilder();
    var aplStack = new StringBuilder();

    int? major = null, minor = null, revision = null, bits = null;
    string? edition = null;

    foreach (var line in rawTrailer.AsSpan().EnumerateLines()) {
      if (line.StartsWith("!AddressSpace:")) {
        addressSpace.Append(line);
        addressSpace.Append('\n');
      } else if (line.StartsWith("!APLStack:")) {
        aplStack.Append(line);
        aplStack.Append('\n');
      } else {
        // Parse version fields
        if (line.StartsWith("!MAJOR_VERSION:"))
          TryParseInt(line["!MAJOR_VERSION:".Length..], out major);
        else if (line.StartsWith("!MINOR_VERSION:"))
          TryParseInt(line["!MINOR_VERSION:".Length..], out minor);
        else if (line.StartsWith("!SVN_REVISION:"))
          TryParseInt(line["!SVN_REVISION:".Length..], out revision);
        else if (line.StartsWith("!BITS:"))
          TryParseInt(line["!BITS:".Length..], out bits);
        else if (line.StartsWith("!EDITION:"))
          edition = line["!EDITION:".Length..].Trim().ToString();

        metadata.Append(line);
        metadata.Append('\n');
      }
    }

    InterpreterVersion? version = null;
    if (major is not null && minor is not null && revision is not null
        && bits is not null && edition is not null) {
      version = new InterpreterVersion {
        Major = major.Value,
        Minor = minor.Value,
        Revision = revision.Value,
        Bits = bits.Value,
        Edition = edition,
      };
    }

    return new TrailerData {
      Metadata = metadata.ToString(),
      AddressSpace = addressSpace.ToString(),
      AplStack = aplStack.ToString(),
      Version = version,
    };
  }

  private static void TryParseInt(ReadOnlySpan<char> span, out int? result)
  {
    result = int.TryParse(span.Trim(), out var v) ? v : null;
  }
}
