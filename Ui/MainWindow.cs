using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RPChronicles.Data;
using RPChronicles.Insights;
using RPChronicles.Recording;

namespace RPChronicles.Ui;

internal sealed class MainWindow : Window, IDisposable
{
    private readonly SceneRecorder recorder;
    private readonly SummarySettings summarySettings;
    private readonly ChatGptSession chatGpt;
    private readonly GrokSession grok;
    private readonly ISharedImmediateTexture? logo;
    private readonly IFontHandle? pageFont;
    private readonly IFontHandle? navFont;
    private Page page = Page.NewScene;
    private string titleDraft = string.Empty;
    private string renameDraft = string.Empty;
    private Guid renameSceneId;
    private Guid? openSceneId;
    private Guid? openNoteId;
    private Guid? pendingDelete;
    private string noteTitleDraft = string.Empty;
    private string noteBodyDraft = string.Empty;
    private Guid noteEditId;
    private Guid scrolledSceneId;
    private int scrolledLineCount;
    private bool draftEveryone = true;
    private readonly List<string> draftCast = new();
    private string draftName = string.Empty;
    private readonly FileDialogManager dialogs = new();
    private string importMessage = string.Empty;
    private SceneRecord? pendingImport;
    private readonly List<string> importKeep = new();
    private bool importKeepUnnamed;
    private string importStoryline = string.Empty;
    private string playerName = string.Empty;
    private string sceneSummaryDraft = string.Empty;
    private Guid sceneSummaryId;
    private string storylineSummaryDraft = string.Empty;
    private string characterSummaryDraft = string.Empty;
    private Guid? openStorylineNoteId;
    private string storylineNoteTitleDraft = string.Empty;
    private string storylineNoteBodyDraft = string.Empty;
    private Guid storylineNoteEditId;
    private string notebookSearch = string.Empty;
    private bool scenesNewestFirst = true;
    private string newStoryline = string.Empty;
    private string storylineTitleDraft = string.Empty;
    private string storylineNotesDraft = string.Empty;
    private Guid storylineEditId;
    private Guid? openStorylineId;
    private Guid? openWrittenStoryId;
    private string writtenTitleDraft = string.Empty;
    private string writtenBodyDraft = string.Empty;
    private Guid writtenEditId;
    private bool writtenDeleteArmed;
    private string newCharacter = string.Empty;
    private string characterNotesDraft = string.Empty;
    private string characterEditName = string.Empty;
    private string characterNameDraft = string.Empty;
    private string combineWith = string.Empty;
    private bool characterDeleteArmed;
    private Guid editSceneId;
    private Guid? editingLineId;
    private string lineSpeakerDraft = string.Empty;
    private string lineTextDraft = string.Empty;
    private Guid? pendingLineDelete;
    private string splitTimeDraft = string.Empty;
    private string lineMessage = string.Empty;
    private string logFolderMessage = string.Empty;
    private string openCharacter = string.Empty;
    private string placeDraft = string.Empty;
    private string scratchDraft = string.Empty;
    private int scratchChannel = 2;
    private int scratchNext;
    private string scratchNotice = string.Empty;
    private string scratchKey = string.Empty;
    private static readonly (string Label, string Header)[] ScratchChannels =
    [
        ("No command", ""),
        ("Say", "/s"),
        ("Emote", "/em"),
        ("Party", "/p"),
        ("Alliance", "/a"),
        ("Free Company", "/fc"),
        ("Shout", "/sh"),
        ("Yell", "/y"),
        ("Echo", "/e"),
    ];
    private bool styled;
    private int pushedColors;
    private int pushedVars;
    private readonly object summaryGate = new();
    private string summaryTarget = string.Empty;
    private string summaryPayload = string.Empty;
    private string summaryInstruction = string.Empty;
    private string summaryStatus = string.Empty;
    private bool summaryAsk;
    private bool summaryBusy;
    private int summaryGeneration;
    private string? pendingSummary;
    private string? pendingError;
    private int pendingGeneration = -1;
    private string pendingTarget = string.Empty;
    private string? summaryEditId;
    private string summaryEditDraft = string.Empty;
    private bool summaryEditOpened;

    public MainWindow(
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textures,
        SceneRecorder recorder,
        SummarySettings summarySettings,
        ChatGptSession chatGpt,
        GrokSession grok)
        : base("Afterword")
    {
        this.recorder = recorder;
        this.summarySettings = summarySettings;
        this.chatGpt = chatGpt;
        this.grok = grok;
        this.logo = LoadLogo(pluginInterface, textures);
        this.pageFont = CreatePageFont(pluginInterface);
        this.navFont = CreateNavFont(pluginInterface);
        this.Size = new Vector2(980, 660);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(740, 480),
            MaximumSize = new Vector2(1680, 1200),
        };
        this.AllowPinning = true;
    }

    internal void OpenSettings()
    {
        this.page = Page.Settings;
        this.IsOpen = true;
    }

    public void Dispose()
    {
        this.pageFont?.Dispose();
        this.navFont?.Dispose();
    }

    public override void PreDraw()
    {
        this.PushColor(ImGuiCol.WindowBg, JournalTheme.Cover);
        this.PushColor(ImGuiCol.ChildBg, JournalTheme.Paper);
        this.PushColor(ImGuiCol.TitleBg, JournalTheme.Cover);
        this.PushColor(ImGuiCol.TitleBgActive, JournalTheme.Cover);
        this.PushColor(ImGuiCol.Text, JournalTheme.CoverInk);
        this.PushColor(ImGuiCol.Button, JournalTheme.Cover);
        this.PushColor(ImGuiCol.ButtonHovered, JournalTheme.CoverHover);
        this.PushColor(ImGuiCol.ButtonActive, JournalTheme.CoverDeep);
        this.PushColor(ImGuiCol.FrameBg, new Vector4(1f, 0.992f, 0.965f, 1f));
        this.PushColor(ImGuiCol.FrameBgHovered, new Vector4(1f, 0.976f, 0.94f, 1f));
        this.PushColor(ImGuiCol.FrameBgActive, JournalTheme.PaperInset);
        this.PushColor(ImGuiCol.CheckMark, JournalTheme.Margin);
        this.PushColor(ImGuiCol.Border, new Vector4(0.141f, 0.118f, 0.094f, 0.28f));
        this.PushColor(ImGuiCol.Separator, JournalTheme.Rule);
        this.PushColor(ImGuiCol.Header, JournalTheme.CoverSelect);
        this.PushColor(ImGuiCol.HeaderHovered, new Vector4(0.957f, 0.937f, 0.902f, 0.24f));
        this.PushColor(ImGuiCol.HeaderActive, JournalTheme.CoverSelect);
        this.PushVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
        this.PushVar(ImGuiStyleVar.FrameRounding, 3f);
        this.PushVar(ImGuiStyleVar.WindowRounding, 8f);
        this.PushVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        this.PushVar(ImGuiStyleVar.FrameBorderSize, 1f);
        this.PushVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        this.styled = true;
    }

    public override void PostDraw()
    {
        if (!this.styled)
            return;

        ImGui.PopStyleVar(this.pushedVars);
        ImGui.PopStyleColor(this.pushedColors);
        this.pushedVars = 0;
        this.pushedColors = 0;
        this.styled = false;
    }

    private void PushColor(ImGuiCol color, Vector4 value)
    {
        ImGui.PushStyleColor(color, value);
        this.pushedColors++;
    }

    private void PushVar(ImGuiStyleVar style, Vector2 value)
    {
        ImGui.PushStyleVar(style, value);
        this.pushedVars++;
    }

    private void PushVar(ImGuiStyleVar style, float value)
    {
        ImGui.PushStyleVar(style, value);
        this.pushedVars++;
    }

    public override void Draw()
    {
        this.ApplyPendingSummary();
        var snapshot = this.recorder.Snapshot();
        DrawCoverWash();
        this.DrawSidebar();
        ImGui.SameLine(0, 12);

        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail();
        var pageSize = new Vector2(Math.Max(80, available.X - 8), Math.Max(80, available.Y - 8));
        DrawPageDeck(origin, pageSize);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28, 24));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, JournalTheme.Paper);
        var pageOpen = ImGui.BeginChild("page", pageSize, false);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        if (pageOpen)
        {
            DrawGutter();
            var contentX = 36f;
            var indent = Math.Max(0f, contentX - ImGui.GetCursorPosX());
            if (indent > 0f)
                ImGui.Indent(indent);

            var pageFontOn = this.TryPushPageFont();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
            this.playerName = snapshot.CharacterName;
            this.DrawBanner(snapshot);
            switch (this.page)
            {
                case Page.NewScene:
                    this.DrawNewScene(snapshot);
                    break;
                case Page.Notebook:
                    this.DrawNotebook(snapshot);
                    break;
                case Page.Import:
                    this.DrawImport(snapshot);
                    break;
                case Page.Characters:
                    this.DrawCharacters(snapshot);
                    break;
                case Page.Storylines:
                    this.DrawStorylines(snapshot);
                    break;
                case Page.Stories:
                    this.DrawStories(snapshot);
                    break;
                case Page.Scratchpad:
                    this.DrawScratchpad();
                    break;
                case Page.Settings:
                    this.DrawSettings();
                    break;
            }

            ImGui.PopStyleColor();
            if (pageFontOn)
                this.pageFont!.Pop();
            if (indent > 0f)
                ImGui.Unindent(indent);
        }

        ImGui.EndChild();
    }

    private static void DrawCoverWash()
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var max = pos + size;
        draw.AddRectFilledMultiColor(
            pos,
            new Vector2(max.X, pos.Y + (size.Y * 0.20f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));
        draw.AddRectFilledMultiColor(
            pos,
            max,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.07f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.18f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.04f)));
    }

    private static void DrawPageDeck(Vector2 origin, Vector2 pageSize)
    {
        var draw = ImGui.GetWindowDrawList();
        for (var layer = 3; layer >= 1; layer--)
        {
            var spread = 2.5f * layer;
            var alpha = 0.05f * layer;
            draw.AddRectFilled(
                origin + new Vector2(spread, spread + 1),
                origin + pageSize + new Vector2(spread + 1, spread + 2),
                ImGui.GetColorU32(new Vector4(JournalTheme.Shadow.X, JournalTheme.Shadow.Y, JournalTheme.Shadow.Z, alpha)),
                2f);
        }

        for (var sheet = 3; sheet >= 1; sheet--)
        {
            var shift = 2f * sheet;
            var tone = sheet == 1 ? JournalTheme.PaperEdge : JournalTheme.PaperInset;
            draw.AddRectFilled(
                origin + new Vector2(shift, shift),
                origin + pageSize + new Vector2(shift, shift),
                ImGui.GetColorU32(tone),
                2f);
        }
    }

    private static void DrawGutter()
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        for (var x = 0; x < 16; x++)
        {
            var alpha = 0.16f * (1f - (x / 16f));
            draw.AddRectFilled(
                pos + new Vector2(x, 0),
                pos + new Vector2(x + 1, size.Y),
                ImGui.GetColorU32(new Vector4(0.06f, 0.10f, 0.16f, alpha)));
        }

        draw.AddLine(
            pos + new Vector2(20, 10),
            pos + new Vector2(20, size.Y - 10),
            ImGui.GetColorU32(JournalTheme.Margin),
            1.25f);
    }

    private void DrawLogo()
    {
        var wrap = this.logo?.GetWrapOrEmpty();
        var avail = ImGui.GetContentRegionAvail().X;
        var width = Math.Min(avail, 148f);
        var height = 42f;
        var ready = wrap is not null && wrap.Handle != nint.Zero && wrap.Width > 0 && wrap.Height > 0;
        if (ready)
            height = width * wrap!.Height / wrap.Width;

        if (ready)
            ImGui.Image(wrap!.Handle, new Vector2(width, height));

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.CoverMuted);
        ImGui.SetWindowFontScale(0.82f);
        ImGui.TextUnformatted("a private journal");
        ImGui.SetWindowFontScale(1f);
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 6));
        var draw = ImGui.GetWindowDrawList();
        var line = ImGui.GetCursorScreenPos();
        var lineWidth = ImGui.GetContentRegionAvail().X;
        draw.AddLine(line, line + new Vector2(lineWidth, 0), ImGui.GetColorU32(JournalTheme.CoverLine), 1f);
        ImGui.Dummy(new Vector2(0, 8));
    }

    private static ISharedImmediateTexture? LoadLogo(IDalamudPluginInterface pluginInterface, ITextureProvider textures)
    {
        var directory = pluginInterface.AssemblyLocation.DirectoryName;
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        var path = Path.Combine(directory, "Assets", "afterword-logo.png");
        return File.Exists(path) ? textures.GetFromFile(path) : null;
    }

    private void DrawSidebar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 2f));
        ImGui.BeginChild("nav", new Vector2(188, 0), false);
        var navFontOn = this.TryPushNavFont();
        this.DrawLogo();
        this.NavItem("New Scene", Page.NewScene);
        this.NavItem("Import Scene", Page.Import);
        this.NavItem("My Notebook", Page.Notebook);
        this.NavItem("Characters", Page.Characters);
        this.NavItem("Storylines", Page.Storylines);
        this.NavItem("Stories", Page.Stories);
        this.NavItem("Scratchpad", Page.Scratchpad);
        this.NavItem("Settings", Page.Settings);
        if (navFontOn)
            this.navFont!.Pop();
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void NavItem(string label, Page target)
    {
        var selected = this.page == target;
        var width = Math.Max(32f, ImGui.GetContentRegionAvail().X);
        var pressed = ImGui.InvisibleButton(label, new Vector2(width, 38));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        if (selected || hovered)
        {
            var barMin = min + new Vector2(1f, 1f);
            var barMax = max - new Vector2(1f, 1f);
            var top = selected ? 0.16f : 0.10f;
            var bottom = selected ? 0.07f : 0.04f;
            draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, bottom)), 4f);
            draw.PushClipRect(barMin + new Vector2(3f, 1f), barMax - new Vector2(3f, 1f), true);
            draw.AddRectFilledMultiColor(
                barMin,
                barMax,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, top)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, top)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, bottom)),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, bottom)));
            draw.PopClipRect();
            draw.AddLine(
                barMin + new Vector2(4f, 1f),
                new Vector2(barMax.X - 4f, barMin.Y + 1f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, selected ? 0.22f : 0.12f)),
                1f);
            draw.AddLine(
                new Vector2(barMin.X + 4f, barMax.Y - 1f),
                new Vector2(barMax.X - 4f, barMax.Y - 1f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, selected ? 0.22f : 0.10f)),
                1f);
        }

        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(min.X + 8f, min.Y + ((38f - textSize.Y) * 0.5f));
        draw.AddText(textPos, ImGui.GetColorU32(JournalTheme.CoverInk), label);

        if (!pressed)
            return;

        this.page = target;
        if (target != Page.Notebook)
        {
            this.openSceneId = null;
            this.openNoteId = null;
        }
        if (target != Page.Storylines)
            this.openStorylineId = null;
        if (target != Page.Stories)
        {
            this.openWrittenStoryId = null;
            this.writtenDeleteArmed = false;
        }
        if (target != Page.Characters)
            this.openCharacter = string.Empty;
    }

    private void DrawCharacterMenu(NotebookSnapshot snapshot)
    {
        var players = this.recorder.Players();
        if (players.Count == 0)
            return;

        var shown = string.IsNullOrWhiteSpace(snapshot.CharacterLabel) ? "Choose a character" : snapshot.CharacterLabel;
        ImGui.SetNextItemWidth(280);
        PushPaperPopup();
        if (ImGui.BeginCombo("##your-character", shown))
        {
            foreach (var player in players)
            {
                var chosen = player.ContentId == snapshot.ContentId;
                if (ImGui.Selectable(player.Label + "##" + player.ContentId, chosen))
                {
                    this.recorder.OpenNotebook(player.ContentId);
                    this.CloseOpenPages();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleColor(5);
        ImGui.Dummy(new Vector2(0, 8));
    }

    private void CloseOpenPages()
    {
        this.openSceneId = null;
        this.openNoteId = null;
        this.openStorylineId = null;
        this.openStorylineNoteId = null;
        this.openWrittenStoryId = null;
        this.openCharacter = string.Empty;
        this.writtenDeleteArmed = false;
        this.characterDeleteArmed = false;
    }

    private void DrawBanner(NotebookSnapshot snapshot)
    {
        this.DrawCharacterMenu(snapshot);
        if (!snapshot.IsLoggedIn)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(snapshot.CharacterLabel)
                ? "Log into a character to start a scene. Nothing is recorded until you press Start."
                : $"Logged out. This is {snapshot.CharacterLabel}'s notebook. Recording is paused.");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }
        else if (!snapshot.IsOwnNotebook && !string.IsNullOrWhiteSpace(snapshot.LoggedInLabel))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped($"Logged in as {snapshot.LoggedInLabel}. You can read and edit this notebook. A new scene is recorded on {snapshot.LoggedInName}.");
            ImGui.PopStyleColor();
        }

        var active = snapshot.ActiveScene;
        if (active == null)
            return;

        DrawStatusDot(active.Status);
        ImGui.SameLine(0, 8);
        ImGui.TextUnformatted(active.Status == SceneStatus.Recording ? "Recording" : "Paused");
        ImGui.SameLine(0, 12);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextUnformatted(active.Title);
        ImGui.PopStyleColor();
        ImGui.Separator();
    }

    public void DrawDialogs() => this.dialogs.Draw();

    private void DrawImport(NotebookSnapshot snapshot)
    {
        if (this.pendingImport != null && string.IsNullOrEmpty(this.importStoryline))
        {
            this.DrawImportPicker(snapshot);
            return;
        }

        ImGui.TextUnformatted("Import scene");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Open a text file on this computer. A line can look like [say] Alyra Voss: hello, or [emote] Corvin Hale glances over. You choose who to keep before it is saved.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        if (CoverButton("Open a file", new Vector2(160, 32)))
            this.OpenImport(string.Empty);

        if (!snapshot.IsLoggedIn)
        {
            ImGui.EndDisabled();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Log into a character first. The imported scene is saved with that character.");
            ImGui.PopStyleColor();
        }

        if (!string.IsNullOrWhiteSpace(this.importMessage))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(this.importMessage);
        }
    }

    private void OnLogFolderChosen(bool selected, string path)
    {
        if (!selected || string.IsNullOrWhiteSpace(path))
            return;

        this.logFolderMessage = this.recorder.SetLogFolder(path);
    }

    private void OpenImport(string storyline)
    {
        this.importStoryline = storyline;
        this.dialogs.OpenFileDialog(
            "Open a transcript",
            "Text{.txt,.md,.log}",
            this.OnFileChosen);
    }

    private void OnFileChosen(bool selected, string path)
    {
        if (!selected || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var scene = this.recorder.ReadFile(path);
            if (!string.IsNullOrWhiteSpace(this.importStoryline))
                scene.Storyline = this.importStoryline;

            this.pendingImport = scene;
            this.importKeep.Clear();
            var named = false;
            foreach (var line in scene.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Speaker))
                    continue;

                named = true;
                if (!this.importKeep.Exists(name => SamePerson(name, line.Speaker)))
                    this.importKeep.Add(line.Speaker.Trim());
            }

            this.importKeepUnnamed = !named;
            this.importMessage = string.Empty;
        }
        catch (Exception)
        {
            this.importMessage = "That file could not be read. Choose a plain text file.";
            this.pendingImport = null;
        }
    }

    private void DrawImportPicker(NotebookSnapshot snapshot)
    {
        var scene = this.pendingImport;
        if (scene == null)
            return;

        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(scene.Title) ? "Imported scene" : scene.Title);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        var where = string.IsNullOrWhiteSpace(this.importStoryline)
            ? "Uncheck anyone you want left out. Only the checked names are saved."
            : $"These lines will be saved under {this.importStoryline}. Uncheck anyone you want left out.";
        ImGui.TextWrapped(where);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var shown = new List<string>();
        foreach (var line in scene.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Speaker))
                continue;

            if (shown.Exists(name => SamePerson(name, line.Speaker)))
                continue;

            shown.Add(line.Speaker.Trim());
        }

        if (shown.Count == 0 && this.CountSpeaker(scene, string.Empty) == 0)
        {
            ImGui.TextUnformatted("This file had no lines.");
        }

        foreach (var name in shown)
        {
            var kept = this.importKeep.Exists(picked => SamePerson(picked, name));
            var label = SamePerson(name, snapshot.CharacterName) ? $"{name}  ·  you" : name;
            ImGui.PushID(name);
            if (ImGui.Checkbox(label, ref kept))
            {
                if (kept)
                    this.importKeep.Add(name);
                else
                    this.importKeep.RemoveAll(picked => SamePerson(picked, name));
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted($"{this.CountSpeaker(scene, name)} lines");
            ImGui.PopStyleColor();
            ImGui.PopID();
        }

        var unnamed = this.CountSpeaker(scene, string.Empty);
        if (unnamed > 0)
        {
            var keep = this.importKeepUnnamed;
            if (ImGui.Checkbox($"Lines with no name  ·  {unnamed}", ref keep))
                this.importKeepUnnamed = keep;
        }

        ImGui.Spacing();
        if (CoverButton("Save these lines", new Vector2(180, 32)))
            this.SaveImport();

        ImGui.SameLine();
        if (CoverButton("Cancel", new Vector2(100, 32)))
        {
            this.pendingImport = null;
            this.importStoryline = string.Empty;
            this.importMessage = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(this.importMessage))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted(this.importMessage);
        }
    }

    private void SaveImport()
    {
        var scene = this.pendingImport;
        if (scene == null)
            return;

        var kept = new List<SceneLine>();
        foreach (var line in scene.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Speaker))
            {
                if (this.importKeepUnnamed)
                    kept.Add(line);
                continue;
            }

            if (this.importKeep.Exists(name => SamePerson(name, line.Speaker)))
                kept.Add(line);
        }

        if (kept.Count == 0)
        {
            this.importMessage = "Choose at least one person.";
            return;
        }

        scene.Lines = kept;
        scene.SelectedSpeakers = new List<string>(this.importKeep);
        scene.Mode = RecordingMode.SelectedCharacters;
        scene.Summary = string.Empty;
        if (!this.recorder.ImportScene(scene, out var sceneId))
        {
            this.importMessage = "Log into a character, then try again.";
            return;
        }

        this.pendingImport = null;
        this.importStoryline = string.Empty;
        this.importMessage = string.Empty;
        this.openSceneId = sceneId;
        this.page = Page.Notebook;
    }

    private int CountSpeaker(SceneRecord scene, string speaker)
    {
        var count = 0;
        foreach (var line in scene.Lines)
        {
            if (string.IsNullOrWhiteSpace(speaker))
            {
                if (string.IsNullOrWhiteSpace(line.Speaker))
                    count++;
            }
            else if (SamePerson(speaker, line.Speaker))
            {
                count++;
            }
        }

        return count;
    }

    private static bool SamePerson(string left, string right)
    {
        left = PlayerNames.Clean(left);
        right = PlayerNames.Clean(right);
        if (left.Length == 0 || right.Length == 0)
            return false;

        return left.Equals(right, StringComparison.OrdinalIgnoreCase)
            || left.StartsWith(right + " ", StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left + " ", StringComparison.OrdinalIgnoreCase);
    }

    private void DrawNewScene(NotebookSnapshot snapshot)
    {
        var active = snapshot.ActiveScene;
        if (active != null)
        {
            this.DrawActiveScene(active, snapshot.IsLoggedIn && snapshot.IsOwnNotebook);
            return;
        }

        ImGui.TextUnformatted("New scene");
        ImGui.Spacing();
        ImGui.TextUnformatted("Title");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##new-title", ref this.titleDraft, 120);
        ImGui.Spacing();
        ImGui.TextUnformatted("Place");
        ImGui.SetNextItemWidth(-1);
        if (string.IsNullOrWhiteSpace(this.placeDraft))
            this.placeDraft = this.recorder.CurrentPlace;
        ImGui.InputTextWithHint("##new-place", "A letter, a dream, or where you are", ref this.placeDraft, 80);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextUnformatted("Only the chats turned on in Settings are kept, and nothing from before you press Start.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        this.DrawCast(live: false, scene: null);

        var canStart = snapshot.IsLoggedIn && snapshot.IsOwnNotebook;
        if (!canStart)
            ImGui.BeginDisabled();

        if (CoverButton("Start recording", new Vector2(180, 32)))
        {
            if (this.recorder.Start(this.titleDraft, this.placeDraft, this.draftEveryone, this.draftCast))
            {
                this.titleDraft = string.Empty;
                this.placeDraft = string.Empty;
                this.draftCast.Clear();
                this.draftEveryone = true;
            }
        }

        if (!canStart)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(snapshot.IsLoggedIn
                    ? $"Log into {snapshot.CharacterName} to record a new scene."
                    : "Log into a character first.");
        }
    }

    private void DrawActiveScene(SceneRecord scene, bool loggedIn)
    {
        if (this.renameSceneId != scene.Id)
        {
            this.renameSceneId = scene.Id;
            this.renameDraft = scene.Title;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##active-title", ref this.renameDraft, 120))
            this.recorder.Rename(scene.Id, this.renameDraft);

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextUnformatted($"{scene.StartedAt.ToLocalTime():MMM d, yyyy · h:mm tt}  ·  {scene.Location}");
        if (scene.Interrupted)
            ImGui.TextUnformatted("The game closed while this was recording. Every line saved before that is still here.");
        ImGui.PopStyleColor();
        this.DrawStorylineField(scene);
        ImGui.Spacing();

        if (scene.Status == SceneStatus.Recording)
        {
            if (CoverButton("Pause", new Vector2(120, 30)))
                this.recorder.Pause();
        }
        else if (loggedIn)
        {
            if (CoverButton("Resume", new Vector2(120, 30)))
                this.recorder.Resume();
        }
        else
        {
            ImGui.BeginDisabled();
            CoverButton("Resume", new Vector2(120, 30));
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (CoverButton("Stop and save", new Vector2(140, 30)))
        {
            var id = scene.Id;
            this.recorder.Stop();
            this.openSceneId = id;
            this.page = Page.Notebook;
            return;
        }

        ImGui.Spacing();
        this.DrawCast(live: true, scene: scene);
        ImGui.Spacing();
        this.DrawTranscript(scene);
    }

    private void DrawNotebook(NotebookSnapshot snapshot)
    {
        if (this.openNoteId != null)
        {
            JournalNote? note = null;
            foreach (var candidate in snapshot.Notes)
            {
                if (candidate.Id == this.openNoteId)
                {
                    note = candidate;
                    break;
                }
            }

            if (note == null)
            {
                this.openNoteId = null;
            }
            else
            {
                this.DrawOpenNote(note);
                return;
            }
        }

        if (this.openSceneId != null)
        {
            SceneRecord? selected = null;
            foreach (var scene in snapshot.Scenes)
            {
                if (scene.Id == this.openSceneId)
                {
                    selected = scene;
                    break;
                }
            }

            if (selected == null)
            {
                this.openSceneId = null;
            }
            else
            {
                if (CoverButton("Back to notebook", new Vector2(160, 30)))
                {
                    this.openSceneId = null;
                    return;
                }

                ImGui.Spacing();
                if (selected.Status is SceneStatus.Recording or SceneStatus.Paused)
                {
                    this.DrawActiveScene(selected, snapshot.IsLoggedIn && snapshot.IsOwnNotebook);
                    return;
                }

                this.DrawSavedScene(selected);
                return;
            }
        }

        ImGui.TextUnformatted("My notebook");
        ImGui.Spacing();
        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        if (CoverButton("New note", new Vector2(140, 32)) && this.recorder.AddNote(out var noteId))
            this.openNoteId = noteId;

        if (!snapshot.IsLoggedIn)
        {
            ImGui.EndDisabled();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Log into a character first. The note is saved with that character.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        if (snapshot.Notes.Count == 0 && snapshot.Scenes.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No notes or scenes yet. Write a note here, or start one from New Scene.");
            ImGui.PopStyleColor();
            return;
        }

        this.DrawListTools();
        var keyword = this.notebookSearch.Trim();
        var notes = new List<JournalNote>();
        foreach (var note in snapshot.Notes)
        {
            if (MatchesKeyword(keyword, note.Title, note.Body))
                notes.Add(note);
        }

        notes.Sort((a, b) => this.CompareWhen(a.WrittenAt, b.WrittenAt));
        var scenes = new List<SceneRecord>();
        foreach (var scene in snapshot.Scenes)
        {
            if (SceneMatches(scene, keyword))
                scenes.Add(scene);
        }

        scenes.Sort((a, b) => this.CompareWhen(a.StartedAt, b.StartedAt));
        if (keyword.Length > 0 && notes.Count == 0 && scenes.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Nothing has that word.");
            ImGui.PopStyleColor();
            return;
        }

        if (notes.Count > 0)
        {
            ImGui.TextUnformatted("Notes");
            foreach (var note in notes)
            {
                ImGui.PushID(note.Id.ToString());
                var width = Math.Max(120f, ImGui.GetContentRegionAvail().X - 96f);
                if (CoverButton(note.Title, new Vector2(width, 32)))
                    this.openNoteId = note.Id;
                ImGui.SameLine();
                if (this.ConfirmDelete(note.Id))
                {
                    this.recorder.RemoveNote(note.Id);
                    if (this.openNoteId == note.Id)
                        this.openNoteId = null;
                }

                ImGui.PopID();
                ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
                ImGui.TextUnformatted($"{note.WrittenAt.ToLocalTime():MMM d, yyyy · h:mm tt}   ·   Note");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }

        if (scenes.Count == 0)
            return;

        ImGui.TextUnformatted("Scenes");
        foreach (var scene in scenes)
        {
            var subtitle = $"{scene.StartedAt.ToLocalTime():MMM d, yyyy · h:mm tt}   ·   {scene.Location}   ·   {LineCount(scene)}   ·   {StatusLabel(scene)}";
            ImGui.PushID(scene.Id.ToString());
            var width = Math.Max(120f, ImGui.GetContentRegionAvail().X - 96f);
            if (CoverButton(scene.Title, new Vector2(width, 32)))
                this.openSceneId = scene.Id;
            ImGui.SameLine();
            if (this.ConfirmDelete(scene.Id))
            {
                this.recorder.RemoveScene(scene.Id);
                if (this.openSceneId == scene.Id)
                    this.openSceneId = null;
            }

            ImGui.PopID();

            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted(subtitle);
            if (!string.IsNullOrWhiteSpace(scene.Summary))
                ImGui.TextWrapped(scene.Summary);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawOpenNote(JournalNote note)
    {
        if (this.noteEditId != note.Id)
        {
            this.noteEditId = note.Id;
            this.noteTitleDraft = note.Title;
            this.noteBodyDraft = note.Body;
        }

        if (CoverButton("Back to notebook", new Vector2(160, 30)))
        {
            this.openNoteId = null;
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Title");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##note-title", ref this.noteTitleDraft, 120))
            this.recorder.SetNoteTitle(note.Id, this.noteTitleDraft);

        ImGui.Spacing();
        ImGui.TextUnformatted("Note");
        if (ImGui.InputTextMultiline("##note-body", ref this.noteBodyDraft, 8000, new Vector2(-1, 220)))
            this.recorder.SetNoteBody(note.Id, this.noteBodyDraft);

        ImGui.Spacing();
        if (this.ConfirmDelete(note.Id))
        {
            this.recorder.RemoveNote(note.Id);
            this.openNoteId = null;
        }
    }

    private void DrawSavedScene(SceneRecord scene)
    {
        if (this.renameSceneId != scene.Id)
        {
            this.renameSceneId = scene.Id;
            this.renameDraft = scene.Title;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##saved-title", ref this.renameDraft, 120))
            this.recorder.Rename(scene.Id, this.renameDraft);

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        var ended = scene.EndedAt?.ToLocalTime().ToString("h:mm tt");
        var when = scene.StartedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt");
        ImGui.TextUnformatted(ended == null ? $"{when}  ·  {scene.Location}" : $"{when} – {ended}  ·  {scene.Location}");
        ImGui.PopStyleColor();
        this.DrawStorylineField(scene);
        this.DrawSummary(
            "scene:" + scene.Id,
            ref this.sceneSummaryId,
            ref this.sceneSummaryDraft,
            () => scene.Summary,
            text => this.recorder.SetSceneSummary(scene.Id, text),
            () => SceneTranscript(scene),
            "this scene");
        ImGui.Spacing();
        if (this.ConfirmDelete(scene.Id))
        {
            this.recorder.RemoveScene(scene.Id);
            this.openSceneId = null;
            return;
        }

        ImGui.Spacing();
        if (scene.Status == SceneStatus.Saved)
            this.DrawSplit(scene);

        this.DrawTranscript(scene);
    }

    private void DrawSplit(SceneRecord scene)
    {
        if (this.editSceneId != scene.Id)
        {
            this.editSceneId = scene.Id;
            this.editingLineId = null;
            this.pendingLineDelete = null;
            this.lineMessage = string.Empty;
            this.splitTimeDraft = string.Empty;
        }

        ImGui.TextUnformatted("New scene starts at");
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##split-time", "10:45 AM", ref this.splitTimeDraft, 40);
        ImGui.SameLine();
        if (CoverButton("Split", new Vector2(88, 28)))
            this.SplitAtTime(scene);

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Lines from that time on become their own scene. This one keeps the earlier lines. You can also edit a line below.");
        ImGui.PopStyleColor();
        if (!string.IsNullOrWhiteSpace(this.lineMessage))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.lineMessage);
            ImGui.PopStyleColor();
        }
    }

    private void SplitAtTime(SceneRecord scene)
    {
        if (!TryParseClock(this.splitTimeDraft, scene, out var start))
        {
            this.lineMessage = "Use a time like 10:45 AM.";
            return;
        }

        var index = -1;
        for (var i = 0; i < scene.Lines.Count; i++)
        {
            if (scene.Lines[i].At.ToLocalTime() >= start)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            this.lineMessage = "No line starts at that time.";
            return;
        }

        this.SplitAt(scene, index, start.ToString("h:mm tt"));
    }

    private void SplitAt(SceneRecord scene, int index, string label)
    {
        if (index <= 0)
        {
            this.lineMessage = "That is already the start of this scene.";
            return;
        }

        if (!this.recorder.SplitScene(scene.Id, index, out _))
        {
            this.lineMessage = "That split did not take. Try another time.";
            return;
        }

        this.editingLineId = null;
        this.pendingLineDelete = null;
        this.lineMessage = "Lines from " + label + " are a new scene in the notebook. This one keeps the earlier lines.";
    }

    private static bool TryParseClock(string text, SceneRecord scene, out DateTimeOffset start)
    {
        start = default;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || scene.Lines.Count == 0 || !DateTime.TryParse(trimmed, out var parsed))
            return false;

        var day = scene.Lines[0].At.ToLocalTime().Date;
        var local = DateTime.SpecifyKind(day.Add(parsed.TimeOfDay), DateTimeKind.Local);
        start = new DateTimeOffset(local);
        if (scene.Lines[^1].At.ToLocalTime() < start && scene.Lines[0].At.ToLocalTime().Date != scene.Lines[^1].At.ToLocalTime().Date)
            start = start.AddDays(1);

        return true;
    }

    private void DrawTranscript(SceneRecord scene)
    {
        ImGui.BeginChild("transcript", new Vector2(0, 0), false);
        if (scene.Lines.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted(scene.Status == SceneStatus.Recording
                ? "Waiting for /say or an emote."
                : "This scene has no lines yet.");
            ImGui.PopStyleColor();
        }
        else
        {
            foreach (var line in scene.Lines)
                this.DrawLine(scene, line);
        }

        if (scene.Id != this.scrolledSceneId || scene.Lines.Count != this.scrolledLineCount)
        {
            if (scene.Status == SceneStatus.Recording)
                ImGui.SetScrollHereY(1f);

            this.scrolledSceneId = scene.Id;
            this.scrolledLineCount = scene.Lines.Count;
        }

        ImGui.EndChild();
    }

    private void DrawLine(SceneRecord scene, SceneLine line)
    {
        var editable = scene.Status == SceneStatus.Saved;
        var tag = line.Kind == LineKind.Emote
            ? "emote"
            : string.IsNullOrWhiteSpace(line.Channel) ? "say" : line.Channel.ToLowerInvariant();
        var speaker = PlayerNames.Clean(line.Speaker);
        if (string.IsNullOrWhiteSpace(speaker))
            speaker = PlayerNames.Clean(this.playerName);
        if (string.IsNullOrWhiteSpace(speaker))
            speaker = "Unknown";

        ImGui.PushID(line.Id.ToString());
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextUnformatted($"{line.At.ToLocalTime():h:mm tt}   [{tag}]");
        ImGui.PopStyleColor();
        if (editable)
        {
            ImGui.SameLine(0, 8);
            if (CoverButton("Edit", new Vector2(64, 24)))
            {
                this.editingLineId = line.Id;
                this.lineSpeakerDraft = speaker;
                this.lineTextDraft = line.Text;
                this.pendingLineDelete = null;
            }

            var index = scene.Lines.IndexOf(line);
            if (index > 0)
            {
                ImGui.SameLine(0, 8);
                if (CoverButton("New scene", new Vector2(110, 24)))
                    this.SplitAt(scene, index, line.At.ToLocalTime().ToString("h:mm tt"));
            }
        }

        ImGui.SameLine(0, 8);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Name);
        ImGui.TextUnformatted(speaker);
        ImGui.PopStyleColor();

        if (editable && this.editingLineId == line.Id)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##line-speaker", ref this.lineSpeakerDraft, 80);
            if (ImGui.InputTextMultiline("##line-text", ref this.lineTextDraft, 4000, new Vector2(-1, 80)))
            {
                // The box keeps the draft until Save.
            }

            if (CoverButton("Save line", new Vector2(120, 28)))
            {
                this.recorder.UpdateLine(scene.Id, line.Id, this.lineSpeakerDraft, this.lineTextDraft);
                this.editingLineId = null;
            }

            ImGui.SameLine();
            var armed = this.pendingLineDelete == line.Id;
            if (CoverButton(armed ? "Delete?" : "Delete line", new Vector2(120, 28)))
            {
                if (!armed)
                {
                    this.pendingLineDelete = line.Id;
                }
                else
                {
                    this.recorder.RemoveLine(scene.Id, line.Id);
                    this.editingLineId = null;
                    this.pendingLineDelete = null;
                }
            }
        }
        else
        {
            ImGui.Indent(16f);
            ImGui.PushStyleColor(ImGuiCol.Text, line.Kind == LineKind.Emote ? JournalTheme.Emote : JournalTheme.Ink);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextUnformatted(PlayerNames.StripLeadingMark(line.Text));
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.Unindent(16f);
        }

        ImGui.PopID();
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var left = ImGui.GetWindowPos().X + 28;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X - 16;
        draw.AddLine(new Vector2(left, pos.Y), new Vector2(right, pos.Y), ImGui.GetColorU32(JournalTheme.Rule), 1f);
        ImGui.Dummy(new Vector2(1, 8));
    }

    private void DrawCast(bool live, SceneRecord? scene)
    {
        var everyone = live && scene != null
            ? scene.Mode == RecordingMode.Everyone
            : this.draftEveryone;
        var selected = live && scene != null ? scene.SelectedSpeakers : this.draftCast;

        ImGui.TextUnformatted("Who is in this scene");
        if (ImGui.RadioButton("Everyone", everyone))
        {
            if (live)
                this.recorder.SetEveryone(true);
            else
                this.draftEveryone = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Only these people", !everyone))
        {
            if (live)
                this.recorder.SetEveryone(false);
            else
                this.draftEveryone = false;
        }

        var names = new List<string>();
        foreach (var nearby in this.recorder.NearbyNames())
            AddName(names, nearby);

        foreach (var name in selected)
            AddName(names, name);

        if (names.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No one else is nearby. Type a name to add them.");
            ImGui.PopStyleColor();
        }

        foreach (var name in names)
        {
            var on = ContainsName(selected, name);
            if (!ImGui.Checkbox($"{name}##cast", ref on))
                continue;

            if (live)
                this.recorder.IncludeSpeaker(name, on);
            else
                this.SetDraft(name, on);
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("##add-person", ref this.draftName, 64);
        ImGui.SameLine();
        if (CoverButton("Add", new Vector2(72, 28)) && !string.IsNullOrWhiteSpace(this.draftName))
        {
            if (live)
                this.recorder.IncludeSpeaker(this.draftName, true);
            else
                this.SetDraft(this.draftName, true);

            this.draftName = string.Empty;
        }

        if (!everyone && selected.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Nobody is selected, so the scene stays quiet until you add someone.");
            ImGui.PopStyleColor();
        }
    }

    private void DrawSettings()
    {
        var settings = this.recorder.Settings;
        ImGui.TextUnformatted("Settings");
        ImGui.Spacing();
        ImGui.TextUnformatted("Record these chats");
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Combat, loot, echoes, and other system lines are left out. Random rolls keep a /random, and that box stays off until you turn it on.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        foreach (ChatChannel channel in Enum.GetValues<ChatChannel>())
        {
            var enabled = settings.Allows(channel);
            if (!ImGui.Checkbox(SupportedChat.Label(channel), ref enabled))
                continue;

            settings.Set(channel, enabled);
        }

        ImGui.Spacing();
        var skip = settings.SkipOutOfCharacter;
        if (ImGui.Checkbox("Skip out-of-character text", ref skip))
        {
            settings.SkipOutOfCharacter = skip;
            settings.Save();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Leaves out text wrapped in [ ], ( ), or (( )). A line that only starts or ends with one of those is left out too.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Where logs are saved");
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Each scene is a text file in this folder. A scene with a title is named with the date and that title. A scene without a title is named with the date and time.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.TextWrapped(this.recorder.LogFolder);
        ImGui.Spacing();
        if (CoverButton("Choose a folder", new Vector2(160, 30)))
        {
            this.dialogs.OpenFolderDialog(
                "Choose a folder for logs",
                this.OnLogFolderChosen,
                this.recorder.LogFolder,
                true);
        }

        if (!this.recorder.IsUsualLogFolder)
        {
            ImGui.SameLine();
            if (CoverButton("Usual place", new Vector2(140, 30)))
                this.logFolderMessage = this.recorder.SetLogFolder(null);
        }

        if (!string.IsNullOrWhiteSpace(this.logFolderMessage))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.logFolderMessage);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted("Summaries");
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Choose ChatGPT or Grok. Afterword asks before a scene is sent. Notes stay on this computer. There is no API key and no second bill.");
        ImGui.PopStyleColor();
        ImGui.Spacing();
        var useGrok = this.summarySettings.Provider == "grok";
        if (ImGui.RadioButton("ChatGPT", !useGrok))
        {
            this.summarySettings.Provider = "chatgpt";
            this.summarySettings.Save();
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Grok", useGrok))
        {
            this.summarySettings.Provider = "grok";
            this.summarySettings.Save();
        }

        ImGui.Spacing();
        if (useGrok)
            this.DrawGrokSignIn();
        else
            this.DrawChatSignIn();
    }

    private void DrawChatSignIn()
    {
        if (this.chatGpt.IsSignedIn)
        {
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(this.chatGpt.Email) ? "Signed in with ChatGPT." : "Signed in as " + this.chatGpt.Email + ".");
            if (CoverButton("Sign out##chat", new Vector2(140, 28)))
                this.chatGpt.SignOut();
        }
        else if (this.chatGpt.Busy)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(this.chatGpt.Status)
                ? "Waiting for the browser."
                : this.chatGpt.Status);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped("Click Sign in with ChatGPT. A browser opens. Sign in, allow Afterword to use that plan, then come back here.");
            ImGui.PopStyleColor();
            ImGui.Spacing();
            if (CoverButton("Sign in with ChatGPT", new Vector2(220, 32)))
                this.chatGpt.StartSignIn();
        }

        if (!this.chatGpt.Busy && !string.IsNullOrWhiteSpace(this.chatGpt.Status))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.chatGpt.Status);
            ImGui.PopStyleColor();
        }
    }

    private void DrawGrokSignIn()
    {
        if (this.grok.IsSignedIn)
        {
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(this.grok.Email) ? "Signed in with Grok." : "Signed in to Grok as " + this.grok.Email + ".");
            if (CoverButton("Sign out##grok", new Vector2(140, 28)))
                this.grok.SignOut();
        }
        else if (this.grok.Busy)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(this.grok.Status)
                ? "Waiting for the browser."
                : this.grok.Status);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped("Click Sign in with Grok. A browser opens. Sign in with the Grok plan you already pay for, then come back here.");
            ImGui.PopStyleColor();
            ImGui.Spacing();
            if (CoverButton("Sign in with Grok", new Vector2(200, 32)))
                this.grok.StartSignIn();
        }

        if (!this.grok.Busy && !string.IsNullOrWhiteSpace(this.grok.Status))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.grok.Status);
            ImGui.PopStyleColor();
        }
    }

    private void SetDraft(string name, bool include)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        var index = -1;
        for (var i = 0; i < this.draftCast.Count; i++)
        {
            if (string.Equals(this.draftCast[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (include && index < 0)
            this.draftCast.Add(trimmed);
        else if (!include && index >= 0)
            this.draftCast.RemoveAt(index);

        if (include)
            this.draftEveryone = false;
    }

    private static void AddName(List<string> names, string name)
    {
        var cleaned = PlayerNames.Clean(name);
        if (cleaned.Length == 0 || ContainsName(names, cleaned))
            return;

        names.Add(cleaned);
    }

    private static bool ContainsName(IReadOnlyList<string> names, string name)
    {
        foreach (var existing in names)
        {
            if (string.Equals(existing.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CoverButton(string label, Vector2 size)
    {
        if (size.X < 0)
            size.X = Math.Max(32f, ImGui.GetContentRegionAvail().X);

        var pressed = ImGui.InvisibleButton(label, size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var held = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        DrawRaisedFace(min, max, held, hovered);

        var text = label;
        var marker = text.IndexOf("##", StringComparison.Ordinal);
        if (marker >= 0)
            text = text[..marker];

        var textSize = ImGui.CalcTextSize(text);
        var textPos = min + ((max - min - textSize) * 0.5f);
        if (held)
            textPos.Y += 1f;

        ImGui.GetWindowDrawList().AddText(textPos, ImGui.GetColorU32(JournalTheme.CoverInk), text);
        return pressed;
    }

    private bool ConfirmDelete(Guid id)
    {
        var armed = this.pendingDelete == id;
        if (!CoverButton(armed ? "Delete?" : "Delete", new Vector2(88, 32)))
            return false;

        if (!armed)
        {
            this.pendingDelete = id;
            return false;
        }

        this.pendingDelete = null;
        return true;
    }

    private static void DrawRaisedFace(Vector2 min, Vector2 max, bool held, bool hovered)
    {
        var draw = ImGui.GetWindowDrawList();
        var faceMin = min;
        var faceMax = max;
        if (held)
        {
            faceMin.Y += 1f;
            faceMax.Y += 1f;
        }
        else
        {
            draw.AddRectFilled(
                min + new Vector2(0f, 2f),
                max + new Vector2(1f, 3f),
                ImGui.GetColorU32(new Vector4(0.03f, 0.06f, 0.11f, 0.45f)),
                3f);
        }

        var body = held ? JournalTheme.CoverDeep : hovered ? JournalTheme.CoverHover : JournalTheme.Cover;
        draw.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(body), 3f);

        var clipMin = faceMin + new Vector2(1f, 1f);
        var clipMax = faceMax - new Vector2(1f, 1f);
        if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
            return;

        draw.PushClipRect(clipMin, clipMax, true);
        var midY = faceMin.Y + ((faceMax.Y - faceMin.Y) * 0.46f);
        var highlight = held ? 0.06f : 0.22f;
        var shade = held ? 0.30f : 0.24f;
        draw.AddRectFilledMultiColor(
            faceMin,
            new Vector2(faceMax.X, midY),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, highlight)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, highlight)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0f)));
        draw.AddRectFilledMultiColor(
            new Vector2(faceMin.X, midY),
            faceMax,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0f)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shade)),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, shade)));
        draw.PopClipRect();

        draw.AddLine(
            faceMin + new Vector2(4f, 1.25f),
            new Vector2(faceMax.X - 4f, faceMin.Y + 1.25f),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, held ? 0.10f : 0.40f)),
            1.25f);
    }

    private bool TryPushNavFont()
    {
        if (this.navFont is not { Available: true })
            return false;

        this.navFont.Push();
        return true;
    }

    private bool TryPushPageFont()
    {
        if (this.pageFont is not { Available: true })
            return false;

        this.pageFont.Push();
        if (ImGui.GetFontSize() >= 13f)
            return true;

        this.pageFont.Pop();
        return false;
    }

    private static IFontHandle? CreatePageFont(IDalamudPluginInterface pluginInterface)
    {
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "georgia.ttf");
        if (!File.Exists(fontPath))
            return null;

        return pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit =>
        {
            toolkit.Font = toolkit.AddFontFromFile(fontPath, new SafeFontConfig { SizePx = 18 });
        }));
    }

    private static IFontHandle? CreateNavFont(IDalamudPluginInterface pluginInterface)
    {
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(fontPath))
            return null;

        return pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(toolkit =>
        {
            toolkit.Font = toolkit.AddFontFromFile(fontPath, new SafeFontConfig { SizePx = 23 });
        }));
    }

    private static void DrawStatusDot(SceneStatus status)
    {
        var draw = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var color = status == SceneStatus.Recording ? JournalTheme.Record : JournalTheme.Name;
        if (status == SceneStatus.Recording)
        {
            var pulse = 0.45f + (0.55f * (0.5f + (0.5f * (float)Math.Sin(ImGui.GetTime() * 4))));
            color = new Vector4(color.X, color.Y, color.Z, pulse);
        }

        draw.AddCircleFilled(pos + new Vector2(6, 8), 5f, ImGui.GetColorU32(color));
        ImGui.Dummy(new Vector2(16, 16));
    }

    private void DrawStorylines(NotebookSnapshot snapshot)
    {
        if (this.openStorylineId != null)
        {
            StorylineRecord? selected = null;
            foreach (var story in snapshot.Storylines)
            {
                if (story.Id == this.openStorylineId)
                {
                    selected = story;
                    break;
                }
            }

            if (selected == null)
                this.openStorylineId = null;
            else
            {
                this.DrawOpenStoryline(snapshot, selected);
                return;
            }
        }

        ImGui.TextUnformatted("Storylines");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Name a story, then import a scene or add a note. A summary is written only when you ask to send the storyline.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(280);
        ImGui.InputTextWithHint("##new-storyline", "Storyline name", ref this.newStoryline, 80);
        ImGui.SameLine();
        if (CoverButton("Add", new Vector2(72, 28)) && this.recorder.AddStoryline(this.newStoryline, out var id))
        {
            this.openStorylineId = id;
            this.newStoryline = string.Empty;
        }

        if (!snapshot.IsLoggedIn)
        {
            ImGui.EndDisabled();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Log into a character first. The storyline is saved with that character.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        if (snapshot.Storylines.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No storylines yet.");
            ImGui.PopStyleColor();
            return;
        }

        foreach (var story in snapshot.Storylines)
        {
            var scenes = CountScenes(snapshot, story.Title);
            var subtitle = scenes == 1 ? "1 scene" : $"{scenes} scenes";
            ImGui.PushID(story.Id.ToString());
            if (CoverButton(story.Title, new Vector2(-1, 32)))
                this.openStorylineId = story.Id;
            ImGui.PopID();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted($"{story.CreatedAt.ToLocalTime():MMM d, yyyy}   ·   {subtitle}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawOpenStoryline(NotebookSnapshot snapshot, StorylineRecord story)
    {
        if (this.openStorylineNoteId != null)
        {
            this.DrawOpenStorylineNote(story);
            return;
        }

        if (this.pendingImport != null
            && string.Equals(this.importStoryline, story.Title, StringComparison.OrdinalIgnoreCase))
        {
            this.DrawImportPicker(snapshot);
            return;
        }

        if (this.storylineEditId != story.Id)
        {
            this.storylineEditId = story.Id;
            this.storylineTitleDraft = story.Title;
            this.storylineNotesDraft = story.Notes;
            this.storylineSummaryDraft = story.Summary;
        }

        if (CoverButton("Back to storylines", new Vector2(180, 30)))
        {
            this.openStorylineId = null;
            this.openStorylineNoteId = null;
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##story-title", ref this.storylineTitleDraft, 80))
            this.recorder.RenameStoryline(story.Id, this.storylineTitleDraft);

        this.DrawSummary(
            "story:" + story.Id,
            ref this.storylineEditId,
            ref this.storylineSummaryDraft,
            () => story.Summary,
            text => this.recorder.SetStorylineSummary(story.Id, text),
            () => StorylineTranscript(snapshot, story),
            "every scene in this storyline",
            loadOnFirst: false);

        ImGui.Spacing();
        ImGui.TextUnformatted("Notes");
        if (ImGui.InputTextMultiline("##story-notes", ref this.storylineNotesDraft, 4000, new Vector2(-1, 90)))
            this.recorder.SetStorylineNotes(story.Id, this.storylineNotesDraft);

        ImGui.Spacing();
        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        if (CoverButton("New note", new Vector2(140, 30)) && this.recorder.AddStorylineNote(story.Id, out var noteId))
            this.openStorylineNoteId = noteId;

        ImGui.SameLine();
        if (CoverButton("Import a scene", new Vector2(160, 30)))
            this.OpenImport(story.Title);

        if (!snapshot.IsLoggedIn)
            ImGui.EndDisabled();

        story.NotePages ??= new List<JournalNote>();
        if (story.NotePages.Count > 0)
        {
            ImGui.Spacing();
            foreach (var note in story.NotePages)
            {
                ImGui.PushID(note.Id.ToString());
                var width = Math.Max(120f, ImGui.GetContentRegionAvail().X - 96f);
                if (CoverButton(note.Title, new Vector2(width, 32)))
                    this.openStorylineNoteId = note.Id;
                ImGui.SameLine();
                if (this.ConfirmDelete(note.Id))
                {
                    this.recorder.RemoveStorylineNote(story.Id, note.Id);
                    if (this.openStorylineNoteId == note.Id)
                        this.openStorylineNoteId = null;
                }

                ImGui.PopID();
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Stories");
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Long stories linked here are part of this storyline. Ones marked as lore are treated as background that happened.");
        ImGui.PopStyleColor();
        var linkedStories = new List<WrittenStory>();
        foreach (var written in snapshot.Stories)
        {
            if (string.Equals(written.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                linkedStories.Add(written);
        }

        if (linkedStories.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No stories linked yet.");
            ImGui.PopStyleColor();
        }

        foreach (var written in linkedStories)
        {
            ImGui.PushID(written.Id.ToString());
            var label = written.AffectsLore ? written.Title + "  ·  Lore" : written.Title;
            if (CoverButton(label, new Vector2(-1, 32)))
            {
                this.openWrittenStoryId = written.Id;
                this.page = Page.Stories;
            }

            ImGui.PopID();
            ImGui.Spacing();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Scenes");
        this.DrawListTools();
        var keyword = this.notebookSearch.Trim();
        var storyScenes = new List<SceneRecord>();
        var anyStoryScene = false;
        foreach (var scene in snapshot.Scenes)
        {
            if (!string.Equals(scene.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                continue;

            anyStoryScene = true;
            if (SceneMatches(scene, keyword))
                storyScenes.Add(scene);
        }

        storyScenes.Sort((a, b) => this.CompareWhen(a.StartedAt, b.StartedAt));
        var any = storyScenes.Count > 0;
        foreach (var scene in storyScenes)
        {
            ImGui.PushID(scene.Id.ToString());
            if (CoverButton(scene.Title, new Vector2(-1, 32)))
            {
                this.openSceneId = scene.Id;
                this.page = Page.Notebook;
            }

            ImGui.PopID();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted($"{scene.StartedAt.ToLocalTime():MMM d, yyyy}");
            if (!string.IsNullOrWhiteSpace(scene.Summary))
                ImGui.TextWrapped(scene.Summary);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        if (!anyStoryScene)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No scenes yet. Import one, or choose this storyline on a scene.");
            ImGui.PopStyleColor();
        }
        else if (!any)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Nothing in this storyline has that word.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        if (CoverButton("Delete storyline", new Vector2(160, 30)))
        {
            this.recorder.RemoveStoryline(story.Id);
            this.openStorylineId = null;
            this.openStorylineNoteId = null;
        }
    }

    private void DrawOpenStorylineNote(StorylineRecord story)
    {
        JournalNote? note = null;
        story.NotePages ??= new List<JournalNote>();
        foreach (var candidate in story.NotePages)
        {
            if (candidate.Id == this.openStorylineNoteId)
            {
                note = candidate;
                break;
            }
        }

        if (note == null)
        {
            this.openStorylineNoteId = null;
            return;
        }

        if (this.storylineNoteEditId != note.Id)
        {
            this.storylineNoteEditId = note.Id;
            this.storylineNoteTitleDraft = note.Title;
            this.storylineNoteBodyDraft = note.Body;
        }

        if (CoverButton("Back to storyline", new Vector2(180, 30)))
        {
            this.openStorylineNoteId = null;
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Title");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##story-note-title", ref this.storylineNoteTitleDraft, 120))
            this.recorder.SetStorylineNoteTitle(story.Id, note.Id, this.storylineNoteTitleDraft);

        ImGui.Spacing();
        ImGui.TextUnformatted("Note");
        if (ImGui.InputTextMultiline("##story-note-body", ref this.storylineNoteBodyDraft, 8000, new Vector2(-1, 220)))
            this.recorder.SetStorylineNoteBody(story.Id, note.Id, this.storylineNoteBodyDraft);

        ImGui.Spacing();
        if (this.ConfirmDelete(note.Id))
        {
            this.recorder.RemoveStorylineNote(story.Id, note.Id);
            this.openStorylineNoteId = null;
        }
    }

    private void DrawSummary(
        string target,
        ref Guid seenId,
        ref string draft,
        Func<string> readSaved,
        Action<string> save,
        Func<string> transcript,
        string what,
        bool loadOnFirst = true)
    {
        if (loadOnFirst)
        {
            var saved = readSaved() ?? string.Empty;
            var editing = this.summaryEditId == $"##summary-{target}";
            if (!editing && (seenId.ToString() != TargetId(target) || !string.Equals(draft, saved, StringComparison.Ordinal)))
            {
                if (Guid.TryParse(TargetId(target), out var parsed))
                    seenId = parsed;

                draft = saved;
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Summary");
        this.DrawWrappedSummary($"##summary-{target}", ref draft, save);
        this.DrawSummarizeControls(target, transcript, what);
    }

    private void DrawSummarizeControls(string target, Func<string> transcript, string what)
    {
        if (this.summaryBusy && this.summaryTarget == target)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Sending this to the internet…");
            ImGui.PopStyleColor();
            return;
        }

        if (this.summaryAsk && this.summaryTarget == target)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            var service = this.summarySettings.Provider == "grok" ? "Grok" : "ChatGPT";
            ImGui.TextWrapped($"Send {what} to {service}? This uses the plan you already pay for. Only that text leaves this computer. Your notes stay here.");
            ImGui.PopStyleColor();
            if (CoverButton("Send", new Vector2(100, 28)))
                this.StartSummary();

            ImGui.SameLine();
            if (CoverButton("Cancel##summary", new Vector2(100, 28)))
            {
                this.summaryAsk = false;
                this.summaryStatus = string.Empty;
            }
        }
        else if (CoverButton("Summarize", new Vector2(140, 28)))
        {
            this.OfferSummary(target, transcript(), what);
        }

        if (!string.IsNullOrWhiteSpace(this.summaryStatus) && this.summaryTarget == target)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.summaryStatus);
            ImGui.PopStyleColor();
        }
    }

    private void OfferSummary(string target, string transcript, string what)
    {
        this.summaryTarget = target;
        this.summaryStatus = string.Empty;
        var useGrok = this.summarySettings.Provider == "grok";
        if (useGrok && !this.grok.IsSignedIn)
        {
            this.summaryStatus = "Sign in with Grok in Settings. That uses the plan you already pay for.";
            this.summaryAsk = false;
            return;
        }

        if (!useGrok && !this.chatGpt.IsSignedIn)
        {
            this.summaryStatus = "Sign in with ChatGPT in Settings. That uses the plan you already pay for.";
            this.summaryAsk = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(transcript) || !transcript.Contains('[', StringComparison.Ordinal))
        {
            this.summaryStatus = "There is nothing to summarize yet.";
            this.summaryAsk = false;
            return;
        }

        this.summaryPayload = transcript;
        this.summaryInstruction = what.Contains("character", StringComparison.OrdinalIgnoreCase)
            ? "Write a short portrait of this character for a private roleplay journal. Use 2 to 4 sentences. Say who they are as a person and what they seem to want. Do not retell what they did. Use only the lines below."
            : "Write a short summary of this roleplay for a private journal. Use 2 to 4 sentences. Read it as an outside record of the text, not through any character's eyes. Name the people and say what the lines show them doing and saying. Do not write as if you are one of them, and do not use I or my. Use only events and names that appear below.";
        this.summaryAsk = true;
    }

    private void StartSummary()
    {
        var target = this.summaryTarget;
        var payload = this.summaryPayload;
        var instruction = this.summaryInstruction;
        this.summaryAsk = false;
        this.summaryBusy = true;
        this.summaryStatus = string.Empty;
        var generation = ++this.summaryGeneration;
        var useGrok = this.summarySettings.Provider == "grok";
        var chat = this.chatGpt;
        var grok = this.grok;

        _ = Task.Run(async () =>
        {
            try
            {
                var summary = useGrok
                    ? await grok.SummarizeAsync(instruction, payload, CancellationToken.None)
                    : await chat.SummarizeAsync(instruction, payload, CancellationToken.None);
                lock (this.summaryGate)
                {
                    if (generation != this.summaryGeneration)
                        return;

                    this.pendingSummary = summary;
                    this.pendingError = null;
                    this.pendingGeneration = generation;
                    this.pendingTarget = target;
                }
            }
            catch (Exception ex)
            {
                lock (this.summaryGate)
                {
                    if (generation != this.summaryGeneration)
                        return;

                    this.pendingSummary = null;
                    this.pendingError = ex.Message;
                    this.pendingGeneration = generation;
                    this.pendingTarget = target;
                }
            }
        });
    }

    private void ApplyPendingSummary()
    {
        string? text;
        string? error;
        string target;
        int generation;
        lock (this.summaryGate)
        {
            if (this.pendingGeneration != this.summaryGeneration)
                return;

            text = this.pendingSummary;
            error = this.pendingError;
            target = this.pendingTarget;
            generation = this.pendingGeneration;
            this.pendingSummary = null;
            this.pendingError = null;
            this.pendingGeneration = -1;
        }

        if (generation < 0)
            return;

        this.summaryBusy = false;
        this.summaryTarget = target;
        if (!string.IsNullOrWhiteSpace(error))
        {
            this.summaryStatus = error;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (target.StartsWith("scene:", StringComparison.Ordinal) && Guid.TryParse(target["scene:".Length..], out var sceneId))
        {
            this.recorder.SetSceneSummary(sceneId, text);
            if (this.sceneSummaryId == sceneId || this.openSceneId == sceneId)
            {
                this.sceneSummaryDraft = text;
                this.sceneSummaryId = sceneId;
            }
        }
        else if (target.StartsWith("story:", StringComparison.Ordinal) && Guid.TryParse(target["story:".Length..], out var storyId))
        {
            this.recorder.SetStorylineSummary(storyId, text);
            if (this.openStorylineId == storyId)
                this.storylineSummaryDraft = text;
        }
        else if (target.StartsWith("character:", StringComparison.Ordinal))
        {
            var name = target["character:".Length..];
            this.recorder.SetCharacterSummary(name, text);
            if (string.Equals(this.openCharacter, name, StringComparison.OrdinalIgnoreCase))
                this.characterSummaryDraft = text;
        }

        this.summaryStatus = "Summary saved. You can rewrite it.";
    }

    private void DrawWrappedSummary(string id, ref string draft, Action<string> save)
    {
        var width = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        var height = SummaryBoxHeight(this.summaryEditId == id ? this.summaryEditDraft : draft, width);
        if (this.summaryEditId == id)
        {
            if (!string.Equals(draft, CollapseSoftBreaks(this.summaryEditDraft), StringComparison.Ordinal))
                this.summaryEditDraft = draft;

            if (this.summaryEditOpened)
                ImGui.SetKeyboardFocusHere();

            if (ImGui.InputTextMultiline(id, ref this.summaryEditDraft, 8000, new Vector2(-1, height)))
            {
                draft = CollapseSoftBreaks(this.summaryEditDraft);
                save(draft);
            }

            var opened = this.summaryEditOpened;
            this.summaryEditOpened = false;
            if (!opened && !ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                this.summaryEditId = null;

            return;
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 0.992f, 0.965f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.BeginChild(id, new Vector2(width, height), true);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        if (string.IsNullOrWhiteSpace(draft))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("A summary shows up here. Click the box to write your own.");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextWrapped(draft);
        }

        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        if (!ImGui.IsItemClicked())
            return;

        this.summaryEditId = id;
        this.summaryEditDraft = SoftWrap(draft, Math.Max(80f, width - 48f));
        this.summaryEditOpened = true;
    }

    private static float SummaryBoxHeight(string text, float width)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 78f;

        var inner = Math.Max(40f, width - 28f);
        var measured = ImGui.CalcTextSize(text.AsSpan(), false, inner);
        return Math.Max(78f, measured.Y + 28f);
    }

    private static string SoftWrap(string text, float width)
    {
        if (string.IsNullOrWhiteSpace(text) || width < 40f)
            return text;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder();
        var paragraphs = normalized.Split("\n\n");
        for (var p = 0; p < paragraphs.Length; p++)
        {
            if (p > 0)
                builder.Append("\n\n");

            var line = new StringBuilder();
            foreach (var word in paragraphs[p].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && ImGui.CalcTextSize(next).X > width)
                {
                    builder.Append(line).Append('\n');
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');

                line.Append(word);
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string CollapseSoftBreaks(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n");
        var builder = new StringBuilder();
        for (var i = 0; i < paragraphs.Length; i++)
        {
            if (i > 0)
                builder.Append("\n\n");

            var line = paragraphs[i].Replace('\n', ' ');
            while (line.Contains("  ", StringComparison.Ordinal))
                line = line.Replace("  ", " ", StringComparison.Ordinal);

            builder.Append(line.Trim());
        }

        return builder.ToString().Trim();
    }

    private void DrawListTools()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##notebook-search", "Search by keyword", ref this.notebookSearch, 80);
        if (ImGui.RadioButton("Newest first", this.scenesNewestFirst))
            this.scenesNewestFirst = true;

        ImGui.SameLine();
        if (ImGui.RadioButton("Oldest first", !this.scenesNewestFirst))
            this.scenesNewestFirst = false;

        ImGui.Spacing();
    }

    private int CompareWhen(DateTimeOffset left, DateTimeOffset right)
    {
        var order = left.CompareTo(right);
        return this.scenesNewestFirst ? -order : order;
    }

    private static bool SceneMatches(SceneRecord scene, string keyword)
    {
        if (keyword.Length == 0)
            return true;

        if (MatchesKeyword(keyword, scene.Title, scene.Location, scene.Summary, scene.Storyline))
            return true;

        foreach (var line in scene.Lines)
        {
            if (MatchesKeyword(keyword, line.Speaker, line.Text))
                return true;
        }

        return false;
    }

    private static bool MatchesKeyword(string keyword, params string?[] parts)
    {
        if (keyword.Length == 0)
            return true;

        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part) && part.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string TargetId(string target)
    {
        var split = target.IndexOf(':');
        return split < 0 ? target : target[(split + 1)..];
    }

    private static string SceneTranscript(SceneRecord scene)
    {
        var text = new StringBuilder();
        text.Append("Title: ").AppendLine(scene.Title);
        text.Append("Place: ").AppendLine(scene.Location);
        AppendLines(text, scene.Lines, string.Empty);
        return text.ToString().Trim();
    }

    private static string StorylineTranscript(NotebookSnapshot snapshot, StorylineRecord story)
    {
        var text = new StringBuilder();
        text.Append("Storyline: ").AppendLine(story.Title);
        foreach (var scene in snapshot.Scenes)
        {
            if (!string.Equals(scene.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                continue;

            text.AppendLine();
            text.Append("Scene: ").AppendLine(scene.Title);
            AppendLines(text, scene.Lines, string.Empty);
        }

        foreach (var written in snapshot.Stories)
        {
            if (!string.Equals(written.Storyline, story.Title, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(written.Body))
                continue;

            text.AppendLine();
            text.AppendLine(written.AffectsLore ? "Lore:" : "Story:");
            text.AppendLine(written.Title);
            text.AppendLine(written.Body.Trim());
        }

        return text.ToString().Trim();
    }

    private static string CharacterTranscript(NotebookSnapshot snapshot, string name)
    {
        var text = new StringBuilder();
        text.Append("Character: ").AppendLine(name);
        foreach (var scene in snapshot.Scenes)
        {
            var spoken = new StringBuilder();
            AppendLines(spoken, scene.Lines, name);
            if (spoken.Length == 0)
                continue;

            text.AppendLine();
            text.Append("Scene: ").AppendLine(scene.Title);
            text.Append(spoken);
        }

        return text.ToString().Trim();
    }

    private static void AppendLines(StringBuilder text, IReadOnlyList<SceneLine> lines, string onlySpeaker)
    {
        foreach (var line in lines)
        {
            var speaker = PlayerNames.Clean(line.Speaker);
            if (speaker.Length == 0)
                speaker = "Someone";

            if (onlySpeaker.Length > 0 && !SamePerson(speaker, onlySpeaker))
                continue;

            var spoken = PlayerNames.StripLeadingMark(line.Text).Trim();
            if (spoken.Length == 0)
                continue;

            var tag = line.Kind == LineKind.Emote ? "emote" : "say";
            text.Append('[').Append(tag).Append("] ").Append(speaker).Append(": ").AppendLine(spoken);
        }
    }

    private void DrawStorylineField(SceneRecord scene)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Storyline");
        var storylines = this.recorder.Snapshot().Storylines;
        var shown = string.IsNullOrWhiteSpace(scene.Storyline) ? "None yet" : scene.Storyline;
        ImGui.SetNextItemWidth(280);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, JournalTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.82f, 0.74f, 0.60f, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.74f, 0.64f, 0.48f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.66f, 0.55f, 0.40f, 1f));
        var open = ImGui.BeginCombo("##scene-storyline", shown);
        if (!open)
        {
            ImGui.PopStyleColor(5);
            return;
        }

        if (ImGui.Selectable("None yet", string.IsNullOrWhiteSpace(scene.Storyline)))
            this.recorder.SetSceneStoryline(scene.Id, string.Empty);

        foreach (var story in storylines)
        {
            ImGui.PushID(story.Id.ToString());
            var chosen = string.Equals(scene.Storyline, story.Title, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(story.Title, chosen))
                this.recorder.SetSceneStoryline(scene.Id, story.Title);
            ImGui.PopID();
        }

        ImGui.EndCombo();
        ImGui.PopStyleColor(5);
    }

    private void DrawCharacters(NotebookSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(this.openCharacter))
        {
            this.DrawOpenCharacter(snapshot);
            return;
        }

        ImGui.TextUnformatted("Characters");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Add someone who is not on screen, such as an NPC. People who speak in a scene are listed here too. Open a name to rename them, or combine them with someone else.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(280);
        ImGui.InputTextWithHint("##new-character", "Name", ref this.newCharacter, 64);
        ImGui.SameLine();
        if (CoverButton("Add##character", new Vector2(72, 28)) && !string.IsNullOrWhiteSpace(this.newCharacter))
        {
            var name = this.newCharacter.Trim();
            if (this.recorder.AddCharacter(name))
            {
                this.openCharacter = name;
                this.newCharacter = string.Empty;
            }
        }

        if (!snapshot.IsLoggedIn)
        {
            ImGui.EndDisabled();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Log into a character first. These names are saved with that character.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.Spacing();
        var names = CollectCharacters(snapshot);
        if (names.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No one here yet.");
            ImGui.PopStyleColor();
            return;
        }

        foreach (var name in names)
        {
            ImGui.PushID(name);
            if (CoverButton(name, new Vector2(-1, 32)))
                this.openCharacter = name;
            ImGui.PopID();
            ImGui.Spacing();
        }
    }

    private void DrawOpenCharacter(NotebookSnapshot snapshot)
    {
        CharacterProfile? profile = null;
        foreach (var candidate in snapshot.Characters)
        {
            if (string.Equals(candidate.Name, this.openCharacter, StringComparison.OrdinalIgnoreCase))
            {
                profile = candidate;
                break;
            }
        }

        if (!string.Equals(this.characterEditName, this.openCharacter, StringComparison.OrdinalIgnoreCase))
        {
            this.characterEditName = this.openCharacter;
            this.characterNameDraft = this.openCharacter;
            this.combineWith = string.Empty;
            this.characterDeleteArmed = false;
            this.characterNotesDraft = profile?.Notes ?? string.Empty;
            this.characterSummaryDraft = profile?.Summary ?? string.Empty;
        }

        if (CoverButton("Back to characters", new Vector2(180, 30)))
        {
            this.openCharacter = string.Empty;
            this.characterEditName = string.Empty;
            this.characterNameDraft = string.Empty;
            this.combineWith = string.Empty;
            this.characterNotesDraft = string.Empty;
            this.characterSummaryDraft = string.Empty;
            this.characterDeleteArmed = false;
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##character-name", ref this.characterNameDraft, 80);
        if (CoverButton("Save name", new Vector2(140, 28)))
        {
            var next = PlayerNames.Clean(this.characterNameDraft);
            if (next.Length > 0 && !string.Equals(next, this.openCharacter, StringComparison.Ordinal))
            {
                this.recorder.RenameCharacter(this.openCharacter, next);
                this.openCharacter = next;
                this.characterNameDraft = next;
                this.characterEditName = string.Empty;
                this.combineWith = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Same person as");
        var others = new List<string>();
        foreach (var name in CollectCharacters(snapshot))
        {
            if (!string.Equals(PlayerNames.Clean(name), this.openCharacter, StringComparison.OrdinalIgnoreCase))
                others.Add(name);
        }

        if (others.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No other names to combine.");
            ImGui.PopStyleColor();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(this.combineWith) || !others.Contains(this.combineWith))
                this.combineWith = others[0];

            ImGui.SetNextItemWidth(-1);
            PushPaperPopup();
            if (ImGui.BeginCombo("##combine-with", this.combineWith))
            {
                foreach (var name in others)
                {
                    if (ImGui.Selectable(name, string.Equals(name, this.combineWith, StringComparison.Ordinal)))
                        this.combineWith = name;
                }

                ImGui.EndCombo();
            }

            ImGui.PopStyleColor(5);

            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped("Combine moves that name onto this one. Their lines and notes come along.");
            ImGui.PopStyleColor();
            if (CoverButton("Combine", new Vector2(140, 28)) && !string.IsNullOrWhiteSpace(this.combineWith))
            {
                this.recorder.RenameCharacter(this.combineWith, this.openCharacter);
                this.combineWith = string.Empty;
                this.characterEditName = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Summary");
        this.DrawWrappedSummary(
            "##character-summary",
            ref this.characterSummaryDraft,
            text => this.recorder.SetCharacterSummary(this.openCharacter, text));

        this.DrawSummarizeControls(
            "character:" + this.openCharacter,
            () => CharacterTranscript(snapshot, this.openCharacter),
            "this character's lines");

        ImGui.Spacing();
        ImGui.TextUnformatted("Notes");
        if (ImGui.InputTextMultiline("##character-notes", ref this.characterNotesDraft, 4000, new Vector2(-1, 160)))
            this.recorder.SetCharacterNotes(this.openCharacter, this.characterNotesDraft);

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Delete removes this name and their lines from your scenes.");
        ImGui.PopStyleColor();
        if (!CoverButton(this.characterDeleteArmed ? "Delete?" : "Delete character", new Vector2(170, 30)))
            return;

        if (!this.characterDeleteArmed)
        {
            this.characterDeleteArmed = true;
            return;
        }

        this.recorder.RemoveCharacter(this.openCharacter);
        this.openCharacter = string.Empty;
        this.characterEditName = string.Empty;
        this.characterNameDraft = string.Empty;
        this.combineWith = string.Empty;
        this.characterNotesDraft = string.Empty;
        this.characterSummaryDraft = string.Empty;
        this.characterDeleteArmed = false;
    }

    private static int CountScenes(NotebookSnapshot snapshot, string title)
    {
        var count = 0;
        foreach (var scene in snapshot.Scenes)
        {
            if (string.Equals(scene.Storyline, title, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static List<string> CollectCharacters(NotebookSnapshot snapshot)
    {
        var names = new List<string>();
        foreach (var profile in snapshot.Characters)
            AddName(names, PlayerNames.Clean(profile.Name));

        foreach (var scene in snapshot.Scenes)
        {
            foreach (var line in scene.Lines)
                AddName(names, PlayerNames.Clean(line.Speaker));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private void DrawScratchpad()
    {
        ImGui.TextUnformatted("Scratchpad");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Write the post. Afterword fills each chat line, breaks on a sentence when it can, and numbers the pieces 1/2, 2/2.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var channel = this.scratchChannel;
        if (channel < 0 || channel >= ScratchChannels.Length)
            channel = 2;

        ImGui.TextUnformatted("Channel");
        ImGui.SetNextItemWidth(220);
        PushPaperPopup();
        if (ImGui.BeginCombo("##scratch-channel", ScratchChannels[channel].Label))
        {
            for (var i = 0; i < ScratchChannels.Length; i++)
            {
                if (ImGui.Selectable(ScratchChannels[i].Label, i == channel))
                    channel = i;
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleColor(5);
        this.scratchChannel = channel;

        ImGui.Spacing();
        ImGui.InputTextMultiline("##scratch", ref this.scratchDraft, 8000, new Vector2(-1, 140));

        var pieces = ScratchPad.Split(this.scratchDraft, ScratchChannels[channel].Header);
        var key = channel + "\n" + this.scratchDraft;
        if (!string.Equals(key, this.scratchKey, StringComparison.Ordinal))
        {
            this.scratchKey = key;
            this.scratchNext = 0;
            this.scratchNotice = string.Empty;
        }

        if (pieces.Count == 0)
            return;

        ImGui.Spacing();
        var next = this.scratchNext;
        if (next < 0 || next >= pieces.Count)
            next = 0;

        var label = pieces.Count == 1 ? "Copy" : $"Copy {next + 1}/{pieces.Count}";
        if (CoverButton(label, new Vector2(160, 32)))
        {
            ImGui.SetClipboardText(pieces[next].CopyText);
            var last = next >= pieces.Count - 1;
            this.scratchNotice = last
                ? $"Copied {next + 1}/{pieces.Count}. That was the last piece."
                : $"Copied {next + 1}/{pieces.Count}. Paste it into chat, then come back for the next piece.";
            this.scratchNext = last ? 0 : next + 1;
        }

        if (!string.IsNullOrWhiteSpace(this.scratchNotice))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextWrapped(this.scratchNotice);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        for (var i = 0; i < pieces.Count; i++)
        {
            ImGui.PushID(i);
            if (pieces.Count > 1)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Name);
                ImGui.TextUnformatted($"{pieces[i].Number}/{pieces[i].Total}");
                ImGui.PopStyleColor();
                ImGui.SameLine();
            }

            if (CoverButton("Copy##piece", new Vector2(72, 24)))
            {
                ImGui.SetClipboardText(pieces[i].CopyText);
                this.scratchNext = i + 1 >= pieces.Count ? 0 : i + 1;
                this.scratchNotice = $"Copied {pieces[i].Number}/{pieces[i].Total}.";
            }

            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
            ImGui.TextWrapped(pieces[i].CopyText);
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }

    private void DrawStories(NotebookSnapshot snapshot)
    {
        if (this.openWrittenStoryId != null)
        {
            WrittenStory? selected = null;
            foreach (var story in snapshot.Stories)
            {
                if (story.Id == this.openWrittenStoryId)
                {
                    selected = story;
                    break;
                }
            }

            if (selected == null)
                this.openWrittenStoryId = null;
            else
            {
                this.DrawOpenStory(snapshot, selected);
                return;
            }
        }

        ImGui.TextUnformatted("Stories");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Write a long story here. It can belong to a storyline, and you can mark it as lore when it should shape what came before.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (!snapshot.IsLoggedIn)
            ImGui.BeginDisabled();

        if (CoverButton("New story", new Vector2(140, 32)) && this.recorder.AddWrittenStory(out var id))
            this.openWrittenStoryId = id;

        if (!snapshot.IsLoggedIn)
        {
            ImGui.EndDisabled();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("Log into a character first. The story is saved with that character.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.Spacing();
        if (snapshot.Stories.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            ImGui.TextUnformatted("No stories yet.");
            ImGui.PopStyleColor();
            return;
        }

        var stories = new List<WrittenStory>(snapshot.Stories);
        stories.Sort((a, b) => b.WrittenAt.CompareTo(a.WrittenAt));
        foreach (var story in stories)
        {
            ImGui.PushID(story.Id.ToString());
            if (CoverButton(story.Title, new Vector2(-1, 32)))
                this.openWrittenStoryId = story.Id;
            ImGui.PopID();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
            var where = string.IsNullOrWhiteSpace(story.Storyline) ? "No storyline yet" : story.Storyline;
            var lore = story.AffectsLore ? "   ·   Lore" : string.Empty;
            ImGui.TextUnformatted($"{story.WrittenAt.ToLocalTime():MMM d, yyyy}   ·   {where}{lore}");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawOpenStory(NotebookSnapshot snapshot, WrittenStory story)
    {
        if (this.writtenEditId != story.Id)
        {
            this.writtenEditId = story.Id;
            this.writtenTitleDraft = story.Title;
            this.writtenBodyDraft = story.Body;
            this.writtenDeleteArmed = false;
        }

        if (CoverButton("Back to stories", new Vector2(160, 30)))
        {
            this.openWrittenStoryId = null;
            this.writtenDeleteArmed = false;
            return;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Title");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##written-title", ref this.writtenTitleDraft, 120))
            this.recorder.SetWrittenStoryTitle(story.Id, this.writtenTitleDraft);

        ImGui.Spacing();
        ImGui.TextUnformatted("Storyline");
        var shown = string.IsNullOrWhiteSpace(story.Storyline) ? "None yet" : story.Storyline;
        ImGui.SetNextItemWidth(280);
        PushPaperPopup();
        if (ImGui.BeginCombo("##written-storyline", shown))
        {
            if (ImGui.Selectable("None yet", string.IsNullOrWhiteSpace(story.Storyline)))
                this.recorder.SetWrittenStoryStoryline(story.Id, string.Empty);

            foreach (var line in snapshot.Storylines)
            {
                ImGui.PushID(line.Id.ToString());
                var chosen = string.Equals(story.Storyline, line.Title, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(line.Title, chosen))
                    this.recorder.SetWrittenStoryStoryline(story.Id, line.Title);
                ImGui.PopID();
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleColor(5);

        ImGui.Spacing();
        var lore = story.AffectsLore;
        if (ImGui.Checkbox("This affects the lore", ref lore))
            this.recorder.SetWrittenStoryLore(story.Id, lore);

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("Lore is background that happened. A summary of the storyline can use it, and it does not retell the story as a scene.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.TextUnformatted("Story");
        if (ImGui.InputTextMultiline("##written-body", ref this.writtenBodyDraft, 20000, new Vector2(-1, 280)))
            this.recorder.SetWrittenStoryBody(story.Id, this.writtenBodyDraft);

        ImGui.Spacing();
        if (CoverButton(this.writtenDeleteArmed ? "Delete?" : "Delete story", new Vector2(150, 30)))
        {
            if (!this.writtenDeleteArmed)
            {
                this.writtenDeleteArmed = true;
            }
            else
            {
                this.recorder.RemoveWrittenStory(story.Id);
                this.openWrittenStoryId = null;
                this.writtenDeleteArmed = false;
            }
        }
    }

    private static void DrawPlaceholder(string title, string message)
    {
        ImGui.TextUnformatted(title);
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(message);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private static void PushPaperPopup()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, JournalTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.82f, 0.74f, 0.60f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.74f, 0.64f, 0.48f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.66f, 0.55f, 0.40f, 1f));
    }

    private static string LineCount(SceneRecord scene) =>
        scene.Lines.Count == 1 ? "1 line" : $"{scene.Lines.Count} lines";

    private static string StatusLabel(SceneRecord scene) => scene.Status switch
    {
        SceneStatus.Recording => "Recording",
        SceneStatus.Paused => scene.Interrupted ? "Paused · recovered" : "Paused",
        _ => "Saved",
    };

    private enum Page
    {
        NewScene,
        Import,
        Notebook,
        Characters,
        Storylines,
        Stories,
        Scratchpad,
        Settings,
    }
}
