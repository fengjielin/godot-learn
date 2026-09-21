# CLAUDE.md

本文件是给 AI 助手 / 未来的自己看的项目约定。人读的话，
请先看 [README.md](../README.md) 和 [docs/00-学习路线图.md](../docs/00-学习路线图.md)。

## 项目概述

Godot 4.7.1（.NET / mono）的 2D 俯视角「练功房」：中央枢纽 + 16 个练习站。
定位是**学习场地**，不是产品。代码的首要目标是"可读、可讲清楚为什么"，
性能与抽象层次都要为这个目标让路。

```yaml
引擎: Godot 4.7.1 (mono)
语言: C# / net8.0
渲染: Forward+ (Vulkan)
分辨率基准: 1280x720, stretch=canvas_items, aspect=expand
根命名空间: Dojo
```

## 环境事实（重要）

- Godot 可执行文件：`E:\software\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe`
- 本机 .NET SDK 只有 `8.0.303` / `9.0.200`。**Godot 4.7.1 编辑器的 C# 工具链要求 SDK 10.0.8**，
  因此编辑器里会报 `.NET Sdk not found`，其构建按钮不可用。
  → 一律用 `dotnet build` 命令行构建；编辑器按 F5 仍可运行已构建的程序集。
- **shell 是 Windows PowerShell 5.1（没有 pwsh）。**
  → `.ps1` 文件必须**纯 ASCII**。PS 5.1 会把无 BOM 的 UTF-8 按 GBK 解码，
    行尾中文的第二个字节会吃掉换行符，静默删掉下一行代码。中文说明写在 `.md` 里。
  → **绝对不要用 `Get-Content` / `Set-Content` 给项目文件做文本往返**（哪怕只是想替换一处）。
    PS 5.1 的 `Get-Content -Raw` 默认按 ANSI 读，`Set-Content -Encoding UTF8` 又写 BOM，
    结果是 `.tscn` / `.godot` 里的中文全部变成乱码、节点树直接解析不出来
    （症状是 `Parent path ... has vanished when instantiating`）。
    **改文件一律用 edit / write 工具。**
- DSH 文件沙箱只允许写工作区，`%APPDATA%\Godot` 不可写。
  → 用 `tools/godot-run.ps1` 跑 headless，它会把子进程的 `APPDATA` 重定向到 `<项目>/.logs/appdata`。
    直接跑 Godot 会卡在 `Could not create directory: 'user://logs'`。

## 目录约定

```
common/          跨站复用的基础设施（按能力分目录，不用 scenes/ scripts/ 这类按类型分）
  event_bus/ ui/ save/ audio/ debug/ physics/ util/ fx/ settings/ state_machine/ combat/ nav/
  event_bus/     EventBus（Autoload）：全游戏的**事件目录**，所有信号集中声明在这一个文件
                 纪律：Godot 信号在任一端释放时引擎会自动断开；**普通 C# event 不会** ——
                 用它就必须 _Ready 订阅 / _ExitTree 退订（S13 会演示漏掉的后果）
  fx/           打击感通用件：ScreenFx（Autoload：屏幕震动/顿帧/白闪）、CameraRig（跟随相机）
                注意 ScreenFx 会改全局 Engine.TimeScale，StationBase._ExitTree 负责兜底复位
  settings/     GameSettings：音量/全屏的读写与生效（与存档共用一份 JSON）
  state_machine/ State / StateMachine / Transition：通用有限状态机，供 S04 / S08 使用
  combat/       Damage / Damageable（整条伤害管线）/ CombatRules / DamageNumber + 对象池
  data/         WeaponData（自定义 Resource）+ data/weapons/*.tres
                纪律：**绝不在 _Process 里调 GD.Load** —— 高频加载会触发
                Godot C# 绑定的 gchandle 崩溃（本工程踩过）。配置只加载一次并缓存引用
  nav/          NavMeshBuilder：从"墙的矩形"生成导航网格（Godot 4.7 用 set_vertices + add_polygon）
                纪律：墙只定义一份数据，碰撞/导航/画面都由它生成
  effects/      StatusEffectDef（定义表 + StackRule）/ StatusEffects（容器：计时/叠层/冷却/免疫）
src/
  game/          场景路由(SceneRouter)、练习站目录(StationCatalog)、站点元信息
  hub/           枢纽与传送门
  entities/      可复用实体（player/ …）
  ui/            通用 UI 场景与脚本（station_hud/ …）
  dev/           开发期工具（DevAutomation 输入回放）
  stations/      ★ 一个练习站一个目录
    station_base/  StationBase 基类
    s01_lifecycle/ s01_lifecycle.tscn + S01Lifecycle.cs + 本站专用场景与脚本
docs/            学习文档；docs/stations/<id 带连字符>.md 与站点一一对应
tools/           验证脚本（纯 ASCII）
```

**约定**：`src/stations/<id>/<id>.tscn` 的 `id` 必须与 `StationBase.StationId`、
`StationCatalog` 里的 `Id` 三者完全一致，否则 `StationBase._Ready` 会直接抛异常。

**类名冲突警告**：所有站点的脚本都在 `Dojo.Stations` 命名空间下。
不同功能目录里的同类东西**必须带领域前缀**（`PoolBullet` 而不是 `Bullet`）,
否则编译器报"类型已包含定义"，而**报错行号会指向完全无辜的字段声明**（本工程踩过）。
"文件夹分开了"不等于"类型分开了"。

**任务判定映射**：`StationCatalog` 里每站的任务文本顺序，必须和该站
`CheckGoals()` 里 `MarkTaskDone(i)` 的下标**一一对应**。两处改一处就要核另一处 ——
本工程在 S16 和 S14 都因为下标错位把勾打到了隔壁任务上。

**输入动作只有 `aux_1`~`aux_5`**（由 `tools/gen-inputmap.ps1` 统一生成）。
**用了不存在的动作名（如 `aux_6`）会在每次按键时真的报错**，headless 验证直接 FAIL ——
本工程在 S11 踩过。要加新动作，先改 `gen-inputmap.ps1` 重新生成，别手写 InputEvent。

**提交约定**：每完成一个练习站（里程碑）就提交一次本地 git，**不推送**。
提交信息用 Conventional Commits（主题 + 正文），正文分点列出该站做了什么。

## 命名规范

| 类型 | 规范 | 示例 |
|---|---|---|
| 目录 | snake_case | `s01_lifecycle/`, `station_base/` |
| C# 文件 | PascalCase，且**必须与类名一致**（Godot 靠文件名找类） | `Wanderer.cs` → `class Wanderer` |
| 场景文件 | snake_case | `wanderer.tscn`, `station_hud.tscn` |
| 节点名 | PascalCase | `Spawned`, `TaskPanel` |
| 输入动作 | snake_case | `move_left`, `toggle_tasks` |

## 手写 .tscn 的注意事项

本工程的场景文件是手写的，已实测 Godot 4.7 可以接受下列简化写法：

- 可以不写 `uid=`（Godot 会自己补）与节点上的 `unique_id=`；
- `[ext_resource type="Script" path="res://....cs" id="1_x"]` 不写 `uid` 也正常；
- 容器子节点写 `layout_mode = 2`；锚定定位的 Control 要显式写 `anchor_*` 与 `offset_*`；
- 圆要用多边形逼近（`LevelKit.CirclePoints`），Godot 没有内置圆形图元。

## 常用命令

```powershell
cd E:\Code\GameDev\Godot\dojo

dotnet build                                    # 构建 C#（改完代码必做）

# headless 跑，有 ERROR/WARNING 则退出码为 1
powershell -ExecutionPolicy Bypass -File .\tools\godot-run.ps1 -ProjectPath . -Frames 180

# 跑单个练习站
powershell -ExecutionPolicy Bypass -File .\tools\godot-run.ps1 -ProjectPath . `
    -Scene res://src/stations/s01_lifecycle/s01_lifecycle.tscn

# 导入资源
powershell -ExecutionPolicy Bypass -File .\tools\godot-run.ps1 -ProjectPath . -Import

# 截图（不需要手动操作，用输入回放自动"玩"一段）
powershell -ExecutionPolicy Bypass -File .\tools\godot-run.ps1 -ProjectPath . -Frames 460 `
    -WriteMovie '.logs/shots/e2e.png' `
    -UserArg '--dev-input=move_left:0.3:2.3,interact:2.6:2.7'

# 重新生成 InputMap（改了输入动作就用它，别手写 InputEvent 序列化）
powershell -ExecutionPolicy Bypass -File .\tools\gen-inputmap.ps1
```

## 交付纪律

改完任何东西，至少跑一遍：

1. `dotnet build` —— 0 error；
2. `tools/godot-run.ps1 -Frames 180` —— RESULT: OK；
3. 涉及画面布局的改动，用 `-WriteMovie` 截图**实际看一眼**，别靠脑补。

`tools/godot-run.ps1` 的已知环境噪音白名单里有一条
`Failed to read the root certificate store`（沙箱挡了 Windows 证书存储），
那是环境问题，不是工程问题。

## 素材政策

**不引入任何第三方美术/音频资源。** 画面全部用 Godot 图元
（`Polygon2D` / `ColorRect` / `LevelKit` 工具）在代码或场景里画；
中文字体走 `SystemFont`（`common/ui/UiTheme.cs`）。
这样仓库干净、无授权问题，也逼着我们把注意力放在机制而不是贴图上。
