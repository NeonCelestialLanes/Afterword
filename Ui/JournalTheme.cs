using System.Numerics;

namespace RPChronicles.Ui;

/// <summary>
/// Navy cloth cover and a cream paper page. Values are red, green, blue, and opacity from 0 to 1.
/// </summary>
internal static class JournalTheme
{
    public static readonly Vector4 CoverDeep = new(0.082f, 0.157f, 0.231f, 1f);
    public static readonly Vector4 Cover = new(0.110f, 0.192f, 0.278f, 1f);
    public static readonly Vector4 CoverHover = new(0.153f, 0.275f, 0.388f, 1f);
    public static readonly Vector4 CoverInk = new(0.957f, 0.937f, 0.902f, 1f);
    public static readonly Vector4 CoverMuted = new(0.718f, 0.765f, 0.820f, 1f);
    public static readonly Vector4 CoverLine = new(0.957f, 0.937f, 0.902f, 0.22f);
    public static readonly Vector4 CoverSelect = new(0.957f, 0.937f, 0.902f, 0.16f);

    public static readonly Vector4 Paper = new(0.969f, 0.953f, 0.918f, 1f);
    public static readonly Vector4 PaperEdge = new(0.910f, 0.878f, 0.816f, 1f);
    public static readonly Vector4 PaperInset = new(0.945f, 0.918f, 0.863f, 1f);
    public static readonly Vector4 Ink = new(0.141f, 0.118f, 0.094f, 1f);
    public static readonly Vector4 Muted = new(0.435f, 0.396f, 0.345f, 1f);
    public static readonly Vector4 Name = new(0.110f, 0.192f, 0.278f, 1f);
    public static readonly Vector4 Emote = new(0.247f, 0.290f, 0.235f, 1f);
    public static readonly Vector4 Rule = new(0.690f, 0.604f, 0.478f, 0.55f);
    public static readonly Vector4 Margin = new(0.831f, 0.325f, 0.290f, 1f);
    public static readonly Vector4 Record = new(0.831f, 0.325f, 0.290f, 1f);
    public static readonly Vector4 Shadow = new(0.04f, 0.07f, 0.11f, 0.18f);
}
