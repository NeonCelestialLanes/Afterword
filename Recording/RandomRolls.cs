using System.Text.RegularExpressions;
using RPChronicles.Data;

namespace RPChronicles.Recording;

/// <summary>
/// A /random from the game. Loot rolls are a different chat type and are not read here.
/// </summary>
internal static partial class RandomRolls
{
    public static bool TryRead(string speaker, string text, string selfName, out string name, out string spoken)
    {
        name = PlayerNames.Clean(speaker);
        spoken = text.Trim();
        if (spoken.Length == 0)
            return false;

        var match = EnglishRoll().Match(spoken);
        if (!match.Success)
        {
            if (name.Length == 0)
                name = PlayerNames.Clean(selfName);
            return true;
        }

        var who = match.Groups["who"].Value;
        if (who.Equals("You", System.StringComparison.OrdinalIgnoreCase))
        {
            if (name.Length == 0 || name.Equals("You", System.StringComparison.OrdinalIgnoreCase))
                name = PlayerNames.Clean(selfName);
        }
        else
        {
            name = PlayerNames.Clean(who);
        }

        spoken = $"rolls a {match.Groups["roll"].Value} (out of {match.Groups["max"].Value}).";
        return spoken.Length > 0;
    }

    [GeneratedRegex(@"(?<who>You|[A-Za-z][A-Za-z'’\-.]*(?: [A-Za-z][A-Za-z'’\-.]*){0,3}) rolls? a (?<roll>\d+) \(out of (?<max>\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishRoll();
}
