# S09 · 数据驱动

> **练什么**：自定义 Resource · `.tres` 配置 · `[Export]` · 热重载 · 什么该进配置
> **一句话**：把数值从代码里搬出去，**迭代速度**才会从 30 秒变成 3 秒 —— 而迭代速度决定了游戏好不好玩。
> **难度**：★★★☆☆（API 十分钟学会，难的是判断"什么该搬出去"）

---

## 一、先玩一遍

走过去砍假人。**但这一站真正的内容是"数值从哪来"。**

三把武器（匕首 / 长剑 / 大剑）的**全部参数都写在 `.tres` 文件里**，代码里一个伤害数字都没有。

| 按键 | 作用 |
|---|---|
| `1` `2` `3` | 切换三把预设武器 |
| `4` | **随机生成**一把武器（运行时造对象，磁盘上没有这个文件） |
| `5` | **从磁盘重新加载**当前 `.tres` —— 这就是热重载 |

### ★ 面板上最值得看的一行

```
【三把预设的 DPS 对比】  匕首 33.3   长剑 37.5   大剑 33.3
　→ DPS 几乎一样，但手感完全是三个游戏。平衡 ≠ 体验相同。
```

三把武器的**理论 DPS 几乎一样**（33~38），但：

| | 匕首 | 长剑 | 大剑 |
|---|---|---|---|
| 伤害 | 6 | 12 | 26 |
| 间隔 | 0.18s | 0.32s | 0.78s |
| 距离 | 40 | 52 | 74 |
| 范围 | 34 | 44 | 76 |

**这才是"调参"这个词的全部意义**：算 DPS 是为了确认它们强度相当；
而"重不重、快不快、够不够远"完全是另一回事 —— **数值保证平衡，配置决定手感。**

---

## 二、核心概念一：热重载（数据驱动最大的回报）

```csharp
public static WeaponData? ReloadFromDisk(string resPath)
    => ResourceLoader.Load<WeaponData>(resPath, cacheMode: ResourceLoader.CacheMode.Replace);
```

关键是 `CacheMode.Replace`：**默认加载走缓存，你改了文件也读不到新值。**

试试看：

1. 用记事本打开 `common/data/weapons/greatsword.tres`
2. 把 `Damage = 26` 改成 `Damage = 200`，保存
3. 回到游戏，按 `3` 切到大剑，按 `5` 热重载

面板上的数字立刻变了 —— **不用重新编译，不用重启，不用走到那个场景。**

> 改一次数值的代价从"30 秒（改代码 → 编译 → 重开 → 走到场景）"
> 降到"3 秒（改文件 → 按一下键）"。
> **只有代价降到这个量级，你才会真的去调它。** 否则最后所有武器手感都一样。

---

## 三、核心概念二：`[GlobalClass]` 与 `[Export]`

```csharp
[GlobalClass]                                    // 让它在编辑器里能直接新建
public partial class WeaponData : Resource
{
    [Export] public int Damage { get; set; } = 10;
    [Export] public float AttackInterval { get; set; } = 0.35f;
    [Export] public Color TintColor { get; set; } = new(1f, 0.94f, 0.7f);
    ...
}
```

- `[GlobalClass]` 让类型出现在编辑器的「新建资源」列表里，也让 `.tres` 能写 `script_class="WeaponData"`。
- `[Export]` 的字段会出现在检查器里，可以拖滑块；**也可以直接用文本编辑器改 `.tres`**。
- `PropertyHint` 能给编辑器更多信息：`[Export(PropertyHint.Range,"0,1,0.01")]` 会变成滑块，
  `[Export(PropertyHint.MultilineText)]` 会变成多行输入框。

`.tres` 文件长这样（**就是纯文本，可以手写**）：

```
[gd_resource type="Resource" script_class="WeaponData" load_steps=2 format=3]

[ext_resource type="Script" path="res://common/data/WeaponData.cs" id="1_weapon"]

[resource]
script = ExtResource("1_weapon")
DisplayName = "大剑"
Damage = 26
AttackInterval = 0.78
Reach = 74.0
TintColor = Color(1, 0.6, 0.42, 0.34)
```

**属性名就是 C# 的属性名**（这里是大写开头的 PascalCase）。

---

## 四、核心概念三：Resource 是共享的

```csharp
var a = GD.Load<WeaponData>("res://.../greatsword.tres");
var b = GD.Load<WeaponData>("res://.../greatsword.tres");
// a 和 b 是**同一个对象**
```

这既是特性也是陷阱：

- **是特性**：同一份配置各处引用，改一处全局生效，内存里也只有一份。
- **是陷阱**：你改了 `a.Damage`，`b` 也变了。
  想要一个独立的副本必须 `a.Duplicate()`。
  **S10 背包里"改一个道具结果所有同种道具都变了"就是这个坑。**

### ★ 本站踩到的另一个相关坑

`sub_resource` **默认也是共享的**。所以 `WeaponSwing` 不能这样写：

```csharp
// ❌ 改的是共享的形状资源，所有挥砍实例的判定框会一起变
shape.Shape.Size = new Vector2(reach, arcWidth);

// ✅ 每个实例新建一份
shape.Shape = new RectangleShape2D { Size = new Vector2(reach, arcWidth) };
```

**判断标准：这个资源是"读的"还是"写的"？** 只读的共享没问题；要写的必须自己复制一份。

---

## 五、核心概念四：什么该进配置

| | 举例 | 为什么 |
|---|---|---|
| ✅ **该进** | 伤害、攻速、范围、颜色、曲线、贴图引用、音效引用、子弹场景引用 | 它们**在运行期间不变**，而且需要反复试 |
| ❌ **不该进** | 当前耐久、冷却剩余时间、指向某个场景节点的引用 | 前两个**运行时会变**；第三个**换场景就失效** |

一句话判断：

> **"这行数据在游戏运行期间会变吗？会变就不该进配置。"**

还有一条同样重要：**不要为了"以后可能要用"而把所有东西都做成配置。**
数字写死在代码里、等真的需要调的时候再搬出去，也是很合理的做法。
**过早数据驱动会让一个简单功能散落在三个文件里。**

---

## 六、代码地图

| 文件 | 负责什么 |
|---|---|
| `common/data/WeaponData.cs` | ★ 自定义 Resource：字段 + DPS 计算 + 热重载方法 |
| `common/data/weapons/*.tres` | ★ 三把武器的配置（**纯文本，可以直接改**） |
| `src/stations/s09_data/WeaponSwing.cs` | 判定框：几何形状**来自数据** |
| `src/stations/s09_data/S09Data.cs` | 站台：切换 / 随机生成 / 热重载 + 面板 |

---

## 七、五条练习任务的提示

### ① 新建一个 WeaponData.tres，做出「大剑：慢、重、范围大」
**难度**：★★ 已经做了。练习：

1. 用编辑器做一个**第四把**武器：「长枪」—— 距离很远（`Reach = 110`）、范围很窄
   （`ArcWidth = 20`）、伤害中等。**体会"数据能表达多少种设计"。**
2. 把它的 `AttackInterval` 调到 `1.5`，感受"高风险高回报"是什么手感。
3. 想一想：如果一件武器的参数有 20 个，靠手写 `.tres` 还靠谱吗？
   那时候该怎么组织？（提示：**拆成"武器 + 词条"两层**，这是 RPG 的常见做法。）

### ② 给 WeaponData 加一个 [Export] 字段，让它在调参台上生效
**难度**：★★ 已经做了（热重载）。练习：

1. 加一个 `[Export] public float ScreenShakeScale { get; set; } = 1f;`，
   在 `OnSwingHit` 里用它乘一下 `ScreenFx.Shake` 的强度。
   然后给大剑设 `2.0`、给匕首设 `0.4`，**手感差别立刻就出来了。**
2. 加一个 `[Export] public float HitStopSeconds { get; set; } = 0.045f;`，同样接上去。
3. **回答**：`ScreenShakeScale` 算"手感参数"还是"数值参数"？
   它该不该进配置？（提示：没有标准答案，**取决于你的团队里谁会去调它**。）

### ③ 把 s07 战斗站里写死的伤害数字换成读 WeaponData
**难度**：★★★
S07 的 `S07Combat` 里有一行 `private const int BaseDamage = 12;`。练习：

1. 把 S07 也改成读一个 `.tres`（可以直接复用 `WeaponData`）。
2. **回答**：改完之后，S07 里还剩哪些常量？它们为什么不该搬出去？
3. **这一步真正的收获**：你会发现"S07 的 `CritChance` / `Knockback` 已经在
   `DamageInfo` 里了，而 `DamageInfo` 是从 `WeaponData` 生成的"。
   → **这就是"数据流"的雏形：配置 → DamageInfo → DamageResult → 表现。**
   S07 那条管线的前半段，其实就是"数据从哪来"。

### ④ 加一个「随机武器」按钮，体会配置驱动的灵活性
**难度**：★★ 已经做了。练习：

1. 现在的随机范围是手写的。改成**从一个"词条池"里随机组合**
   （比如「+30% 伤害」「-20% 攻速」「+50% 范围」各随机抽 2 个）。
   这是 Roguelike 装备生成的最小实现。
2. 加一个"保存这把随机武器"的功能：`ResourceSaver.Save(weapon, "user://my_weapon.tres")`。
   **然后回答：为什么保存到 `user://` 而不是 `res://`？**
   （提示：**导出后的游戏里 `res://` 是只读的**。这是 S11 存档站的第一课。）

### ⑤ 思考：什么该进 Resource，什么不该？
**难度**：★★
用第四节那张表回答这几个具体问题：

1. 玩家的**当前生命值**该不该进 Resource？（提示：不该 —— 它是运行时状态。）
2. **关卡的地形数据**该不该进？（提示：该 —— 它不变，而且很大，放进配置才不会让代码文件爆炸。）
3. **技能的冷却剩余时间**呢？（不该 —— 同 1。）
4. **一个指向场景里某个节点的引用**呢？（不该 —— 换场景就失效。存 NodePath 字符串可以，
   但那是另一回事，需要自己处理"找不到"的情况。）
5. 最后回答这个：**为什么本站的 `WeaponData` 里有 `TintColor`，却没有 `Mesh`？**
   （提示：本工程用图元画东西。真实项目里**会有** `Mesh`/`Texture` 引用 ——
   所以这一条不是"不该"，而是"取决于你有什么"。）

---

## 八、常见坑速查

| 现象 | 原因 |
|---|---|
| 改了 `.tres` 但游戏里没变 | 走了缓存。要用 `CacheMode.Replace` 或重启 |
| **崩溃 / `gchandle.is_released()`** | **在 `_Process` 里反复 `GD.Load`**。本站踩过：每秒 6 次加载，20 秒后直接崩。**配置只加载一次，把引用存起来** |
| 改一个道具，所有同种道具都变了 | Resource 是共享的。要独立副本就 `Duplicate()` |
| 所有挥砍实例的判定框一起变 | 改了共享的 `sub_resource`。要每个实例新建资源 |
| `.tres` 加载失败 | 属性名拼错（要跟 C# 属性名一致），或者 `[GlobalClass]` 忘了加 |
| 导出后的游戏里存档写不进去 | 往 `res://` 写了。**导出后 `res://` 是只读的，只能写 `user://`** |
| 一个简单功能散落在三个文件里 | 过早数据驱动。**先写死，需要调的时候再搬出去** |

---

## 九、通关后做什么

1. **真去改一次 `.tres`**：把大剑的 `Reach` 改成 `40`（和匕首一样），按下 `5`，
   然后感受"大剑变成了什么"。**这一下比读十遍文档都值。**
2. 打开 `common/data/weapons/greatsword.tres`，把它整个删掉内容再按 `5` ——
   看看加载失败是什么表现。**好的配置系统必须有"坏配置怎么办"的答案。**
   （提示：本站会 `throw`。真实项目里更常见的是"退回默认值 + 打一条警告"。）
3. 想清楚：**如果策划要自己调数值，他需要什么？**
   （提示：不是"给他看代码"，而是"给他一个不用打开 Godot 也能改的表格"。
   CSV → 导入成 `.tres` 是很常见的做法 —— **配置的格式要让写它的人舒服**。）
4. 去 `docs/00-学习路线图.md` 看一眼自检清单里 S09 那条，确认你能答上来。
