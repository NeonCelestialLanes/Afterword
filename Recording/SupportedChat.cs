using Dalamud.Game.Text;
using RPChronicles.Data;

namespace RPChronicles.Recording;

/// <summary>
/// Player chat only. Combat, loot, echoes, and other system lines have no channel here, so they are ignored.
/// </summary>
internal static class SupportedChat
{
    public static bool TryClassify(XivChatType type, out ChatChannel channel)
    {
        channel = type switch
        {
            XivChatType.Say => ChatChannel.Say,
            XivChatType.CustomEmote or XivChatType.StandardEmote => ChatChannel.Emote,
            XivChatType.Shout => ChatChannel.Shout,
            XivChatType.Yell => ChatChannel.Yell,
            XivChatType.TellIncoming or XivChatType.TellOutgoing => ChatChannel.Tell,
            XivChatType.Party or XivChatType.CrossParty => ChatChannel.Party,
            XivChatType.Alliance => ChatChannel.Alliance,
            XivChatType.FreeCompany => ChatChannel.FreeCompany,
            XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
                or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => ChatChannel.Linkshell,
            XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3
                or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
                or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => ChatChannel.CrossWorldLinkshell,
            XivChatType.NoviceNetwork => ChatChannel.NoviceNetwork,
            _ => ChatChannel.Say,
        };

        return type is XivChatType.Say
            or XivChatType.CustomEmote or XivChatType.StandardEmote
            or XivChatType.Shout or XivChatType.Yell
            or XivChatType.TellIncoming or XivChatType.TellOutgoing
            or XivChatType.Party or XivChatType.CrossParty
            or XivChatType.Alliance
            or XivChatType.FreeCompany
            or XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
            or XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3
            or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
            or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
            or XivChatType.NoviceNetwork;
    }

    public static LineKind KindFor(ChatChannel channel) =>
        channel == ChatChannel.Emote ? LineKind.Emote : LineKind.Say;

    public static string Label(ChatChannel channel) => channel switch
    {
        ChatChannel.Say => "Say",
        ChatChannel.Emote => "Emote",
        ChatChannel.Shout => "Shout",
        ChatChannel.Yell => "Yell",
        ChatChannel.Tell => "Tell",
        ChatChannel.Party => "Party",
        ChatChannel.Alliance => "Alliance",
        ChatChannel.FreeCompany => "Free Company",
        ChatChannel.Linkshell => "Linkshell",
        ChatChannel.CrossWorldLinkshell => "Cross-world linkshell",
        ChatChannel.NoviceNetwork => "Novice Network",
        ChatChannel.Random => "Random rolls",
        _ => "Say",
    };
}
