namespace AplcoreHandler.Formatting;

public static class ByteSizeFormatter
{
  public static string Format(long bytes) => bytes switch {
    < 1024 => FormattableString.Invariant($"{bytes} B"),
    < 1024 * 1024 => FormattableString.Invariant($"{bytes / 1024.0:F1} KB"),
    < 1024L * 1024 * 1024 => FormattableString.Invariant($"{bytes / (1024.0 * 1024):F1} MB"),
    _ => FormattableString.Invariant($"{bytes / (1024.0 * 1024 * 1024):F2} GB")
  };
}
