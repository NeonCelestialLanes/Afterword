using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RPChronicles.Ui;

internal readonly record struct SpellHit(int Start, string Word, bool Delete, IReadOnlyList<string> Suggestions)
{
    public int Length => this.Word.Length;
}

/// <summary>
/// Uses the spelling checker already installed on Windows. Names can be kept for this session.
/// </summary>
internal static class WindowsSpellcheck
{
    private static readonly HashSet<string> Kept = new(StringComparer.OrdinalIgnoreCase);
    private static ISpellChecker? checker;
    private static bool unavailable;

    internal static void Keep(string word)
    {
        if (!string.IsNullOrWhiteSpace(word))
            Kept.Add(word);
    }

    internal static List<SpellHit> Check(string text)
    {
        var hits = new List<SpellHit>();
        if (unavailable || string.IsNullOrWhiteSpace(text))
            return hits;

        try
        {
            var spell = Checker();
            if (spell == null)
                return hits;

            var sample = text.Length > 8000 ? text[..8000] : text;
            var errors = spell.Check(sample);
            try
            {
                while (hits.Count < 12)
                {
                    if (errors.Next(out var error) != 0 || error == null)
                        break;

                    try
                    {
                        var start = (int)error.StartIndex;
                        var length = (int)error.Length;
                        if (start < 0 || length <= 0 || start + length > sample.Length)
                            continue;

                        var word = sample.Substring(start, length);
                        if (word.Length < 2 || Kept.Contains(word))
                            continue;

                        var action = error.CorrectiveAction;
                        if (action == 3)
                        {
                            hits.Add(new SpellHit(start, word, true, []));
                            continue;
                        }

                        var suggestions = new List<string>();
                        if (action == 2)
                        {
                            var replacement = error.Replacement;
                            if (!string.IsNullOrWhiteSpace(replacement))
                                suggestions.Add(replacement);
                        }
                        else if (action == 1)
                        {
                            suggestions.AddRange(Suggest(spell, word));
                        }

                        if (suggestions.Count == 0)
                            continue;

                        hits.Add(new SpellHit(start, word, false, suggestions));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(error);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(errors);
            }
        }
        catch
        {
            unavailable = true;
            checker = null;
        }

        return hits;
    }

    private static ISpellChecker? Checker()
    {
        if (checker != null)
            return checker;

        var clsid = new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
        var iid = new Guid("8E018A9D-2415-4677-BF08-794EA61F94BB");
        if (CoCreateInstance(in clsid, 0, 1, in iid, out var factory) != 0 || factory == null)
        {
            unavailable = true;
            return null;
        }

        var language = CultureInfo.CurrentUICulture.Name;
        if (factory.IsSupported(language) == 0)
            language = "en-US";
        if (factory.IsSupported(language) == 0)
        {
            unavailable = true;
            return null;
        }

        checker = factory.CreateSpellChecker(language);
        return checker;
    }

    private static List<string> Suggest(ISpellChecker spell, string word)
    {
        var suggestions = new List<string>();
        var list = spell.Suggest(word);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                if (list.Next(1, out var pointer, out var fetched) != 0 || fetched != 1 || pointer == 0)
                    break;

                var suggestion = Marshal.PtrToStringUni(pointer);
                Marshal.FreeCoTaskMem(pointer);
                if (!string.IsNullOrWhiteSpace(suggestion) && !string.Equals(suggestion, word, StringComparison.OrdinalIgnoreCase))
                    suggestions.Add(suggestion);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }

        return suggestions;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid clsid,
        nint outer,
        uint context,
        in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out ISpellCheckerFactory? factory);

    [ComImport]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { [return: MarshalAs(UnmanagedType.Interface)] get; }

        int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport]
    [Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport]
    [Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig]
        int Next([MarshalAs(UnmanagedType.Interface)] out ISpellingError? value);
    }

    [ComImport]
    [Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }

        uint Length { get; }

        int CorrectiveAction { get; }

        string Replacement { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
    }

    [ComImport]
    [Guid("00000101-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumString
    {
        [PreserveSig]
        int Next(uint count, out nint text, out uint fetched);

        void Skip(uint count);

        void Reset();

        void Clone([MarshalAs(UnmanagedType.Interface)] out IEnumString copy);
    }
}
