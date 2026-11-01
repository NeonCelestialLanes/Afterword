using System.Numerics;

namespace RPChronicles.Ui;

/// <summary>
/// Navy cloth cover and a paper page. Dark mode keeps the cover and darkens the page.
/// Values are red, green, blue, and opacity from 0 to 1.
/// </summary>
internal static class JournalTheme
{
    public static bool Dark { get; set; }

    public static Vector4 CoverDeep => Dark ? new(0.051f, 0.078f, 0.118f, 1f) : new(0.082f, 0.157f, 0.231f, 1f);

    public static Vector4 Cover => Dark ? new(0.078f, 0.110f, 0.157f, 1f) : new(0.110f, 0.192f, 0.278f, 1f);

    public static Vector4 CoverHover => Dark ? new(0.137f, 0.196f, 0.275f, 1f) : new(0.153f, 0.275f, 0.388f, 1f);

    public static Vector4 CoverInk => new(0.957f, 0.937f, 0.902f, 1f);

    public static Vector4 CoverMuted => Dark ? new(0.620f, 0.659f, 0.722f, 1f) : new(0.718f, 0.765f, 0.820f, 1f);

    public static Vector4 CoverLine => new(0.957f, 0.937f, 0.902f, 0.22f);

    public static Vector4 CoverSelect => new(0.957f, 0.937f, 0.902f, Dark ? 0.12f : 0.16f);

    public static Vector4 Paper => Dark ? new(0.141f, 0.129f, 0.118f, 1f) : new(0.969f, 0.953f, 0.918f, 1f);

    public static Vector4 PaperEdge => Dark ? new(0.180f, 0.165f, 0.149f, 1f) : new(0.910f, 0.878f, 0.816f, 1f);

    public static Vector4 PaperInset => Dark ? new(0.110f, 0.102f, 0.094f, 1f) : new(0.945f, 0.918f, 0.863f, 1f);

    public static Vector4 Ink => Dark ? new(0.933f, 0.906f, 0.847f, 1f) : new(0.141f, 0.118f, 0.094f, 1f);

    public static Vector4 Muted => Dark ? new(0.659f, 0.620f, 0.561f, 1f) : new(0.435f, 0.396f, 0.345f, 1f);

    public static Vector4 Name => Dark ? new(0.733f, 0.800f, 0.878f, 1f) : new(0.110f, 0.192f, 0.278f, 1f);

    public static Vector4 Emote => Dark ? new(0.702f, 0.761f, 0.659f, 1f) : new(0.247f, 0.290f, 0.235f, 1f);

    public static Vector4 Rule => Dark ? new(0.400f, 0.361f, 0.302f, 0.70f) : new(0.690f, 0.604f, 0.478f, 0.55f);

    public static Vector4 Margin => new(0.831f, 0.325f, 0.290f, 1f);

    public static Vector4 Record => Dark ? new(0.863f, 0.447f, 0.400f, 1f) : new(0.831f, 0.325f, 0.290f, 1f);

    public static Vector4 Shadow => Dark ? new(0f, 0f, 0f, 0.40f) : new(0.04f, 0.07f, 0.11f, 0.18f);

    public static Vector4 Frame => Dark ? new(0.180f, 0.165f, 0.149f, 1f) : new(1f, 0.992f, 0.965f, 1f);

    public static Vector4 FrameHover => Dark ? new(0.220f, 0.200f, 0.180f, 1f) : new(1f, 0.976f, 0.940f, 1f);

    public static Vector4 Border => Dark ? new(0.933f, 0.906f, 0.847f, 0.18f) : new(0.141f, 0.118f, 0.094f, 0.28f);

    public static Vector4 PopupHeader => Dark ? new(0.243f, 0.286f, 0.345f, 1f) : new(0.82f, 0.74f, 0.60f, 1f);

    public static Vector4 PopupHeaderHover => Dark ? new(0.302f, 0.357f, 0.431f, 1f) : new(0.74f, 0.64f, 0.48f, 1f);

    public static Vector4 PopupHeaderActive => Dark ? new(0.180f, 0.235f, 0.314f, 1f) : new(0.66f, 0.55f, 0.40f, 1f);
}
