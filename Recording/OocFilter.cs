using System.Text.RegularExpressions;

namespace RPChronicles.Recording;

/// <summary>
/// Drops out-of-character asides. A finished pair is removed and the rest of the line is kept.
/// A line that only starts or only ends with those marks is dropped, because the aside was never closed.
/// </summary>
internal static partial class OocFilter
{
    public static bool TryKeep(string text, out string kept)
    {
        var source = text.Trim();
        var stripped = DoubleParen().Replace(source, " ");
        stripped = Brackets().Replace(stripped, " ");
        stripped = Parens().Replace(stripped, " ");
        stripped = Whitespace().Replace(stripped, " ").Trim();

        if (string.IsNullOrWhiteSpace(stripped) || HasLooseMarker(stripped))
        {
            kept = string.Empty;
            return false;
        }

        kept = stripped;
        return true;
    }

    private static bool HasLooseMarker(string text) =>
        text.StartsWith("((", System.StringComparison.Ordinal)
        || text.StartsWith('[')
        || text.StartsWith('(')
        || text.EndsWith("))", System.StringComparison.Ordinal)
        || text.EndsWith(']')
        || text.EndsWith(')');

    [GeneratedRegex(@"\(\(.*?\)\)", RegexOptions.Singleline)]
    private static partial Regex DoubleParen();

    [GeneratedRegex(@"\[.*?\]", RegexOptions.Singleline)]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\(.*?\)", RegexOptions.Singleline)]
    private static partial Regex Parens();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
