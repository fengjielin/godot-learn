namespace Dojo.Game;

/// <summary>
/// 练功房的全部课程目录 —— 整个项目的「学习地图」。
///
/// 为什么把 16 个站集中写在这里：
///   枢纽里的传送门、进度统计、文档索引全都由这一份清单驱动。
///   想加一个练习站，只需要在这里加一条 + 建好对应目录，不需要改 UI 代码。
///   这也是「数据驱动」最朴素的一种形式（更进阶的做法见练习站 s09_data：用自定义 Resource + .tres）。
///
/// 读法建议：
///   每个练习站都按「先玩 → 再改 → 再重写」三步走。
///   Implemented = false 的站还没建好，枢纽里会显示为「待建设」。
/// </summary>
public static class StationCatalog
{
    public static readonly StationInfo[] All =
    {
        new(
            Id: "s01_lifecycle",
            Order: 1,
            Title: "节点与生命周期",
            Skill: "场景树 / 生命周期 / 信号 / 分组",
            Summary: "节点什么时候被创建、初始化、逐帧更新、销毁；用信号和分组代替直接引用。",
            Tasks: new[]
            {
                "让屏幕上同时存在 10 个游走方块（按空格生成）",
                "把 Wanderer 的存活时间从 3 秒改成 8 秒，观察 _ExitTree 何时打印",
                "给 Wanderer 加一个 OnBounced 信号，撞墙时通知本站并显示弹跳次数",
                "用 GetTree().CallGroup 给所有方块下指令，按 G 键让它们全部冲向中心",
                "思考：为什么在 _Ready 里 GetNode 是安全的，而在 _EnterTree 里不一定？",
            },
            Implemented: true),

        new(
            Id: "s02_input",
            Order: 2,
            Title: "输入与手感",
            Skill: "InputMap / 轮询 vs 事件 / 输入缓冲 / 土狼时间",
            Summary: "输入不只是「按键」。好手感来自缓冲窗口、容错时间和状态互斥。",
            Tasks: new[]
            {
                "实现跳跃缓冲：落地前 0.12 秒内按下的跳跃要记住",
                "实现土狼时间：离开平台后 0.1 秒内仍然可以起跳",
                "实现可变跳跃高度：提前松手就截断上升速度",
                "加冲刺：Shift + 方向，0.5 秒冷却，冲刺过程中无敌",
                "分别用 Input.IsActionPressed（轮询）和 _Input（事件）各实现一个功能，说出区别",
            },
            Implemented: true),

        new(
            Id: "s03_physics",
            Order: 3,
            Title: "物理与碰撞",
            Skill: "碰撞层/掩码 / CharacterBody2D / Area2D / RayCast2D",
            Summary: "层＝我是什么，掩码＝我检测什么。这两个搞错，碰撞就永远不触发。",
            Tasks: new[]
            {
                "只用碰撞层设置（不写代码）让子弹只打敌人、不打墙",
                "实现推箱子：把箱子推到指定位置",
                "用 RayCast2D 做视线判定：被墙挡住时守卫看不到你",
                "用 Area2D 做伤害区域，并提示进入/离开",
                "打开 调试 -> 可见碰撞形状，对照层设置观察",
            },
            Implemented: true),

        new(
            Id: "s04_fsm",
            Order: 4,
            Title: "状态机",
            Skill: "有限状态机 / 状态转移 / 与动画同步",
            Summary: "把「一堆 if-else 决定角色该干什么」换成显式的状态与转移条件。",
            Tasks: new[]
            {
                "读懂守卫的 巡逻 → 警觉 → 追击 → 攻击 转移条件",
                "新增「眩晕」状态：被石头砸中后进入 2 秒",
                "给状态加 OnEnter/OnExit，进入攻击时播放特效",
                "把转移条件抽成一张表，比较改起来是不是更容易",
                "思考：追击与受伤同时发生时，优先级该怎么定？",
            },
            Implemented: true),

        new(
            Id: "s05_camera",
            Order: 5,
            Title: "相机与打击感",
            Skill: "Camera2D / 平滑跟随 / 屏幕震动 / 顿帧",
            Summary: "同样的数值，加不加震动和顿帧，是两个游戏。",
            Tasks: new[]
            {
                "实现屏幕震动：撞墙时按力度抖动并衰减",
                "实现顿帧：受击时把 Engine.TimeScale 降到 0.05 持续 0.06 秒",
                "给相机加 limits，让它不越出关卡边界",
                "加「前瞻」：相机朝鼠标方向最多偏移 40 像素",
                "对比：位置直接跟随 vs 平滑插值，哪个更舒服？",
            },
            Implemented: true),

        new(
            Id: "s06_ui",
            Order: 6,
            Title: "UI 与 HUD",
            Skill: "Control / 容器布局 / 锚点 / 主题 / 暂停菜单",
            Summary: "用容器做布局，而不是手动摆像素；一次就把分辨率适配做好。",
            Tasks: new[]
            {
                "用 VBoxContainer + MarginContainer 重做这块 HUD，改窗口大小看效果",
                "做暂停菜单：ESC 打开，键鼠与手柄都能导航，暂停时游戏冻结",
                "给按钮统一做一套 Theme，改字号和颜色",
                "加设置面板：主音量滑条 + 全屏开关，并写进存档",
                "思考：为什么 ui_cancel 这类内置动作不用自己定义？",
            },
            Implemented: true),

        new(
            Id: "s07_combat",
            Order: 7,
            Title: "战斗与数值管线",
            Skill: "Hitbox/Hurtbox / 伤害计算 / 无敌帧 / 击退",
            Summary: "战斗是一整条数据管线，不是「碰到就减血」。",
            Tasks: new[]
            {
                "让伤害先扣护盾、再扣血，并显示护盾条",
                "加入暴击：15% 概率双倍伤害，飘字变黄",
                "受击后 0.5 秒无敌帧，角色闪烁提示",
                "加击退：方向 = 攻击者 → 受击者，力度可调",
                "给伤害飘字做对象池，避免每次都 Instantiate",
            },
            Implemented: true),

        new(
            Id: "s08_ai",
            Order: 8,
            Title: "敌人 AI 与寻路",
            Skill: "感知 / 导航网格 / 群体分离",
            Summary: "会绕路、会被墙挡住视线、会互相避让的，才像个敌人。",
            Tasks: new[]
            {
                "加视野锥：朝向 ±60° 且在 300 像素内才看得到你",
                "加听觉：冲刺产生的噪音会吸引附近敌人",
                "用 NavigationRegion2D 烘焙导航网格，让敌人绕开墙",
                "加分离力：5 个敌人一起追时不要叠在一起",
                "加「丢失目标后搜索最后已知位置」的行为",
            },
            Implemented: true),

        new(
            Id: "s09_data",
            Order: 9,
            Title: "数据驱动",
            Skill: "自定义 Resource / .tres 配置 / 热调参",
            Summary: "把数值从代码里搬进 .tres 文件，你才敢放心地改。",
            Tasks: new[]
            {
                "新建一个 WeaponData.tres，做出「大剑：慢、重、范围大」",
                "给 WeaponData 加一个 [Export] 字段，让它在调参台上生效",
                "把 s07 战斗站里写死的伤害数字换成读 WeaponData",
                "加一个「随机武器」按钮，体会配置驱动的灵活性",
                "思考：什么该进 Resource，什么不该？",
            },
            Implemented: true),

        new(
            Id: "s10_inventory",
            Order: 10,
            Title: "背包与物品",
            Skill: "数据与视图分离 / 拖拽 / 堆叠 / 装备",
            Summary: "背包的难点从来不是 UI，而是「数据模型」和「它怎么显示」必须分开。",
            Tasks: new[]
            {
                "实现堆叠：同种物品最多叠 5 个，超出占新格子",
                "实现拖拽交换两个格子",
                "加装备槽，装备后移动速度变化",
                "加右键使用物品：药水回血",
                "思考：为什么不该把「图标节点」本身当作数据？",
            },
            Implemented: false),

        new(
            Id: "s11_save",
            Order: 11,
            Title: "存档与读档",
            Skill: "JSON 序列化 / 存档槽 / 版本迁移 / 损坏处理",
            Summary: "存档真正难的地方，是「三个月前的老存档还能不能读」。",
            Tasks: new[]
            {
                "加一个字段，然后故意读旧存档，写迁移逻辑让它不崩",
                "做 3 个存档槽 + 每 30 秒自动存档",
                "写入前做一次校验（校验和或版本号）",
                "把游戏设置（音量、全屏）也存进去",
                "找到 user:// 的真实路径，用记事本打开看看",
            },
            Implemented: true),

        new(
            Id: "s12_audio",
            Order: 12,
            Title: "音频",
            Skill: "音频总线 / SFX 复用池 / 音乐交叉淡入 / 音高随机",
            Summary: "音效是「一次性播放」的重灾区，不管好池子很快会爆音、卡顿。",
            Tasks: new[]
            {
                "新建 Music / Sfx 两条总线，并做音量设置界面",
                "实现 SFX 复用池：同时最多 12 个音，超出抢占最早的",
                "实现音乐交叉淡入淡出（需要两个播放器）",
                "给打击音效加 ±10% 随机音高，听是不是不腻了",
                "加静音快捷键和「切到后台自动静音」",
            },
            Implemented: false),

        new(
            Id: "s13_events",
            Order: 13,
            Title: "事件总线",
            Skill: "Autoload / 全局信号 / 解耦 / 连接泄漏",
            Summary: "让「击杀」「拾取」被成就系统听到，而玩家完全不需要认识成就系统。",
            Tasks: new[]
            {
                "新增一个事件，让成就系统弹出提示",
                "把本站里的直接函数调用改成走事件总线，对比两种写法",
                "故意不断开连接然后销毁发送方，观察报错",
                "加「本局统计」面板：击杀数、拾取数、移动距离",
                "思考：什么时候不该用事件总线？",
            },
            Implemented: true),

        new(
            Id: "s14_pooling",
            Order: 14,
            Title: "对象池与性能",
            Skill: "对象池 / 性能分析 / 批绘制 / Debug HUD",
            Summary: "按 1 生成 1000 颗子弹，按 2 用对象池，看 FPS 差多少。",
            Tasks: new[]
            {
                "实现对象池：预热 200 颗子弹并循环复用",
                "用 MultiMesh 或 RenderingServer 批量绘制，再对比一次",
                "打开 F3 调试面板，记录三种做法的 FPS 与节点数",
                "用 调试器 -> 分析器 找到最耗时的函数",
                "思考：对象池什么时候反而更慢？",
            },
            Implemented: true),

        new(
            Id: "s15_procgen",
            Order: 15,
            Title: "程序化生成",
            Skill: "种子随机 / 噪声 / 房间生成 / TileMapLayer",
            Summary: "同一套算法 + 同一个种子 = 同一个世界。可复现是关键。",
            Tasks: new[]
            {
                "支持输入种子，让同一颗种子每次生成完全一样的地图",
                "切换三种算法：随机撒点 / FastNoiseLite 洞穴 / BSP 房间",
                "保证所有房间连通（用 flood fill 验证）",
                "用 TileMapLayer 把生成结果画出来",
                "加生成耗时统计，思考怎么把 100ms 降到 10ms",
            },
            Implemented: true),

        new(
            Id: "s16_status",
            Order: 16,
            Title: "状态效果",
            Skill: "Buff/Debuff / 叠加与刷新 / 持续伤害 / Tween",
            Summary: "加速、中毒、无敌同时存在时，谁说了算？",
            Tasks: new[]
            {
                "加「冷却」：同一个 buff 在冷却期内不能重复施加",
                "实现叠层：中毒最多 5 层，每层独立计时",
                "定义叠加规则：同名 buff 是刷新时长还是叠加强度？",
                "用 Tween 做图标上的倒计时环",
                "加「免疫」：无敌期间免疫中毒",
            },
            Implemented: true),
    };

    public static int TotalCount => All.Length;

    public static int ImplementedCount
    {
        get
        {
            var n = 0;
            foreach (var s in All) if (s.Implemented) n++;
            return n;
        }
    }

    public static StationInfo? Get(string id)
    {
        foreach (var s in All)
            if (s.Id == id) return s;
        return null;
    }
}
