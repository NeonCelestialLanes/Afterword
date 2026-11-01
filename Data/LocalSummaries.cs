using System;
using System.Collections.Generic;
using System.Text;

namespace RPChronicles.Data;

/// <summary>
/// Writes a short, readable recap from lines already saved on this computer.
/// It never sends the text anywhere, and it never touches the notes.
/// </summary>
internal static class LocalSummaries
{
    public const int Revision = 2;

    public static string FromScene(SceneRecord scene)
    {
        var lines = Spoken(scene.Lines);
        if (lines.Count == 0)
            return "This scene has no lines yet.";

        var names = NamesOf(lines);
        var where = Place(scene.Location);
        var count = lines.Count == 1 ? "1 line" : $"{lines.Count} lines";
        var head = string.IsNullOrEmpty(where)
            ? $"{JoinNames(names)}. {count}."
            : $"{JoinNames(names)}, in {where}. {count}.";

        var text = new StringBuilder(head);
        foreach (var line in PickBeats(lines))
        {
            text.AppendLine();
            text.Append(Describe(line));
        }

        return text.ToString();
    }

    public static string FromStoryline(string title, IReadOnlyList<SceneRecord> scenes)
    {
        if (scenes.Count == 0)
            return $"{title} has no scenes yet.";

        var count = scenes.Count == 1 ? "one scene" : $"{scenes.Count} scenes";
        var text = new StringBuilder($"{title}. {count}.");
        var shown = Math.Min(scenes.Count, 4);
        for (var i = 0; i < shown; i++)
        {
            var scene = scenes[i];
            var lines = Spoken(scene.Lines);
            var names = lines.Count == 0 ? "No one spoke" : JoinNames(NamesOf(lines));
            text.AppendLine();
            text.Append(scene.Title);
            text.Append(" — ");
            text.Append(names);
            if (lines.Count > 0)
            {
                text.Append(". ");
                text.Append(Describe(lines[0]));
            }
        }

        if (scenes.Count > shown)
        {
            text.AppendLine();
            text.Append($"And {scenes.Count - shown} more scenes.");
        }

        return text.ToString();
    }

    public static string FromCharacter(string name, IReadOnlyList<SceneRecord> scenes)
    {
        var who = PlayerNames.Clean(name);
        if (who.Length == 0)
            who = name.Trim();

        var lines = new List<SceneLine>();
        var sceneCount = 0;
        foreach (var scene in scenes)
        {
            var found = false;
            foreach (var line in scene.Lines)
            {
                if (!SamePerson(PlayerNames.Clean(line.Speaker), who) || string.IsNullOrWhiteSpace(line.Text))
                    continue;

                lines.Add(line);
                found = true;
            }

            if (found)
                sceneCount++;
        }

        if (lines.Count == 0)
            return $"{who} has no recorded lines yet.";

        var where = sceneCount == 1 ? "one scene" : $"{sceneCount} scenes";
        var text = new StringBuilder($"{who} appears in {where}.");
        text.AppendLine();
        text.Append(Describe(lines[0]));
        if (lines.Count > 1)
        {
            text.AppendLine();
            text.Append(Describe(lines[^1]));
        }

        return text.ToString();
    }

    private static List<SceneLine> Spoken(IReadOnlyList<SceneLine> lines)
    {
        var spoken = new List<SceneLine>();
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(PlayerNames.StripLeadingMark(line.Text)))
                spoken.Add(line);
        }

        return spoken;
    }

    private static List<SceneLine> PickBeats(List<SceneLine> lines)
    {
        if (lines.Count <= 5)
            return lines;

        var picked = new List<SceneLine> { lines[0] };
        var previous = PlayerNames.Clean(lines[0].Speaker);
        for (var i = 1; i < lines.Count - 1 && picked.Count < 4; i++)
        {
            var speaker = PlayerNames.Clean(lines[i].Speaker);
            if (SamePerson(speaker, previous))
                continue;

            picked.Add(lines[i]);
            previous = speaker;
        }

        picked.Add(lines[^1]);
        return picked;
    }

    private static string Describe(SceneLine line)
    {
        var speaker = PlayerNames.Clean(line.Speaker);
        if (speaker.Length == 0)
            speaker = "Someone";

        var text = Clip(PlayerNames.StripLeadingMark(line.Text));
        if (line.Kind == LineKind.Emote)
        {
            return text.StartsWith(speaker, StringComparison.OrdinalIgnoreCase)
                ? text
                : $"{speaker} {text}";
        }

        return $"{speaker}: {text}";
    }

    private static string Clip(string text)
    {
        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);

        if (flat.Length <= 160)
            return flat;

        var cut = flat.LastIndexOf(' ', 157);
        if (cut < 40)
            cut = 157;

        return flat[..cut].TrimEnd() + "…";
    }

    private static List<string> NamesOf(List<SceneLine> lines)
    {
        var names = new List<string>();
        foreach (var line in lines)
        {
            var speaker = PlayerNames.Clean(line.Speaker);
            if (speaker.Length == 0)
                continue;

            var known = false;
            foreach (var name in names)
            {
                if (SamePerson(name, speaker))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
                names.Add(speaker);
        }

        if (names.Count == 0)
            names.Add("Someone");

        return names;
    }

    private static string JoinNames(List<string> names)
    {
        if (names.Count == 1)
            return names[0];

        if (names.Count == 2)
            return $"{names[0]} and {names[1]}";

        var shown = Math.Min(names.Count, 4);
        if (names.Count > shown)
            return string.Join(", ", names.GetRange(0, shown)) + ", and others";

        return string.Join(", ", names.GetRange(0, shown - 1)) + ", and " + names[shown - 1];
    }

    private static string Place(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return string.Empty;

        if (location.Equals("Unknown location", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (location.Equals("Imported", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return location.Trim();
    }

    private static bool SamePerson(string left, string right)
    {
        left = left.Trim();
        right = right.Trim();
        if (left.Length == 0 || right.Length == 0)
            return false;

        return left.Equals(right, StringComparison.OrdinalIgnoreCase)
            || left.StartsWith(right + " ", StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left + " ", StringComparison.OrdinalIgnoreCase);
    }
}
