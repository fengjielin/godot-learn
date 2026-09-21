using Godot;

namespace Dojo.Common;

/// <summary>
/// 导航网格工具。
///
/// ## 导航网格到底是什么
///   它就是**一组互相连通的凸多边形**，拼出关卡里"能走的地方"。
///   寻路算法（A*）在这个图上游走，找出从 A 到 B 的折线路径。
///   所以"敌人会绕开墙"这件事，**不是 AI 聪明，而是导航网格里根本就没有墙那一块**。
///   理解这一点之后，你就不会再把导航当成黑魔法。
///
/// ## 为什么这里用代码生成，而不是在编辑器里点「烘焙」
///   编辑器里的「烘焙」（Bake NavigationPolygon）做的正是同一件事：
///   读场景里的静态碰撞体 → 扣除掉它们 → 把剩下的区域切成多边形。
///   本工程改用代码生成，理由有两条：
///     ① 结果**在任何机器上完全一致**，自动化验证才有意义
///        （编辑器烘焙的结果会随版本、设置、甚至浮点差异变化）；
///     ② 程序化生成的关卡（S15）本来就无法预先烘焙 —— 它得在运行时生成。
///
/// ## 用法
///   `BuildFromWalls(可走区域, 所有墙的矩形)` —— 把区域切成格子，去掉被墙覆盖的格子，
///   再把每个空格子输出成一个四边形多边形。
///
/// 代价说明：格子越细，网格越贴合、多边形越多。40 像素的格子对这个规模足够了；
/// 真实项目里应该用更聪明的算法（比如把相邻空格子合并成大矩形）来减少多边形数。
/// </summary>
public static class NavMeshBuilder
{
    public static NavigationPolygon BuildFromWalls(Rect2 area, IEnumerable<Rect2> walls, float cell = 40f)
    {
        var wallList = new List<Rect2>(walls);

        var polygon = new NavigationPolygon();
        polygon.AgentRadius = 0f; // 我们用格子近似，半径由格子大小隐含表达

        // Godot 4.7 的 NavigationPolygon 是"先一次性给全部顶点，再用下标描述每个多边形"：
        //   set_vertices(所有顶点)  →  add_polygon(这个多边形的顶点下标)
        // 没有 add_vertex 这种逐个追加的接口（4.6 之前有，后来去掉了）。
        var vertices = new List<Vector2>();
        var polygons = new List<int[]>();

        var cols = Mathf.CeilToInt(area.Size.X / cell);
        var rows = Mathf.CeilToInt(area.Size.Y / cell);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var topLeft = area.Position + new Vector2(col * cell, row * cell);
                var quad = new Rect2(topLeft, new Vector2(cell, cell));

                if (IsBlocked(quad, wallList)) continue;
                AddQuad(vertices, polygons, quad);
            }
        }

        polygon.SetVertices(vertices.ToArray());
        foreach (var indices in polygons)
            polygon.AddPolygon(indices);

        return polygon;
    }

    private static bool IsBlocked(Rect2 quad, List<Rect2> walls)
    {
        foreach (var wall in walls)
            if (wall.Intersects(quad)) return true;
        return false;
    }

    private static void AddQuad(List<Vector2> vertices, List<int[]> polygons, Rect2 rect)
    {
        var start = vertices.Count;
        vertices.Add(rect.Position);
        vertices.Add(new Vector2(rect.End.X, rect.Position.Y));
        vertices.Add(rect.End);
        vertices.Add(new Vector2(rect.Position.X, rect.End.Y));
        polygons.Add(new[] { start, start + 1, start + 2, start + 3 });
    }
}
