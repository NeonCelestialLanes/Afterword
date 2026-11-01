using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RPChronicles.Data;

/// <summary>
/// Reads and writes one JSON file per character. Chat can arrive on a different thread than the
/// window, so every change goes through a lock. Each saved line is written immediately, and the
/// write goes to a temporary file first so a crash mid-save keeps the previous notebook.
/// </summary>
internal sealed class NotebookStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private readonly string directory;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private NotebookFile? current;
    private bool isLoggedIn;

    public NotebookStore(string configDirectory, IPluginLog log)
    {
        this.directory = Path.Combine(configDirectory, "notebooks");
        Directory.CreateDirectory(this.directory);
        this.log = log;
    }

    public void LoadCharacter(ulong contentId, string name, string world)
    {
        if (contentId == 0)
            return;

        lock (this.gate)
        {
            if (this.current?.ContentId == contentId)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    this.current.CharacterName = name;
                if (!string.IsNullOrWhiteSpace(world))
                    this.current.World = world;
                this.isLoggedIn = true;
                return;
            }

            this.SaveUnlocked();
            this.current = this.ReadOrCreate(this.PathFor(contentId), contentId, name, world);
            this.isLoggedIn = true;
            this.log.Information("Opened the notebook for {Character}.", this.current.CharacterName);
        }
    }

    public bool MarkLoggedOut()
    {
        lock (this.gate)
        {
            this.isLoggedIn = false;
            var active = this.ActiveUnlocked();
            var wasRecording = active?.Status == SceneStatus.Recording;
            if (wasRecording && active != null)
                active.Status = SceneStatus.Paused;

            this.SaveUnlocked();
            return wasRecording;
        }
    }

    public bool TryStart(string title, string location, bool everyone, IReadOnlyList<string> cast, out Guid sceneId)
    {
        sceneId = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            if (this.ActiveUnlocked() != null)
                return false;

            var scene = new SceneRecord
            {
                Id = Guid.NewGuid(),
                Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle() : title.Trim(),
                Location = string.IsNullOrWhiteSpace(location) ? "Unknown location" : location.Trim(),
                StartedAt = DateTimeOffset.Now,
                Status = SceneStatus.Recording,
                Mode = everyone ? RecordingMode.Everyone : RecordingMode.SelectedCharacters,
                SelectedSpeakers = CleanNames(cast),
            };

            this.current.Scenes.Add(scene);
            sceneId = scene.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public bool Pause()
    {
        lock (this.gate)
        {
            var active = this.ActiveUnlocked();
            if (active?.Status != SceneStatus.Recording)
                return false;

            active.Status = SceneStatus.Paused;
            active.Interrupted = false;
            this.SaveUnlocked();
            return true;
        }
    }

    public bool Resume()
    {
        lock (this.gate)
        {
            if (!this.isLoggedIn)
                return false;

            var active = this.ActiveUnlocked();
            if (active?.Status != SceneStatus.Paused)
                return false;

            active.Status = SceneStatus.Recording;
            active.Interrupted = false;
            this.SaveUnlocked();
            return true;
        }
    }

    public bool Stop()
    {
        lock (this.gate)
        {
            var active = this.ActiveUnlocked();
            if (active == null)
                return false;

            active.Status = SceneStatus.Saved;
            active.Interrupted = false;
            active.EndedAt = DateTimeOffset.Now;
            this.SaveUnlocked();
            return true;
        }
    }

    public void SetEveryone(bool everyone)
    {
        lock (this.gate)
        {
            var active = this.ActiveUnlocked();
            if (active == null)
                return;

            active.Mode = everyone ? RecordingMode.Everyone : RecordingMode.SelectedCharacters;
            this.SaveUnlocked();
        }
    }

    public void IncludeSpeaker(string name, bool include)
    {
        lock (this.gate)
        {
            var active = this.ActiveUnlocked();
            if (active == null)
                return;

            var trimmed = PlayerNames.Clean(name);
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            active.SelectedSpeakers ??= new List<string>();
            var index = IndexOfName(active.SelectedSpeakers, trimmed);
            if (include && index < 0)
                active.SelectedSpeakers.Add(trimmed);
            else if (!include && index >= 0)
                active.SelectedSpeakers.RemoveAt(index);

            if (include)
                active.Mode = RecordingMode.SelectedCharacters;

            this.SaveUnlocked();
        }
    }

    public void Rename(Guid sceneId, string title)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null)
                return;

            var trimmed = title.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == scene.Title)
                return;

            scene.Title = trimmed;
            this.SaveUnlocked();
        }
    }

    public bool AddImportedScene(SceneRecord scene, out Guid sceneId)
    {
        sceneId = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            if (scene.Lines.Count == 0 && string.IsNullOrWhiteSpace(scene.Description))
                return false;

            if (scene.Id == Guid.Empty)
                scene.Id = Guid.NewGuid();

            scene.Status = SceneStatus.Saved;
            scene.EndedAt ??= scene.StartedAt;
            this.current.Scenes.Add(scene);
            sceneId = scene.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public bool RemoveScene(Guid sceneId)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null || this.current == null)
                return false;

            this.current.Scenes.Remove(scene);
            this.SaveUnlocked();
            return true;
        }
    }

    public void UpdateLine(Guid sceneId, Guid lineId, string speaker, string text)
    {
        lock (this.gate)
        {
            var line = this.FindLine(sceneId, lineId);
            if (line == null)
                return;

            var name = PlayerNames.Clean(speaker);
            var body = text.Trim();
            if (name.Length == 0 || body.Length == 0)
                return;

            if (name == line.Speaker && body == line.Text)
                return;

            line.Speaker = name;
            line.Text = body;
            this.SaveUnlocked();
        }
    }

    public void RemoveLine(Guid sceneId, Guid lineId)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene?.Lines == null)
                return;

            SceneLine? line = null;
            foreach (var candidate in scene.Lines)
            {
                if (candidate.Id == lineId)
                {
                    line = candidate;
                    break;
                }
            }

            if (line == null)
                return;

            scene.Lines.Remove(line);
            this.Restamp(scene);
            this.SaveUnlocked();
        }
    }

    public bool SplitScene(Guid sceneId, int firstLine, out Guid newSceneId)
    {
        newSceneId = Guid.Empty;
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null || this.current == null || scene.Status != SceneStatus.Saved)
                return false;

            scene.Lines ??= new List<SceneLine>();
            if (firstLine <= 0 || firstLine >= scene.Lines.Count)
                return false;

            var moved = scene.Lines.GetRange(firstLine, scene.Lines.Count - firstLine);
            scene.Lines.RemoveRange(firstLine, moved.Count);
            this.Restamp(scene);

            var stamp = moved[0].At.ToLocalTime().ToString("h:mm tt");
            var title = string.IsNullOrWhiteSpace(scene.Title) ? "Scene" : scene.Title.Trim();
            var created = new SceneRecord
            {
                Id = Guid.NewGuid(),
                Title = title + " · " + stamp,
                Location = scene.Location,
                Storyline = scene.Storyline,
                Status = SceneStatus.Saved,
                Mode = scene.Mode,
                SelectedSpeakers = new List<string>(scene.SelectedSpeakers ?? new List<string>()),
                Lines = moved,
                StartedAt = moved[0].At,
                EndedAt = moved[^1].At,
            };

            var index = this.current.Scenes.IndexOf(scene);
            this.current.Scenes.Insert(index + 1, created);
            newSceneId = created.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public bool AddNote(out Guid id)
    {
        id = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            this.current.Notes ??= new List<JournalNote>();
            var note = new JournalNote
            {
                Id = Guid.NewGuid(),
                Title = "New note",
                WrittenAt = DateTimeOffset.Now,
            };
            this.current.Notes.Add(note);
            id = note.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public void SetNoteTitle(Guid id, string title)
    {
        lock (this.gate)
        {
            var note = this.FindNote(id);
            if (note == null)
                return;

            var trimmed = title.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == note.Title)
                return;

            note.Title = trimmed;
            this.SaveUnlocked();
        }
    }

    public void SetNoteBody(Guid id, string body)
    {
        lock (this.gate)
        {
            var note = this.FindNote(id);
            if (note == null || note.Body == body)
                return;

            note.Body = body;
            this.SaveUnlocked();
        }
    }

    public void RemoveNote(Guid id)
    {
        lock (this.gate)
        {
            var note = this.FindNote(id);
            if (note == null || this.current?.Notes == null)
                return;

            this.current.Notes.Remove(note);
            this.SaveUnlocked();
        }
    }

    public bool AddStoryline(string title, out Guid id)
    {
        id = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            var trimmed = title.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            this.current.Storylines ??= new List<StorylineRecord>();
            var existing = this.FindStoryline(trimmed);
            if (existing != null)
            {
                id = existing.Id;
                return true;
            }

            var story = new StorylineRecord
            {
                Id = Guid.NewGuid(),
                Title = trimmed,
                CreatedAt = DateTimeOffset.Now,
            };
            this.current.Storylines.Add(story);
            id = story.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public void RenameStoryline(Guid id, string title)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null)
                return;

            var trimmed = title.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, story.Title, StringComparison.Ordinal))
                return;

            var previous = story.Title;
            story.Title = trimmed;
            foreach (var scene in this.current!.Scenes)
            {
                if (string.Equals(scene.Storyline, previous, StringComparison.OrdinalIgnoreCase))
                    scene.Storyline = trimmed;
            }

            this.SaveUnlocked();
        }
    }

    public void SetStorylineNotes(Guid id, string notes)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null || story.Notes == notes)
                return;

            story.Notes = notes;
            this.SaveUnlocked();
        }
    }

    public void RemoveStoryline(Guid id)
    {
        lock (this.gate)
        {
            if (this.current?.Storylines == null)
                return;

            var story = this.FindStoryline(id);
            if (story == null)
                return;

            this.current.Storylines.Remove(story);
            foreach (var scene in this.current.Scenes)
            {
                if (string.Equals(scene.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                    scene.Storyline = string.Empty;
            }

            this.SaveUnlocked();
        }
    }

    public void SetSceneStoryline(Guid sceneId, string title)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null)
                return;

            var trimmed = title.Trim();
            if (string.Equals(scene.Storyline, trimmed, StringComparison.Ordinal))
                return;

            scene.Storyline = trimmed;
            this.SaveUnlocked();
        }
    }

    public void SetSceneLocation(Guid sceneId, string location)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null)
                return;

            var trimmed = location.Trim();
            if (trimmed.Length == 0 || string.Equals(scene.Location, trimmed, StringComparison.Ordinal))
                return;

            scene.Location = trimmed;
            this.SaveUnlocked();
        }
    }

    public string FillSceneSummaryIfEmpty(Guid sceneId)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null)
                return string.Empty;

            if (scene.SummaryRevision < LocalSummaries.Revision || string.IsNullOrWhiteSpace(scene.Summary))
            {
                scene.Summary = LocalSummaries.FromScene(scene);
                scene.SummaryRevision = LocalSummaries.Revision;
                this.SaveUnlocked();
            }

            return scene.Summary;
        }
    }

    public string ReplaceSceneSummary(Guid sceneId)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null)
                return string.Empty;

            scene.Summary = LocalSummaries.FromScene(scene);
            scene.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
            return scene.Summary;
        }
    }

    public void SetSceneSummary(Guid sceneId, string summary)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null || scene.Summary == summary)
                return;

            scene.Summary = summary;
            scene.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
        }
    }

    public string FillStorylineSummaryIfEmpty(Guid id)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null)
                return string.Empty;

            if (story.SummaryRevision < LocalSummaries.Revision || string.IsNullOrWhiteSpace(story.Summary))
            {
                story.Summary = LocalSummaries.FromStoryline(story.Title, this.ScenesIn(story.Title));
                story.SummaryRevision = LocalSummaries.Revision;
                this.SaveUnlocked();
            }

            return story.Summary;
        }
    }

    public string ReplaceStorylineSummary(Guid id)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null)
                return string.Empty;

            story.Summary = LocalSummaries.FromStoryline(story.Title, this.ScenesIn(story.Title));
            story.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
            return story.Summary;
        }
    }

    public void SetStorylineSummary(Guid id, string summary)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null || story.Summary == summary)
                return;

            story.Summary = summary;
            story.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
        }
    }

    public bool AddStorylineNote(Guid storyId, out Guid noteId)
    {
        noteId = Guid.Empty;
        lock (this.gate)
        {
            var story = this.FindStoryline(storyId);
            if (story == null)
                return false;

            story.NotePages ??= new List<JournalNote>();
            var note = new JournalNote
            {
                Id = Guid.NewGuid(),
                Title = "New note",
                WrittenAt = DateTimeOffset.Now,
            };
            story.NotePages.Add(note);
            noteId = note.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public void SetStorylineNoteTitle(Guid storyId, Guid noteId, string title)
    {
        lock (this.gate)
        {
            var note = this.FindStorylineNote(storyId, noteId);
            if (note == null)
                return;

            var trimmed = string.IsNullOrWhiteSpace(title) ? "New note" : title.Trim();
            if (note.Title == trimmed)
                return;

            note.Title = trimmed;
            this.SaveUnlocked();
        }
    }

    public void SetStorylineNoteBody(Guid storyId, Guid noteId, string body)
    {
        lock (this.gate)
        {
            var note = this.FindStorylineNote(storyId, noteId);
            if (note == null || note.Body == body)
                return;

            note.Body = body;
            this.SaveUnlocked();
        }
    }

    public void RemoveStorylineNote(Guid storyId, Guid noteId)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(storyId);
            if (story?.NotePages == null)
                return;

            JournalNote? note = null;
            foreach (var candidate in story.NotePages)
            {
                if (candidate.Id == noteId)
                {
                    note = candidate;
                    break;
                }
            }

            if (note == null)
                return;

            story.NotePages.Remove(note);
            this.SaveUnlocked();
        }
    }

    public string FillCharacterSummaryIfEmpty(string name)
    {
        lock (this.gate)
        {
            var profile = this.EnsureCharacter(name);
            if (profile == null)
                return string.Empty;

            if (profile.SummaryRevision < LocalSummaries.Revision || string.IsNullOrWhiteSpace(profile.Summary))
            {
                profile.Summary = LocalSummaries.FromCharacter(profile.Name, this.current!.Scenes);
                profile.SummaryRevision = LocalSummaries.Revision;
                this.SaveUnlocked();
            }

            return profile.Summary;
        }
    }

    public string ReplaceCharacterSummary(string name)
    {
        lock (this.gate)
        {
            var profile = this.EnsureCharacter(name);
            if (profile == null)
                return string.Empty;

            profile.Summary = LocalSummaries.FromCharacter(profile.Name, this.current!.Scenes);
            profile.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
            return profile.Summary;
        }
    }

    public void SetCharacterSummary(string name, string summary)
    {
        lock (this.gate)
        {
            var profile = this.EnsureCharacter(name);
            if (profile == null || profile.Summary == summary)
                return;

            profile.Summary = summary;
            profile.SummaryRevision = LocalSummaries.Revision;
            this.SaveUnlocked();
        }
    }

    public void RenameCharacter(string from, string to)
    {
        lock (this.gate)
        {
            if (this.current == null)
                return;

            var source = PlayerNames.Clean(from);
            var target = PlayerNames.Clean(to);
            if (source.Length == 0 || target.Length == 0)
                return;

            if (string.Equals(source, target, StringComparison.Ordinal))
                return;

            var changed = false;
            this.current.Scenes ??= new List<SceneRecord>();
            foreach (var scene in this.current.Scenes)
            {
                scene.Lines ??= new List<SceneLine>();
                foreach (var line in scene.Lines)
                {
                    if (!string.Equals(PlayerNames.Clean(line.Speaker), source, StringComparison.OrdinalIgnoreCase))
                        continue;

                    line.Speaker = target;
                    changed = true;
                }

                scene.SelectedSpeakers ??= new List<string>();
                for (var i = 0; i < scene.SelectedSpeakers.Count; i++)
                {
                    if (!string.Equals(PlayerNames.Clean(scene.SelectedSpeakers[i]), source, StringComparison.OrdinalIgnoreCase))
                        continue;

                    scene.SelectedSpeakers[i] = target;
                    changed = true;
                }

                scene.SelectedSpeakers = CleanNames(scene.SelectedSpeakers);
            }

            this.current.Characters ??= new List<CharacterProfile>();
            CharacterProfile? sourceProfile = null;
            CharacterProfile? targetProfile = null;
            foreach (var profile in this.current.Characters)
            {
                if (string.Equals(PlayerNames.Clean(profile.Name), source, StringComparison.OrdinalIgnoreCase))
                    sourceProfile = profile;
                else if (string.Equals(PlayerNames.Clean(profile.Name), target, StringComparison.OrdinalIgnoreCase))
                    targetProfile = profile;
            }

            if (sourceProfile != null && targetProfile != null)
            {
                if (string.IsNullOrWhiteSpace(targetProfile.Notes))
                    targetProfile.Notes = sourceProfile.Notes;
                else if (!string.IsNullOrWhiteSpace(sourceProfile.Notes) && !targetProfile.Notes.Contains(sourceProfile.Notes, StringComparison.Ordinal))
                    targetProfile.Notes = targetProfile.Notes.TrimEnd() + "\n" + sourceProfile.Notes.Trim();

                if (string.IsNullOrWhiteSpace(targetProfile.Summary))
                {
                    targetProfile.Summary = sourceProfile.Summary;
                    targetProfile.SummaryRevision = sourceProfile.SummaryRevision;
                }

                this.current.Characters.Remove(sourceProfile);
                changed = true;
            }
            else if (sourceProfile != null)
            {
                sourceProfile.Name = target;
                changed = true;
            }

            if (changed)
                this.SaveUnlocked();
        }
    }

    public void RemoveCharacter(string name)
    {
        lock (this.gate)
        {
            if (this.current == null)
                return;

            var target = PlayerNames.Clean(name);
            if (target.Length == 0)
                return;

            var changed = false;
            this.current.Scenes ??= new List<SceneRecord>();
            foreach (var scene in this.current.Scenes)
            {
                scene.Lines ??= new List<SceneLine>();
                var kept = new List<SceneLine>();
                foreach (var line in scene.Lines)
                {
                    if (string.Equals(PlayerNames.Clean(line.Speaker), target, StringComparison.OrdinalIgnoreCase))
                    {
                        changed = true;
                        continue;
                    }

                    kept.Add(line);
                }

                scene.Lines = kept;
                scene.SelectedSpeakers ??= new List<string>();
                var speakers = new List<string>();
                foreach (var speaker in scene.SelectedSpeakers)
                {
                    if (string.Equals(PlayerNames.Clean(speaker), target, StringComparison.OrdinalIgnoreCase))
                    {
                        changed = true;
                        continue;
                    }

                    speakers.Add(speaker);
                }

                scene.SelectedSpeakers = speakers;
            }

            this.current.Characters ??= new List<CharacterProfile>();
            var profiles = new List<CharacterProfile>();
            foreach (var profile in this.current.Characters)
            {
                if (string.Equals(PlayerNames.Clean(profile.Name), target, StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    continue;
                }

                profiles.Add(profile);
            }

            this.current.Characters = profiles;
            if (changed)
                this.SaveUnlocked();
        }
    }

    public bool AddCharacter(string name)
    {
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            var trimmed = PlayerNames.Clean(name);
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            this.current.Characters ??= new List<CharacterProfile>();
            if (this.FindCharacter(trimmed) != null)
                return true;

            this.current.Characters.Add(new CharacterProfile { Name = trimmed });
            this.SaveUnlocked();
            return true;
        }
    }

    public void SetCharacterNotes(string name, string notes)
    {
        lock (this.gate)
        {
            if (this.current == null)
                return;

            var trimmed = PlayerNames.Clean(name);
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            this.current.Characters ??= new List<CharacterProfile>();
            var profile = this.FindCharacter(trimmed);
            if (profile == null)
            {
                profile = new CharacterProfile { Name = trimmed };
                this.current.Characters.Add(profile);
            }

            if (profile.Notes == notes)
                return;

            profile.Notes = notes;
            this.SaveUnlocked();
        }
    }

    public void AppendLine(SceneLine line)
    {
        lock (this.gate)
        {
            var active = this.ActiveUnlocked();
            if (active?.Status != SceneStatus.Recording)
                return;

            if (active.Mode == RecordingMode.SelectedCharacters && !IsSelected(active, line.Speaker))
                return;

            active.Lines.Add(line);
            this.SaveUnlocked();
        }
    }

    public void CleanSpeakers()
    {
        lock (this.gate)
        {
            if (this.current == null)
                return;

            var changed = false;
            foreach (var scene in this.current.Scenes)
            {
                scene.Lines ??= new List<SceneLine>();
                foreach (var line in scene.Lines)
                {
                    var speaker = PlayerNames.Separate(line.Speaker, out var extra);
                    var text = PlayerNames.DropTell(PlayerNames.StripLeadingMark(line.Text));
                    if (extra.Length > 0)
                        text = string.IsNullOrWhiteSpace(text) ? extra : extra + " " + text.Trim();

                    if (!string.Equals(speaker, line.Speaker, StringComparison.Ordinal))
                    {
                        line.Speaker = speaker;
                        changed = true;
                    }

                    if (!string.Equals(text, line.Text, StringComparison.Ordinal))
                    {
                        line.Text = text;
                        changed = true;
                    }
                }

                scene.SelectedSpeakers ??= new List<string>();
                var speakers = new List<string>();
                foreach (var name in scene.SelectedSpeakers)
                {
                    var cleaned = PlayerNames.Clean(name);
                    if (cleaned.Length == 0 || IndexOfName(speakers, cleaned) >= 0)
                    {
                        changed = true;
                        continue;
                    }

                    if (!string.Equals(cleaned, name, StringComparison.Ordinal))
                        changed = true;

                    speakers.Add(cleaned);
                }

                scene.SelectedSpeakers = speakers;
            }

            this.current.Characters ??= new List<CharacterProfile>();
            var profiles = new List<CharacterProfile>();
            foreach (var profile in this.current.Characters)
            {
                var cleaned = PlayerNames.Clean(profile.Name);
                if (cleaned.Length == 0)
                {
                    changed = true;
                    continue;
                }

                CharacterProfile? existing = null;
                foreach (var candidate in profiles)
                {
                    if (candidate.Name.Equals(cleaned, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = candidate;
                        break;
                    }
                }

                if (existing == null)
                {
                    if (!string.Equals(profile.Name, cleaned, StringComparison.Ordinal))
                    {
                        profile.Name = cleaned;
                        changed = true;
                    }

                    profiles.Add(profile);
                    continue;
                }

                changed = true;
                if (string.IsNullOrWhiteSpace(existing.Notes))
                    existing.Notes = profile.Notes;
                else if (!string.IsNullOrWhiteSpace(profile.Notes) && !existing.Notes.Contains(profile.Notes, StringComparison.Ordinal))
                    existing.Notes = existing.Notes.TrimEnd() + "\n" + profile.Notes.Trim();

                if (string.IsNullOrWhiteSpace(existing.Summary))
                {
                    existing.Summary = profile.Summary;
                    existing.SummaryRevision = profile.SummaryRevision;
                }
            }

            this.current.Characters = profiles;
            var owner = PlayerNames.Clean(this.current.CharacterName);
            if (!string.Equals(owner, this.current.CharacterName, StringComparison.Ordinal) && owner.Length > 0)
            {
                this.current.CharacterName = owner;
                changed = true;
            }

            if (changed)
                this.SaveUnlocked();
        }
    }

    public void Flush()
    {
        lock (this.gate)
            this.SaveUnlocked();
    }

    public NotebookSnapshot Snapshot()
    {
        lock (this.gate)
        {
            if (this.current == null)
            {
                return new NotebookSnapshot
                {
                    IsLoggedIn = this.isLoggedIn,
                };
            }

            var scenes = new List<SceneRecord>(this.current.Scenes.Count);
            foreach (var scene in this.current.Scenes)
                scenes.Add(scene.Copy());

            this.current.Characters ??= new List<CharacterProfile>();
            this.current.Storylines ??= new List<StorylineRecord>();

            return new NotebookSnapshot
            {
                IsLoggedIn = this.isLoggedIn,
                CharacterName = this.current.CharacterName,
                World = this.current.World,
                Scenes = scenes,
                Characters = new List<CharacterProfile>(this.current.Characters),
                Storylines = new List<StorylineRecord>(this.current.Storylines),
                Notes = new List<JournalNote>(this.current.Notes ?? new List<JournalNote>()),
            };
        }
    }

    private static bool IsSelected(SceneRecord scene, string speaker)
    {
        var who = PlayerNames.Clean(speaker);
        foreach (var name in scene.SelectedSpeakers)
        {
            var picked = PlayerNames.Clean(name);
            if (picked.Length == 0 || who.Length == 0)
                continue;

            if (string.Equals(picked, who, StringComparison.OrdinalIgnoreCase)
                || who.StartsWith(picked + " ", StringComparison.OrdinalIgnoreCase)
                || picked.StartsWith(who + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int IndexOfName(List<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(PlayerNames.Clean(names[i]), PlayerNames.Clean(name), StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static List<string> CleanNames(IReadOnlyList<string> names)
    {
        var clean = new List<string>();
        foreach (var name in names)
        {
            var trimmed = PlayerNames.Clean(name);
            if (string.IsNullOrWhiteSpace(trimmed) || IndexOfName(clean, trimmed) >= 0)
                continue;

            clean.Add(trimmed);
        }

        return clean;
    }

    private SceneRecord? ActiveUnlocked()
    {
        if (this.current == null)
            return null;

        foreach (var scene in this.current.Scenes)
        {
            if (scene.Status is SceneStatus.Recording or SceneStatus.Paused)
                return scene;
        }

        return null;
    }

    private SceneRecord? FindUnlocked(Guid sceneId)
    {
        if (this.current == null)
            return null;

        foreach (var scene in this.current.Scenes)
        {
            if (scene.Id == sceneId)
                return scene;
        }

        return null;
    }

    private SceneLine? FindLine(Guid sceneId, Guid lineId)
    {
        var scene = this.FindUnlocked(sceneId);
        if (scene?.Lines == null)
            return null;

        foreach (var line in scene.Lines)
        {
            if (line.Id == lineId)
                return line;
        }

        return null;
    }

    private void Restamp(SceneRecord scene)
    {
        if (scene.Lines == null || scene.Lines.Count == 0)
            return;

        scene.StartedAt = scene.Lines[0].At;
        scene.EndedAt = scene.Lines[^1].At;
    }

    private List<SceneRecord> ScenesIn(string title)
    {
        var found = new List<SceneRecord>();
        if (this.current == null)
            return found;

        foreach (var scene in this.current.Scenes)
        {
            if (string.Equals(scene.Storyline, title, StringComparison.OrdinalIgnoreCase))
                found.Add(scene);
        }

        return found;
    }

    private JournalNote? FindStorylineNote(Guid storyId, Guid noteId)
    {
        var story = this.FindStoryline(storyId);
        if (story?.NotePages == null)
            return null;

        foreach (var note in story.NotePages)
        {
            if (note.Id == noteId)
                return note;
        }

        return null;
    }

    private CharacterProfile? EnsureCharacter(string name)
    {
        if (this.current == null || !this.isLoggedIn)
            return null;

        var trimmed = PlayerNames.Clean(name);
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        this.current.Characters ??= new List<CharacterProfile>();
        var profile = this.FindCharacter(trimmed);
        if (profile != null)
            return profile;

        profile = new CharacterProfile { Name = trimmed };
        this.current.Characters.Add(profile);
        return profile;
    }

    private StorylineRecord? FindStoryline(Guid id)
    {
        if (this.current?.Storylines == null)
            return null;

        foreach (var story in this.current.Storylines)
        {
            if (story.Id == id)
                return story;
        }

        return null;
    }

    private StorylineRecord? FindStoryline(string title)
    {
        if (this.current?.Storylines == null)
            return null;

        foreach (var story in this.current.Storylines)
        {
            if (string.Equals(story.Title.Trim(), title, StringComparison.OrdinalIgnoreCase))
                return story;
        }

        return null;
    }

    private CharacterProfile? FindCharacter(string name)
    {
        if (this.current?.Characters == null)
            return null;

        foreach (var profile in this.current.Characters)
        {
            if (string.Equals(PlayerNames.Clean(profile.Name), PlayerNames.Clean(name), StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return null;
    }

    private JournalNote? FindNote(Guid id)
    {
        if (this.current?.Notes == null)
            return null;

        foreach (var note in this.current.Notes)
        {
            if (note.Id == id)
                return note;
        }

        return null;
    }

    private NotebookFile ReadOrCreate(string path, ulong contentId, string name, string world)
    {
        if (!File.Exists(path))
            return CreateFile(contentId, name, world);

        try
        {
            var text = File.ReadAllText(path);
            var file = JsonConvert.DeserializeObject<NotebookFile>(text, JsonSettings);
            if (file == null)
                throw new InvalidDataException("The notebook file was empty.");

            file.Scenes ??= new List<SceneRecord>();
            file.Characters ??= new List<CharacterProfile>();
            file.Storylines ??= new List<StorylineRecord>();
            file.Notes ??= new List<JournalNote>();
            foreach (var story in file.Storylines)
            {
                story.NotePages ??= new List<JournalNote>();
                story.Notes ??= string.Empty;
                story.Summary ??= string.Empty;
            }
            file.ContentId = contentId;
            if (!string.IsNullOrWhiteSpace(name))
                file.CharacterName = name;
            if (!string.IsNullOrWhiteSpace(world))
                file.World = world;

            var recovered = false;
            foreach (var scene in file.Scenes)
            {
                scene.Lines ??= new List<SceneLine>();
                scene.Tags ??= new List<string>();
                scene.SelectedSpeakers ??= new List<string>();
                if (scene.Status != SceneStatus.Recording)
                    continue;

                scene.Status = SceneStatus.Paused;
                scene.Interrupted = true;
                recovered = true;
            }

            if (recovered)
            {
                this.Write(path, file);
                this.log.Information("A scene was still recording when the game closed. Its lines were kept and the scene is paused.");
            }

            return file;
        }
        catch (Exception ex)
        {
            var backup = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                File.Move(path, backup);
            }
            catch (Exception moveEx)
            {
                this.log.Error(moveEx, "Could not move aside an unreadable notebook at {Path}.", path);
            }

            this.log.Error(ex, "Could not read the notebook at {Path}. A new notebook was started and the old file was left beside it.", path);
            return CreateFile(contentId, name, world);
        }
    }

    private void SaveUnlocked()
    {
        if (this.current == null || this.current.ContentId == 0)
            return;

        try
        {
            this.Write(this.PathFor(this.current.ContentId), this.current);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not save the notebook.");
        }
    }

    private void Write(string path, NotebookFile file)
    {
        var json = JsonConvert.SerializeObject(file, JsonSettings);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(ulong contentId) => Path.Combine(this.directory, contentId + ".json");

    private static NotebookFile CreateFile(ulong contentId, string name, string world) => new()
    {
        ContentId = contentId,
        CharacterName = name,
        World = world,
    };

    private static string DefaultTitle() => "Scene · " + DateTime.Now.ToString("MMM d, h:mm tt");
}
