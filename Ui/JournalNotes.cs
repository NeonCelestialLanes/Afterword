namespace RPChronicles.Ui;

internal sealed class ReleaseNote
{
    public required string Version { get; init; }

    public required string Title { get; init; }

    public ReleaseSection[] Sections { get; init; } = [];
}

internal sealed class ReleaseSection
{
    public required string Heading { get; init; }

    public string[] Lines { get; init; } = [];
}

internal sealed class GuideSection
{
    public required string Heading { get; init; }

    public string[] Lines { get; init; } = [];
}

internal static class JournalNotes
{
    public static ReleaseNote[] Releases { get; } =
    [
        new ReleaseNote
        {
            Version = "0.1.26",
            Title = "What's new opens after an update.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "What's new opens when an update is installed. Close it, and it stays closed until the next one.",
                        "The bottom of Settings has What's new, and How to use Afterword.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.25",
            Title = "Record all the time, then press Save.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Settings can keep lines from the moment you log in. Press Save when a scene is finished, and the next one starts.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.24",
            Title = "Other words for the scratchpad.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Thesaurus is in Settings. Turn it on, then double-click a word in the scratchpad. Click one of the other words to use it.",
                        "That window follows the font size you choose.",
                    ],
                },
                new ReleaseSection
                {
                    Heading = "Fixed",
                    Lines =
                    [
                        "A ChatGPT account without a paid plan now says a paid plan is needed.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.23",
            Title = "ChatGPT sign-in waits for the browser.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "Fixed",
                    Lines =
                    [
                        "Sign-in waits until you finish in the browser, instead of stopping early.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.22",
            Title = "A sound when someone else speaks.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Line sound plays one of Snooper's chat sounds. Choose targeted, selected in the scene, or everyone. Your own lines stay quiet.",
                        "It stays quiet after you press the X. A minimized window still plays.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.20",
            Title = "Larger writing.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "New",
                    Lines =
                    [
                        "Font size in Settings is Usual, Bigger, Large, or Largest. Usual is the default size.",
                    ],
                },
            ],
        },
        new ReleaseNote
        {
            Version = "0.1.19",
            Title = "Party chat is kept.",
            Sections =
            [
                new ReleaseSection
                {
                    Heading = "Fixed",
                    Lines =
                    [
                        "Party chat is kept, including your own lines.",
                    ],
                },
            ],
        },
    ];

    public static GuideSection[] Guide { get; } =
    [
        new GuideSection
        {
            Heading = "Open the journal",
            Lines =
            [
                "Type /afw. The notebook belongs to the character you are logged in as.",
            ],
        },
        new GuideSection
        {
            Heading = "Record a scene",
            Lines =
            [
                "On New Scene, give it a title and a place. Choose everyone nearby, or check the people you want. Press Start recording.",
                "Only the chats turned on in Settings are kept. Combat, loot, and echoes stay out. Nothing from before you press Start is kept.",
                "Pause holds the scene. Stop and save puts it in My Notebook, where you can read it, edit a line, or check lines off.",
            ],
        },
        new GuideSection
        {
            Heading = "Record all the time",
            Lines =
            [
                "Turn this on in Settings. Lines are kept from the moment you log in. Press Save when a scene is finished, and the next one starts.",
            ],
        },
        new GuideSection
        {
            Heading = "Scratchpad",
            Lines =
            [
                "Write a line and choose Say, Emote, or another command. It can be posted into the scene you are recording.",
                "If Thesaurus is on, double-click a word. A window offers other words. Click one to use it in place of the word you chose.",
            ],
        },
        new GuideSection
        {
            Heading = "Notes, storylines, and characters",
            Lines =
            [
                "Notes stay on this computer. They are never sent anywhere.",
                "A scene can sit in a storyline. Stories is for writing of your own. Characters can hold notes, and two names can be combined when they are the same person.",
            ],
        },
        new GuideSection
        {
            Heading = "Summaries",
            Lines =
            [
                "Choose ChatGPT or Grok in Settings. Afterword asks before a scene is sent. You can write the summary yourself instead.",
                "A ChatGPT summary needs a paid plan. Notes are not included.",
            ],
        },
        new GuideSection
        {
            Heading = "Sounds, size, and logs",
            Lines =
            [
                "Line sound plays when someone else speaks. It stays quiet after you press the X.",
                "Font size changes the journal, the thesaurus, this guide, and What's new.",
                "Each saved scene is also a text file. Choose that folder at the bottom of Settings.",
            ],
        },
    ];
}
