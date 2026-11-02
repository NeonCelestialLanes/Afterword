using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    private readonly string defaultLogDirectory;
    private string logDirectory;
    private readonly IPluginLog log;
    private readonly object gate = new();
    private NotebookFile? current;
    private NotebookFile? session;
    private ulong loggedInId;
    private bool isLoggedIn;

    public NotebookStore(string configDirectory, IPluginLog log)
    {
        this.directory = Path.Combine(configDirectory, "notebooks");
        this.defaultLogDirectory = Path.Combine(configDirectory, "logs");
        this.logDirectory = this.defaultLogDirectory;
        Directory.CreateDirectory(this.directory);
        Directory.CreateDirectory(this.logDirectory);
        this.log = log;
    }

    public string LogFolder => this.logDirectory;

    public string UsualLogFolder => this.defaultLogDirectory;

    public bool IsUsualLogFolder =>
        string.Equals(Path.GetFullPath(this.logDirectory), Path.GetFullPath(this.defaultLogDirectory), StringComparison.OrdinalIgnoreCase);

    public string UseLogFolder(string? folder, out bool changed)
    {
        lock (this.gate)
        {
            changed = false;
            string next;
            try
            {
                next = string.IsNullOrWhiteSpace(folder)
                    ? this.defaultLogDirectory
                    : Path.GetFullPath(folder.Trim());
                Directory.CreateDirectory(next);
            }
            catch (Exception ex)
            {
                this.log.Error(ex, "Could not open the log folder.");
                return "That folder could not be opened. Logs stay in the usual place.";
            }

            if (string.Equals(Path.GetFullPath(this.logDirectory), next, StringComparison.OrdinalIgnoreCase))
                return "Logs are already saved there.";

            this.logDirectory = next;
            changed = true;
            this.SaveUnlocked();
            return "New logs are saved in that folder. A titled scene uses the date and its title. A scene without a title uses the date and time.";
        }
    }

    public void LoadCharacter(ulong contentId, string name, string world)
    {
        if (contentId == 0)
            return;

        lock (this.gate)
        {
            var sameLogin = this.isLoggedIn && this.loggedInId == contentId;
            this.loggedInId = contentId;
            this.isLoggedIn = true;
            this.MoveHandProfile(contentId, name, world);

            if (this.current?.ContentId == contentId)
            {
                ApplyIdentity(this.current, name, world);
                this.session = null;
                return;
            }

            if (sameLogin && this.current != null)
            {
                if (this.session?.ContentId == contentId)
                {
                    ApplyIdentity(this.session, name, world);
                    return;
                }

                this.SaveSession();
                this.session = this.ReadOrCreate(this.PathFor(contentId), contentId, name, world);
                return;
            }

            this.SaveUnlocked();
            this.SaveSession();
            this.session = null;
            this.current = this.ReadOrCreate(this.PathFor(contentId), contentId, name, world);
            this.SaveUnlocked();
            this.log.Information("Opened the notebook for {Character}.", this.current.CharacterName);
        }
    }

    public void ShowLoggedIn()
    {
        if (this.loggedInId == 0)
            return;

        this.OpenNotebook(this.loggedInId);
    }

    public void OpenNotebook(ulong contentId)
    {
        if (contentId == 0)
            return;

        lock (this.gate)
        {
            if (this.current?.ContentId == contentId)
                return;

            this.SaveUnlocked();
            this.SaveSession();

            if (this.session?.ContentId == contentId)
            {
                var leaving = this.current;
                this.current = this.session;
                this.session = leaving != null && this.isLoggedIn && leaving.ContentId == this.loggedInId
                    ? leaving
                    : null;
                return;
            }

            if (this.isLoggedIn && this.current?.ContentId == this.loggedInId)
                this.session = this.current;

            if (!File.Exists(this.PathFor(contentId)))
                return;

            this.current = this.ReadOrCreate(this.PathFor(contentId), contentId, string.Empty, string.Empty);
            if (this.current.ContentId == this.loggedInId)
                this.session = null;
        }
    }

    public ulong AddProfile(string name, string world)
    {
        var cleanName = PlayerNames.Clean(name);
        var cleanWorld = world.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || string.IsNullOrWhiteSpace(cleanWorld))
            return 0;

        lock (this.gate)
        {
            var existing = this.FindProfile(cleanName, cleanWorld);
            if (existing != 0)
                return existing;

            var id = HandId(cleanName, cleanWorld);
            Directory.CreateDirectory(this.directory);
            var path = this.PathFor(id);
            if (!File.Exists(path))
                this.Write(path, CreateFile(id, cleanName, cleanWorld));

            this.log.Information("Added a notebook for {Character}.", cleanName);
            return id;
        }
    }

    public bool RemoveProfile(ulong contentId)
    {
        if (contentId == 0)
            return false;

        lock (this.gate)
        {
            if (this.isLoggedIn && this.loggedInId == contentId)
                return false;

            if (this.current?.ContentId == contentId)
            {
                if (this.session != null && this.session.ContentId != contentId)
                {
                    this.current = this.session;
                    this.session = null;
                }
                else if (this.isLoggedIn && this.loggedInId != 0 && File.Exists(this.PathFor(this.loggedInId)))
                {
                    this.current = this.ReadOrCreate(this.PathFor(this.loggedInId), this.loggedInId, string.Empty, string.Empty);
                    this.session = null;
                }
                else
                {
                    this.current = null;
                }
            }
            else if (this.session?.ContentId == contentId)
            {
                this.session = null;
            }

            var path = this.PathFor(contentId);
            if (File.Exists(path))
                File.Delete(path);
            this.log.Information("Deleted a notebook.");
            return true;
        }
    }

    public IReadOnlyList<PlayerCharacter> ListPlayers()
    {
        lock (this.gate)
        {
            var found = new List<PlayerCharacter>();
            var seen = new HashSet<ulong>();
            if (Directory.Exists(this.directory))
            {
                foreach (var path in Directory.GetFiles(this.directory, "*.json"))
                {
                    try
                    {
                        var file = JsonConvert.DeserializeObject<NotebookFile>(File.ReadAllText(path), JsonSettings);
                        AddPlayer(found, seen, file);
                    }
                    catch (Exception ex)
                    {
                        this.log.Warning(ex, "Could not read a notebook at {Path}.", path);
                    }
                }
            }

            AddPlayer(found, seen, this.current);
            AddPlayer(found, seen, this.session);
            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }
    }

    public bool MarkLoggedOut()
    {
        lock (this.gate)
        {
            var recording = this.RecordingFile();
            this.isLoggedIn = false;
            var active = ActiveOn(recording);
            var wasRecording = active?.Status == SceneStatus.Recording;
            if (wasRecording && active != null)
                active.Status = SceneStatus.Paused;

            if (recording != null)
                this.SaveFile(recording);
            else
                this.SaveUnlocked();
            return wasRecording;
        }
    }

    public bool TryStart(string title, string location, bool everyone, IReadOnlyList<string> cast, out Guid sceneId)
    {
        sceneId = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn || this.current.ContentId != this.loggedInId)
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
            if (!this.isLoggedIn || this.current?.ContentId != this.loggedInId)
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
            if (this.current == null || this.current.ContentId == 0)
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

            var logFile = scene.LogFile;
            scene.LogFile = string.Empty;
            this.current.Scenes.Remove(scene);
            this.DeleteLog(logFile);
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

    public void RemoveLineIndexes(Guid sceneId, IReadOnlyCollection<int> indexes)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene?.Lines == null || indexes.Count == 0)
                return;

            var ordered = new List<int>();
            foreach (var index in indexes)
            {
                if (index >= 0 && index < scene.Lines.Count && !ordered.Contains(index))
                    ordered.Add(index);
            }

            if (ordered.Count == 0)
                return;

            ordered.Sort((left, right) => right.CompareTo(left));
            foreach (var index in ordered)
                scene.Lines.RemoveAt(index);

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

    public bool CombineScenes(Guid keepId, Guid otherId)
    {
        lock (this.gate)
        {
            if (keepId == otherId || this.current == null)
                return false;

            var keep = this.FindUnlocked(keepId);
            var other = this.FindUnlocked(otherId);
            if (keep == null || other == null)
                return false;

            if (keep.Status != SceneStatus.Saved || other.Status != SceneStatus.Saved)
                return false;

            keep.Lines ??= new List<SceneLine>();
            other.Lines ??= new List<SceneLine>();
            var merged = new List<(DateTimeOffset At, int Order, SceneLine Line)>(keep.Lines.Count + other.Lines.Count);
            var order = 0;
            foreach (var line in keep.Lines)
                merged.Add((line.At, order++, line));
            foreach (var line in other.Lines)
                merged.Add((line.At, order++, line));

            merged.Sort((left, right) =>
            {
                var time = left.At.CompareTo(right.At);
                return time != 0 ? time : left.Order.CompareTo(right.Order);
            });

            keep.Lines = new List<SceneLine>(merged.Count);
            foreach (var item in merged)
                keep.Lines.Add(item.Line);

            this.Restamp(keep);
            if (string.IsNullOrWhiteSpace(keep.Location))
                keep.Location = other.Location;
            if (string.IsNullOrWhiteSpace(keep.Storyline))
                keep.Storyline = other.Storyline;
            if (string.IsNullOrWhiteSpace(keep.Summary))
            {
                keep.Summary = other.Summary;
                keep.SummaryRevision = other.SummaryRevision;
            }

            if (string.IsNullOrWhiteSpace(keep.Notes))
                keep.Notes = other.Notes;
            if (string.IsNullOrWhiteSpace(keep.Description))
                keep.Description = other.Description;

            MergeCast(keep, other);
            MergeTags(keep, other);

            var logFile = other.LogFile;
            other.LogFile = string.Empty;
            this.current.Scenes.Remove(other);
            this.DeleteLog(logFile);
            this.SaveUnlocked();
            return true;
        }
    }

    private static void MergeCast(SceneRecord keep, SceneRecord other)
    {
        if (keep.Mode == RecordingMode.Everyone || other.Mode == RecordingMode.Everyone)
        {
            keep.Mode = RecordingMode.Everyone;
            return;
        }

        keep.SelectedSpeakers ??= new List<string>();
        other.SelectedSpeakers ??= new List<string>();
        foreach (var name in other.SelectedSpeakers)
        {
            if (keep.SelectedSpeakers.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            keep.SelectedSpeakers.Add(name);
        }
    }

    private static void MergeTags(SceneRecord keep, SceneRecord other)
    {
        keep.Tags ??= new List<string>();
        if (other.Tags == null)
            return;

        foreach (var tag in other.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            if (keep.Tags.Exists(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                continue;

            keep.Tags.Add(tag);
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

            this.current.Stories ??= new List<WrittenStory>();
            foreach (var written in this.current.Stories)
            {
                if (string.Equals(written.Storyline, previous, StringComparison.OrdinalIgnoreCase))
                    written.Storyline = trimmed;
            }

            this.SaveUnlocked();
        }
    }

    public void SetStorylineDate(Guid id, DateTimeOffset when)
    {
        lock (this.gate)
        {
            var story = this.FindStoryline(id);
            if (story == null || story.CreatedAt == when)
                return;

            story.CreatedAt = when;
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

            this.current.Stories ??= new List<WrittenStory>();
            foreach (var written in this.current.Stories)
            {
                if (string.Equals(written.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                    written.Storyline = string.Empty;
            }

            this.SaveUnlocked();
        }
    }

    public bool AddWrittenStory(out Guid id)
    {
        id = Guid.Empty;
        lock (this.gate)
        {
            if (this.current == null || !this.isLoggedIn)
                return false;

            this.current.Stories ??= new List<WrittenStory>();
            var story = new WrittenStory
            {
                Id = Guid.NewGuid(),
                WrittenAt = DateTimeOffset.Now,
            };
            this.current.Stories.Add(story);
            id = story.Id;
            this.SaveUnlocked();
            return true;
        }
    }

    public void SetWrittenStoryTitle(Guid id, string title)
    {
        lock (this.gate)
        {
            var story = this.FindWrittenStory(id);
            if (story == null)
                return;

            var trimmed = title.Trim();
            if (trimmed.Length == 0 || trimmed == story.Title)
                return;

            story.Title = trimmed;
            this.SaveUnlocked();
        }
    }

    public void SetWrittenStoryBody(Guid id, string body)
    {
        lock (this.gate)
        {
            var story = this.FindWrittenStory(id);
            if (story == null || story.Body == body)
                return;

            story.Body = body;
            this.SaveUnlocked();
        }
    }

    public void SetWrittenStoryStoryline(Guid id, string title)
    {
        lock (this.gate)
        {
            var story = this.FindWrittenStory(id);
            if (story == null)
                return;

            var trimmed = title.Trim();
            if (string.Equals(story.Storyline, trimmed, StringComparison.Ordinal))
                return;

            story.Storyline = trimmed;
            this.SaveUnlocked();
        }
    }

    public void SetWrittenStoryLore(Guid id, bool affectsLore)
    {
        lock (this.gate)
        {
            var story = this.FindWrittenStory(id);
            if (story == null || story.AffectsLore == affectsLore)
                return;

            story.AffectsLore = affectsLore;
            this.SaveUnlocked();
        }
    }

    public void RemoveWrittenStory(Guid id)
    {
        lock (this.gate)
        {
            var story = this.FindWrittenStory(id);
            if (story == null || this.current?.Stories == null)
                return;

            this.current.Stories.Remove(story);
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

    public void SetSceneNotes(Guid sceneId, string notes)
    {
        lock (this.gate)
        {
            var scene = this.FindUnlocked(sceneId);
            if (scene == null || scene.Notes == notes)
                return;

            scene.Notes = notes;
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
            var recording = this.RecordingFile();
            var active = ActiveOn(recording);
            if (active?.Status != SceneStatus.Recording)
                return;

            if (active.Mode == RecordingMode.SelectedCharacters && !IsSelected(active, line.Speaker))
                return;

            active.Lines.Add(line);
            this.SaveFile(recording!);
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

            this.LoginIdentity(out var loggedInName, out var loggedInWorld);
            return new NotebookSnapshot
            {
                IsLoggedIn = this.isLoggedIn,
                ContentId = this.current.ContentId,
                LoggedInId = this.loggedInId,
                LoggedInName = loggedInName,
                LoggedInWorld = loggedInWorld,
                CharacterName = this.current.CharacterName,
                World = this.current.World,
                Scenes = scenes,
                Characters = new List<CharacterProfile>(this.current.Characters),
                Storylines = new List<StorylineRecord>(this.current.Storylines),
                Stories = new List<WrittenStory>(this.current.Stories ?? new List<WrittenStory>()),
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

    private SceneRecord? ActiveUnlocked() => ActiveOn(this.current);

    private NotebookFile? RecordingFile()
    {
        if (!this.isLoggedIn || this.loggedInId == 0)
            return null;

        if (this.current?.ContentId == this.loggedInId)
            return this.current;

        if (this.session?.ContentId == this.loggedInId)
            return this.session;

        return null;
    }

    private void LoginIdentity(out string name, out string world)
    {
        var file = this.session?.ContentId == this.loggedInId
            ? this.session
            : this.current?.ContentId == this.loggedInId ? this.current : null;
        name = file?.CharacterName ?? string.Empty;
        world = file?.World ?? string.Empty;
    }

    private static void ApplyIdentity(NotebookFile file, string name, string world)
    {
        if (!string.IsNullOrWhiteSpace(name))
            file.CharacterName = name;
        if (!string.IsNullOrWhiteSpace(world))
            file.World = world;
    }

    private static void AddPlayer(List<PlayerCharacter> found, HashSet<ulong> seen, NotebookFile? file)
    {
        if (file == null || file.ContentId == 0 || string.IsNullOrWhiteSpace(file.CharacterName) || !seen.Add(file.ContentId))
            return;

        found.Add(new PlayerCharacter
        {
            ContentId = file.ContentId,
            Name = file.CharacterName,
            World = file.World ?? string.Empty,
        });
    }

    private static SceneRecord? ActiveOn(NotebookFile? file)
    {
        if (file?.Scenes == null)
            return null;

        foreach (var scene in file.Scenes)
        {
            if (scene.Status is SceneStatus.Recording or SceneStatus.Paused)
                return scene;
        }

        return null;
    }

    private void SaveSession()
    {
        if (this.session == null || this.session.ContentId == 0)
            return;

        this.SaveFile(this.session);
    }

    private void SaveFile(NotebookFile file)
    {
        if (ReferenceEquals(file, this.current))
        {
            this.SaveUnlocked();
            return;
        }

        var viewed = this.current;
        this.current = file;
        try
        {
            this.SaveUnlocked();
        }
        finally
        {
            this.current = viewed;
        }
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

    private WrittenStory? FindWrittenStory(Guid id)
    {
        if (this.current?.Stories == null)
            return null;

        foreach (var story in this.current.Stories)
        {
            if (story.Id == id)
                return story;
        }

        return null;
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
            file.Stories ??= new List<WrittenStory>();
            file.Notes ??= new List<JournalNote>();
            foreach (var written in file.Stories)
            {
                written.Title ??= "New story";
                written.Body ??= string.Empty;
                written.Storyline ??= string.Empty;
            }
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
            this.WriteLogsUnlocked();
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

    private void WriteLogsUnlocked()
    {
        if (this.current == null)
            return;

        try
        {
            Directory.CreateDirectory(this.logDirectory);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not create the log folder.");
            return;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in this.current.Scenes)
        {
            var fileName = UniqueLogName(scene, used);
            var previous = scene.LogFile;
            scene.LogFile = fileName;
            if (!string.IsNullOrWhiteSpace(previous)
                && !string.Equals(previous, fileName, StringComparison.OrdinalIgnoreCase))
                this.DeleteLog(previous);
            try
            {
                this.WriteText(Path.Combine(this.logDirectory, fileName), LogText(scene));
            }
            catch (Exception ex)
            {
                this.log.Error(ex, "Could not write the log {File}.", fileName);
            }
        }
    }

    private void DeleteLog(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || this.current == null)
            return;

        foreach (var scene in this.current.Scenes)
        {
            if (string.Equals(scene.LogFile, fileName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        try
        {
            var path = Path.Combine(this.logDirectory, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not remove the old log {File}.", fileName);
        }
    }

    private static string UniqueLogName(SceneRecord scene, HashSet<string> used)
    {
        var name = LogFileName(scene);
        var unique = name;
        var number = 2;
        while (!used.Add(unique))
        {
            unique = Path.GetFileNameWithoutExtension(name) + " " + number + Path.GetExtension(name);
            number++;
        }

        return unique;
    }

    private static string LogFileName(SceneRecord scene)
    {
        var when = scene.StartedAt.ToLocalTime();
        var title = scene.Title?.Trim() ?? string.Empty;
        var named = title.Length > 0 && !title.StartsWith("Scene · ", StringComparison.Ordinal);
        if (!named)
            return when.ToString("yyyy-MM-dd h-mm tt") + ".txt";

        var safe = SanitizeFileName(title);
        if (safe.Length == 0)
            return when.ToString("yyyy-MM-dd h-mm tt") + ".txt";

        return when.ToString("yyyy-MM-dd") + " " + safe + ".txt";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = ' ';
        }

        var cleaned = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        cleaned = cleaned.TrimEnd('.', ' ');
        if (cleaned.Length > 80)
            cleaned = cleaned[..80].TrimEnd('.', ' ');

        return cleaned;
    }

    private static string LogText(SceneRecord scene)
    {
        var text = new StringBuilder();
        var title = scene.Title?.Trim() ?? string.Empty;
        if (title.Length > 0 && !title.StartsWith("Scene · ", StringComparison.Ordinal))
            text.AppendLine(title);
        if (!string.IsNullOrWhiteSpace(scene.Location))
            text.AppendLine(scene.Location.Trim());
        text.AppendLine(scene.StartedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt"));
        if (!string.IsNullOrWhiteSpace(scene.Storyline))
            text.AppendLine(scene.Storyline.Trim());
        if (!string.IsNullOrWhiteSpace(scene.Description))
        {
            text.AppendLine();
            text.AppendLine(scene.Description.Trim());
        }

        foreach (var line in scene.Lines)
        {
            var spoken = line.Text?.Trim() ?? string.Empty;
            if (spoken.Length == 0)
                continue;

            text.AppendLine();
            var speaker = line.Speaker?.Trim() ?? string.Empty;
            if (speaker.Length == 0)
                text.Append(spoken);
            else if (line.Kind == LineKind.Emote)
                text.Append(speaker).Append(' ').Append(spoken);
            else
                text.Append(speaker).Append(": ").Append(spoken);
        }

        return text.ToString().Trim() + Environment.NewLine;
    }

    private void WriteText(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    private ulong FindProfile(string name, string world)
    {
        ulong found = 0;
        void Consider(NotebookFile? file)
        {
            if (found != 0 || file == null || file.ContentId == 0)
                return;

            if (SameProfile(file.CharacterName, file.World, name, world))
                found = file.ContentId;
        }

        Consider(this.current);
        Consider(this.session);
        if (!Directory.Exists(this.directory))
            return found;

        foreach (var path in Directory.GetFiles(this.directory, "*.json"))
        {
            if (found != 0)
                break;

            try
            {
                Consider(JsonConvert.DeserializeObject<NotebookFile>(File.ReadAllText(path), JsonSettings));
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Could not read a notebook at {Path}.", path);
            }
        }

        return found;
    }

    private void MoveHandProfile(ulong contentId, string name, string world)
    {
        if (contentId == 0 || IsHandId(contentId) || !Directory.Exists(this.directory))
            return;

        ulong handId = 0;
        foreach (var path in Directory.GetFiles(this.directory, "*.json"))
        {
            try
            {
                var file = JsonConvert.DeserializeObject<NotebookFile>(File.ReadAllText(path), JsonSettings);
                if (file == null || !IsHandId(file.ContentId))
                    continue;

                if (!SameProfile(file.CharacterName, file.World, name, world))
                    continue;

                handId = file.ContentId;
                break;
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Could not read a notebook at {Path}.", path);
            }
        }

        if (handId == 0 || File.Exists(this.PathFor(contentId)))
            return;

        NotebookFile? notebook = null;
        if (this.current?.ContentId == handId)
            notebook = this.current;
        else if (this.session?.ContentId == handId)
            notebook = this.session;
        else
        {
            try
            {
                notebook = JsonConvert.DeserializeObject<NotebookFile>(File.ReadAllText(this.PathFor(handId)), JsonSettings);
            }
            catch (Exception ex)
            {
                this.log.Warning(ex, "Could not move a notebook onto the logged-in character.");
                return;
            }
        }

        if (notebook == null)
            return;

        notebook.ContentId = contentId;
        notebook.CharacterName = name;
        notebook.World = world;
        this.Write(this.PathFor(contentId), notebook);
        var handPath = this.PathFor(handId);
        if (File.Exists(handPath))
            File.Delete(handPath);
    }

    private static bool SameProfile(string leftName, string leftWorld, string rightName, string rightWorld) =>
        string.Equals(PlayerNames.Clean(leftName), PlayerNames.Clean(rightName), StringComparison.OrdinalIgnoreCase)
        && string.Equals(leftWorld.Trim(), rightWorld.Trim(), StringComparison.OrdinalIgnoreCase);

    private static ulong HandId(string name, string world)
    {
        var text = PlayerNames.Clean(name).ToLowerInvariant() + "\n" + world.Trim().ToLowerInvariant();
        ulong hash = 14695981039346656037UL;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }

        if (hash == 0)
            hash = 1;

        return (hash & 0x3FFF_FFFF_FFFF_FFFFUL) | (1UL << 62);
    }

    private static bool IsHandId(ulong contentId) => (contentId & (1UL << 62)) != 0;

    private static NotebookFile CreateFile(ulong contentId, string name, string world) => new()
    {
        ContentId = contentId,
        CharacterName = name,
        World = world,
    };

    private static string DefaultTitle() => "Scene · " + DateTime.Now.ToString("MMM d, h:mm tt");
}
