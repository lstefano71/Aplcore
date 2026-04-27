using System.Text;
using AplcoreHandler.Models;

namespace AplcoreHandler.Services;

public static class MetadataSplitter
{
    public static TrailerData Split(string rawTrailer)
    {
        var metadata = new StringBuilder();
        var addressSpace = new StringBuilder();
        var aplStack = new StringBuilder();

        foreach (var line in rawTrailer.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("!AddressSpace:"))
            {
                addressSpace.Append(line);
                addressSpace.Append('\n');
            }
            else if (line.StartsWith("!APLStack:"))
            {
                aplStack.Append(line);
                aplStack.Append('\n');
            }
            else
            {
                metadata.Append(line);
                metadata.Append('\n');
            }
        }

        return new TrailerData
        {
            Metadata = metadata.ToString(),
            AddressSpace = addressSpace.ToString(),
            AplStack = aplStack.ToString()
        };
    }
}
