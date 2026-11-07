using FFXIVClientStructs.FFXIV.Client.UI;

namespace RPChronicles.Recording;

/// <summary>
/// The same sixteen chat sounds Snooper offers. None is 0. Sound Effect 1 is 1.
/// </summary>
internal static class LineSounds
{
    public const int Count = 16;

    public static string Name(int id) => id is >= 1 and <= Count ? "Sound Effect " + id : "None";

    public static void Play(int id)
    {
        if (id is < 1 or > Count)
            return;

        try
        {
            UIGlobals.PlayChatSoundEffect((uint)id);
        }
        catch
        {
            // A missing effect should not stop the line from being kept.
        }
    }
}
