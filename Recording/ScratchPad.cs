using System;
using System.Collections.Generic;
using System.Text;

namespace RPChronicles.Recording;

/// <summary>
/// Splits a post so each piece fits in the FFXIV chat box, breaking on a sentence when it can.
/// </summary>
internal static class ScratchPad
{
    public const int ChatByteLimit = 500;
    private const int SafetyBytes = 8;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    internal readonly record struct Piece(string CopyText, int Number, int Total);

    internal static IReadOnlyList<Piece> Split(string raw, string header)
    {
        var paragraphs = Paragraphs(raw);
        if (paragraphs.Count == 0)
            return [];

        var prefix = string.IsNullOrWhiteSpace(header) ? string.Empty : header.Trim() + " ";
        var bodies = Pack(paragraphs, Budget(prefix, reserveMarker: true));
        if (bodies.Count == 1)
        {
            var single = Pack(paragraphs, Budget(prefix, reserveMarker: false));
            if (single.Count == 1)
                bodies = single;
        }

        var total = bodies.Count;
        var pieces = new List<Piece>(total);
        for (var n = 0; n < total; n++)
        {
            var body = bodies[n];
            var marked = total > 1 ? $"{body} ({n + 1}/{total})" : body;
            pieces.Add(new Piece(prefix + marked, n + 1, total));
        }

        return pieces;
    }

    private static int Budget(string prefix, bool reserveMarker)
    {
        var marker = reserveMarker ? " (99/99)" : string.Empty;
        var used = Utf8.GetByteCount(prefix) + Utf8.GetByteCount(marker) + SafetyBytes;
        return Math.Max(32, ChatByteLimit - used);
    }

    private static List<string> Paragraphs(string raw)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = CollapseSpaces(line.Trim());
            if (trimmed.Length == 0)
            {
                if (current.Length == 0)
                    continue;

                paragraphs.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(trimmed);
        }

        if (current.Length > 0)
            paragraphs.Add(current.ToString());

        return paragraphs;
    }

    private static string CollapseSpaces(string text)
    {
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        return text;
    }

    private static List<string> Pack(IReadOnlyList<string> paragraphs, int budget)
    {
        var bodies = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var index = 0;
            while (index < paragraph.Length)
            {
                while (index < paragraph.Length && char.IsWhiteSpace(paragraph[index]))
                    index++;
                if (index >= paragraph.Length)
                    break;

                var cut = NextCut(paragraph, index, budget);
                if (cut <= index)
                    cut = Math.Min(paragraph.Length, index + 1);

                var body = paragraph[index..cut].Trim();
                if (body.Length > 0)
                    bodies.Add(body);
                index = cut;
            }
        }

        return bodies;
    }

    private static int NextCut(string text, int start, int budget)
    {
        var max = start;
        var bytes = 0;
        while (max < text.Length)
        {
            var add = Utf8.GetByteCount(text.AsSpan(max, 1));
            if (bytes + add > budget)
                break;

            bytes += add;
            max++;
        }

        if (max >= text.Length)
            return text.Length;
        if (max == start)
            return Math.Min(text.Length, start + 1);

        var sentence = LastSentenceEnd(text, start, max);
        if (sentence > start)
            return sentence;

        var space = text.LastIndexOf(' ', max - 1, max - start);
        if (space > start)
            return space;

        return max;
    }

    private static int LastSentenceEnd(string text, int start, int max)
    {
        for (var i = max - 1; i > start; i--)
        {
            if (text[i] == ' ' && IsSentenceEnd(text, start, i))
                return i;
        }

        return IsSentenceEnd(text, start, max) ? max : -1;
    }

    private static bool IsSentenceEnd(string text, int start, int boundary)
    {
        var index = boundary - 1;
        while (index > start && "\"')*".Contains(text[index]))
            index--;

        return index >= start && ".?!".Contains(text[index]);
    }
}
