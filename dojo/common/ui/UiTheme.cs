using Godot;

namespace Dojo.Common;

/// <summary>
/// 全局 UI 主题（Autoload）。
///
/// 它干两件事：
///   ① 用系统里的中文字体做默认字体（否则界面上的中文全是"豆腐块"）
///   ② 提供两套配色，可以在运行时切换
///
/// 为什么字体要用 SystemFont：
///   Godot 内置的默认字体（Open Sans）不含中日韩字形。
///   常见做法是往 assets/ 里塞一个几十 MB 的 CJK 字体文件；
///   用 SystemFont 则是让 Godot 去问操作系统要，不占仓库体积、也没有授权问题。
///   代价是导出到没有这些字体的机器上会退回系统默认（可能显示方框）——
///   真要发布就应该把字体打包进去。
///
/// 学习要点（对应练习站 s06_ui）：
///   1. Theme 是 Godot 的"样式表"。挂到根 Window 上，所有 Control 子节点都会继承。
///      控制单个节点用 `theme_override_*` 属性（优先级高于 Theme）。
///   2. Theme 是按 **(类型, 名称)** 索引的：
///      `SetColor("font_color", "Button", c)` 的意思是"所有 Button 的 font_color 用这个"。
///      所以改一次 Button 的样式，全工程所有按钮一起变 —— 这就是 Theme 的价值。
///   3. StyleBox 描述"怎么画一个盒子"（背景色、圆角、边框、内边距）。
///      Button 有 normal / hover / pressed / disabled / focus 五个状态，
///      每个都要给一个 StyleBox，否则某个状态会突然变得很丑。
/// </summary>
public partial class UiTheme : Node
{
    public static UiTheme Instance { get; private set; } = null!;

    /// <summary>按优先级排列的候选字体名。系统找不到第一个就顺延到下一个。</summary>
    private static readonly string[] CjkFontCandidates =
    {
        "Microsoft YaHei UI",   // Windows 10/11 微软雅黑
        "Microsoft YaHei",
        "Noto Sans CJK SC",     // Linux / Android 常见
        "Source Han Sans SC",   // 思源黑体
        "PingFang SC",          // macOS
        "WenQuanYi Micro Hei",
        "SimHei",               // 黑体，最后的 Windows 兜底
        "sans-serif",
    };

    /// <summary>两套配色。名字刻意取得直观，方便在游戏里切换时一眼看出区别。</summary>
    public enum Palette
    {
        /// <summary>冷调（默认）：蓝青色强调。</summary>
        Ocean = 0,

        /// <summary>暖调：橙黄色强调。</summary>
        Ember = 1,
    }

    public Palette CurrentPalette { get; private set; } = Palette.Ocean;

    private Font _font = null!;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        _font = new SystemFont
        {
            FontNames = CjkFontCandidates,
            Antialiasing = TextServer.FontAntialiasing.Gray,
        };

        ApplyPalette(CurrentPalette);
        GD.Print($"[UiTheme] default font resolved to: {_font.GetFontName()}");
    }

    public void TogglePalette()
        => ApplyPalette(CurrentPalette == Palette.Ocean ? Palette.Ember : Palette.Ocean);

    public void ApplyPalette(Palette palette)
    {
        CurrentPalette = palette;

        var accent = palette == Palette.Ocean
            ? new Color(0.26f, 0.62f, 0.88f)
            : new Color(0.92f, 0.58f, 0.24f);

        var accentDim = accent.Darkened(0.35f);
        var textColor = palette == Palette.Ocean
            ? new Color(0.88f, 0.92f, 0.98f)
            : new Color(0.99f, 0.94f, 0.88f);

        var theme = new Theme
        {
            DefaultFont = _font,
            DefaultFontSize = 18,
        };

        // ---- Label ----
        theme.SetColor("font_color", "Label", textColor);

        // ---- Button：五个状态都要给，缺一个就会在某个状态下"变丑" ----
        // 正常态的背景直接用强调色压暗，这样"换主题"才看得出来 ——
        // 如果正常态是固定的灰色、只有边框用强调色，两套配色的差别几乎看不见。
        theme.SetStylebox("normal", "Button", MakeButtonBox(accent.Darkened(0.70f), accentDim));
        theme.SetStylebox("hover", "Button", MakeButtonBox(new Color(0.21f, 0.24f, 0.30f), accent));
        theme.SetStylebox("pressed", "Button", MakeButtonBox(accent.Darkened(0.25f), accent));
        theme.SetStylebox("disabled", "Button", MakeButtonBox(new Color(0.13f, 0.14f, 0.17f), new Color(0.22f, 0.23f, 0.26f)));
        theme.SetStylebox("focus", "Button", MakeFocusBox(accent));
        theme.SetColor("font_color", "Button", textColor);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Colors.White);
        theme.SetFontSize("font_size", "Button", 18);

        // ---- PanelContainer：卡片背景 ----
        theme.SetStylebox("panel", "PanelContainer", new StyleBoxFlat
        {
            BgColor = new Color(0.086f, 0.098f, 0.133f, 0.97f),
            BorderColor = accentDim,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
        });

        // ---- 其它常用控件 ----
        theme.SetStylebox("panel", "Panel", new StyleBoxFlat { BgColor = new Color(0.09f, 0.10f, 0.14f, 0.95f) });
        theme.SetColor("font_color", "CheckBox", textColor);
        theme.SetColor("font_color", "CheckButton", textColor);
        theme.SetStylebox("slider", "HSlider", new StyleBoxFlat { BgColor = new Color(0.16f, 0.18f, 0.23f), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        theme.SetStylebox("grabber_area", "HSlider", new StyleBoxFlat { BgColor = accent, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        theme.SetStylebox("fill", "ProgressBar", new StyleBoxFlat { BgColor = accent, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
        theme.SetStylebox("background", "ProgressBar", new StyleBoxFlat { BgColor = new Color(0.14f, 0.16f, 0.20f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });

        GetTree().Root.Theme = theme;
    }

    private static StyleBoxFlat MakeButtonBox(Color bg, Color border) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 16,
        ContentMarginRight = 16,
        ContentMarginTop = 10,
        ContentMarginBottom = 10,
    };

    /// <summary>焦点框：键盘导航时告诉玩家"现在选中的是哪个按钮"。</summary>
    private static StyleBoxFlat MakeFocusBox(Color accent) => new()
    {
        BgColor = new Color(0, 0, 0, 0),
        BorderColor = accent,
        BorderWidthLeft = 3,
        BorderWidthTop = 3,
        BorderWidthRight = 3,
        BorderWidthBottom = 3,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ContentMarginLeft = 16,
        ContentMarginRight = 16,
        ContentMarginTop = 10,
        ContentMarginBottom = 10,
    };
}
