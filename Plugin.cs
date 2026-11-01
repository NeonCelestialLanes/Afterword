using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
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
    private readonly IPluginLog log;
    private readonly SceneRecorder recorder;
    private readonly WindowSystem windowSystem = new("RPChronicles");
    private readonly MainWindow window;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objectTable,
        IDataManager dataManager,
        ITextureProvider textures,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.log = log;

        var settings = RecordingSettings.Load(pluginInterface.GetPluginConfigDirectory());
        var summarySettings = SummarySettings.Load(pluginInterface.GetPluginConfigDirectory());
        var chatGpt = new ChatGptSession(pluginInterface.GetPluginConfigDirectory());
        var grok = new GrokSession(pluginInterface.GetPluginConfigDirectory());
        var store = new NotebookStore(pluginInterface.GetPluginConfigDirectory(), log);
        this.recorder = new SceneRecorder(store, settings, objectTable, playerState, clientState, dataManager, chatGui, log);

        var window = new MainWindow(pluginInterface, textures, this.recorder, summarySettings, chatGpt, grok);
        this.window = window;
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
        this.log.Information("Afterword is ready. Type {Command} to open it.", CommandName);
    }

    public void Dispose()
    {
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
        if (!string.IsNullOrWhiteSpace(value.TextValue))
            return value.TextValue.Trim();

        var extracted = original.ExtractText();
        return string.IsNullOrWhiteSpace(extracted) ? string.Empty : extracted.Trim();
    }
}
