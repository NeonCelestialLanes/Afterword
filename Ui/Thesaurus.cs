using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace RPChronicles.Ui;

/// <summary>
/// Other words for the scratchpad. The list stays on this computer.
/// Synonyms are from WordNet 3.0, copyright 2006 Princeton University.
/// </summary>
internal static class Thesaurus
{
    private static Dictionary<string, string[]>? words;

    public static IReadOnlyList<string> Others(string word)
    {
        var key = word.Trim().ToLowerInvariant();
        if (key.Length < 2)
            return [];

        var map = Load();
        return map.TryGetValue(key, out var list) ? list : [];
    }

    private static Dictionary<string, string[]> Load()
    {
        if (words != null)
            return words;

        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(Thesaurus).Assembly;
        string? resource = null;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("thesaurus.txt", StringComparison.OrdinalIgnoreCase))
            {
                resource = name;
                break;
            }
        }

        if (resource == null)
        {
            words = map;
            return map;
        }

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream == null)
        {
            words = map;
            return map;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab >= line.Length - 1)
                continue;

            var syns = line[(tab + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (syns.Length == 0)
                continue;

            map[line[..tab]] = syns;
        }

        words = map;
        return map;
    }
}
