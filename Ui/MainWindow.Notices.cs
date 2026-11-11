using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RPChronicles.Ui;

internal sealed partial class MainWindow
{
    private bool whatsNewOpen;
    private bool guideOpen;
    private bool noticesReady;
    private int whatsNewExpanded;

    private void ConsiderWhatsNew()
    {
        if (this.noticesReady)
            return;

        this.noticesReady = true;
        if (!string.Equals(this.recorder.Settings.SeenVersion, CurrentVersion(), StringComparison.Ordinal))
            this.OpenWhatsNew();
    }

    private void OpenWhatsNew()
    {
        this.whatsNewOpen = true;
        this.whatsNewExpanded = 0;
    }

    private void MarkVersionSeen()
    {
        var current = CurrentVersion();
        if (string.Equals(this.recorder.Settings.SeenVersion, current, StringComparison.Ordinal))
            return;

        this.recorder.Settings.SeenVersion = current;
        this.recorder.Settings.Save();
    }

    private static string CurrentVersion()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        if (version == null)
            return "0.1.25";

        return version.Revision > 0
            ? version.ToString()
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void DrawWhatsNew()
    {
        if (!this.whatsNewOpen)
            return;

        if (!this.BeginNotice("What's new###afterword-news", ref this.whatsNewOpen, out var fontOn))
        {
            if (!this.whatsNewOpen)
                this.MarkVersionSeen();
            return;
        }

        var version = CurrentVersion();
        var headline = "Afterword " + version;
        foreach (var note in JournalNotes.Releases)
        {
            if (!string.Equals(note.Version, version, StringComparison.Ordinal))
                continue;

            headline = note.Title;
            break;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextUnformatted("Afterword " + version);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
        ImGui.TextWrapped(headline);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        for (var i = 0; i < JournalNotes.Releases.Length; i++)
        {
            var note = JournalNotes.Releases[i];
            var open = this.whatsNewExpanded == i;
            if (NoticeRow("news" + i, note.Version + "    " + note.Title, open))
                this.whatsNewExpanded = open ? -1 : i;

            if (this.whatsNewExpanded != i)
            {
                ImGui.Spacing();
                continue;
            }

            ImGui.Spacing();
            foreach (var section in note.Sections)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Name);
                ImGui.TextUnformatted(section.Heading);
                ImGui.PopStyleColor();
                foreach (var line in section.Lines)
                    NoticeLine(line);
                ImGui.Spacing();
            }
        }

        ImGui.Spacing();
        if (CoverButton("Close##news", new Vector2(120, 32)))
            this.whatsNewOpen = false;

        this.EndNotice(fontOn);
        if (!this.whatsNewOpen)
            this.MarkVersionSeen();
    }

    private void DrawGuide()
    {
        if (!this.guideOpen)
            return;

        if (!this.BeginNotice("How to use Afterword###afterword-guide", ref this.guideOpen, out var fontOn))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Muted);
        ImGui.TextWrapped("The notebook stays on this computer. Nothing is sent unless you agree to one summary.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        foreach (var section in JournalNotes.Guide)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Name);
            ImGui.TextUnformatted(section.Heading);
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
            foreach (var line in section.Lines)
            {
                ImGui.TextWrapped(line);
                ImGui.Spacing();
            }

            ImGui.PopStyleColor();
        }

        if (CoverButton("Close##guide", new Vector2(120, 32)))
            this.guideOpen = false;

        this.EndNotice(fontOn);
    }

    private bool BeginNotice(string title, ref bool open, out bool fontOn)
    {
        var step = this.FontStep();
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(460 + (step * 36f), 520), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(380, 280), new Vector2(680 + (step * 48f), 760));
        ImGui.SetNextWindowFocus();
        fontOn = this.TryPushPageFont();
        ImGui.PushStyleColor(ImGuiCol.WindowBg, JournalTheme.Paper);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, JournalTheme.Cover);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, JournalTheme.Cover);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.CoverInk);
        ImGui.PushStyleColor(ImGuiCol.Border, JournalTheme.PaperEdge);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse;
        if (ImGui.Begin(title, ref open, flags))
            return true;

        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
        if (fontOn)
            this.PopPageFont();
        fontOn = false;
        return false;
    }

    private void EndNotice(bool fontOn)
    {
        ImGui.End();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
        if (fontOn)
            this.PopPageFont();
    }

    private static bool NoticeRow(string id, string label, bool open)
    {
        var padX = 14f;
        var padY = 8f;
        var textSize = ImGui.CalcTextSize(label, false, Math.Max(48f, ImGui.GetContentRegionAvail().X - 36f));
        var width = ImGui.GetContentRegionAvail().X;
        var height = textSize.Y + (padY * 2f);
        var pressed = ImGui.InvisibleButton(id, new Vector2(width, height));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var fill = open ? JournalTheme.Cover : hovered ? JournalTheme.PaperEdge : JournalTheme.PaperInset;
        var ink = open ? JournalTheme.CoverInk : JournalTheme.Ink;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(fill), 4f);
        var midY = min.Y + (height * 0.5f);
        var tri = min.X + 14f;
        if (open)
        {
            draw.AddTriangleFilled(
                new Vector2(tri - 4f, midY - 2f),
                new Vector2(tri + 4f, midY - 2f),
                new Vector2(tri, midY + 3f),
                ImGui.GetColorU32(ink));
        }
        else
        {
            draw.AddTriangleFilled(
                new Vector2(tri - 2f, midY - 4f),
                new Vector2(tri - 2f, midY + 4f),
                new Vector2(tri + 3f, midY),
                ImGui.GetColorU32(ink));
        }

        draw.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            new Vector2(min.X + padX + 12f, min.Y + padY),
            ImGui.GetColorU32(ink),
            label,
            Math.Max(48f, width - 36f));
        return pressed;
    }

    private static void NoticeLine(string text)
    {
        var origin = ImGui.GetCursorScreenPos();
        var y = origin.Y + (ImGui.GetFontSize() * 0.42f);
        ImGui.GetWindowDrawList().AddCircleFilled(new Vector2(origin.X + 5f, y), 2.5f, ImGui.GetColorU32(JournalTheme.Margin));
        ImGui.Indent(16f);
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Ink);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.Unindent(16f);
    }

    private static bool TextLink(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, JournalTheme.Name);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X, max.Y - 1f),
            new Vector2(max.X, max.Y - 1f),
            ImGui.GetColorU32(hovered ? JournalTheme.Name : JournalTheme.Muted),
            1f);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return ImGui.IsItemClicked();
    }
}
