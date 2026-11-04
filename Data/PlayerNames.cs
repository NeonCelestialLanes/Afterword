using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace RPChronicles.Data;

/// <summary>
/// Turns a log name into the character's name.
/// A mod star often arrives as "?" before or after the name, and a world can be stuck to the surname.
/// </summary>
internal static class PlayerNames
{
    private static string[] worlds = Array.Empty<string>();

    public static int WorldCount => worlds.Length;

    public static void RememberWorlds(IReadOnlyList<string> names)
    {
        var list = new List<string>();
        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (trimmed.Length >= 4)
                list.Add(trimmed);
        }

        list.Sort((left, right) => right.Length.CompareTo(left.Length));
        worlds = list.ToArray();
    }

    public static List<string> LoadWorlds(IDataManager data)
    {
        var names = new List<string>();
        try
        {
            var sheet = data.GetExcelSheet<World>();
            if (sheet == null)
                return names;

            foreach (var row in sheet)
            {
                var name = row.Name.ExtractText().Trim();
                if (name.Length >= 4)
                    names.Add(name);
            }
        }
        catch
        {
            return names;
        }

        return names;
    }

    public static string Clean(string name) => Separate(name, out _);

    /// <summary>
    /// True when both strings are the same person. Extra marks or a world stuck on the name still count.
    /// </summary>
    public static bool Same(string left, string right)
    {
        var a = Letters(Clean(left));
        var b = Letters(Clean(right));
        if (a.Length < 2 || b.Length < 2)
            return false;

        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        return b.StartsWith(a, StringComparison.Ordinal) && b.Length - a.Length <= 16
            || a.StartsWith(b, StringComparison.Ordinal) && a.Length - b.Length <= 16;
    }

    private static string Letters(string name)
    {
        var kept = new char[name.Length];
        var count = 0;
        foreach (var letter in name)
        {
            if (char.IsLetter(letter))
                kept[count++] = char.ToLowerInvariant(letter);
        }

        return new string(kept, 0, count);
    }

    /// <summary>
    /// A tell in a log is written as &gt;&gt; before the name. That mark is not part of the person.
    /// </summary>
    public static string DropTell(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(">>", StringComparison.Ordinal))
            return text ?? string.Empty;

        var value = text;
        while (value.Contains(">>", StringComparison.Ordinal))
            value = value.Replace(">>", string.Empty, StringComparison.Ordinal);

        while (value.Contains("  ", StringComparison.Ordinal))
            value = value.Replace("  ", " ", StringComparison.Ordinal);

        return value.Trim();
    }

    /// <summary>
    /// Pulls a character name out of a log. A world glued to the surname, and words after it, are not part of the name.
    /// </summary>
    public static string Separate(string raw, out string leftover)
    {
        leftover = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        raw = raw.Replace('\u00A0', ' ').Replace('\u2007', ' ').Replace('\u202F', ' ');

        var parts = StripMarks(DropTell(raw.Trim())).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        for (var i = 0; i < parts.Length; i++)
        {
            if (TryUnglue(parts[i], out var surname))
            {
                parts[i] = surname;
                leftover = string.Join(' ', parts[(i + 1)..]);
                return string.Join(' ', parts[..(i + 1)]);
            }

            if (i >= 2 && IsWorld(parts[i]))
            {
                leftover = string.Join(' ', parts[(i + 1)..]);
                return string.Join(' ', parts[..i]);
            }
        }

        var given = 0;
        while (given < parts.Length && given < 4 && parts[given].Length > 0 && char.IsUpper(parts[given][0]))
            given++;

        if (given >= 2 && given < parts.Length)
        {
            leftover = string.Join(' ', parts[given..]);
            return string.Join(' ', parts[..given]);
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Drops a mod mark that landed at the start of the spoken line. Later question marks stay.
    /// </summary>
    public static string StripLeadingMark(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var trimmed = text.TrimStart();
        var index = 0;
        while (index < trimmed.Length && IsMark(trimmed[index]))
            index++;

        if (index == 0)
            return text;

        return trimmed[index..].TrimStart();
    }

    private static string StripMarks(string text)
    {
        var kept = new char[text.Length];
        var count = 0;
        foreach (var letter in text)
        {
            if (IsMark(letter))
                continue;

            kept[count++] = letter;
        }

        var joined = new string(kept, 0, count);
        while (joined.Contains("  ", StringComparison.Ordinal))
            joined = joined.Replace("  ", " ", StringComparison.Ordinal);

        return joined.Trim();
    }

    private static bool IsMark(char letter) =>
        letter is '?' or '★' or '☆' or '✦' or '✧' or '※' or '⋆' or '*' or '\uFFFD'
        || char.GetUnicodeCategory(letter) is System.Globalization.UnicodeCategory.PrivateUse
            or System.Globalization.UnicodeCategory.OtherSymbol
            or System.Globalization.UnicodeCategory.Format;

    private static bool TryUnglue(string word, out string surname)
    {
        surname = word;
        if (word.Length < 6)
            return false;

        for (var i = 1; i < word.Length - 3; i++)
        {
            if (!char.IsUpper(word[i]) || !char.IsLower(word[i - 1]))
                continue;

            var tail = word[i..];
            var head = word[..i];
            if (head.Length < 2)
                continue;

            if (worlds.Length > 0 && !IsWorld(tail))
                continue;

            surname = head;
            return true;
        }

        foreach (var world in worlds)
        {
            if (word.Length <= world.Length + 1 || !word.EndsWith(world, StringComparison.OrdinalIgnoreCase))
                continue;

            var head = word[..^world.Length];
            if (head.Length < 2 || !char.IsLetter(head[0]))
                continue;

            surname = head;
            return true;
        }

        return false;
    }

    private static bool IsWorld(string word)
    {
        foreach (var world in worlds)
        {
            if (word.Equals(world, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
