using System;
using System.Collections.Generic;

namespace RPChronicles.Data;

/// <summary>
/// One character's notebook. Saved as JSON on this computer, never uploaded.
/// Extra fields such as notes and tags are stored now so later features can fill them in
/// without rewriting old scenes.
/// </summary>
internal sealed class NotebookFile
{
    public int Version { get; set; } = 1;

    public ulong ContentId { get; set; }

    public string CharacterName { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public List<SceneRecord> Scenes { get; set; } = new();

    public List<CharacterProfile> Characters { get; set; } = new();

    public List<StorylineRecord> Storylines { get; set; } = new();

    public List<JournalNote> Notes { get; set; } = new();
}

internal enum SceneStatus
{
    Recording,
    Paused,
    Saved,
}

/// <summary>
/// Everyone records every supported line. SelectedCharacters is reserved for the next step,
/// when the journal can pick specific people. New scenes always start as Everyone.
/// </summary>
internal enum RecordingMode
{
    Everyone,
    SelectedCharacters,
}

internal enum LineKind
{
    Say,
    Emote,
}

internal sealed class SceneRecord
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Reserved for a later summary, written by the player or an opt-in assistant.</summary>
    public string Summary { get; set; } = string.Empty;

    public int SummaryRevision { get; set; }

    /// <summary>Scenes that share this label are grouped together in the notebook.</summary>
    public string Storyline { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public SceneStatus Status { get; set; }

    /// <summary>True when the game closed while this scene was still recording. The lines were kept.</summary>
    public bool Interrupted { get; set; }

    public RecordingMode Mode { get; set; } = RecordingMode.Everyone;

    public List<string> SelectedSpeakers { get; set; } = new();

    public List<SceneLine> Lines { get; set; } = new();

    public SceneRecord Copy()
    {
        var copy = new SceneRecord
        {
            Id = this.Id,
            Title = this.Title,
            Location = this.Location,
            Description = this.Description,
            Notes = this.Notes,
            Summary = this.Summary,
            SummaryRevision = this.SummaryRevision,
            Storyline = this.Storyline,
            StartedAt = this.StartedAt,
            EndedAt = this.EndedAt,
            Status = this.Status,
            Interrupted = this.Interrupted,
            Mode = this.Mode,
            Tags = new List<string>(this.Tags),
            SelectedSpeakers = new List<string>(this.SelectedSpeakers),
            Lines = new List<SceneLine>(this.Lines.Count),
        };

        foreach (var line in this.Lines)
            copy.Lines.Add(line.Copy());

        return copy;
    }
}

internal sealed class SceneLine
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    public string Speaker { get; set; } = string.Empty;

    /// <summary>The game channel name, such as Say or CustomEmote, kept for later export.</summary>
    public string Channel { get; set; } = string.Empty;

    public LineKind Kind { get; set; }

    public string Text { get; set; } = string.Empty;

    public SceneLine Copy() => new()
    {
        Id = this.Id,
        At = this.At,
        Speaker = this.Speaker,
        Channel = this.Channel,
        Kind = this.Kind,
        Text = this.Text,
    };
}

internal sealed class CharacterProfile
{
    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public int SummaryRevision { get; set; }

    public string Notes { get; set; } = string.Empty;
}

internal sealed class StorylineRecord
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>A short recap written on this computer. The player can rewrite it.</summary>
    public string Summary { get; set; } = string.Empty;

    public int SummaryRevision { get; set; }

    public List<JournalNote> NotePages { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class JournalNote
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "New note";

    public string Body { get; set; } = string.Empty;

    public DateTimeOffset WrittenAt { get; set; }
}
internal sealed class NotebookSnapshot
{
    public bool IsLoggedIn { get; init; }

    public string CharacterName { get; init; } = string.Empty;

    public string World { get; init; } = string.Empty;

    public IReadOnlyList<SceneRecord> Scenes { get; init; } = Array.Empty<SceneRecord>();

    public IReadOnlyList<CharacterProfile> Characters { get; init; } = Array.Empty<CharacterProfile>();

    public IReadOnlyList<StorylineRecord> Storylines { get; init; } = Array.Empty<StorylineRecord>();

    public IReadOnlyList<JournalNote> Notes { get; init; } = Array.Empty<JournalNote>();

    public string CharacterLabel =>
        string.IsNullOrWhiteSpace(this.CharacterName)
            ? string.Empty
            : string.IsNullOrWhiteSpace(this.World)
                ? this.CharacterName
                : $"{this.CharacterName} · {this.World}";

    public SceneRecord? ActiveScene
    {
        get
        {
            foreach (var scene in this.Scenes)
            {
                if (scene.Status is SceneStatus.Recording or SceneStatus.Paused)
                    return scene;
            }

            return null;
        }
    }
}
