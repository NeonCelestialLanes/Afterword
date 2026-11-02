using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using RPChronicles.Data;

namespace RPChronicles.Recording;

/// <summary>
/// Starts and stops a scene, and decides which chat lines belong in it.
/// Nothing is stored unless a scene is actively recording.
/// </summary>
internal sealed class SceneRecorder
{
    private readonly NotebookStore store;
    private readonly RecordingSettings settings;
    private readonly IObjectTable objects;
    private readonly IPlayerState playerState;
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private uint cachedTerritory = uint.MaxValue;
    private string cachedPlace = "Unknown location";
    private bool marksCleaned;
    private bool worldsApplied;

    public SceneRecorder(
        NotebookStore store,
        RecordingSettings settings,
        IObjectTable objects,
        IPlayerState playerState,
        IClientState clientState,
        IDataManager dataManager,
        IChatGui chat,
        IPluginLog log)
    {
        this.store = store;
        this.settings = settings;
        this.objects = objects;
        this.playerState = playerState;
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.chat = chat;
        this.log = log;

        if (playerState.IsLoaded)
            this.RememberCharacter();
    }

    public RecordingSettings Settings => this.settings;

    public string LogFolder => this.store.LogFolder;

    public string UsualLogFolder => this.store.UsualLogFolder;

    public bool IsUsualLogFolder => this.store.IsUsualLogFolder;

    public string SetLogFolder(string? folder)
    {
        var message = this.store.UseLogFolder(folder, out var changed);
        if (!changed)
            return message;

        this.settings.LogFolder = this.store.IsUsualLogFolder ? string.Empty : this.store.LogFolder;
        this.settings.Save();
        return message;
    }

    public string CurrentPlace
    {
        get
        {
            var territory = this.clientState.TerritoryType;
            if (territory == this.cachedTerritory)
                return this.cachedPlace;

            this.cachedTerritory = territory;
            this.cachedPlace = LocationNames.Describe(this.dataManager, this.clientState);
            return this.cachedPlace;
        }
    }

    public void OnLogin()
    {
        this.worldsApplied = false;
        this.RememberCharacter();
    }

    public void OnLogout()
    {
        if (this.store.MarkLoggedOut())
            this.chat.Print("Afterword paused. Log back in and press Resume to continue the scene.");
    }

    public IReadOnlyList<PlayerCharacter> Players() => this.store.ListPlayers();

    public void OpenNotebook(ulong contentId)
    {
        this.store.OpenNotebook(contentId);
        this.marksCleaned = false;
    }

    public ulong AddProfile(string name, string world) => this.store.AddProfile(name, world);

    public bool RemoveProfile(ulong contentId)
    {
        var removed = this.store.RemoveProfile(contentId);
        if (removed)
            this.marksCleaned = false;
        return removed;
    }

    public bool Start(string title, string place, bool everyone, IReadOnlyList<string> cast)
    {
        this.store.ShowLoggedIn();
        var where = string.IsNullOrWhiteSpace(place) ? this.CurrentPlace : place.Trim();
        var started = this.store.TryStart(title, where, everyone, cast, out _);
        if (!started)
            return false;

        this.chat.Print(everyone
            ? "Afterword is recording everyone in the chats you turned on."
            : "Afterword is recording the people you added to this scene.");
        this.log.Information("Started recording a scene.");
        return true;
    }

    public void Pause()
    {
        if (!this.store.Pause())
            return;

        this.chat.Print("Afterword paused.");
    }

    public void Resume()
    {
        if (!this.store.Resume())
            return;

        this.chat.Print("Afterword is recording again.");
    }

    public void Stop()
    {
        if (!this.store.Stop())
            return;

        this.chat.Print("Afterword saved the scene to your notebook.");
        this.log.Information("Saved a scene.");
    }

    public void Rename(Guid sceneId, string title) => this.store.Rename(sceneId, title);

    public SceneRecord ReadFile(string path)
    {
        this.EnsureNames();
        var scene = TranscriptImport.Read(path);
        foreach (var line in scene.Lines)
        {
            line.Speaker = PlayerNames.Clean(line.Speaker);
            line.Text = PlayerNames.DropTell(PlayerNames.StripLeadingMark(line.Text));
        }

        return scene;
    }

    public bool ImportScene(SceneRecord scene, out Guid sceneId)
    {
        sceneId = Guid.Empty;
        var saved = this.store.AddImportedScene(scene, out sceneId);
        if (saved)
            this.log.Information("Imported a scene titled {Title}.", scene.Title);

        return saved;
    }

    public void RemoveScene(Guid sceneId)
    {
        if (!this.store.RemoveScene(sceneId))
            return;

        this.chat.Print("Afterword removed that scene from your notebook.");
    }

    public void UpdateLine(Guid sceneId, Guid lineId, string speaker, string text) => this.store.UpdateLine(sceneId, lineId, speaker, text);

    public void RemoveLine(Guid sceneId, Guid lineId) => this.store.RemoveLine(sceneId, lineId);

    public bool SplitScene(Guid sceneId, int firstLine, out Guid newSceneId) => this.store.SplitScene(sceneId, firstLine, out newSceneId);

    public bool CombineScenes(Guid keepId, Guid otherId) => this.store.CombineScenes(keepId, otherId);

    public bool AddNote(out Guid id) => this.store.AddNote(out id);

    public void SetNoteTitle(Guid id, string title) => this.store.SetNoteTitle(id, title);

    public void SetNoteBody(Guid id, string body) => this.store.SetNoteBody(id, body);

    public void RemoveNote(Guid id) => this.store.RemoveNote(id);

    public bool AddStoryline(string title, out Guid id) => this.store.AddStoryline(title, out id);

    public void RenameStoryline(Guid id, string title) => this.store.RenameStoryline(id, title);

    public void SetStorylineDate(Guid id, DateTimeOffset when) => this.store.SetStorylineDate(id, when);

    public void SetStorylineNotes(Guid id, string notes) => this.store.SetStorylineNotes(id, notes);

    public string FillStorylineSummaryIfEmpty(Guid id) => this.store.FillStorylineSummaryIfEmpty(id);

    public string ReplaceStorylineSummary(Guid id) => this.store.ReplaceStorylineSummary(id);

    public void SetStorylineSummary(Guid id, string summary) => this.store.SetStorylineSummary(id, summary);

    public bool AddStorylineNote(Guid storyId, out Guid noteId) => this.store.AddStorylineNote(storyId, out noteId);

    public void SetStorylineNoteTitle(Guid storyId, Guid noteId, string title) => this.store.SetStorylineNoteTitle(storyId, noteId, title);

    public void SetStorylineNoteBody(Guid storyId, Guid noteId, string body) => this.store.SetStorylineNoteBody(storyId, noteId, body);

    public void RemoveStorylineNote(Guid storyId, Guid noteId) => this.store.RemoveStorylineNote(storyId, noteId);

    public void RemoveStoryline(Guid id) => this.store.RemoveStoryline(id);

    public bool AddWrittenStory(out Guid id) => this.store.AddWrittenStory(out id);

    public void SetWrittenStoryTitle(Guid id, string title) => this.store.SetWrittenStoryTitle(id, title);

    public void SetWrittenStoryBody(Guid id, string body) => this.store.SetWrittenStoryBody(id, body);

    public void SetWrittenStoryStoryline(Guid id, string title) => this.store.SetWrittenStoryStoryline(id, title);

    public void SetWrittenStoryLore(Guid id, bool affectsLore) => this.store.SetWrittenStoryLore(id, affectsLore);

    public void RemoveWrittenStory(Guid id) => this.store.RemoveWrittenStory(id);

    public void SetSceneStoryline(Guid sceneId, string title) => this.store.SetSceneStoryline(sceneId, title);

    public void SetSceneLocation(Guid sceneId, string location) => this.store.SetSceneLocation(sceneId, location);

    public bool AddWrittenStory(string title, string location, string storyline, string body, out Guid sceneId)
    {
        var scene = new SceneRecord
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Story" : title.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? "Off screen" : location.Trim(),
            Storyline = storyline.Trim(),
            Description = body.Trim(),
            StartedAt = DateTimeOffset.Now,
            EndedAt = DateTimeOffset.Now,
            Status = SceneStatus.Saved,
        };
        return this.store.AddImportedScene(scene, out sceneId);
    }

    public string FillSceneSummaryIfEmpty(Guid sceneId) => this.store.FillSceneSummaryIfEmpty(sceneId);

    public string ReplaceSceneSummary(Guid sceneId) => this.store.ReplaceSceneSummary(sceneId);

    public void SetSceneSummary(Guid sceneId, string summary) => this.store.SetSceneSummary(sceneId, summary);

    public void SetSceneNotes(Guid sceneId, string notes) => this.store.SetSceneNotes(sceneId, notes);

    public bool AddCharacter(string name) => this.store.AddCharacter(name);

    public void SetCharacterNotes(string name, string notes) => this.store.SetCharacterNotes(name, notes);

    public string FillCharacterSummaryIfEmpty(string name) => this.store.FillCharacterSummaryIfEmpty(name);

    public string ReplaceCharacterSummary(string name) => this.store.ReplaceCharacterSummary(name);

    public void SetCharacterSummary(string name, string summary) => this.store.SetCharacterSummary(name, summary);

    public void RenameCharacter(string from, string to) => this.store.RenameCharacter(from, to);

    public void RemoveCharacter(string name) => this.store.RemoveCharacter(name);

    public void SetEveryone(bool everyone) => this.store.SetEveryone(everyone);

    public void IncludeSpeaker(string name, bool include) => this.store.IncludeSpeaker(name, include);

    public IReadOnlyList<string> NearbyNames()
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var player in this.objects.PlayerObjects)
            {
                var name = PlayerNames.Clean(player.Name.TextValue ?? string.Empty);
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;

                found.Add(name);
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Could not list nearby players.");
        }

        return found;
    }

    public void Capture(XivChatType type, string speaker, string text, int timestamp)
    {
        if (type == XivChatType.RandomNumber)
        {
            if (!this.settings.Allows(ChatChannel.Random))
                return;

            var self = this.playerState.IsLoaded ? this.playerState.CharacterName ?? string.Empty : string.Empty;
            if (!RandomRolls.TryRead(speaker, text, self, out var roller, out var rolled))
                return;

            this.store.AppendLine(new SceneLine
            {
                At = FromGameTime(timestamp),
                Speaker = roller,
                Channel = "Random rolls",
                Kind = LineKind.Emote,
                Text = rolled,
            });
            return;
        }

        if (!SupportedChat.TryClassify(type, out var channel) || !this.settings.Allows(channel))
            return;

        var spoken = text.Trim();
        if (this.settings.SkipOutOfCharacter && !OocFilter.TryKeep(spoken, out spoken))
            return;

        if (string.IsNullOrWhiteSpace(spoken))
            return;

        var name = PlayerNames.Clean(speaker);
        if (string.IsNullOrWhiteSpace(name) && this.playerState.IsLoaded)
            name = PlayerNames.Clean(this.playerState.CharacterName ?? string.Empty);

        this.store.AppendLine(new SceneLine
        {
            At = FromGameTime(timestamp),
            Speaker = name,
            Channel = SupportedChat.Label(channel),
            Kind = SupportedChat.KindFor(channel),
            Text = PlayerNames.DropTell(PlayerNames.StripLeadingMark(spoken)),
        });
    }

    public void Flush() => this.store.Flush();

    public NotebookSnapshot Snapshot()
    {
        this.EnsureNames();
        this.RememberCharacter();
        return this.store.Snapshot();
    }

    private void EnsureNames()
    {
        if (!this.worldsApplied)
        {
            var loaded = PlayerNames.LoadWorlds(this.dataManager);
            if (loaded.Count > 0)
            {
                PlayerNames.RememberWorlds(loaded);
                this.worldsApplied = true;
                this.marksCleaned = false;
            }
        }

        if (this.marksCleaned)
            return;

        this.store.CleanSpeakers();
        this.marksCleaned = true;
    }

    private void RememberCharacter()
    {
        if (!this.playerState.IsLoaded || this.playerState.ContentId == 0)
            return;

        var name = PlayerNames.Clean(this.playerState.CharacterName ?? string.Empty);
        var world = LocationNames.HomeWorldName(this.playerState);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world))
            return;

        try
        {
            if (this.objects.LocalPlayer is not IPlayerCharacter local)
                return;

            var localName = PlayerNames.Clean(local.Name.TextValue ?? string.Empty);
            var localWorld = LocationNames.HomeWorldName(local);
            if (!string.Equals(localName, name, StringComparison.Ordinal)
                || !string.Equals(localWorld, world, StringComparison.Ordinal))
                return;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Could not read the current character.");
            return;
        }

        this.store.LoadCharacter(this.playerState.ContentId, name, world);
    }

    private static DateTimeOffset FromGameTime(int timestamp)
    {
        if (timestamp <= 1_000_000_000)
            return DateTimeOffset.Now;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
        }
        catch
        {
            return DateTimeOffset.Now;
        }
    }
}
