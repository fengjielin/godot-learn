using Godot;

namespace Dojo.Stations;

/// <summary>三种生成算法。**同一个种子 + 同一个算法 = 同一张地图**，这是本站的核心。</summary>
public enum GenAlgorithm
{
    /// <summary>随机撒点。**故意不保证连通** —— 用来演示为什么必须验证。</summary>
    RandomScatter,

    /// <summary>FastNoiseLite 洞穴。噪声是连续的，所以天然会形成"洞"。</summary>
    NoiseCave,

    /// <summary>BSP 房间 + 走廊。**结构性生成，天然连通**。</summary>
    BspRooms,
}

/// <summary>
/// 地牢生成器。**纯算法，不碰任何节点** ——
/// 这一点很重要：把"算什么"和"画什么"分开，才能单独测量各自的耗时
/// （任务 ⑤：100ms 里有多少是算法，有多少是建节点）。
///
/// ★ 关于"可复现"：
///   **绝不要用 `GD.Randf()` 这类全局随机函数做生成。**
///   全局随机状态会被游戏里任何一处随机调用污染 —— 你在生成前多捡一个金币、
///   多播一次音效随机音高，地图就变了。**必须用自己持有的、由种子初始化的
///   `RandomNumberGenerator`。**
/// </summary>
public sealed class DungeonGenerator
{
    public const int Width = 40;
    public const int Height = 20;
    public const float CellSize = 28f;

    /// <summary>true = 墙，false = 可走。</summary>
    public bool[,] Grid { get; private set; } = new bool[Width, Height];

    public int Seed { get; private set; }
    public GenAlgorithm Algorithm { get; private set; }
    public int OpenCells { get; private set; }
    public int RegionCount { get; private set; }
    public int LargestRegion { get; private set; }
    public bool IsFullyConnected => RegionCount == 1 && OpenCells > 0;

    /// <summary>地图内容的指纹。**同种子两次生成应该得到同一个指纹。**</summary>
    public string Fingerprint { get; private set; } = "";

    public void Generate(int seed, GenAlgorithm algorithm)
    {
        Seed = seed;
        Algorithm = algorithm;

        // ★ 每次生成都用一个**全新的、只由 seed 决定**的随机数发生器。
        //   做不到这一点，"同种子同地图"就只是运气。
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };

        Grid = algorithm switch
        {
            GenAlgorithm.RandomScatter => BuildRandom(rng),
            GenAlgorithm.NoiseCave => BuildCave(seed),
            _ => BuildBsp(rng),
        };

        AnalyzeConnectivity();
        Fingerprint = ComputeFingerprint();
    }

    // ---------- 算法一：随机撒点 ----------

    private static bool[,] BuildRandom(RandomNumberGenerator rng)
    {
        var grid = new bool[Width, Height];
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            grid[x, y] = x == 0 || y == 0 || x == Width - 1 || y == Height - 1 || rng.Randf() < 0.42f;
        return grid;
    }

    // ---------- 算法二：噪声洞穴 ----------

    private static bool[,] BuildCave(int seed)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.085f,
            FractalOctaves = 3,
        };

        var grid = new bool[Width, Height];
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1) { grid[x, y] = true; continue; }
            grid[x, y] = noise.GetNoise2D(x, y) > 0.08f;
        }
        return grid;
    }

    // ---------- 算法三：BSP 房间 + 走廊 ----------

    private static bool[,] BuildBsp(RandomNumberGenerator rng)
    {
        var grid = new bool[Width, Height];
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            grid[x, y] = true;

        var rooms = new List<Rect2I>();
        Split(new Rect2I(1, 1, Width - 2, Height - 2), 0, rng, rooms);

        // 挖房间
        foreach (var room in rooms)
            for (var x = room.Position.X; x < room.End.X; x++)
            for (var y = room.Position.Y; y < room.End.Y; y++)
                grid[x, y] = false;

        // 把相邻房间用 L 形走廊连起来 —— **连通性由这个循环保证，不是碰运气**
        for (var i = 1; i < rooms.Count; i++)
            CarveCorridor(grid, rng, Center(rooms[i - 1]), Center(rooms[i]));

        return grid;
    }

    private static void Split(Rect2I area, int depth, RandomNumberGenerator rng, List<Rect2I> rooms)
    {
        const int minSize = 7;
        if (depth >= 4 || (area.Size.X < minSize * 2 && area.Size.Y < minSize * 2))
        {
            rooms.Add(area);
            return;
        }

        var horizontal = area.Size.X < area.Size.Y;
        if (!horizontal && area.Size.X < minSize * 2) horizontal = true;
        if (horizontal && area.Size.Y < minSize * 2) horizontal = false;

        if (horizontal)
        {
            var cut = rng.RandiRange(minSize, area.Size.Y - minSize);
            Split(new Rect2I(area.Position, new Vector2I(area.Size.X, cut)), depth + 1, rng, rooms);
            Split(new Rect2I(area.Position + new Vector2I(0, cut), new Vector2I(area.Size.X, area.Size.Y - cut)), depth + 1, rng, rooms);
        }
        else
        {
            var cut = rng.RandiRange(minSize, area.Size.X - minSize);
            Split(new Rect2I(area.Position, new Vector2I(cut, area.Size.Y)), depth + 1, rng, rooms);
            Split(new Rect2I(area.Position + new Vector2I(cut, 0), new Vector2I(area.Size.X - cut, area.Size.Y)), depth + 1, rng, rooms);
        }
    }

    private static Vector2I Center(Rect2I room) => room.Position + room.Size / 2;

    private static void CarveCorridor(bool[,] grid, RandomNumberGenerator rng, Vector2I from, Vector2I to)
    {
        // 先横后竖或先竖后横，随机选一种 —— 两种都通，只是长相不同
        var horizontalFirst = rng.Randf() < 0.5f;

        var x = from.X;
        var y = from.Y;
        if (horizontalFirst)
        {
            while (x != to.X) { grid[x, y] = false; x += Math.Sign(to.X - x); }
            while (y != to.Y) { grid[x, y] = false; y += Math.Sign(to.Y - y); }
        }
        else
        {
            while (y != to.Y) { grid[x, y] = false; y += Math.Sign(to.Y - y); }
            while (x != to.X) { grid[x, y] = false; x += Math.Sign(to.X - x); }
        }
        grid[to.X, to.Y] = false;
    }

    // ---------- 连通性验证（任务 ③） ----------

    /// <summary>
    /// 洪水填充（flood fill）：从每个未访问的可走格子出发，把相连的一整片标出来。
    /// 片的数量就是"连通区个数" —— **1 才说明整张地图是通的。**
    ///
    /// ★ 为什么必须验证：随机撒点的地图**大概率是不连通的**。
    ///   如果玩家出生在 A 区、出口在 B 区，游戏直接卡死 ——
    ///   而且这种 bug 只在某些种子下出现，靠手动试玩几乎不可能发现。
    ///   **程序化生成必须自带验证，因为"你没法把每一张地图都玩一遍"。**
    /// </summary>
    private void AnalyzeConnectivity()
    {
        var visited = new bool[Width, Height];
        var queue = new Queue<Vector2I>();
        RegionCount = 0;
        LargestRegion = 0;
        OpenCells = 0;

        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            if (!Grid[x, y]) OpenCells++;

        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            if (Grid[x, y] || visited[x, y]) continue;

            RegionCount++;
            var size = 0;
            queue.Clear();
            queue.Enqueue(new Vector2I(x, y));
            visited[x, y] = true;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                size++;

                foreach (var offset in Neighbors)
                {
                    var nx = cell.X + offset.X;
                    var ny = cell.Y + offset.Y;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    if (Grid[nx, ny] || visited[nx, ny]) continue;

                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2I(nx, ny));
                }
            }

            LargestRegion = Mathf.Max(LargestRegion, size);
        }
    }

    private static readonly Vector2I[] Neighbors =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>
    /// 把除最大连通区以外的所有可走格子**填成墙**。
    /// 这是"让随机地图可用"最省事的做法 —— 代价是丢掉了一部分空间。
    /// 更讲究的做法是把各个区域用走廊连起来（BSP 算法就是这么干的）。
    /// </summary>
    public int KeepLargestRegionOnly()
    {
        var visited = new bool[Width, Height];
        var queue = new Queue<Vector2I>();

        // 先找到最大的那片
        var best = new List<Vector2I>();
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            if (Grid[x, y] || visited[x, y]) continue;

            var region = new List<Vector2I>();
            queue.Clear();
            queue.Enqueue(new Vector2I(x, y));
            visited[x, y] = true;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                region.Add(cell);
                foreach (var offset in Neighbors)
                {
                    var nx = cell.X + offset.X;
                    var ny = cell.Y + offset.Y;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    if (Grid[nx, ny] || visited[nx, ny]) continue;
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2I(nx, ny));
                }
            }

            if (region.Count > best.Count) best = region;
        }

        // 然后把它以外的全部填掉
        var kept = new bool[Width, Height];
        foreach (var cell in best) kept[cell.X, cell.Y] = true;

        var filled = 0;
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            if (!Grid[x, y] && !kept[x, y]) { Grid[x, y] = true; filled++; }

        AnalyzeConnectivity();
        Fingerprint = ComputeFingerprint();
        return filled;
    }

    /// <summary>把每一格写成一个字符，得到一份内容指纹。</summary>
    private string ComputeFingerprint()
    {
        var hash = 17;
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            hash = unchecked(hash * 31 + (Grid[x, y] ? 1 : 0));
        return hash.ToString("X8");
    }

    /// <summary>取一个可走格子的世界坐标（用来放玩家）。</summary>
    public Vector2 CellToWorld(Vector2I cell, Vector2 origin)
        => origin + new Vector2(cell.X * CellSize + CellSize / 2f, cell.Y * CellSize + CellSize / 2f);

    public Vector2I FindOpenCell()
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            if (!Grid[x, y]) return new Vector2I(x, y);
        return new Vector2I(1, 1);
    }

    /// <summary>把墙的矩形交出去 —— 碰撞、导航、画面**都用这一份数据**（S08 的纪律）。</summary>
    public IEnumerable<Rect2> WallRects(Vector2 origin)
    {
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            if (!Grid[x, y]) continue;
            yield return new Rect2(origin + new Vector2(x * CellSize, y * CellSize),
                new Vector2(CellSize, CellSize));
        }
    }
}
