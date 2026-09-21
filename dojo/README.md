# Godot 练功房（Godot Dojo）

一个 **Godot 4.7 + C#（.NET）** 的 2D 俯视角「练功房」。

它不是又一份教程 Demo，而是一个**可以反复练的场地**：中间是枢纽，四周 16 道传送门，
每道门后是一个独立的练习站，专门练游戏开发里的一项必备能力。每个站都能立刻玩起来，
站内按 `TAB` 能看到 5 条**分级练习任务**（先改参数 → 再改逻辑 → 最后重写）。

> 引擎：Godot 4.7.1（.NET / mono 版）· 语言：C# / net8.0 · 渲染：Forward+ · 物理：内置 2D

---

## 快速开始

```powershell
# 1. 构建 C# 程序集（必须先做，Godot 才能加载脚本）
cd E:\Code\GameDev\Godot\dojo
dotnet build

# 2. 用编辑器打开（首次会导入资源，稍等）
& 'E:\software\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe' --editor --path .

# 3. 在编辑器里按 F5 运行，或直接命令行运行：
& 'E:\software\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe' --path .
```

**操作**：`WASD` / 方向键移动 · `E` 进入传送门 · `TAB` 展开/收起任务列表 ·
`R` 重开当前站 · `ESC` 回枢纽 · `F3` 性能面板

---

## 环境注意（本机实测，务必先看）

| 项 | 状态 |
|---|---|
| Godot | `4.7.1.stable.mono` @ `E:\software\Godot_v4.7.1-stable_mono_win64\` ✅ |
| .NET SDK | 本机有 `8.0.303` 与 `9.0.200` ✅ 命令行构建正常 |
| 编辑器内置 C# 工具链 | ⚠️ **要求 .NET SDK `10.0.8`，本机没有** |

**这意味着什么**：打开 Godot 编辑器时会看到一条 `.NET Sdk not found. The required version is '10.0.8'`
的报错，编辑器里的「构建」按钮用不了（那是 GodotTools 自己的构建器在要 SDK 10）。

**但它不影响你学习和运行**：

- 用命令行 `dotnet build` 构建完全正常（本工程已验证）。
- 构建产物在 `.godot/mono/temp/bin/Debug/dojo.dll`，编辑器按 F5 播放会直接加载它。

**推荐做法**（二选一）：

1. 装一个 [.NET SDK 10](https://dotnet.microsoft.com/download)，编辑器里的构建/新建脚本等功能就全好了；
2. 或者保持现状：**改完 C# 就在终端跑一次 `dotnet build`，再回编辑器按 F5**。

---

## 目录结构

```
dojo/
├── project.godot            # 总配置：主场景、Autoload、InputMap、显示设置
├── dojo.csproj / dojo.sln   # C# 工程
├── common/                  # 跨练习站复用的基础设施
│   ├── event_bus/           #   全局事件总线（Autoload）
│   ├── save/                #   存档系统（JSON，带版本迁移）
│   ├── audio/               #   音频管理（总线 + SFX 复用池）
│   ├── ui/                  #   默认主题（用系统字体解决中文豆腐块）
│   ├── debug/               #   性能面板（F3）
│   ├── physics/             #   碰撞层命名的集中定义
│   └── util/                #   关卡搭建工具箱（墙体、色块、标签）
├── src/
│   ├── game/                # 场景路由、练习站目录（StationCatalog）
│   ├── hub/                 # 枢纽场景、传送门
│   ├── entities/player/     # 玩家角色（各站复用）
│   ├── ui/station_hud/      # 练习站通用 HUD（任务面板、完成横幅）
│   ├── dev/                 # 开发期工具：输入回放（截图/自动化用）
│   └── stations/            # ★ 16 个练习站，一目录一站
├── docs/                    # 学习文档（路线图、使用方法、逐站详解）
└── tools/                   # 验证脚本（headless 运行、InputMap 生成）
```

**加一个新练习站的完整步骤**：在 `src/stations/` 下建目录 → 继承 `StationBase`
→ 在 `src/game/StationCatalog.cs` 里登记 → 枢纽里自动出现传送门。不需要改任何 UI 代码。

---

## 16 个练习站

| 序号 | 练习站 | 练什么 | 状态 |
|---|---|---|---|
| S01 | [节点与生命周期](docs/stations/s01-lifecycle.md) | 场景树 / 生命周期 / 信号 / 分组 | ✅ 已建成 |
| S02 | [输入与手感](docs/stations/s02-input.md) | InputMap / 轮询 vs 事件 / 输入缓冲 / 土狼时间 | ✅ 已建成 |
| S03 | [物理与碰撞](docs/stations/s03-physics.md) | 碰撞层掩码 / CharacterBody2D / Area2D / RayCast2D | ✅ 已建成 |
| S04 | [状态机](docs/stations/s04-fsm.md) | 有限状态机 / 状态转移 / 优先级 / 可观测性 | ✅ 已建成 |
| S05 | [相机与打击感](docs/stations/s05-camera.md) | Camera2D / 平滑跟随 / 屏幕震动 / 顿帧 | ✅ 已建成 |
| S06 | [UI 与 HUD](docs/stations/s06-ui.md) | Control / 容器布局 / 锚点 / 主题 / 暂停菜单 | ✅ 已建成 |
| S07 | [战斗与数值管线](docs/stations/s07-combat.md) | Hitbox/Hurtbox / 伤害管线 / 无敌帧 / 击退 / 飘字池 | ✅ 已建成 |
| S08 | [敌人 AI 与寻路](docs/stations/s08-ai.md) | 视野锥 / 听觉 / 导航网格 / 群体分离 | ✅ 已建成 |
| S09 | [数据驱动](docs/stations/s09-data.md) | 自定义 Resource / .tres 配置 / 热重载 | ✅ 已建成 |
| S10 | [背包与物品](docs/stations/s10-inventory.md) | 数据与视图分离 / 拖拽 / 堆叠 / 装备 | ✅ 已建成 |
| S11 | [存档与读档](docs/stations/s11-save.md) | JSON / 存档槽 / 版本迁移 / 损坏处理 | ✅ 已建成 |
| S12 | 音频 | 音频总线 / SFX 池 / 交叉淡入 / 音高随机 | 待建设 |
| S13 | [事件总线](docs/stations/s13-events.md) | Autoload / 全局信号 / 解耦 / 连接泄漏 | ✅ 已建成 |
| S14 | [对象池与性能](docs/stations/s14-pooling.md) | 对象池 / 性能测量 / 节点数 / 什么时候不该池化 | ✅ 已建成 |
| S15 | [程序化生成](docs/stations/s15-procgen.md) | 种子随机 / 噪声 / 房间生成 / TileMapLayer | ✅ 已建成 |
| S16 | [状态效果](docs/stations/s16-status.md) | Buff/Debuff / 叠加与刷新 / 持续伤害 / Tween | ✅ 已建成 |

枢纽右上角实时显示「已建成 / 已通关」，进度存在
`%APPDATA%\Godot\app_userdata\Godot Dojo\dojo_save.json`。

---

## 推荐的练法

每个站都按同一个循环走，**别只读代码**：

1. **先玩**：进去按提示操作一遍，建立"它应该是什么样"的直觉。
2. **再读**：对照 `docs/stations/` 里这一站的详解，看代码为什么这么写。
3. **后改**：按 `TAB` 里的任务逐条改。每改一条就重跑一次，**观察现象**而不是只看有没有报错。
4. **最后重写**：把这一站的核心机制自己从零写一遍。写不出来就说明还没掌握。

改坏了不用怕：`R` 重开当前站，`git checkout .` 恢复代码。

---

## 常见问题

**中文显示成方块？**
`UiTheme` 会用系统里的中文字体（优先微软雅黑）。如果换到没有中文字体的机器上，
在 `common/ui/UiTheme.cs` 的候选列表里加上该机器已有的字体名即可。

**改了 C# 没生效？**
Godot 加载的是已编译的程序集。跑一次 `dotnet build` 再重新运行。

**想看画面但不想手动操作？**（自动化截图 / 回归检查）
见 [docs/01-如何使用练功房.md](docs/01-如何使用练功房.md) 里的 headless 与截图用法。

**这个仓库根目录的 `README.md` 和 `docs/01~05` 是 Unity 的？**
那是早期的 Unity 学习方案，已被本工程取代，保留仅作参考。

---

## 授权 / 素材

本工程不含任何第三方美术与音频资源：所有画面都是 Godot 图元（`Polygon2D`）用代码画的，
字体走系统字体。可以放心提交到公开仓库。
