using System;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Text.ReadOnly;
using RPChronicles.Data;
using RPChronicles.Insights;
using RPChronicles.Recording;
using RPChronicles.Ui;

namespace RPChronicles;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/afw";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IContextMenu contextMenu;
    private readonly IPluginLog log;
    private readonly SceneRecorder recorder;
    private readonly WindowSystem windowSystem = new("RPChronicles");
    private readonly MainWindow window;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IContextMenu contextMenu,
        IPlayerState playerState,
        IObjectTable objectTable,
        ITargetManager targets,
        IDataManager dataManager,
        ITextureProvider textures,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.contextMenu = contextMenu;
        this.log = log;

        var settings = RecordingSettings.Load(pluginInterface.GetPluginConfigDirectory());
        var summarySettings = SummarySettings.Load(pluginInterface.GetPluginConfigDirectory());
        var chatGpt = new ChatGptSession(pluginInterface.GetPluginConfigDirectory());
        var grok = new GrokSession(pluginInterface.GetPluginConfigDirectory());
        var store = new NotebookStore(pluginInterface.GetPluginConfigDirectory(), log);
        if (!string.IsNullOrWhiteSpace(settings.LogFolder))
        {
            store.UseLogFolder(settings.LogFolder, out var opened);
            if (!opened)
            {
                settings.LogFolder = string.Empty;
                settings.Save();
            }
        }

        this.recorder = new SceneRecorder(store, settings, objectTable, targets, playerState, clientState, dataManager, chatGui, log);

        var window = new MainWindow(pluginInterface, textures, this.recorder, summarySettings, chatGpt, grok);
        this.window = window;
        this.recorder.JournalIsOpen = () => window.IsOpen;
        this.windowSystem.AddWindow(window);

        if (commandManager.Commands.ContainsKey(CommandName))
            commandManager.RemoveHandler(CommandName);

        commandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Afterword journal.",
        });

        pluginInterface.UiBuilder.Draw += this.DrawUi;
        pluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        chatGui.ChatMessage += this.OnChatMessage;
        clientState.Login += this.OnLogin;
        clientState.Logout += this.OnLogout;
        contextMenu.OnMenuOpened += this.OnMenuOpened;
        this.log.Information("Afterword is ready. Type {Command} to open it.", CommandName);
    }

    public void Dispose()
    {
        this.contextMenu.OnMenuOpened -= this.OnMenuOpened;
        this.clientState.Logout -= this.OnLogout;
        this.clientState.Login -= this.OnLogin;
        this.chatGui.ChatMessage -= this.OnChatMessage;
        this.pluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        this.pluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        this.pluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.commandManager.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
        this.window.Dispose();
        this.recorder.Flush();
    }

    private void OnCommand(string command, string args) => this.windowSystem.Windows[0].IsOpen = true;

    private void OpenMain() => this.windowSystem.Windows[0].IsOpen = true;

    private void OpenConfig() => this.window.OpenSettings();

    private void DrawUi()
    {
        this.windowSystem.Draw();
        this.window.DrawDialogs();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            if (!TryPlayer(args, out var name))
                return;

            var already = this.window.IsInRecording(name);
            args.AddMenuItem(new MenuItem
            {
                Name = already ? "In the recording" : "Add to the recording",
                IsEnabled = !already,
                UseDefaultPrefix = true,
                OnClicked = _ => this.window.AddToRecording(name),
            });
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not add the recording option to the menu.");
        }
    }

    private static bool TryPlayer(IMenuOpenedArgs args, out string name)
    {
        name = string.Empty;
        if (args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target)
            return false;

        var raw = target.TargetName;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (target.TargetObject != null)
        {
            if (target.TargetObject.ObjectKind != ObjectKind.Pc)
                return false;
        }
        else if (target.TargetContentId == 0 && target.TargetCharacter == null)
        {
            return false;
        }

        name = PlayerNames.Clean(raw);
        return name.Length > 0;
    }

    private void OnLogin() => this.recorder.OnLogin();

    private void OnLogout(int type, int code) => this.recorder.OnLogout();

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var text = ReadText(message.Message, message.OriginalMessage);
            var speaker = ReadText(message.Sender, message.OriginalSender);
            this.recorder.Capture(message.LogKind, speaker, text, message.Timestamp);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Could not read a chat line.");
        }
    }

    private static string ReadText(SeString value, ReadOnlySeString original)
    {
        var current = value.TextValue?.Trim() ?? string.Empty;
        var extracted = string.Empty;
        try
        {
            extracted = original.ExtractText()?.Trim() ?? string.Empty;
        }
        catch
        {
            extracted = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(current))
            return extracted;

        if (string.IsNullOrWhiteSpace(extracted))
            return current;

        return LetterCount(extracted) > LetterCount(current) ? extracted : current;
    }

    private static int LetterCount(string text)
    {
        var count = 0;
        foreach (var letter in text)
        {
            if (char.IsLetter(letter))
                count++;
        }

        return count;
    }
}
