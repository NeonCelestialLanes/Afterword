using System;
using System.IO;

namespace RPChronicles.Data;

/// <summary>
/// Turns a text file on this computer into a saved scene. Nothing is uploaded.
/// A line can start with [say] or [emote], then the character's name.
/// </summary>
internal static class TranscriptImport
{
    public static SceneRecord Read(string path)
    {
        var written = File.GetLastWriteTime(path);
        var when = new DateTimeOffset(written);
        var scene = new SceneRecord
        {
            Id = Guid.NewGuid(),
            Title = Path.GetFileNameWithoutExtension(path).Trim(),
            Location = "Imported",
            StartedAt = when,
            EndedAt = when,
            Status = SceneStatus.Saved,
            Mode = RecordingMode.SelectedCharacters,
        };

        if (string.IsNullOrWhiteSpace(scene.Title))
            scene.Title = "Imported scene";

        var text = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            scene.Lines.Add(ParseLine(line, when));
        }

        if (scene.Lines.Count == 0)
        {
            scene.Lines.Add(new SceneLine
            {
                Id = Guid.NewGuid(),
                At = when,
                Text = "(This file had no text.)",
            });
        }

        return scene;
    }

    private static SceneLine ParseLine(string line, DateTimeOffset fallback)
    {
        var body = line;
        var at = fallback;
        var kind = LineKind.Say;
        var channel = "Say";
        for (var i = 0; i < 3 && body.StartsWith('['); i++)
        {
            var end = body.IndexOf(']');
            if (end <= 1)
                break;

            var tag = body[1..end].Trim();
            var rest = body[(end + 1)..].TrimStart();
            if (IsChannel(tag, out var taggedKind, out var taggedChannel))
            {
                kind = taggedKind;
                channel = taggedChannel;
                body = rest;
                continue;
            }

            if (DateTime.TryParse(tag, out var parsed))
            {
                at = new DateTimeOffset(parsed);
                body = rest;
                continue;
            }

            break;
        }

        SplitSpeaker(body, kind, out var speaker, out var text);
        if (text.StartsWith('*') || text.StartsWith('/'))
        {
            kind = LineKind.Emote;
            channel = "Emote";
        }

        return new SceneLine
        {
            Id = Guid.NewGuid(),
            At = at,
            Speaker = speaker,
            Channel = channel,
            Kind = kind,
            Text = text.Length == 0 ? body : text,
        };
    }

    private static bool IsChannel(string tag, out LineKind kind, out string channel)
    {
        switch (tag.Trim().ToLowerInvariant())
        {
            case "say":
                kind = LineKind.Say;
                channel = "Say";
                return true;
            case "emote":
            case "em":
                kind = LineKind.Emote;
                channel = "Emote";
                return true;
            case "shout":
                kind = LineKind.Say;
                channel = "Shout";
                return true;
            case "yell":
                kind = LineKind.Say;
                channel = "Yell";
                return true;
            case "tell":
            case "t":
                kind = LineKind.Say;
                channel = "Tell";
                return true;
            case "party":
            case "p":
                kind = LineKind.Say;
                channel = "Party";
                return true;
            default:
                kind = LineKind.Say;
                channel = "Say";
                return false;
        }
    }

    private static void SplitSpeaker(string body, LineKind kind, out string speaker, out string text)
    {
        var colon = body.IndexOf(':');
        if (colon is > 0 and <= 80)
        {
            var name = body[..colon].Trim();
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (name.Length > 0 && words.Length <= 6 && !name.Contains('.'))
            {
                speaker = name;
                text = body[(colon + 1)..].Trim();
                Finish(ref speaker, ref text);
                return;
            }
        }

        if (kind == LineKind.Emote)
        {
            var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = 0;
            while (count < words.Length && count < 4 && words[count].Length > 0 && char.IsUpper(words[count][0]))
                count++;

            if (count > 0)
            {
                speaker = string.Join(' ', words[..count]);
                text = string.Join(' ', words[count..]);
                Finish(ref speaker, ref text);
                return;
            }
        }

        speaker = string.Empty;
        text = body;
        Finish(ref speaker, ref text);
    }

    private static void Finish(ref string speaker, ref string text)
    {
        speaker = PlayerNames.Separate(speaker, out var extra);
        if (extra.Length == 0)
            return;

        text = string.IsNullOrWhiteSpace(text) ? extra : extra + " " + text.Trim();
    }
}
