# S06 · UI 与 HUD

> **练什么**：Control / 容器布局 / 锚点 / Theme / 焦点导航 / 暂停菜单 / 设置持久化
> **一句话**：UI 用**容器**做布局，不用手摆像素；一次把分辨率适配做对，之后永远不用管。
> **难度**：★★★☆☆（API 很好学，难的是"什么时候该用容器、什么时候该用锚点"）

---

## 一、先玩一遍

这一站不是走动的房间，而是一整块 **UI 实验屏**，分三部分。

### ① 布局实验台（左）
同一个盒子，两种做法：

| 按键 | 作用 |
|---|---|
| `1` | 在**容器布局**和**绝对定位**之间切换 |
| `3` | 把盒子宽度在 `360 / 270 / 190` 之间循环 |

你会看到：

- **容器布局**：文字用 `autowrap` 自动折行，盒子再窄内容也待在盒子里。
- **绝对定位**：文字宽度写死在 `16~330`，盒子一窄，**右边直接被切掉** ——
  而且**不会报任何错**。这是 UI 新手最常撞的坑：它不崩，只是"看起来怪"。

### ② 控件陈列柜（右）
一排标准控件：按钮、复选框、滑条、进度条、下拉框。
按 `4` 切换主题配色（冷调 / 暖调），**所有控件一起变** —— 这就是 `Theme` 的价值。

下面还有一排按钮做**焦点导航**实验：点一下把焦点放进去，然后用方向键移动、回车确认。
**这套东西不需要写一行代码**（原因见第四节）。

### ③ 暂停菜单（`ESC`）
**每个练习站都装了同一份**（由 `StationBase` 自动装配）。里面是真正的设置面板：
三条音量滑条 + 全屏开关，改完立刻生效，并且**写进存档**。

---

## 二、核心概念一：容器 vs 锚点，什么时候用哪个

Godot 的 Control 有两套定位机制，新手最容易混着乱用：

| | 锚点 + 偏移（Anchor / Offset） | 容器（Container） |
|---|---|---|
| 表达的是 | "我这个控件贴在父级的哪个位置，多大" | "我的子节点该怎么自动排" |
| 适合 | **最外层**的骨架：顶部栏、右下角提示、全屏遮罩 | **内部**的成组内容：一列按钮、一个卡片 |
| 优点 | 直观、能做任意形状 | 自动算大小、自动重排、改内容不用改坐标 |
| 缺点 | 内容一改就得重算坐标；换语言/字号就崩 | 表达力有限，绝对定位做不了 |

**正确的用法是组合，而不是二选一：**

```
CanvasLayer                     ← 不参与布局
└── TopBar (ColorRect)          ← 用锚点：贴顶、宽 100%
└── 右下角提示 (Label)           ← 用锚点：贴右下
└── 暂停菜单 (CenterContainer)   ← 用锚点：占满全屏
    └── Stack (VBoxContainer)    ← 容器：垂直排列
        └── MainPanel (PanelContainer)   ← 容器：自动给子节点套背景
            └── Margin (MarginContainer) ← 容器：管内边距
                └── VBox (VBoxContainer) ← 容器：按钮一列排开
```

**一条实用判断法**：
> 这个控件的位置和大小，是"相对于屏幕的某个角"吗？→ 用锚点。
> 是"跟着兄弟节点排"吗？→ 丢进容器，别自己算坐标。

### 关于分辨率适配

本工程用的是 `stretch_mode = canvas_items` + `aspect = expand`：

- **canvas_items**：整个 2D 画布按比例缩放，所以逻辑坐标永远是 `1280×720`，
  你的锚点和容器都基于这个基准算 —— **不需要为每种分辨率写一套布局**。
- **expand**：窗口宽高比和基准不一致时，**扩大可视范围**（而不是拉伸变形）。
  所以超宽屏会看到更多横向内容，方屏会看到更多纵向内容。
  如果换成 `keep`，画面会加黑边；换成 `ignore`，画面会被拉变形。

**这就是为什么 UI 一定要用容器和锚点**：基准视口是固定的，
但"可视范围"会随窗口变，写死坐标的东西迟早会跑到屏幕外。

---

## 三、核心概念二：Theme 是按 (类型, 名称) 索引的

```csharp
theme.SetColor("font_color", "Button", textColor);
theme.SetStylebox("normal", "Button", normalBox);
theme.SetStylebox("hover",  "Button", hoverBox);
```

意思是"**所有** `Button` 的 `font_color` 用这个颜色"。改一次，全工程所有按钮一起变。

三个关键点：

1. **按钮有五个状态**：`normal` / `hover` / `pressed` / `disabled` / `focus`。
   **五个都要给 StyleBox**，缺一个那个状态就会"突然变丑"（回到引擎默认样式）。
   最容易漏的是 `focus` —— 键盘导航时会在按钮上画一圈引擎默认的虚线框。
2. **单个控件可以用 `theme_override_*` 覆盖 Theme。**
   优先级：`theme_override` > 最近的祖先 Theme > 项目默认 Theme。
   本站的任务面板就用了 `theme_override_styles/panel` 给它单独一个背景。
   **但别滥用**：到处 override 就等于没有 Theme，改一次样式要改十个地方。
3. **Theme 挂在根 Window 上**，所有 Control 自动继承。
   本工程在 `common/ui/UiTheme.cs` 里用代码构建，并提供了两套配色。

---

## 四、核心概念三：焦点导航（为什么不用写代码）

```csharp
_continueButton.GrabFocus();   // ← 就这一行
```

Godot 的 `Button` 默认 `focus_mode = All`。当某个按钮拿到焦点时：

- **方向键 / `ui_up` `ui_down` `ui_left` `ui_right`** → 引擎**自动**在几何上找最近的邻居，
  移动焦点。你不需要给每个按钮配"下一个是谁"。
- **`ui_accept`（回车 / 空格 / 手柄 A）** → 按下当前有焦点的按钮。
- **`ui_focus_next`（TAB）** → 按树顺序切焦点。

所以"支持手柄导航"的全部工作，就是**在打开界面时把焦点放到一个合理的位置**。
反过来，如果忘了 `GrabFocus`，键盘玩家打开菜单会发现**按什么都没反应** ——
这是最常见的"手柄不能用"的原因，而不是缺了什么配置。

**焦点落在哪，决定了这个界面好不好用。** 本站的暂停菜单打开时焦点在「继续游戏」，
进设置时焦点在「主音量滑条」—— 玩家一进来就能用左右键改音量，不用先找路。

---

## 五、核心概念四：暂停

```csharp
GetTree().Paused = true;                    // 冻结全世界
ProcessMode = ProcessModeEnum.Always;       // 但菜单自己不能被冻住
```

`GetTree().Paused` 会让所有 `ProcessMode = Inherit` 的节点停止
`_Process` / `_PhysicsProcess` / `_Input`。三个必须注意的地方：

1. **菜单自己必须 `ProcessMode = Always`**，否则它会把自己也冻住，玩家再也点不动 ——
   这是最常见的"一暂停就死机"。
2. **★ 暂停状态必须兜底复位。** `GetTree().Paused` 是**全局**的，
   带着暂停切场景会让新场景永远冻住。所以 `PauseMenu._ExitTree()` 里必须放开。
   这和 S05 里 `Engine.TimeScale` 的兜底是同一类问题：
   **全局开关忘了复位，症状是"整个游戏不对劲"，而且不报任何错。**
3. **暂停期间，别人的 `_Process` 是停的。**
   所以"菜单开了吗 / 玩家改设置了吗"这类判定**不能靠轮询**——
   轮询代码本身已经停了。必须让菜单发出**信号**。
   本站的 `PauseMenu` 就有 `Opened` / `Closed` 两个信号，S06 靠它们判定任务。

---

## 六、设置持久化

```csharp
public static void SetVolume(string bus, float linear)
{
    SaveSystem.Instance.SetNumber(key, linear);      // 1. 存起来
    AudioManager.Instance.SetBusVolumeLinear(bus, linear);  // 2. 立刻生效
    SaveSystem.Instance.Save();                      // 3. 落盘
    VolumeChangeCount++;
}
```

**设置分两步：改（写进存档）和生效（调 AudioServer / DisplayServer）。**
最容易漏掉其中一步，症状是"滑条动了但音量没变"或者"音量变了但重启又回去了"。
所以统一收在 `GameSettings` 的 `Set` 系列方法里，两边一起做。

启动时还要 `ApplyAll()` 一次（本工程在 `SceneRouter._Ready()` 里调用，
那时 `SaveSystem` 已经读完存档），否则玩家上次的设置不会生效。

---

## 七、代码地图

| 文件 | 负责什么 |
|---|---|
| `src/ui/pause_menu/pause_menu.tscn` + `PauseMenu.cs` | ★ 全站共用的暂停菜单 + 设置面板 |
| `common/ui/UiTheme.cs` | ★ 全局主题与两套配色 |
| `common/settings/GameSettings.cs` | ★ 设置的读写与生效 |
| `src/stations/station_base/StationBase.cs` | 给每个站装配 HUD 和暂停菜单 |
| `src/stations/s06_ui/s06_ui.tscn` | ★ UI 实验屏的布局（**重点看这个文件的容器嵌套**） |
| `src/stations/s06_ui/S06Ui.cs` | 站台逻辑：布局切换、主题、焦点、判定 |

**读 `s06_ui.tscn` 的建议**：在编辑器里打开它，展开场景树，
对照第三节那张"锚点 + 容器组合"的示意图看。**这是本站最该抄走的东西。**

---

## 八、五条练习任务的提示

### ① 用 VBoxContainer + MarginContainer 重做这块 HUD，改窗口大小看效果
**难度**：★★
本站已经演示了。真正的练习：

1. 按 `3` 把盒子调到最窄，**在两种布局模式下各看一遍**。
2. 打开 `src/ui/station_hud/station_hud.tscn`，看它的标题栏和任务面板
   分别是用锚点还是容器做的，说出为什么。
3. **挑战**：给任务面板加一个"折叠/展开"按钮。要求：
   展开时面板高度自适应内容，折叠时只留一行标题。
   （提示：`VBoxContainer` 里把内容的 `Visible` 设 false，容器就会自动重算 ——
   **这就是"不用改坐标"的意思**。）

### ② 做暂停菜单：ESC 打开，键鼠与手柄都能导航，暂停时游戏冻结
**难度**：★★★
已经做好了，而且是全站共用。练习：

1. 打开 `pause_menu.tscn`，把 `process_mode` 从 `Always` 改回 `Inherit`，
   运行，按 ESC —— 你会看到菜单打开后**按什么都没反应**。
   **这就是"一暂停就死机"的真身。**
2. 加一个"返回枢纽前确认"的二段菜单。
   （提示：又是一个"栈式 UI"问题 —— 和 S09 的调参台、S10 的背包是同一类结构。）
3. **真正的挑战**：给暂停菜单加一个**按键重绑定**界面。
   提示：`InputMap.ActionEraseEvent()` + `InputMap.ActionAddEvent()`，
   并且要把结果存进存档。做完你会明白"为什么动作名和按键要分开"。

### ③ 给按钮统一做一套 Theme，改字号和颜色
**难度**：★★
现有实现有两套配色。练习：

1. 把 `UiTheme.ApplyPalette` 里的 `theme.SetFontSize("font_size", "Button", 18)`
   改成 `26`，看整个工程的按钮一起变大。
2. **故意删掉 `focus` 那一行**，然后用键盘导航 ——
   焦点框会变回引擎默认的样子（和你的主题格格不入）。
   **记住这个手感：Theme 少给一个状态，就会在某个场景下突然"破功"。**
3. 加第三套配色，并且把它也接到暂停菜单的切换按钮上
   （提示：现在 `TogglePalette` 是两值切换，改成循环）。

### ④ 加设置面板：主音量滑条 + 全屏开关，并写进存档
**难度**：★★
已经做好了。练习：

1. 把音量调到 20%，关掉游戏，重新运行 —— 音量应该还是 20%。
   然后去 `%APPDATA%\Godot\app_userdata\Godot Dojo\dojo_save.json`
   里找到 `settings.master_volume` 这一项，**亲眼看一眼存档长什么样**。
2. 加一个"分辨率"下拉框（`OptionButton`），选项是几组常见窗口尺寸。
3. 加一个"语言"选项，并用 `TranslationServer` 真的切一次语言。
   （提示：这需要先建一个 `.csv` 翻译表，见 `localization/` 目录的规划。）

### ⑤ 思考：为什么 `ui_cancel` 这类内置动作不用自己定义？
**难度**：★（但结论要记住）
按 `5` 会弹出一块完整的说明面板。核心结论：

> **判断标准是这个输入在"操作界面"还是"操作游戏世界"。**
> 操作界面 → 用内置动作；操作游戏世界 → 自己定义。

内置动作（`ui_*`）每个 Godot 新工程默认就有，而且**已经绑好了键盘和手柄**。
自己定义一套 ESC 的坏处：手柄玩不了、跟引擎的 UI 系统打架
（控件默认就吃 `ui_cancel` / `ui_accept`）、改键位要改两处。

---

## 九、常见坑速查

| 现象 | 原因 |
|---|---|
| 暂停后菜单点不动 | 菜单的 `ProcessMode` 不是 `Always` |
| 换场景后整个游戏冻住 | 暂停没复位。`_ExitTree` 里必须 `GetTree().Paused = false` |
| 菜单打开时其它脚本的判定失灵 | 暂停冻住了它们的 `_Process`。改用信号推送 |
| 键盘/手柄打开菜单后没反应 | 忘了 `GrabFocus()` |
| 焦点框丑得和主题不搭 | Theme 里漏了 `focus` 状态的 StyleBox |
| 文字在窄容器里被切掉 | 没开 `autowrap_mode`，或者宽度是写死的 |
| 改了窗口大小 UI 跑到屏幕外 | 用了锚点/容器，但父子关系不对（锚点是相对**父级**的） |
| 换语言/换字号后布局崩了 | 有地方写死了坐标或尺寸。用容器让它自己算 |
| 全屏开关改了没反应 | 只改了存档没调 `DisplayServer`，或者反过来 |

---

## 十、通关后做什么

1. **去改 `pause_menu.tscn` 的 `process_mode`，体验"一暂停就死机"**，
   然后改回来。亲手制造一次这个 bug，比读十遍都记得住。
2. 想一想：本工程的项目设置是 `stretch_mode = canvas_items` + `aspect = expand`。
   如果改成 `viewport` 或 `ignore`，本站的 UI 会变成什么样？
   （提示：`viewport` 会把整个画面当图片缩放，UI 会跟着糊；
   `ignore` 会让 UI 被拉变形。**先想清楚再改，改完记得改回来。**）
3. 去 `common/settings/GameSettings.cs` 看 `ApplyAll()` 在哪被调用。
   然后回答：为什么它放在 `SceneRouter` 的 `_Ready`，而不是某个场景里？
4. 把这一站的"容器 vs 绝对定位"演示搬到你以后的项目里当自检工具：
   **每做完一屏 UI，都按一次 `3` 把它压到最窄** —— 压不坏，才算布局合格。
