using System.IO;
using Dalamud.Game.Text;
using Newtonsoft.Json;

namespace RPChronicles.Data;

/// <summary>
/// Which player chats to keep. System lines are not in this list, so they are never recorded.
/// Saved on this computer, next to the notebooks.
/// </summary>
internal sealed class RecordingSettings
{
    public bool Say { get; set; } = true;

    public bool Emote { get; set; } = true;

    public bool Shout { get; set; }

    public bool Yell { get; set; }

    public bool Tell { get; set; }

    public bool Party { get; set; }

    public bool Alliance { get; set; }

    public bool FreeCompany { get; set; }

    public bool Linkshell { get; set; }

    public bool CrossWorldLinkshell { get; set; }

    public bool NoviceNetwork { get; set; }

    /// <summary>When on, a /random is kept. Loot rolls stay out. Off until the player turns it on.</summary>
    public bool Random { get; set; }

    /// <summary>
    /// When on, text wrapped in [], (), or (()) is left out, and a line that
    /// starts or ends with those marks is left out too.
    /// </summary>
    public bool SkipOutOfCharacter { get; set; } = true;

    /// <summary>Folder for scene logs. Empty means the usual logs folder next to the plugin.</summary>
    public string LogFolder { get; set; } = string.Empty;

    /// <summary>When on, the journal page is dark. The navy cover stays.</summary>
    public bool DarkMode { get; set; }

    /// <summary>0 is the usual size. 1 to 3 make the writing bigger.</summary>
    public int FontSize { get; set; }

    /// <summary>When on, double-clicking a word in the scratchpad opens other words.</summary>
    public bool Thesaurus { get; set; }

    /// <summary>0 is off. 1 is the current target. 2 is the people checked on the scene. 3 is everyone.</summary>
    public int LineAlert { get; set; }

    /// <summary>0 is no sound. 1 through 16 are the same chat sounds Snooper uses.</summary>
    public int LineSound { get; set; }

    /// <summary>When on, the side panel is hidden so the window can be smaller.</summary>
    public bool SidebarHidden { get; set; }

    /// <summary>When on, Afterword is the small scratchpad or scene window.</summary>
    public bool Mini { get; set; }

    /// <summary>When mini is on, true shows the scratchpad and false shows the recording scene.</summary>
    public bool MiniScratch { get; set; } = true;

    [JsonIgnore]
    public string FilePath { get; set; } = string.Empty;

    public bool Allows(ChatChannel channel) => channel switch
    {
        ChatChannel.Say => this.Say,
        ChatChannel.Emote => this.Emote,
        ChatChannel.Shout => this.Shout,
        ChatChannel.Yell => this.Yell,
        ChatChannel.Tell => this.Tell,
        ChatChannel.Party => this.Party,
        ChatChannel.Alliance => this.Alliance,
        ChatChannel.FreeCompany => this.FreeCompany,
        ChatChannel.Linkshell => this.Linkshell,
        ChatChannel.CrossWorldLinkshell => this.CrossWorldLinkshell,
        ChatChannel.NoviceNetwork => this.NoviceNetwork,
        ChatChannel.Random => this.Random,
        _ => false,
    };

    public void Set(ChatChannel channel, bool enabled)
    {
        switch (channel)
        {
            case ChatChannel.Say: this.Say = enabled; break;
            case ChatChannel.Emote: this.Emote = enabled; break;
            case ChatChannel.Shout: this.Shout = enabled; break;
            case ChatChannel.Yell: this.Yell = enabled; break;
            case ChatChannel.Tell: this.Tell = enabled; break;
            case ChatChannel.Party: this.Party = enabled; break;
            case ChatChannel.Alliance: this.Alliance = enabled; break;
            case ChatChannel.FreeCompany: this.FreeCompany = enabled; break;
            case ChatChannel.Linkshell: this.Linkshell = enabled; break;
            case ChatChannel.CrossWorldLinkshell: this.CrossWorldLinkshell = enabled; break;
            case ChatChannel.NoviceNetwork: this.NoviceNetwork = enabled; break;
            case ChatChannel.Random: this.Random = enabled; break;
        }

        this.Save();
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(this.FilePath))
            return;

        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        var temp = this.FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, this.FilePath, overwrite: true);
    }

    public static RecordingSettings Load(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        var path = Path.Combine(configDirectory, "recording-settings.json");
        RecordingSettings settings;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonConvert.DeserializeObject<RecordingSettings>(File.ReadAllText(path)) ?? new RecordingSettings();
            }
            catch
            {
                settings = new RecordingSettings();
            }
        }
        else
        {
            settings = new RecordingSettings();
        }

        settings.FilePath = path;
        return settings;
    }
}

internal enum ChatChannel
{
    Say,
    Emote,
    Shout,
    Yell,
    Tell,
    Party,
    Alliance,
    FreeCompany,
    Linkshell,
    CrossWorldLinkshell,
    NoviceNetwork,
    Random,
}
