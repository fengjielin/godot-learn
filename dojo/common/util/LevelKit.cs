using Godot;

namespace Dojo.Common;

/// <summary>
/// 关卡搭建工具箱 —— 用代码快速造出常见的几何与节点。
///
/// 为什么需要它：
///   练功房有 16 个练习站，每个站都需要「房间边界、地板、几个色块、一行说明文字」。
///   如果每个站都在 .tscn 里手摆一遍，改一次颜色要改 16 个场景。
///   这类「程序化搭场景」的做法在真实项目里也很常见 —— 尤其是关卡由数据生成的游戏。
///
/// 学习要点：
///   1. Godot 里没有内置的「圆」图元。要画圆就得用足够多的多边形顶点去逼近，
///      这就是 CirclePolygon 存在的原因（段数越高越圆，但顶点也越多）。
///   2. 碰撞形状和视觉表现是两套东西：Polygon2D 只负责"看起来像"，
///      CollisionShape2D 才负责"撞起来像"。两者可以不一致 —— 但别差太多，否则玩家会觉得被骗。
///   3. 新建节点后设属性再 AddChild 更安全，因为 AddChild 会立刻触发 _Ready 和 _EnterTree。
/// </summary>
public static class LevelKit
{
    /// <summary>生成一个矩形的多边形顶点（以中心为原点）。</summary>
    public static Vector2[] RectPoints(Vector2 size)
    {
        var h = size / 2f;
        return new[]
        {
            new Vector2(-h.X, -h.Y),
            new Vector2(h.X, -h.Y),
            new Vector2(h.X, h.Y),
            new Vector2(-h.X, h.Y),
        };
    }

    /// <summary>用一个正多边形逼近圆。segments 建议 16~32。</summary>
    public static Vector2[] CirclePoints(float radius, int segments = 24)
    {
        segments = Mathf.Max(3, segments);
        var points = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var angle = Mathf.Tau * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    /// <summary>造一个矩形色块。position 是中心点。</summary>
    public static Polygon2D MakeRect(Node parent, Vector2 position, Vector2 size, Color color, string name = "Rect")
    {
        var node = new Polygon2D
        {
            Name = name,
            Color = color,
            Polygon = RectPoints(size),
            Position = position,
        };
        parent.AddChild(node);
        return node;
    }

    /// <summary>造一个圆形色块。position 是中心点。</summary>
    public static Polygon2D MakeCircle(Node parent, Vector2 position, float radius, Color color, int segments = 28, string name = "Circle")
    {
        var node = new Polygon2D
        {
            Name = name,
            Color = color,
            Polygon = CirclePoints(radius, segments),
            Position = position,
        };
        parent.AddChild(node);
        return node;
    }

    /// <summary>造一个带描边的圆形色块（外圈 + 内圈两层）。</summary>
    public static Polygon2D MakeRing(Node parent, Vector2 position, float radius, float thickness, Color color, string name = "Ring")
    {
        // Polygon2D 不支持挖洞，所以这里用 Line2D 画一个闭合的粗线圆 —— 这才是"环"的正确做法。
        var points = new Vector2[33];
        for (var i = 0; i <= 32; i++)
        {
            var angle = Mathf.Tau * i / 32f;
            points[i] = position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        var line = new Line2D
        {
            Name = name,
            Width = thickness,
            Closed = true,
            DefaultColor = color,
            Points = points,
            Antialiased = true,
        };
        parent.AddChild(line);

        // 返回一个同心圆只是为了保持签名一致（调用方多数只关心"有个环"）。
        return MakeCircle(parent, position, radius, new Color(0, 0, 0, 0), 8, name + "Anchor");
    }

    /// <summary>
    /// 造一个世界坐标里的文字标签。
    ///
    /// 定位约定（很容易踩坑，所以写清楚）：
    ///   · align = Center → 文字的**中心**落在 position 上
    ///   · align = Left   → 文字的**左端**从 position.X 开始
    ///   · 两种情况下 position.Y 都是文字矩形的**上边**
    ///
    /// 之所以要这样定：Label 是个矩形 Control，它并不能"围绕某个点"定位。
    /// 想让文字居中，就必须把矩形自己往左挪半个宽度 —— 否则文字会整体右偏半宽，
    /// 而且这种偏移在单行短文字上很不显眼，等到长文字才发现，很难查。
    /// </summary>
    public static Label MakeLabel(
        Node parent,
        Vector2 position,
        string text,
        int fontSize = 16,
        Color? color = null,
        HorizontalAlignment align = HorizontalAlignment.Center,
        string name = "Label",
        float width = 320f)
    {
        var left = align == HorizontalAlignment.Center
            ? position.X - width / 2f
            : position.X;

        var label = new Label
        {
            Name = name,
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(left, position.Y),
            Size = new Vector2(width, 40f),
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        // 描边让文字在任何背景上都看得清
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 5);
        parent.AddChild(label);
        return label;
    }

    /// <summary>要建哪几面墙。用 [Flags] 是为了能组合出「有左右墙但没有地板」这种常见需求。</summary>
    [Flags]
    public enum WallSides
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,

        /// <summary>四面都有：俯视角房间的标准做法。</summary>
        All = Top | Bottom | Left | Right,

        /// <summary>只有左右两面。平台跳跃站用它 —— 没有地板，掉下去才是真的掉下去。</summary>
        SidesOnly = Left | Right,
    }

    /// <summary>
    /// 给一个矩形区域加上不可见的墙。
    /// rect 是「可活动区域」，墙会紧贴它外侧建立。
    /// </summary>
    public static StaticBody2D CreateBorderWalls(Node parent, Rect2 playableArea, float thickness = 48f, string name = "Walls")
        => CreateWalls(parent, playableArea, WallSides.All, thickness, name);

    /// <summary>按需创建指定几面墙。</summary>
    public static StaticBody2D CreateWalls(
        Node parent,
        Rect2 playableArea,
        WallSides sides,
        float thickness = 48f,
        string name = "Walls")
    {
        var body = new StaticBody2D
        {
            Name = name,
            CollisionLayer = GameLayers.World,
            CollisionMask = 0,
        };
        parent.AddChild(body);

        var center = playableArea.GetCenter();
        var end = playableArea.End;
        var spanX = playableArea.Size.X + thickness * 2f;

        if (sides.HasFlag(WallSides.Top))
            AddWallShape(body, new Vector2(center.X, playableArea.Position.Y - thickness / 2f),
                new Vector2(spanX, thickness), "Top");

        if (sides.HasFlag(WallSides.Bottom))
            AddWallShape(body, new Vector2(center.X, end.Y + thickness / 2f),
                new Vector2(spanX, thickness), "Bottom");

        if (sides.HasFlag(WallSides.Left))
            AddWallShape(body, new Vector2(playableArea.Position.X - thickness / 2f, center.Y),
                new Vector2(thickness, playableArea.Size.Y), "Left");

        if (sides.HasFlag(WallSides.Right))
            AddWallShape(body, new Vector2(end.X + thickness / 2f, center.Y),
                new Vector2(thickness, playableArea.Size.Y), "Right");

        return body;
    }

    /// <summary>
    /// 造一块「能站上去的平台」：一个 StaticBody2D + 矩形碰撞 + 顶部高亮条。
    /// topLeft 与 size 描述平台本体（size.Y 就是厚度，视觉上会画成实心块）。
    /// </summary>
    public static StaticBody2D MakePlatform(
        Node parent,
        Vector2 topLeft,
        Vector2 size,
        Color fill,
        string name = "Platform")
    {
        var body = new StaticBody2D
        {
            Name = name,
            CollisionLayer = GameLayers.World,
            CollisionMask = 0,
        };
        parent.AddChild(body);

        MakeRect(body, topLeft + size / 2f, size, fill, "Body");
        // 顶面亮一条，让玩家一眼看出"这里可以站"
        MakeRect(body, topLeft + new Vector2(size.X / 2f, 3f), new Vector2(size.X, 6f), fill.Lightened(0.45f), "Top");

        AddWallShape(body, topLeft + size / 2f, size, "Shape");
        return body;
    }

    /// <summary>往一个物理体上挂一块矩形碰撞形状。center 是中心点。</summary>
    public static CollisionShape2D AddWallShape(Node parent, Vector2 center, Vector2 size, string name = "Shape")
    {
        var shape = new RectangleShape2D { Size = size };
        var node = new CollisionShape2D
        {
            Name = name,
            Shape = shape,
            Position = center,
        };
        parent.AddChild(node);
        return node;
    }
}
