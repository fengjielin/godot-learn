# CLAUDE.md

## 项目概述

Godot 4.7 游戏项目，使用 C# (.NET) 脚本、Jolt Physics 物理引擎、Forward Plus 渲染器。

```yaml
引擎: Godot 4.7
语言: C# (.NET)
渲染: Forward Plus
物理: Jolt Physics
平台: Windows (D3D12)
```

## 目录约定

场景文件（.tscn）存放在对应的实体目录中，不单独设立 scenes/ 目录。

```
src/
├── game/              # 主场景 (main.tscn) + 游戏核心逻辑 (GameManager 等)
├── entities/
│   ├── player/        # 玩家 — 场景、脚本、美术、数据、音效都在此目录
│   │   ├── .tscn
│   │   ├── .cs
│   │   ├── art/
│   │   ├── data/
│   │   └── sound/
│   ├── npcs/          # NPC
│   ├── organisms/     # 生物/敌人
│   └── ui/            # UI 场景 (HUD、菜单等)
├── stages/            # 关卡场景
assets/                # 全局共享资源
├── fonts/
└── sounds/
common/                # 跨模块共享的代码/工具
config/                # 游戏配置文件
localization/          # 本地化 / i18n 文件
addons/                # Godot 插件
```

## 命名规范

| 类型 | 规范 | 示例 |
|---|---|---|
| 目录 | snake_case | `player/`, `game_manager/` |
| C# 脚本 | PascalCase | `PlayerController.cs` |
| 场景文件 | snake_case | `player.tscn`, `hud.tscn` |
| 资源文件 | snake_case | `sword_swing.wav` |

## 常用命令

```bash
# 构建 C# 项目
dotnet build

# 在编辑器中打开项目
godot --editor --path .

# 运行游戏
godot --path .

# 运行指定场景
godot --path . res://src/game/main.tscn
```
