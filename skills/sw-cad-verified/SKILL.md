---
name: sw-cad-verified
description: 用代码驱动 SolidWorks 2026 建模（C# PIA / Python comtypes）的已验证做法与硬边界：连接方式、草图生命周期、选择与引用失效、闭合轮廓判据、拉伸/切除/阵列要点、故障诊断方法论、B-Rep 验收。当用户要求「写脚本画三维件」「自动建模」「COM 调 SolidWorks」「API 返回 null」「草图关系加不上」「轮廓切不掉」「圆周阵列失败」时使用。｜机械线六件套，分工见正文「相关技能」节
whenToUse: 需要脚本化建 SolidWorks 零件（草图/拉伸/切除/阵列/孔/圆角）；自动化跑不通要查原因；要从 B-Rep 回读尺寸做验收；要判断某个 SW API 在当前版本能不能用
---

# SolidWorks 2026 自动化建模（已验证动作集）

**分工**：流程与验收判据在 `mech-part-pipeline`；本技能是它的 API 手册（怎么调、什么会失败）。

**一句话**：SW 的 COM 接口在 2026 有一批反直觉行为——返回 void 的调用会静默失败、重建会让对象引用失效、闭合轮廓可能被判成多个区域。这份技能只写**真机验证过**的做法和边界，避免每次从零试错。

> **本文件约 1000 行。先看这张导航，再决定读哪节——不要通读。**
>
> | 我要… | 读 |
> |---|---|
> | **不知道该先走哪步 / 任务跨多阶段** | **技能 `mech-part-pipeline`（流程总控）** |
> | **开工前：有没有现成模板/规则？** | **§18.0 资产地图（必查）** |
> | 新建一个零件（从零到能出图） | §0 五阶段 → §6 特征 → §8 验收 |
> | 特征建不出来 / 返回 null / 建了没切 | §7 诊断 → §18.2 路由表 |
> | 方向反了、切多切少、两侧对称 | **§6 的 Sd/Dir 表** |
> | 草图不闭合 / 轮廓被吸变形 | §5、§16 |
> | **草图要参数化（以后能改尺寸）** | **§17.1** |
> | 读一个现成零件（逆向） | §23 |
> | **读真值只读体积够不够 / 参数该信几分** | **§23.1 三把尺子 + `mech-part-pipeline` §1.1~1.2 置信度** |
> | **低于规则表阈值算不算不合格 / 要不要上 FEA** | **`mech-part-pipeline` §3.2 三级处置** |
> | 装配 / 配合 / 干涉 | 第 551 行起 |
> | 反复失败、怀疑是环境问题 | §18.2 → `references/troubleshooting.md`（30+ 症状） |

## ⛔ 纪律（先读）

1. **禁止"改一个参数 → 跑一次 → 看结果"**。这是抽卡。没有新的观测数据就不要改代码。
2. **失败后第一个动作是观测**：打印坐标、轮廓数、边序列、状态码、选中数。
3. **不要相信 `IsClosed()`**——它只说明首尾相连。硬判据是 **`contours == 1`**。
4. **长参数 COM 方法、VARIANT / by-ref 参数、枚举值、选择标记，一律先查证再写**。
5. **一个尺度上验证过的参数，换个尺度要重验**（方向位在 180° 弧上正常，在 0.8° 弧上未必）。
6. **每条结论带证据等级**：【实测3+】多次复现 /【单次】只观测过一次 /【推断】没验证，只是合理猜测 /【版本】与 SW 版本绑定。看到【推断】要自己验，别当真理——本技能历史上就因为一条【推断】被误当成【实测】而返工过。
7. **数值对不上时别先怀疑自己**：技能 `solidcraft-verify` 提供第二个独立内核做交叉验证（轮廓面积到 1e-4、体积到 1e-3），用来区分「模型错」「SW 布尔残留」和「工具底噪」。

---

## 工具链（本机 Windows / PowerShell）

- **`.ps1` 一律写纯 ASCII**。PowerShell 5.1 在无 BOM 时按 GBK 读脚本，UTF-8 中文注释会被吃成乱码，**甚至吞掉下一行**（表现为 `$pia` 突然为 null、Add-Type 报参数为空）。
- **跑之前先删旧日志**：`if (Test-Path $log) { Remove-Item $log -Force }`。否则编译失败时你会读到上一轮的日志，以为脚本跑过了——这个坑会浪费整轮时间。
- `$ErrorActionPreference='Stop'`，让 `Add-Type` 编译失败直接中断，而不是继续跑旧程序集。
- 同一进程里 `Add-Type` 过的类型不会因为文件变了而重新编译；每次都要新起 `powershell -File`。

---

## 0. 通用建模流程（任何零件都按这个走）

**先有数值表，再动手。** 把零件拆成「基准 → 草图 → 特征」三层，每个关键尺寸写出数值和来源（图纸/公式/参考件回读）。没有数值表就开写代码 = 抽卡。

### 五个阶段

| 阶段 | 做什么 | 通过判据 |
|---|---|---|
| ① 定基准 | 原点放在**主回转轴 ∩ 主分度面**；第一个草图在主平面上 | 草图里至少一个图元与原点重合 |
| ② 画草图 | 几何 → 几何关系 → 尺寸，中途**不 rebuild** | `GetConstrainedStatus() == 3` |
| ③ 出特征 | 一草图对一特征；要阵列的做成独立特征 | 特征非 null，草图被消费（不是孤儿） |
| ④ 立刻验收 | 体积 / 外形 / 关键直径回读，算误差百分比 | 误差 < 0.5%（或说明来源差异） |
| ⑤ 出图目视 | `SaveBMP` | 形状与预期一致 |

**不要等全部做完再验收**——第 4 步放在每个件之后，出错才定位得到。

### 草图的优先级：关系 > 尺寸

- **位置**：第一个图元落在原点，SW 会**自动**加「与原点重合」。以原点为圆心 `CreateCircleByRadius(0,0,0,r)` 的圆，只加一个直径尺寸就是 `FULLY DEFINED`（实测）。
- **形状**：先用几何关系（相等/平行/垂直/相切/同心/对称）压住自由度，再补尺寸。每条边都硬加尺寸会把草图顶成**过定义**。
- **尺寸**：最后加，且**显式赋 `SystemValue`**（米），加完读回确认。只靠鼠标点出来的尺寸会停在"待输入"状态。

### 常见零件的第一个草图

| 零件类型 | 起步 | 注意 |
|---|---|---|
| 回转体（轴/套/销） | 半剖轮廓 + 中心线 → 旋转 | 轮廓必须全在中心线一侧 |
| 盘类（齿轮/端盖/法兰） | 圆 + 直径尺寸 → 拉伸 | 齿形/孔另做草图，别混进毛坯草图 |
| 板类（箱体壁/支架） | 矩形，一角在原点（自动带水平/垂直）→ 拉伸 | 两尺寸即完全定义（实测） |
| 对称件 | 先做一半，再用对称/镜像 | 镜像面必须是基准面，不是草图线 |

### 可运行模板

`scripts/Starter.cs` + `scripts/run_starter.ps1` 是上面五个阶段的**最小可执行骨架**（连接 → 新建 → 完全定义草图 → 拉伸 → 回读体积/外形 → 存盘 → 出图），实测 R50×20 圆柱 = 157079.63 mm³，与理论值误差 **0.000%**。

换零件只改 `TODO 1` 那段几何参数，其余保持不动——**先让它跑通，再改形状**。`scripts/SketchKit.cs` 是配套的草图封装（关系/尺寸/状态自检/日志落盘）。

---

## 1. 连接与环境

| 项 | 通用事实 |
|---|---|
| 单位 | **米、弧度**。mm 输入一律 ÷1000 |
| 对象 | 保持单一实例；不要每次重启 |
| PIA | 安装目录下 `SolidWorks.Interop.sldworks.dll`、`SolidWorks.Interop.swconst.dll` |

### 三条连接路，只有两条能用

```csharp
// ✅ C# + PIA 强类型
Add-Type -Path '<安装目录>\SolidWorks.Interop.sldworks.dll'
Add-Type -Path 'MyCode.cs' -ReferencedAssemblies $pia
ISldWorks sw = new SldWorks();
```

**Python** 也能连（`comtypes` 1.4.17 实测可用），但**裸 comtypes 写法没有逐行验证**；已验证的是本机封装：
`D:\DSH\DSH\sw-skill\solidworks-automation-skill-main\scripts\sw_connect.py`（`SolidWorksSession` / `connect_solidworks`）。
要用裸 comtypes 就把下面 C# 的规则原样搬过去——**坑与语言无关**。

> 本文代码片段一律 **C# + PIA**：这是本机验证得最透的一条路。

```powershell
# ❌ 绝对不要
New-Object -ComObject SldWorks.Application   # → 0x8002802B TYPE_E_ELEMENTNOTFOUND
```

- `SLDWORKS.exe /regserver` 在 2026 已不支持（弹窗拒绝），不要试图靠它修类型库。
- 症状 `RPC_E_DISCONNECTED` 多数不是崩溃，而是 **SW 弹了模态框在等人按键**（见下节）。

### ⛔ 实例生命周期纪律（2026-11-10 事故后新增，必读）

`new SldWorks()` **不告诉你"新建"还是"接管了用户正在用的实例"**。两种后果都真实发生过：

| 写法 | 桌面已有可见实例时 | 桌面没有实例时 |
|---|---|---|
| `new SldWorks(); sw.Visible = false;` | **把用户的窗口藏起来**，之后双击文件全部"没反应" | 建一个隐藏实例 |
| `finally { sw.ExitApp(); }` | **直接关掉用户的实例**（有未保存改动 = 数据损失） | 正常收尾 |

再叠加：外层超时把进程 kill 掉时 `finally` 不执行 → **隐藏孤儿实例**留下，占住单实例锁。此后双击任何文件都"打开成功但看不见"，用户会以为是文件关联坏了（实测排查了两天）。

**统一改用 `D:\DSH\DSH\cae\SwKit.cs`：**

```csharp
bool created;
ISldWorks sw = SwKit.Acquire(out created);      // 先复用运行中的实例，没有再新建
try {
    // ... 干活 ...
} finally {
    SwKit.Release(sw, created);                 // 只退出"自己创建"的实例
}
```

**四条硬规则：**

1. **先探测进程再决定所有权**（`SwKit` 已实现）：开工前已有 `SLDWORKS.exe` → 一律当"别人的"，不碰 `Visible`、不 `ExitApp`。
2. **绝不对已存在的实例设 `Visible = false`**。要看进度就 `Visible = true`；要静默就用自己新建的实例。
3. 收尾只退出自己创建的实例；`CloseAllDocuments(false)` 同理。
4. 批量任务**一律走 wrapper**（它带超时，且子进程被 kill 后父进程仍会收尾）：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File D:\DSH\DSH\cae\sw-run.ps1 `
       -Script D:\DSH\DSH\cae\part_read\run_reader.ps1 `
       -ScriptArgs '-Part','D:\x\part.SLDPRT' -TimeoutSec 300
   ```

   任何被中断/超时的自动化，事后补一次：
   `powershell -NoProfile -ExecutionPolicy Bypass -File D:\DSH\DSH\cae\sw-sweep.ps1 -Apply`

`sw-sweep.ps1` 只关「有文档框架但 `WS_VISIBLE=0`、且标题不含 `*`」的实例；有可见窗口或有未保存改动的**一律不动**。

---

## 2. 对话框与文档管理（自动化挂死的头号原因）

### 关掉"输入尺寸值"

```csharp
int PREF = 10;   // swUserPreferenceToggle_e.swInputDimValOnCreate
bool was = Sw.GetUserPreferenceToggle(PREF);
Sw.SetUserPreferenceToggle(PREF, false);   // 建模期间关掉
// ... 建模 ...
Sw.SetUserPreferenceToggle(PREF, was);     // 跑完恢复用户设置
```

**后果**：不关的话 `AddDimension2()` 会弹出数值修改框等回车，整个 SW 挂起，后续所有调用阻塞。

### 其他

- `CloseAllDocuments(true)` 遇到未命名文档会弹保存框 → 用 `false`。
- 批量跑之前先 `Model.ClearSelection2(true)`，残留选择会让后续特征操作选错对象。

---

## 3. 草图生命周期：几何 → 关系 → 尺寸，中途不重建

```csharp
mgr.InsertSketch(true);            // 进入
ISketch sk = mgr.ActiveSketch;

SketchSegment l1 = mgr.CreateLine(x1,y1,0, x2,y2,0);

l1.Select4(false, null);                 // 关系
Model.SketchAddConstraints("sgFIXED");   // 返回 void
Model.ClearSelection2(true);

l1.Select4(false, null);                 // 尺寸
IDisplayDimension dd = Model.AddDimension2(tx, ty, 0) as IDisplayDimension;
((IDimension)dd.GetDimension2(0)).SystemValue = valueMeters;
Model.ClearSelection2(true);

mgr.InsertSketch(true);            // 退出
```

判据：`sk.GetConstrainedStatus()` —— **1=Unknown 2=欠定义 3=完全定义 4=过定义 5=无解 6=无效解**。

### 红线：任何 rebuild 都会让旧引用失效

`EditRebuild3()`、特征创建、参数修改之后，之前拿到的 `SketchSegment` / `ISketchPoint` 引用**全部失效**。而 `Select4()` 和 `SketchAddConstraints()` 都返回 void，**失效后静默无效**——表现出来就是"选中了但关系没加上"。

对策：**在重建前把关系全加完**；或重建后用 `sk.GetSketchSegments()` 重新取引用。

### 自动关系

SW 建线时**自动**给轴平行线段加水平/垂直（关系类型 4 / 5）。手工再加是重复，无效——不是失败。

---

## 4. 选择与引用

| 做法 | 结论 |
|---|---|
| `SketchSegment.Select4(append, null)` / `ISketchPoint.Select4` | ✅ 首选，对象级选择不依赖本地化名字 |
| `SketchSegment.GetID()` | ❌ 返回 `int[]`，**不是名字** |
| `SelectByID2("<中文实体名>@草图1", "SKETCHSEGMENT", …)` | ❌ 依赖界面语言，不可靠 |
| `SelectByRay(…, swSelSKETCHSEGS=10, …)` | ❌ 选草图线段常永远 miss |
| 验证选中 | `((ISelectionMgr)Model.SelectionManager).GetSelectedObjectCount2(-1)` |
| `ISketchRelationManager.AddRelation` | ❌ 报 `RPC_E_SERVERFAULT`（`object[]` 和 `DispatchWrapper[]` 都一样） |
| `sgEQUAL` | ❌ GCS 字符串无效 |

---

## 5. 闭合轮廓：一般规律

自动化建闭合轮廓失败，几乎都栽在这几条：

1. **点距过小被判成共点** → 闭环被切成多段开链。经验阈值：同一圆周上相邻点 **≥1.0 mm** 或角度间隔 ≥0.01 rad。
2. **点列未去重** → 距离 <1e-3 mm 的点要合并掉。
3. **首尾同点** → 把第一个点再追加一次会退化（`contours=0`、status=6）。用 `(i+1) % n` 回绕闭合。
4. **短线段被拒** → 短于约 2 mm 且两端落在同一圆周上的线段，`CreateLine` 可能返回 null。**逐条检查返回值并记录失败下标**。
5. **圆弧端点已存在时 CreateArc 可能返回 null** → 先建弧后补线，或调整端点。
6. **同一草图里连续建多条曲线常失败**（第二条开始）；插入 rebuild 只会更乱。
7. **自动推断/捕捉会静默改你的端点** —— 见下，这是最难查的一条。

### 关掉自动推断（程序化建草图必做）

```csharp
// swUserPreferenceToggle_e（枚举在 SolidWorks.Interop.swconst.dll）
int[] OFF = {
   249,  // swSketchInference
     9,  // swSketchAutomaticRelations
    60,  // swSnapToPoints
   275,  // swSketchSnapsHVLines
   276,  // swSketchSnapsHVPoints
   269,  // swSketchSnapsQuadrantPoints
};
// 建草图前逐个 SetUserPreferenceToggle(id, false)，跑完恢复用户原值
```

**必须按草图类型分别设**（实测，同一零件里两种都要用）：

| 草图 | 推断 | 原因 |
|---|---|---|
| 闭合齿廓/复杂轮廓（4 段弧拼） | **关** | 开着会被自动推断挪走端点，两截面面积从 41.4369 变成 33.88 / 39.34 |
| 圆、环、键槽、孔等常规轮廓 | **开** | 关掉后圆不会自动"与原点重合"，草图永远欠定义 |

即：**画复杂轮廓前后各切一次开关**。

> **⚠️ 最容易踩的坑（2026-11 实测）**：把 `249/9/60/275/276/269` **全关**去画矩形/多边形轮廓 → 每条线都是独立开链（`contours=6`，全部 `OPEN`），拉伸直接 null，**而 SW 不报错**。
> 正确做法是**只关捕捉 `60/275/276/269`，保留 `249 (SketchInference)` 与 `9 (AutomaticRelations)`** —— 让 SW 自动合并共用端点。
> 只有在画**复杂齿廓**、并且你准备显式补共点关系时，才需要把 249/9 也关掉。
> 判据：建完立刻打 `contours` + `IsClosed()`，**不是 1 个 closed 就不要往下走**。

**症状**：明明按公式算出的端点，建完回读却被挪到坐标轴上（例：`y=−0.9131` 变成 `0.0000`）；或四段首尾相接的圆弧给出 `contours=4` 而不是 1。日志里看不出任何错误——SW 不报错，直接改。

**代价**：关掉之后 SW **不再自动合并端点**，`contours` 反而会变大。所以必须**显式加共点关系**：

```csharp
// SketchAddConstraints 返回 void，只能靠关系计数判是否生效
p.Select4(false, null);  q.Select4(true, null);
int before = p.GetRelationsCount();
Model.SketchAddConstraints("sgCOINCIDENT");
bool ok = p.GetRelationsCount() > before;   // false = 没加上
```

实测：4 段圆弧 + 4 个显式共点 → `contours=1`、`closed=True`。这套组合是目前**唯一**稳定产出闭合轮廓的路线。

### 两截面放样：先验「两截面面积相等」

放样（loft）要求各截面是**同一形状的变换**。实践中栽得最惨的一类错误是：以为自己在「旋转」截面，实际构造出的是**被压扁的畸变图形**，两个面积都不一样，SW 直接返回 null，而日志里看不出任何线索。

**构造旋转副本的正确做法**：把图形的**全部基点**各自旋转一次再重建。

```csharp
// ✅ 正确：6 个基点全部各自旋转
double[] POU = Rot(poX,  poY, rot), POD = Rot(poX, -poY, rot);
double[] PRU = Rot(prX,  prY, rot), PRD = Rot(prX, -prY, rot);
double[] CU  = Rot(ccx,  ccy, rot), CD  = Rot(ccx, -ccy, rot);

// ❌ 错误：只旋转上侧点，下侧点用 -y 得到 —— 那是在**旋转后的坐标系里又镜像一次**
// double POx = poX*cr - poY*sr;  double POy = poX*sr + poY*cr;
// Arc(cx, cy, POx, POy, POx, -POy, +1);   // ← 面积会变
```

**必须加的验收**：建完草图后用实际弧反算面积，两个截面必须相等（本例 41.4369 vs 41.4369）。面积不等就直接停，不要浪费一次放样。

```csharp
// 沿真实回路逐弧取样，格林公式求面积（mm²）
static double ArcArea(ISketchArc ar, bool reverse) { /* 取样 64 段累加 (x dy - y dx)/2 */ }
```

### 关掉推断后怎么参照原点（重要）

关推断的草图里没法靠自动关系定位到原点，而

```csharp
Model.Extension.SelectByID2("Point1@Origin", "EXTSKETCHPOINT", 0,0,0, false, 0, null, 0);  // ❌ 实测 count=0
```

**选不中模型原点**。可行做法：**用一个构造圆当"原点载体"**——

1. 临时开推断，在本草图里画一个半径已知的构造圆（圆心在原点）→ 自动获得"与原点重合"，再加直径尺寸 → 该圆完全定义；
2. 关推断，再画真正的轮廓；
3. 把轮廓各弧的**圆心点**与构造圆的**圆心点**加共点关系（`ISketchArc.GetCenterPoint2()` 拿得到点）；
4. 需要"圆心落在某半径的圆上"时，用**点 + 曲线**加 `sgCOINCIDENT`（"点在曲线上"）。

这样一个构造圆同时提供「原点」和「基圆半径」两个参照，和参考件的做法一致（它的齿廓草图里就有两条构造圆：r_f 与 r_b）。

### 放样截面草图里**不要随便加实体**

实测：给两截面草图各加一条**水平构造线**后，轮廓几何完全没变（面积仍 41.4369 / 41.4369），但放样的结果变了——单个齿槽切除量从 **1367 → 1327 mm³**，最终体积差 4,216 mm³（0.26%）。

原因：SW 用截面的**顶点序列**做对应，多一条实体就换了映射，扭转得到的实体随之改变。

- 加**构造圆**没事（实测体积不变）；
- 加**构造线**会改对应关系；
- 需要给放样截面加参照时，优先用构造**圆/弧**，或在**另一个草图**里做，别放进截面草图。

### 尺寸方向：点-点尺寸永远给**直线距离**

```csharp
DimTwo(fu, c0c, tx, ty, 51.6813);   // 想加"竖直分量"
// 回读 125.0923 —— SW 给的是两点直线距离，与"点在半径 125.0923 的圆上"重复，白加
```

要水平/竖直分量尺寸，**必须有一个线实体作参照**（点-线距离才是垂直距离）。而放样截面草图又不该加线（见上）——这两条合起来意味着：**放样截面草图很难做到完全定义**，除非接受 FIXED 之类的关系。

### 三点角度尺寸会取**反向弧**

对旋转过的截面，`AddDimension2`（三点角度）可能给出 360°−θ：实测 B 截面算出 **337.38°**（应为 22.62°），把轮廓扭曲、放样直接失败。角度尺寸慎用于「同一图形旋转副本」的场景。

### 硬判据

```csharp
object[] cs = sk.GetSketchContours() as object[];
// 必须 cs.Length == 1 且 ((ISketchContour)cs[0]).IsClosed() == true
// cs.Length > 1 → 点列自交 / 围出多个区域 / 有台阶
```

**`contours == 1` 之前，不要调用切除或拉伸。**

### 草图平面 → 草图坐标 映射表【2026SP0 实测】

| 基准面 | 草图 (u, v) | 拉伸 `Dir=false` 落点 |
|---|---|---|
| 前视基准面 (XY) | `(x, y)` | +Z |
| 上视基准面 (XZ) | `(x, **−z**)` | +Y |
| **上视的所有偏移面** | `(x, **−z**)` | +Y |
| 右视基准面 (YZ) | `(**+z**, y)` | +X |
| 前视的偏移面 (z=d) | `(x, y)` | +Z |

**记忆法**：上视面看进去，屏幕"上"是 −z；所以画 `z=28.4` 要写 `v=−28.4`。

**易错**：以为"偏移面会镜像"（旧版这么写过）——本机实测偏移面与基准面映射**一致**，镜像的是"二次偏移"或别的东西。拿不准就画 1×1 矩形拉伸 1 mm 读 bbox，10 秒定案。

### 曲线 vs 折线 vs 圆弧

| API | SW2026 实测 |
|---|---|
| `CreateSpline2(pts,bool)` / `CreateSpline(pts)` | ❌ **恒返回 null** |
| `CreateSpline3(pts,null,null,false,out status)` | ✅ 能建（`status` 为 null 即成功） |
| 闭合样条（首尾同点） | ❌ 建成但轮廓退化 |
| 多点折线 | ✅ 可闭合；缺点是接缝多、精度靠加密点 |
| **`CreateArc(圆心, 起点, 终点, 方向)`** | ✅ **闭合轮廓首选**（圆弧端点天然共用，链最干净） |
| `Create3PointArc(p1,p2,p3)` | ⚠️ **跨角 < ~2° 的浅弧不可用**：三点近乎共线，拟合圆心偏 0.35 mm、R 偏 0.35 mm |

判定：**要闭合区域去切除/拉伸 → 优先用少量圆弧拼**（4 段弧比 80 段折线稳得多）；纯空间曲线才用样条。

**`CreateArc` 两个坑**：
- 第 10 个参数 `Direction` 是 **`short`**，传 `int` 编译不过（`(short)dir`）。
- 参数是 `(圆心, 起点, 终点, 方向)`，**圆心不是起点**。写反了不会报错，只会得到一个圆心跑到别处、半径离谱的大弧——回读 `GetRadius()`/`GetCenterPoint2()` 立刻能看出来。

---

## 6. 特征操作要点

- `FeatureCut4` 共 27 个参数，**返回 null 是「多因一果」，不要归给某一个参数**【实测 4 组矩阵】：

  | 现象 | 真因（按概率排） | 判据 |
  |---|---|---|
  | 返回 null | 草图开环 / 选择集为空 / **切除落在空气里** / 终止条件与深度矛盾 | 先打 `contours`+`IsClosed()`+选中数；再想一遍"那个位置真有材料吗" |
  | 建了但体积不变 | 方向反了，切到空处 | 每刀后立刻比 `ΔV` 与「截面面积×深度」 |
  | 体积掉得比预期**多一倍** | 把 `Sd=false`（两侧对称）当单向用了 | 见下条 |
  | 切出怪异大体积 | `Flip=true` | 见下条 |

- **`Sd`（第 1 个参数）是「单/双向」开关，不是「是否切除」**【实测 4 组矩阵，20×20×10 块 + y=5 面切 Ø5×3】：

  | `FeatureCut4(Sd, Flip, Dir, …)` | 行为 | 实测 ΔV |
  |---|---|---|
  | `Sd=false` | **两侧对称**：从草图面同时向两侧各切 D1 | **118**（=2×58.9）← 坑 |
  | `Sd=true, Flip=false, Dir=false` | 单向，切 **−法向** | 58.9 ✓ |
  | `Sd=true, Flip=false, Dir=true` | 单向，切 **+法向** | 58.9 ✓ |
  | `Sd=true, Flip=true, …` | 异常大体积，禁用 | 1141 ✗ |

  **写代码时把 `Extrude()` 和 `Cut()` 分别封装，各自固定 `Sd` 与 `Dir` 语义**——别让两个函数共用同一个 `dir` 参数。
- **切除的 `Dir` 语义与拉伸相反**【实测】：拉伸 `Dir=true` → −法向；切除 `Dir=true` → **+法向**。混用必错。
- 圆周阵列：种子特征 `SelectByID2(name,"BODYFEATURE",…,mark=4)`，轴 `SelectByID2(axisName,"AXIS",…,mark=1)`，再 `FeatureCircularPattern5(数量, 弧度, false, 轴名, true, false, …)`。标记错就返回 null。
- 基准轴：选中圆柱面后 `Model.InsertAxis2(true)`。
- **圆角/倒角会占体积的千级量**，验收对不上先查它们（参考件常有 5 个：毛坯外缘倒角、孔口倒角、齿根圆角、轮毂圆角…）。**必须按原特征树的顺序补**：倒角在切齿之前（打在毛坯外缘），圆角在切齿之后（打在齿根边），顺序反了边就选不到。
- 倒角 API（**已打通**）：`IFeatureManager.InsertFeatureChamfer(int Options, int ChamferType, double Width, double Angle, double OtherDist, double V1, double V2, double V3)`。选边用 `SelectByID2("", "EDGE", x, y, z, true, 0, null, 0)`（圆柱面两条圆边可直接选中，`GetSelectedObjectType3` = 1 = swSelEDGES）。

  | 参数组合 | 结果 |
  |---|---|
  | `(0, 1, 0.003, 0, …)` | 返回 **null** |
  | `(1, 0, 0.003, 0.785398, …)` 角度-距离 | 返回 OK 但 **dV = 0**（建了没切） |
  | **`(1, 2, 0.003, 0, 0.003, …)` 等距** | ✅ OK，dV = **7,616 mm³**（与 4.5 mm²×周长×2 的估算 7,672 吻合） |

  **倒角一定要用 `ChamferType=2`（等距）**；`Type=0` 会产出退化特征。
- **旋转体上的孔/槽必须画在「垂直于轴」的基准面上**。画在含轴线的面上（如回转体的主草图面）会变成"沿轴向的孔"，切不到法兰，且**仍会切掉一点料**（实测误切 1,821 mm³），很容易被当成"孔画小了"。判别：算一算切除量是否等于 `孔数 × π/4·d² × 板厚`。
- **`FeatureRevolve2` 返回 null 的第一嫌疑是「轮廓没闭合」**，而不是轴。`GetSketchContours().Length == 1` **不能证明闭合**——实测 `contours=1` 但 `IsClosed()==False`（一条开放的链也算一个 contour）。**每次都要打 `IsClosed()`**：
  ```csharp
  for each contour: log(c == null ? "?" : (c.IsClosed() ? " closed" : " OPEN"));
  ```
  轮廓 OPEN 时旋转/放样/拉伸全都会失败，而 `contours` 数字看着是对的。
- **`CreateArc` 是"轮廓 OPEN"的常见元凶**：它会把端点**吸到圆周上**。若你给的端点坐标与半径差一点点（例：`40·sin60°` 写成 `34.6410`，实际 `|(34.6410,20)|=39.99996`），SW 调整端点后留下**亚微米缝隙**，轮廓就开了。
  **对策：用折线逼近圆弧**（端点坐标本身就精确落在圆上）。实测同一个通气器的 R40 球面顶：
  - 用 `CreateArc` → `contour OPEN`，旋转失败
  - 改用 4 段折线（60°→67.5°→75°→82.5°→90°）→ `contour closed`，旋转成功，体积误差 −0.685%
  折线段数够时精度损失可忽略，可靠性换得非常值。
- **拉伸方向由 `FeatureExtrusion2` 的第 3 个参数 `Dir` 控制，`Flip` 无效**【2026SP0 实测 4 组矩阵；本条推翻本技能旧版"两者都改不动"的说法】：

  | 调用（上视基准面，法向 −Y） | 材料落点 |
  |---|---|
  | `Dir=false` | **+Y**（法向反侧） |
  | `Dir=true` | **−Y**（沿法向） |
  | `Flip` 任意值 | 无影响 |

  控制落点两种办法：**改 `Dir`**（首选）或把草图放到偏移面。回读 `GetPartBox` 确认，别靠推演。
  外部手册对第 3 参数的注释与本条一致：`references/part-modeling.md` → `Dir # bool: 翻转挤出方向`。
  > 教训：旧版只测了 `Flip`，就把 `Dir` "同理"写进去，导致后续一次复刻多花 40 分钟。**没测过的参数不要用"同理"带过。**
- **旋转特征（`FeatureRevolve2`）的轴**：轮廓**贴着轴线**（轮廓上有落在轴上的点）时 SW 能自动推断，轴类零件因此能直接旋转；**环形件（轮廓整体离轴）推断不出来**，用普通构造线不行、用 `CreateCenterLine` 也不行。此时要显式提供轴——建一条基准轴（两个基准面求交）后与轮廓一起选中再旋转，**且轴的选择标记必须是 `mark = 4`**（实测 mark=0/1 全失败、mark=4 成功；前提是轮廓已闭合，OPEN 时任何标记都救不回来）：
  ```csharp
  // 1) 建基准轴：两个基准面求交（前视 x 右视 = Y 轴）
  选两个 RefPlane -> Model.InsertAxis2(true) -> Feature ax = FeatureByPositionReverse(0);
  // 2) 轮廓 mark=0，轴 mark=4
  skf.Select2(false, 0);
  Model.Extension.SelectByID2(ax.Name, "AXIS", 0,0,0, true, 4, null, 0);
  Model.FeatureManager.FeatureRevolve2(...);
  ```
- **`InsertRefPlane(8, +d)` 的偏移方向在本机是稳定的**【2026SP0 实测 4/4】：上视基准面偏移 `+d` 得到的就是 `y=+d` 面。**直接用，不要预先写"翻转兜底"逻辑**，建完读一次坐标确认即可：
  ```csharp
  IRefPlane rp = f.GetSpecificFeature2() as IRefPlane;
  double y = (rp.Transform.ArrayData as double[])[10] * 1000;   // 平面原点 y (mm)
  ```
- **`OptionFlip(256)` 仍然无效**：本机实测传了与没传结果一样（都返回 +d）。
- 旧版记录过"偏移 0.8651 与 60.8651 朝向相反"，那是**特定零件里两次调用的历史现象**，不能推广成"偏移方向随机"——它害得人写了多余的兜底代码。
- 真正不可靠的是 **`ISketch.ModelToSketchTransform`**：本机实测它对不同基准面返回**同一组单位矩阵**，**不能当方向判据**。判据只有一条：**在该面画 1×1 矩形、拉伸 1 mm、读 bbox**，10 秒出答案。
- **特征返回非 null ≠ 切到位**。切除成功但两个截面只重叠 0.865mm 时，体积只掉 19 mm³ / 应掉 2100 mm³。**每个特征做完立刻核对体积变化量**，与「截面面积 × 长度」对得上才算切到位。
- 出图：`Model.ShowNamedView2("*Isometric",7)` → `ViewZoomtofit2()` → `ViewDisplayShaded()` → `Model.SaveBMP(path,1600,1000)`。**多屏/副屏下屏幕截图不可靠，一定用 SaveBMP。**
- **`SaveAs` 成功后必须 `CloseDoc`**【实测踩过】：否则文件被占用，下一轮 `SaveAs` **静默失败**、`Get-FileHash` 抛 `FileReadError`，而你会拿着一份**旧文件**做验收（实测：报 V=12587，真值 12484，整套分析白做）。收尾模板：
  ```csharp
  try { m.SaveAs(path); } finally { try { sw.CloseDoc(m.GetTitle()); } catch { } }
  ```
- **`GetMassProperties2` 返回槽位**：`[0]cx [1]cy [2]cz [3]体积 [4]面积 [5]质量 [6..]惯性矩`（单位 m / m³ / m² / kg）。读错槽位会得到一排 0——先怀疑下标，别怀疑零件。

---

## 7. 诊断方法论（比任何 API 都重要）

失败时按顺序问，问不到答案不要改代码：

1. 调用**返回了什么**？null / false / void（void 要改查副作用：关系数、选中数、状态）。
2. **对象还在吗**？重建过没有？重建后用 `Get*()` 重新取。
3. **选中数是多少**？`GetSelectedObjectCount2(-1)` 是不是预期值。
4. **几何合不合法**？`contours` 数量、`IsClosed`、点的实际坐标（打印出来跟你想要的对一遍）。
5. **状态码是多少**？草图 `GetConstrainedStatus()`；带 `out status` 的 API 一律用它的状态输出。

日志要**逐行落盘**（每步 `File.WriteAllText`），不能只在最后打印——挂死时最后那行就是现场。

---

## 8. 验收（不做等于没做完）

### bbox 只能验「径向」，不能验「宽度」

`IBody2.GetBodyBox()` 的径向尺寸（直径）可靠；**宽度方向会被布尔残留污染**——实测：

| 件 | 毛坯拉伸 | bbox 宽度 | 多出 |
|---|---|---|---|
| 参考件 A | 60 | 61.5562 | +1.5562 |
| 自建件 A | 60 | **60.4631** | +0.4631 |
| 参考件 B | 105 | 107.5100 | +2.5100 |
| 自建件 B | 105 | **60.4600→105.4600** | +0.4600 |

自建件也会多出来，所以这不是「参考件多了个凸台」，而是**放样切除的截面放在实体外侧时，SW 在端面留下薄壳**。残留量随截面偏置大小变化。

**结论**：
- 验收外径（dₐ、d_f）用 bbox 可靠；
- **验收宽度要用毛坯拉伸尺寸，不要用 bbox**；
- 想精确对齐体积，就得把截面偏置做得和参考件完全一致。**参考件的偏置常是不对称的**（实测 −0.8651 / +59.4615，终止面比齿宽浅 0.5385mm），照抄后误差从 −0.075% 收窄到 −0.048%、bbox 宽度也更接近。**不要自作主张用对称 ±offset**。

1. `IPartDoc.GetPartBox(true)` 回读外形尺寸。
2. `Model.Extension.GetMassProperties2(1, out st, false)[3]` 取体积。
3. 与理论值/参考件对比，**报误差百分比**；"看起来像"不算。
4. 特征树核对：草图确实被目标特征消费（不是孤儿草图）。
5. `SaveBMP` 出图目视复核。

---

## 9. 未验证 / 禁止宣称

- 不同 SW 版本的兼容性：以上结论来自 **2026 SP0.0**，其他版本需重新验证。
- 曲面、钣金、焊件、装配配合、工程图、仿真：本技能**未覆盖**。
- 复杂草图能否做到"完全定义"：只在矩形/圆/圆弧组合上验证过（多段折线齿廓尚在攻坚）。
- **裸 Python comtypes**：只验证过经封装的路径，裸写未经逐行验证。
- 自动化产出的模型 ≠ 可投产图纸。

---

## 10. 逆向：把一个已有零件拆成「尺寸 + 特征 + 几何」

要照着一个现成零件复刻时，先用代码把它读干净，再动手建。

### 单位陷阱（最容易栽）

导出尺寸时写 `d.SystemValue * 1000` 会让**角度变成 弧度×1000**：

| 导出值 | 真实含义 |
|---|---|
| `216.9444` | 0.2169444 rad = **12.4295°**（螺旋角 β） |
| `6283.1853` | **2π = 360°**（整圈阵列） |
| `99.3950` | 0.099395 rad = **5.6949°**（齿宽上的总扭转角） |
| `266.2408` | 0.2662408 m = **266.2408 mm**（直径） |

判据：**数值 < ~100 的几乎都是角度**；机械件的线性尺寸 ≥1 mm，×1000 后必然 ≥1000。看到 `62.3053` 这种"不大不小"的数，先当角度算（3.57°），别当 62 mm。

### 特征树：别只看顶层

```csharp
IFeature f = model.FirstFeature() as IFeature;
while (f != null) {
    string tn = f.GetTypeName2();   // ProfileFeature / Extrusion / BlendCut / CirPattern / Helix / RefPlane / RefAxis
    // 该特征的驱动尺寸
    IDisplayDimension dd = f.GetFirstDisplayDimension() as IDisplayDimension;
    while (dd != null) {
        IDimension d = dd.GetDimension2(0) as IDimension;   // d.FullName / d.SystemValue
        dd = dd.GetNext3() as IDisplayDimension;
    }
    // ⚠️ 放样/切除的截面草图在**子特征**里，只看顶层会漏掉决定形状的那两个草图
    IFeature sub = f.GetFirstSubFeature() as IFeature;
    while (sub != null) { sub = sub.GetNextSubFeature() as IFeature; }
    f = f.GetNextFeature() as IFeature;
}
```

### 草图平面朝向

`ISketch.ModelToSketchTransform.ArrayData` → 12 个数：前 9 个是 3×3 基（先行后列，单位 m），第 10–12 是平移。
一眼能看出：平面法向朝哪、草图是否倾斜、剖面是**法向剖面**还是**横向剖面**。

### 草图几何

- `sk.GetSketchSegments()` → 逐条 `GetType()`：**0=直线 1=圆弧 3=样条**
- 圆弧再读 `GetCenterPoint2 / GetStartPoint2 / GetEndPoint2 / GetRadius / GetRotationDir`
- `sg.ConstructionGeometry` 为 true 的是**辅助几何** —— 往往正是原作者用来锚定尺寸的基准，复刻时要一起抄
- `sk.GetSketchContours()` → 轮廓数 + `IsClosed()`

### 读懂的标准：自己算一遍，对到小数第 3 位

把反推出的构造规律写成公式，用同样输入复算参考件的坐标。**对不上就是理解错了**，不是"大概差不多"。对上了才动手建。

---

## 11. 不确定 API 名字/参数/枚举值时——反射查，不要猜

猜参数顺序会浪费整轮时间，而且错了往往**不报错**（只是几何歪掉）。反射一次只要几秒：

```powershell
$pia = '<安装目录>\SolidWorks.Interop.sldworks.dll'
$swc = '<安装目录>\SolidWorks.Interop.swconst.dll'
Add-Type -Path $pia; Add-Type -Path $swc

# 1) 找方法名
$t = [SolidWorks.Interop.sldworks.ISldWorks].Assembly.GetType('SolidWorks.Interop.sldworks.IFeatureManager')
$t.GetMethods() | Where-Object { $_.Name -match 'Blend' } | Select-Object -ExpandProperty Name -Unique

# 2) 看签名（参数类型 + 名字 + 返回值）
$t.GetMethods() | Where-Object { $_.Name -eq 'CreateArc' } | ForEach-Object {
  ($_.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
}

# 3) 查枚举值
$asm = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -match 'swconst' }
$e = $asm.GetType('SolidWorks.Interop.swconst.swUserPreferenceToggle_e')
[Enum]::GetValues($e) | Where-Object { $_ -match 'Sketch|Snap' } | ForEach-Object { "{0} = {1}" -f $_, [int]$_ }
```

**关键：方法在 `sldworks` 程序集，枚举在 `swconst` 程序集**，两个都要 `Add-Type`，只加前者查不到枚举。

### 已实测签名（2026 SP0.0）

| 方法 | 签名要点 |
|---|---|
| `ISketchManager.CreateArc` | `(XC,YC,Zc, X1,Y1,Z1, X2,Y2,Z2, Int16 Direction)` — 末位是 **short** |
| `ISketchManager.Create3PointArc` | `(X1,Y1,Z1, X2,Y2,Z2, X3,Y3,Z3)` |
| `ISketchManager.CreateCircleByRadius` | `(XC,YC,Zc, Radius)` |
| `IFeatureManager.InsertRefPlane` | `(int C1, double V1, int C2, double V2, int C3, double V3)`；C：1 平行 / 4 重合 / **8 距离** / 16 角度 / 256 反向 |
| `IFeatureManager.InsertCutBlend` | 12 参，**没有 `InsertCutBlend2`**：`(bool Closed, bool KeepTangency, bool ForceNonRational, double Tess, short StartMatch, short EndMatch, bool IsThin, double T1, double T2, short ThinType, bool UseFeatScope, bool UseAutoSelect)` |
| `IFeatureManager.InsertProtrusionBlend2` | 17 参（多出切向长度/方向） |
| `IFeatureManager.FeatureCircularPattern5` | `(int Number, double SpacingRad, bool Flip, string AxisName, bool GeometryPattern, bool EqualSpacing, bool VaryInstance, bool SyncSub, bool BDir2, bool BSymmetric, int Number2, double Spacing2, string AxisName2, bool EqualSpacing2)`；`SpacingRad` 用**弧度**，整圈填 `2π` |

`swUserPreferenceToggle_e` 常用值：`10` 输入尺寸框、`9` 自动关系、`60` 捕捉点、`249` 草图推断、`269/275/276` 象限/水平垂直捕捉。

---

## 装配：位姿驱动的装配（比配合驱动更省事）

**推荐顺序：建零件时就定死世界位姿，装配时全用单位变换。**

1. **先从参考装配体导出每个组件的位姿**
   ```csharp
   IAssemblyDoc ad = asm as IAssemblyDoc;
   foreach (object o in ad.GetComponents(false)) {
       IComponent2 c = o as IComponent2;
       double[] a = (c.Transform2 as IMathTransform).ArrayData as double[];  // 9 旋转 + 3 平移(米)
   }
   ```
2. **建零件时直接按世界坐标建**（或建完立刻 `IBody2.ApplyTransform` 把位姿烘进去再保存）
3. 装配时 `AddComponent5` 插入即可，**不需要设 Transform**
4. 配合只剩「同轴 / 贴合」两类，不会再出现"位姿矩阵方向乘错"

### 三个硬坑

- **`AddComponent5` 前必须 `Sw.OpenDoc6(path, 1, 1, ...)` 把零件读进内存**，否则**静默返回 null**（装配体文档本身完全正常、不报错）。这是最隐蔽的一个。
- `AddComponent5` 返回的是**具体类** `Component2`，`as IComponent2` 可能失败；要用 `object o = ...; IComponent2 ci = o as IComponent2;` 或直接操作 `Component2`。
- 若坚持"先建本地坐标、装配时再变换"，要用 `T_mine = T_ref ∘ M`，其中 **M 是「你的本地坐标系 → 参考件本地坐标系」的修正旋转**。M 必须**逐件**核对——同一台机器上，挡油环要 `Rz(+90)`、毛毡密封件什么都不用加，按类别一刀切必错。

### 配合是否成立，先用数值验，别靠看

装配前先算一遍：**两个件在接触处的直径/宽度是否相等**。

⚠️ **检查器本身最容易犯的错：把「点」当成「范围」。** 件的原点几乎总在它的**端面**（草图平面），而件是有宽度的。实测我用单点采样连错三次（齿轮间隙 3mm、齿轮过盈 21、套筒过盈 9），全部是采样点恰好落在台阶另一侧。

正确做法：
```
1. 从位姿矩阵的旋转行判断本地轴在世界中的方向
2. 算出件的**轴向跨度** = 原点 ± 宽度（方向由拉伸方向定）
3. 检查整个跨距内轴的直径是否恒定且等于件内孔
```
例：轴在 `Z=−11.5` 处查轮廓得半径 36.5（直径 73.0），齿轮内孔 Ø73 → 间隙 0.000，必配。
这比装配后目视靠谱，也能提前发现"套筒内孔 Ø70 装在 Ø79 轴段上"这类干涉。

---

## 13. 本技能的实战来源与边界（诚实交代）

本技能的内容来自一次**完整复刻一台二级斜齿圆柱齿轮减速器（22 件 + 总装）**的实战，不是理论整理。

**已验证有效的（可放心照做）**
- 四段式流程（几何 → 关系 → 尺寸 → 重建）与闭合轮廓判据
- 齿轮 4 弧齿槽构造律（与参考件逐位吻合）
- 端盖类特征配方：主体旋转 + 油封槽旋转切除 + 螺栓孔 + 倒角
- 环形件「贴轴旋实心 + 切内孔」或「直接旋转 + 显式轴（mark 按 4→1→0 试）」
- 装配：位姿驱动 + `AddComponent5` 前必须 `OpenDoc6`
- 验收：体积变化量 + 磁盘回读

**已知做不到的（别在这上面耗时间）**
- **把复杂轮廓草图（10+ 段的旋转轮廓）稳定做到 `status=3` 完全定义**。实测三次，最好到「12 个长度尺寸 + 欠定义」；再补就变「无解」。
  根因：`SelectByID2("Point1@Origin","EXTSKETCHPOINT",...)` 返回 count=0，**无法显式把点锚到草图原点**。
  **绕过办法（已实测验证）**：别画复杂轮廓再旋转，改成「一个圆柱 + N 个环形切除」。

  | 草图类型 | 尺寸数 | status |
  |---|---|---|
  | 13 段旋转轮廓 | 12~13 | 无解 / 欠定义 |
  | **圆 + 直径尺寸** | **1** | **完全定义 ✓** |

  实测中间轴改为此法后，基准 Ø43 圆柱与 Ø53 凸台的草图**都是 status=3**，且体积（369,941）与原「轮廓旋转」法（369,941.4）完全一致。

  原因：一个圆只有 3 个自由度（圆心 2 + 半径 1），圆心与原点重合后加一个直径尺寸就完全定义；而 13 段闭合轮廓有 26 个自由度，需要 13 长度 + 10 角度 + 位置/旋转，**角度约束既没有自动关系可用、也不容易加对**。

  **推广（更干净的版本）：用「细轴 + 凸台」代替「粗轴 + 切除」。**

  ```
  粗轴 Ø43 + 切两段到 Ø40          →  细轴 Ø40 + 凸台 Ø43 + 凸台 Ø53
  π[400×82 + 462.25×161 + 702.25×15] = π[400×258 + 62.25×176 + 240×15] = 369,940
  ```

  两者体积**严格相等**，但每个特征都变成「一个圆 + 一个直径尺寸」，全部可 `status=3`，且不需要任何环形切除。

  **环形切除很难做对**：两个同心圆不会自动形成环（SW 会当成两个实心圆都切），加径向连接线又常因端点未合并而失败。能用加法就别用减法。

  **凸台的任意轴向定位 —— 已解决！用 `IExtrudeFeatureData2.FromOffsetDistance`**（本项目最硬的一个 API 坑）

  两条错路（都试过、都失败）：
  - ❌ 新建偏移基准面：`InsertRefPlane` 的 flip 标志失效，同一调用两次结果相反
  - ❌ `FeatureExtrusion2` 的 `T0=3 (swStartOffset) + StartOffset`：实测不生效，给 176 深度的凸台做出来接近全长，体积多 50,286

  正确写法：
  ```csharp
  Feature f = Model.FeatureManager.FeatureExtrusion2(..., 0, 0, false) as Feature;  // 先正常拉伸
  IExtrudeFeatureData2 ed = f.GetDefinition() as IExtrudeFeatureData2;
  ed.AccessSelections(Model, null);
  ed.FromOffsetDistance = startMm;       // 「从…偏移」，单位米
  ed.FromOffsetReverse = false;
  f.ModifyDefinition(ed, Model, null);
  ed.ReleaseSelectionAccess();
  ```

  实测（Ø40 基准 + Ø43 偏移40深176 + Ø53 偏移143深15）得到 **369,941.4**，与「复杂轮廓旋转」法逐位相同，而三个草图全部 `status=完全定义`。

  **这条把「每个草图完全定义」从"做不到"变成了"可实现"**：细轴 + 凸台 + 键槽 + 倒角，全是简单图元。

  **完整验证**（中间轴整件跑通）：基准 Ø40（圆+直径尺寸）+ 凸台 Ø43（偏移40深176）+ 凸台 Ø53（偏移143深15）→ 369,941.4，三个草图全 `status=完全定义`；再减两个键槽 → **363,605.4，参考 362,938.82，误差 +0.184%**。

  **键槽的两个坑**：
  - 轴沿 Z 时，键槽草图要画在**上视面(XZ)**：u = 宽度方向、v = 轴向长度。把轴向长度写进草图 x（那是径向）会让切除直接失败。
  - `FeatureCut4` 的 **D2 必须给极小值**（如 0.0005）。给 0.05 会两个方向都切，把轴切穿（单槽去掉 18,247 而非 3,480）。
- 不要指望 `Dir`/`Flip` 控制拉伸方向（无效），也不要指望 `CreateArc` 能可靠闭合浅弧。

## 12. 项目专有内容放哪

本技能**只放通用规律**。具体零件的参数、齿形公式、参考件读数、进度与接手步骤，一律放项目的 TiddlyWiki 笔记，不要塞进技能里。

本机 SolidWorks 项目（展开式二级齿轮减速器复刻）的专有内容：
- `减速器复刻·参考件参数与建模方法`
- `减速器复刻·进度与下一步`（执行手册）

可复用代码（本目录 `scripts/`）：
- `Starter.cs` + `run_starter.ps1` —— 五阶段最小骨架，改 `TODO 1` 即可套用新零件
- `SketchKit.cs` —— 草图关系/尺寸/状态自检/日志落盘的 C# 封装

---

## 14. 配合（Mate）：2026-11 瓦特连杆实战校准（重要，先读这节）

一次"六件连杆机构"的装配，连续踩了 6 个坑，全部在本机 SW 2026 复现。**以后做任何装配，按这节走。**

### 14.1 `swMateType_e` 的真实值（本机反射得到，勿凭记忆）

| 值 | 名称 |
|---:|---|
| 0 | swMateCOINCIDENT |
| **1** | **swMateCONCENTRIC** |
| 2 | swMatePERPENDICULAR |
| 3 | swMatePARALLEL |
| **4** | **swMateTANGENT** ← 把 4 当同心，会静默加成"相切" |
| 5 | swMateDISTANCE |
| 6 | swMateANGLE |

反射命令（几秒钟，别猜）：
```powershell
$swc='D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.swconst.dll'; Add-Type -Path $swc
$e=[AppDomain]::CurrentDomain.GetAssemblies()|?{$_.GetName().Name -match 'swconst'}|%{$_.GetType('SolidWorks.Interop.swconst.swMateType_e')}
[Enum]::GetValues($e)|%{ '{0,-28}= {1}' -f $_,[int]$_ }
```

### 14.2 `AddMate5` 的错误码是反直觉的

- **`err = 1` 是 `swAddMateError_NoError`（成功）**，不是失败；`mate` 返回非 null 就说明已写入。
- `err = 4` 才是失败（选择/几何不适用）。
- 判据写成 `mate == null` 才报错；不要写成 `err != 0` 就报错——否则成功也被当成失败，人会去"修"一个没坏的东西。

### 14.3 选择标记：两个实体都用 mark = 1

```csharp
((IEntity)faceA).Select2(false, 1);   // 第一个
((IEntity)faceB).Select2(true,  1);   // 第二个，追加
int n = ((ISelectionMgr)m.SelectionManager).GetSelectedObjectCount2(-1);   // 必须 == 2
object mate = ad.AddMate5(1 /*CONCENTRIC*/, 0, false, 0,0,0, 0,0, 0,0,0, false, false, 0, out int err);
```
用 `SelectByMark(false, 1)` + `SelectByMark(true, 4)` 会得到 `err=4`。

### 14.4 装配里按坐标点选面是无效的

`Model.Extension.SelectByID2("", "FACE", x, y, z, …)` 在装配环境下**返回 0 个选中**（零件环境可用）。必须改成**按几何枚举面**：

```csharp
foreach (Component2 c in (object[])ad.GetComponents(false)) {
  foreach (IBody2 b in (object[])c.GetBodies2(0))        // 注意：GetBodies2 只有 1 个参数
    foreach (IFace2 f in (object[])b.GetFaces()) {
      ISurface su = f.GetSurface();
      if (su.IsCylinder()) { double[] cp = su.CylinderParams; /* [点(3), 方向(3), 半径] */ }
      if (su.IsPlane())    { double[] pp = su.PlaneParams;    /* [法向(3), 原点(3)] ← 顺序和直觉相反 */ }
      ((IEntity)f).Select2(append, mark);                   // IFace2 本身没有 Select 方法，要转 IEntity
    }
}
```
按"组件 + 半径 + 轴线位置"过滤，比按坐标过滤可靠得多；同轴两张面用 `res[0]`/`res[1]` 一次取出，不要靠 `ReferenceEquals` 去重（COM 包装对象不相等）。

### 14.5 加完必须回读证明类型

```csharp
Feature f = m.FirstFeature() as Feature;
while (f != null) { if (f.GetTypeName2()=="Mate2") { IMate2 mt=f.GetSpecificFeature2() as IMate2; log(mt.Type); } f=f.GetNextFeature() as Feature; }
```
"mate 创建成功"不等于"类型对了"——我上一版就是 6 个"同心"其实是相切，只有回读 `Type` 才发现。

### 14.6 增量优先级（别整船重画）

**装配 ≠ 建模。** 配合有问题时，只重建 **装配体文档**（插入已有零件 + 配合），零件文件一动不动。六件连杆全量重建约 2.5 min，只重建装配体约 40 s，且不会把已经验收过的零件尺寸再改一遍。

---

## 15. 单位：草图坐标是**米**，不是毫米（毁掉过一次整船）

```
先用 mm 数值直接喂 SketchManager：150 → SW 当成 150 m
拉伸深度又按 mm/1000 给 0.006 → 零件成了"300 m 宽 × 0.006 mm 厚"的面板
```
现象：所有零件**没有厚度**、彼此**严重穿模**、体积大 1e9 倍。判据：`GetPartBox` 回读的跨度与你设计尺寸差 1000 倍就是这个坑。

对策：**定义 `const double MM = 1e-3`，草图/基准面/半径全部乘它**；回读 bbox 时再 ÷ MM 显示成 mm。任何"新零件尺寸不对"先查单位，再查几何。

---

## 16. 草图自动推断会把程序化草图改坏

实测：关掉推断前画 3 个圆，第 2 个圆心被**自动水平对齐**到第 1 个圆心（偏 0.27 mm），后两个圆的半径被改成 **R6.2**（应为 R5）。日志里没有任何报错。

```csharp
int[] TOGGLES = { 249 /*SketchInference*/, 9 /*AutomaticRelations*/, 60, 275, 276, 269 };
// 建复杂/定位草图前逐个 SetUserPreferenceToggle(t, false)，建完恢复原值
```
判据：孔位/半径造完立刻用 `ISurface.CylinderParams` 回读，与设计值逐项对比。

---

## 17. 偏移基准面会镜像局部坐标系；`PlaneY()` 必须复用同一张面

- `InsertRefPlane(8, d, …)` 建出的偏移面，其局部 u/v 轴相对基准面**可能反向**。所以**每次切到某张面后，都要按该面自己的 `Transform.ArrayData` 重算 `UX/VZ`**，不能沿用基准面的。
- 更隐蔽的一个：同一零件里如果"轮廓草图"和"孔草图"各调用一次 `PlaneY()`，而 `PlaneY()` 每次都**新建**一张偏移面，两张面的映射不一致 → 孔相对轮廓整体偏移 0.27 mm，装配时孔对不上、配合连不上。
- 正确做法：`PlaneY(y)` 先**遍历已有 RefPlane 找同 Y 的**，有就复用、没有才新建。

### 17.1 草图驱动尺寸：怎么加、加哪些、怎么验（用户明确要求，**交付前必过**）

**为什么**：坐标驱动的模型（`CreateLine(-21.45, 28.4, …)` 直接给数）几何是准的，但**改一个尺寸要重画整张草图**，装配也没法按尺寸定位。这不是"风格问题"，是交付不合格。

**每个草图必须满足的三件事**：

| 项 | 内容 | 判据 |
|---|---|---|
| 定位 | 至少 1 个图元**与原点发生关系**（重合/对称/尺寸到原点） | 无它则该草图可整体漂移 |
| 形状 | 每个自由度都有尺寸或几何关系覆盖 | `sk.GetConstrainedStatus() == 3`（完全定义） |
| 可读 | 关键尺寸**从整件基准标注**，不从局部特征标注 | 改总长时整件联动 |

**加尺寸的正确顺序**（与 §3 一致，中途不 rebuild）：

```csharp
// 1) 几何：先画线/圆，坐标给"名义值"即可
// 2) 关系：水平/垂直/相切/同心/对称，用 SketchAddConstraints 压自由度
// 3) 尺寸：最后加，且必须显式赋 SystemValue（米）
l1.Select4(false, null);
IDisplayDimension dd = Model.AddDimension2(tx, ty, 0) as IDisplayDimension;
((IDimension)dd.GetDimension2(0)).SystemValue = 21.45e-3;   // 加完立刻读回
Model.ClearSelection2(true);
```

**加哪些**（按零件类型）：

| 特征 | 必加尺寸 |
|---|---|
| 矩形/板轮廓 | 长、宽各 1 个 + 一个角与原点重合（或 2 个到原点距离） |
| 圆孔 | **直径** 1 个 + 圆心到两条基准的定位距离 2 个 |
| 孔组 | 首孔定位 + 间距 + 阵列参数（不要每个孔单独标：14 个尺寸会让 SW 判过定义/无解） |
| 圆角/倒角 | 半径 + 被作用的边 |
| 拉伸/切除 | 深度（`IExtrudeFeatureData2.GetDepth` 回读确认） |

**验证**（每张草图建完就打）：

```csharp
int st = (mgr.ActiveSketch as ISketch).GetConstrainedStatus();
// 1=Unknown 2=欠定义 3=完全定义 4=过定义 5=无解 6=无效解
```
`== 3` 才算过。做不到 `3` 的常见原因：复杂轮廓自由度太多（26 个），此时**换建模策略**——参考 §13 的"细轴 + 凸台"或"圆 + 直径尺寸"，把一张复杂草图拆成多张简单草图，而不是硬加尺寸。

**反例（真实事故）**：2026-11 复刻 5015 安装座，12 个特征的草图**全部是坐标驱动、0 个驱动尺寸**，`GetConstrainedStatus` 全为 2。体积验收过了，但原件改一个壁厚就得整件重画——被用户当场指出。

**约束数量纪律（用户明确要求）**【2026-11 实测】：

`FullyDefineSketch` 的第 3 个参数 `RelationsToApply=1023` 是"10 种关系全开"，实测会把关系数撑爆——
一张 19 个关系的轮廓草图补完后变成 **105 个关系**（50 重合 + 10 水平 + 6 垂直 + 9 共线 + 8 等长 + 14 距离）。
后果：改一个尺寸，几何容易乱跳；关系冗余，后面想局部调整只会更乱。

**规则**：
| 项 | 要求 |
|---|---|
| `FIXED` 关系（`GetRelationType()==17`） | **每张草图 0 个为佳，最多 1 个**（一个"整体钉在原点"可以接受，逐图元 Fix 不行） |
| 几何关系总数 | 够用即可；**优先让"尺寸"定义形状，不要让"约束"钉死形状** |
| `RelationsToApply` | 别给 1023。要么 `UseRelations=false`（只补尺寸），要么只给水平/垂直/重合的位掩码 |
| 验收 | 状态==3 **且** 驱动尺寸>0 **且** Fix≤1；只满足第一条不算过 |

```csharp
// 保守做法：只补尺寸，不加几何关系
int ret = mgr.FullyDefineSketch(true, false, 0, true, 1, null, 1, null, 1, 1);
```
（`UseRelations=false` 下补不到 3 时，再**手工补少量**水平/垂直关系，不要一转手就把 10 种全开。）

**两种策略实测对比**（同一个 11 草图零件）：

| 参数 | 到 status=3 | 关系总数 | 代表草图 | 体积 | Fix |
|---|---|---|---|---|---|
| `UseRelations=true, 1023` | **11/11** | 223 | 草图2：**105** | 12 484.3 | 0 |
| `UseRelations=false, 0` | **9/11** | 122 | 草图2：**40** | **12 484.0** | 0 |

结论：**默认用 `UseRelations=false`**（关系少一半、几何更准），对没到 3 的 2 张草图手工补水平/垂直即可；只有在"必须 100% 完全定义"且不在乎关系冗余时，才用 1023。

**⚠️ 一个反直觉点**：`GetConstrainedStatus()==3` 与"几何正确"是两件事。为压自由度加的**对称/相等关系会静默改几何**（§16），所以加完尺寸要**回读关键坐标**再对一遍。

---

## 18. 相关技能与知识库（互相引用，别再各写一套）

### 18.0 机械线资产地图（**开工前必查**，别凭记忆想"我记得有…"）

> **2026-11 事故实录**：一次"电机安装座复刻 + 分析"全程只用本技能，漏掉了整条链路——
> `manufacturing_rules.yaml`（M3 攻丝底孔应为 **2.5**，我用了 2.6；过孔应 **3.4**，我用了 3.0；漏了 **0.3 孔口倒角**）、
> `sw_hole_features.py`（复合孔封装）、`design-rule-check`（DFM）、`solidworks-threaded-holes`（螺纹孔特征）、`solidworks-vibecad`（设计计划 + **电机安装座官方示例**）。
> 不是不会做，是**开工时没想到它们存在**。根因：技能靠"description 一句话"被发现，而那句话只讲"它是什么"，不讲"什么时候该想起它"。

**按零件类型查（先找模板，再动手）**：

| 我要做的零件/操作 | 先读 | 现成封装/脚本 |
|---|---|---|
| **CNC 安装座 / 连接块 / L 支架 / 底板** | `subskills/solidworks-vibecad`（`knowledge/manufacturing_rules.yaml` + `examples/motor_mount_*`） | `create_cnc_mount_template.py --dry-run` |
| 孔（通孔 / 沉孔 / 沉头 / 槽 / 阵列） | `references/complex-hole-features.md` | `scripts/sw_hole_features.py`（**不调 HoleWizard5**，签名未验证） |
| 螺纹孔（M3–M12 内螺纹） | `subskills/solidworks-threaded-holes` | `create_threaded_hole_template.py` |
| 圆角 / 倒角 / 孔口倒角 / CNC 口袋 | `subskills/solidworks-fillet-chamfer-cnc` | `create_cnc_mount_template.py` |
| 出工程图 / 尺寸链 / 孔表 / BOM | `subskills/solidworks-engineering-drawing` | — |
| DFM / 孔边距 / 壁厚 / 标准件审查 | `references/design-rule-check.md` | `cad_studio.py check-drc` |
| 齿轮 / 链轮 / 弹簧 | `references/gears.md`、`sprocket.md`、`spring.md` | — |
| 传动选型（电机 / 同步带） | `references/drive-sizing.md` | — |
| 公差 | `references/tolerance-extraction.md` | — |
| 选边（圆角/倒角找不到边） | `references/edge-selection.md` | — |
| 装配 / 配合 / 干涉 | `references/assembly.md` | `sw_assembly.py` |
| 万用症状表（30+ 条） | `references/troubleshooting.md` | — |

> 根目录：`D:\DSH\DSH\sw-skill\solidworks-automation-skill-main`

**关键数值表——直接引用，不要自己编**（`vibecad/knowledge/manufacturing_rules.yaml`）：

| 紧固件 | 攻丝底孔 | 过孔 clearance | 孔口倒角 |
|---|---|---|---|
| M3 | **2.5** | **3.4** | **0.3** |
| M4 | 3.3 | 4.5 | 0.4 |
| M5 | 4.2 | 5.5 | 0.5 |
| M6 | 5.0 | 6.6 | 0.6 |
| M8 | 6.8 | 9.0 | 0.8 |

CNC 板件默认：最小厚 **4.0** / 默认厚 8.0 / 外角 **R6** / 边倒角 **1.0**。

**设计计划优先（vibecad 的范式）**：接需求先产出 `part_family + parameters + features[]`（每项带 `required` 与 `fallback`），再写代码。`examples/motor_mount_plan.json` 是"电机安装座"的完整样例，可直接改参数复用。

### 18.1 机械线技能本身（六件套，别只用一本）

| 技能 | 管什么 | 什么时候转过去 |
|---|---|---|
| `engineering-problem-solving` | 解题总控：判题型、排流水线、汇总交付 | 来的是**题目**（要写解答）而不是数模 |
| `problem-image-reading` | 只转写题图 | 试卷/手写照片——**不要**用 §23 的读零件流程去读 |
| `engineering-mechanics` | 力学符号约定、答题骨架 | 拿零件去算内力/应力/刚度 |
| `engineering-visualization` | 二维出图规则与自查 | 要出对比图/曲线/切片轮廓 |
| `sw-cad-verified` | 建模与读零件（本技能） | —— |
| `solidcraft-verify` | 第二内核数值对拍 | 回读的体积/bbox/质心可疑 |

> 这 6 个技能的物理位置不都在 `~/.dsh/skills/`：`engineering-*` / `problem-image-reading` 在
> `~/.dsh/profiles/<profile>/node_modules/dsh-mech-workbench/skills/`，`sw-cad-verified` / `solidcraft-verify` 在 `~/.dsh/skills/`。
> **找技能别只 ls 一个目录。**

**交接口（这些都是已经重复踩过的）**：
- `GetPartBox` 与设计值差 1000 倍 → 先查 §15（草图坐标是米），不是实体异常；再让 `solidcraft-verify` 对拍。
- §23 读出来的数（体积/面积/孔径）要进方程 → 先统一单位再交给 `engineering-mechanics`。
- 3D 视图自己截图/导出；切片轮廓可以交给 `engineering-visualization` 画成二维对比图，但**不要手描顶点**。

### 18.2 症状路由表（比通读整本手册快 10 倍）

> 外部手册根目录：`D:\DSH\DSH\sw-skill\solidworks-automation-skill-main`

| 你现在的症状 | 先查这里 |
|---|---|
| 特征返回 null / 建了没切 / 体积对不上 | 本技能 §7 → 再查 `references/troubleshooting.md` |
| 拉伸/切除方向、深度、"两侧对称" | 本技能 §6 的 Sd/Dir 表 + `references/part-modeling.md`（逐参数注释） |
| 草图不闭合 / 几何被推断挪走 | 本技能 §5、§16 |
| 草图要参数化 / 完全定义 | 本技能 **§17.1** + `references/part-modeling.md` 的 `sw_sketch_quality.fully_define_sketch()` |
| 保存失败 / 文件被占 / 删不掉 | `references/troubleshooting.md` 的「保存/导出失败」「SaveAs 错误码 1」「临时 SLDPRT 删除失败」 |
| 装配 / mate 类型不对 / 轻化组件选不中 | `references/assembly.md` |
| 干涉检查结果可疑 | 本技能 §20 + `references/assembly.md` |
| 想直接抄现成封装 | `references/part-modeling.md`、`scripts/sw_assembly.py`、`scripts/sw_measure.py` |

**重要**：`references/troubleshooting.md` 收录了 30+ 个症状条目（含"特征创建失败（返回 None）""FeatureCut4 长参数不稳定""SaveAs 错误码 1"）。**反复失败三次以上，必须先去翻它，不要继续试错。**
- **`solidcraft-verify`** —— 第二内核数值对拍（体积/质心/包围盒），零件尺寸可疑时用它，不要只信 SW 回的 bbox。
- **真实参考装配体**：`D:\南工小樱\舵\轮组V1.5.SLDASM`（RoboMaster 舵轮，85 组件）。工艺口径：板 **6 mm / 10 mm**、轴承用标准件 **61804-2Z（20×32×7）/ F619-2Z**、M3/M4 螺栓 + 垫片 + 挡圈。做机构前先照它定板厚与标准件。
- **TiddlyWiki**：`瓦特连杆避震·28kg四舵轮底盘`（设计参数与修正方案）、`SolidWorks 自动化·配合与单位踩坑`（本节的笔记版）。

---

## 19. 装配放置：`AddComponent5` 会把包围盒中心对准插入点（2026-11 实测）

**不是**把零件原点放到插入点，而是**把该零件包围盒中心对到插入点，X/Y/Z 三轴都居中**。

实测：把 Y 区间 −12..−6 的机架插到 (0,0,0) → 组件 `tY=+9`，几何变成 −3..3；再插到 (0,50,0) → `tY=+59`。所有 6 mm 板因此全叠在 Y≈0，干涉检查报 **78 处**。

**正确写法**：插入点 = (零件包围盒中心 X, 设计层位中心 Y, 零件包围盒中心 Z)。

```csharp
double[] pb = (pd as IPartDoc).GetPartBox(true) as double[];   // 米
double cx = 0.5*(pb[0]+pb[3]), cz = 0.5*(pb[2]+pb[5]);
ad.AddComponent5(path, 0, "", false, "", cx, yBandCenter, cz);  // yBandCenter 按你的层位设计给
```
改完后同一批零件干涉从 78 → 3 → 1（剩下那 1 处是销端伸进相邻板 1 mm 的设计问题）。

> 顺带：**不要依赖 `IExtrudeFeatureData2.FromOffsetDistance`** 给零件做 Y 偏置——本机实测多次静默不生效（销仍然从 0 起）。要么把草图真的放到偏移基准面上（`InsertRefPlane(8, d)` 后用 `sketch.ModelToSketchTransform` 与回读 bbox 双重确认），要么干脆在装配里按层位插入。

## 20. 干涉检查：`ToolsCheckInterference2` 返回 null ≠ 零干涉

我一开始用 `ad.ToolsCheckInterference2(0, null, false, out c1, out c2)`，得到两个 null 就报"0 干涉"——**错**。null 只说明"没算/拿不到结果"。

正确用法（`scripts/sw_measure.py` 同款）：

```csharp
IInterferenceDetectionMgr idm = ad.InterferenceDetectionManager;
idm.TreatSubAssembliesAsComponents = false;
idm.TreatCoincidenceAsInterference = false;   // 贴合的板面不算干涉
idm.IgnoreHiddenBodies = false;
idm.IncludeMultibodyPartInterferences = false;
object[] items = idm.GetInterferences() as object[];
int n = idm.GetInterferenceCount();
// 每项读 Volume(属性, 米³) 与 Components(组件数组)
idm.Done();
```
判据：`GetInterferenceCount()` 与 `items.Length` 一致才算拿到证据；`null` 一律当"未测量"，不得当零。

## 21. 配合前先量厚度：不要拿"层界面坐标"当靶子

板是有厚度的，两个铰接件在 **Y 方向上贴的是"面"，不是"层界面数值"**。正确顺序：

1. 逐件回读实际 Y 区间（`GetPartBox` / 组件 box），确认各件**没有叠在同一 Y**；
2. 取 A 的 maxY 面与 B 的 minY 面做重合，孔-孔做同心；
3. 加完回读 `IMate2.Type`（1 同心 / 0 重合）确认类型；
4. 再跑一次干涉检查，`GetInterferenceCount` 必须给出数字。

---

## 22. 装配标准流程（2026-11-10 全流程跑通版，按这个顺序抄）

1. **零件**：草图坐标乘 `1e-3`（米）。定位/孔草图前关推断（`249/9/60/275/276/269`），画完再恢复。
2. **层位**：零件可以建在任意 Y；装配时按「设计层位中心」插入即可：
   ```csharp
   double[] pb = (pd as IPartDoc).GetPartBox(true) as double[];   // 米
   double cx = 0.5*(pb[0]+pb[3]), cz = 0.5*(pb[2]+pb[5]);
   ad.AddComponent5(path, 0, "", false, "", cx, yBandCenter, cz);
   ```
   因为 `AddComponent5` 会把**零件包围盒中心**对到插入点（X/Y/Z 三轴都居中）。只补 Y 会让所有板叠成一层（实测 78 处干涉）。
3. **配合前必做**：`ad.ResolveAllLightWeightComponents(false); m.EditRebuild3();`
   轻化组件选面会**静默失败**（选中数=0），这是最容易误判成"找不到面"的坑。
4. **每个转动副**＝同心 + 重合：
   ```csharp
   // 同心
   AddMate5(1 /*CONCENTRIC*/, 0, false, 0,0,0, 0,0, 0,0,0, false, false, 0, out err);
   // 重合：align 必须 2（ANTI_ALIGNED），否则会把板翻过去（实测干涉 1→8）
   AddMate5(0 /*COINCIDENT*/, 2, false, 0,0,0, 0,0, 0,0,0, false, false, 0, out err);
   ```
   两张面都用 `Select2(append, mark=1)`；选中数必须 == 2。
5. **判据**：`mate != null` 即成功（`err=1` 是 NoError，`err=4` 才是失败）；加完回读 `IMate2.Type` 证明类型。
6. **复核**：`IInterferenceDetectionMgr` → 设三个属性 → `GetInterferences()` + `GetInterferenceCount()` 一致 → `Done()`。`ToolsCheckInterference2` 返回 null **不是**零干涉。
7. **别加"层厚总尺寸"**：层间已被重合钉死，再加距离配合就是过定义。

### 22.1 这一步最容易翻车的地方（按出现频率）

| 现象 | 真因 |
|---|---|
| 零件没厚度/全穿模 | 草图按 mm 传给 API（应乘 1e-3） |
| 六件叠在同层、干涉几十处 | `AddComponent5` 三轴居中，插入点没补偿 X/Z |
| "选中数=0" | 组件轻化，未 `ResolveAllLightWeightComponents` |
| 加了配合后干涉反而变多 | 重合配合用了默认 align（同向），把件翻过去 |
| mate 建出来类型不对 | `swMateType_e` 猜错（4 是相切，同心是 1） |
| 孔位偏 0.27 mm、半径变成 6.2 | 草图推断自动对齐/自动相切 |


## 23. 读零件：先看外形，再读特征树（2026-11 实测，界面语言无关）

读零件分两个阶段，别混。

**阶段 A —— 看外形。** 外形是一个东西，用三把尺子量它，三份答案互为证伪：

| 视图 | 把外形看成 | 回答 | 成本 |
|---|---|---|---|
| **向量**（面代数 / 族） | 一组带方程的曲面：平面 `n·x=d`、圆柱 `(轴, R)`、锥/球/环 | 几个方向、几层、壁厚、半径谱、谁跟谁平行/同轴 | 秒 |
| **拓扑** | 面怎么连成壳（面-边-面图、边环） | 通还是盲、内还是外、凸还是凹、几处连通 | 分钟 |
| **微分**（体素 / 切片） | 材料占不占（把体积微分成立方格） | 里面是空是实、薄壁、孔的真实开口 | 分钟 |

三者缺一个，错得都像对的：向量说"这里有个 Ø3 圆柱面"，拓扑说"两端开口 = 通孔"，微分说"沿轴抽样确实空着"。
seat 那次"12 个沉头通孔判成盲坑"，就是只信了微分（射线）没做拓扑端环邻接；反过来只信拓扑 + Euler，又给出 `g=4` 的假数。

**和流程的关系（必须先看清）**：这三把尺子不是"逆向时的高级选项"，是 `mech-part-pipeline` P1 的**必备项**。P1 要求真值读满四层——

| P1 的层 | 这里对应的尺子 | 必须落到真值表里的数 |
|---|---|---|
| ① 标量 | 质量属性 + 零件框 | 体积/面积/质量/质心/惯量/bbox |
| ② 结构 | 阶段 B 特征树 + 草图线段 | 净特征表（剔除过程产物后） |
| ③ 拓扑 | 拓扑尺子 | **通孔数、连通分量、同轴分组** |
| ④ 微分 | 体素 / 切片 | **三向切片面积曲线、最小壁厚** |

只做 ①② 就开工 = 一维真值。真值表缺 ③④ 两行，P3 的"孔系"和"截面"两把尺子就**没有基准可比**。
每个数还要在真值表里标置信度（A 双证 / B 单证 / C 反推 / D 猜），判据精度不得超过基准精度 —— 规则见 `mech-part-pipeline` §1.2。

**阶段 B —— 读特征树。** 只作对账，回答"作者怎么做的、怎么改、有没有被压缩"。

为什么 A 必须在前：外形是**结果**，特征树是**生成顺序**（删改、"所选轮廓"、压缩都会让两者对不上）；
而且形状数据在 B-Rep 里，不经特征树就能拿到，反倒是特征树要先过 `ICE` / Folder / Suppressed 那一堆坑。

**A 内部按成本排队**（都属"看外形"，不要和 B 混）：向量秒级、永远先做；问"整体是什么形状"→ 先切两刀看微分；
问"孔还是凸、通还是盲"→ 先建邻接看拓扑。三把尺子跑到能互相印证，就可以进 B。

**先写"外形小结"（一句话 + 3~6 条事实）再往下**；小结写不出来，说明还没看懂，别急着读树。

**什么时候不必外形优先**：几何简单、特征却很多（平板 + 阵列孔、镜像、系列零件）时，三把尺子里向量几乎没有信息量，
直接读树更快（阵列参数一步到位）。判据是**信息密度 = 几何复杂度 ÷ 特征数**：高 → 外形优先；低 → 树优先。

**两条边界**：
1. 定长 16 维描述子只是**摘要**（给检索/相似件比对用），7 bin 面积加权法向直方图对"12 个 60° 均布径向孔"完全无感——
   要"看"就得用族、拓扑、微分这三把尺子。
2. 先看外形**不省时间**（一样要开文档遍历 B-Rep），省的是"问错问题"的次数。

### 23.0 先认清：`.SLDPRT` 不是能直接解析的容器

实测三个零件（SW2021/SW2026）：

- 文件头**不是 OLE**（不是 `D0CF11E0`），而是变化的 4 字节魔数 + `00000004`：`56E790B2 00000004`、`B0F61513 00000004`、`CA4A2E85 00000004`；
- 全文件**熵 ≈ 7.97–7.99 bits/byte**，可打印连续串只有随机碎片，没有特征名、没有尺寸；
- 结论：几何与特征数据以压缩/编码形式存放，**纯二进制解析拿不到尺寸**。别在这条路上花时间，直接上 API 或中性格式导出。

**参数到底从哪读：**

| 要读什么 | 接口 / 途径 | 备注 |
|---|---|---|
| 体积 / 质心 / 包围盒 | `Extension.GetMassProperties2`、`IPartDoc.GetPartBox` | 需要 SW 会话 |
| 拉伸/切除参数 | `GetDefinition() as IExtrudeFeatureData2` | 端条件、深度、薄壁 |
| **孔向导** | `IWizardHoleFeatureData2` | 用 `IExtrudeFeatureData2` 读会返回 null |
| 草图尺寸 | `IFeature.GetFirstDisplayDimension` + `IDimension.SystemValue` | 长度/角度混用，注意单位 |
| 自定义属性 / 材料 | `ICustomPropertyManager.Get5`、材质字段 | 不启 SW 时可用 Document Manager |
| 面/圆柱/顶点 | `GetBodies2 / GetFaces / GetEdges` | 见 23.3 |
| 无 SW 的替代 | Document Manager API（元数据）；导出 STEP/Parasolid/STL 再解析 | STEP 是文本 ISO 10303；STL 只有三角面 |

### 23.1 打开、收尾、文档级

```csharp
int err = 0, warn = 0;
IModelDoc2 model = sw.OpenDoc6(path, 1 /*part*/, 1 /*silent*/, "", ref err, ref warn) as IModelDoc2;
// ... 
sw.CloseDoc(model.GetTitle()); sw.ExitApp();
```

`err=0 && warn=0` 才继续。配置用 `GetConfigurationNames()`；自定义属性用 `Extension.get_CustomPropertyManager("")` 转 `ICustomPropertyManager` 后 `Get5(name,false,out val,out res,out was)`（Get4 只有两个 out 参数）。出图用命名视图 + `SaveBMP`（多屏下屏幕截图不可靠）。

### 23.2 外形摘要：先把 B-Rep 压成一句话（向量视图的第一步）

**定长描述子（16 维，只用于相似度/检索）**：`log V`、`log A`、归一化包围盒三边（降序）、
填充率 `V/(bx·by·bz)`、`A/V^(2/3)`、7 bin 面积加权法向直方图、平面面积占比、圆柱面积占比。
- 相似度**必须 z-score 后再算欧氏距离**：原始向量直接余弦会退化到 0.99，谁都像谁。
- 直方图之和 = 平面面积占比，可用来对账面积。

**族表示（做"看"用，比定长向量有信息量）**：
- 平面族：按 `(n,d)` 归并 → "几个方向 × 每方向几层"，层间距就是板厚/台阶高。
- 圆柱族：按 `(轴方向, 半径)` 归并 → 半径谱 + 轴方向分布就是"孔系"；再按同轴合并成完整孔。
- **轴方向分布是唯一能一眼看出"均布"的东西**：12 根轴、同半径、每 60° 一根 → 直接读出均布孔系。

**输出格式**（回答里就按这个写，别只贴数字）：

> 外形：<块/板/壳>，<bx×by×bz> mm，填充率 <x>%；主方向 <n> 个；孔 <k> 组，半径谱 <r…>；
> 对称性：<n 次/无>；可疑处：<向量暗示但还没证实的>。

### 23.3 几何层：B-Rep 与面向量

- `GetBodies2(0,false)` → 面/棱/顶点计数；`IsPlane/IsCylinder/IsCone/IsSphere/IsTorus`。
- **`PlaneParams` = [法向, 原点]**，`CylinderParams` = [点, 方向, 半径]；`GetArea()` 是 m²；`GetBox()` 6 个值（m）。
- 平面写 `n·x = d`，`d = n·p`；**符号统一**（首个非零分量取正）；平行 `|n₁·n₂|>0.999`；间距 `|d₁−d₂|`；同轴 `|a₁×a₂|<0.02` 且轴线距离 `<0.02 mm`。
- **`PlaneParams` 的法向符号 ≠ 外法向**（实测两个端面都读成 +y），只能当平面方程用；面朝向要靠体素或 facet 法向。
- 质量属性：`Extension.GetMassProperties2(1,out st,false)[3]`；**没有实体时读不到，先数 body**。
- 弦长恒等式 `2√(R²−d²)` 用来验证"平面确实削在某个圆柱上"。

### 23.4 几何层：体素（含自适应局部加密）

- 输入用 `IBody2.GetTessellation()`（**不要 SaveAs 导 STL**：本机实测带整体平移、体积算出负值）；`MaxFacetWidth ≈ h/2`，`NeedFaceFacetMap=true` 才能 `GetFaceFacets`。
- 边长 `h ≤ 最小特征/5` 且 `h ≤ 壁厚/2`（Ø2.5 → 0.25 mm 可靠）；射线奇偶用**多射线投票**（±0.2h，取多数）压掉奇数交点列。
- 校验：体素体积 vs 内核 ≤1%；切片用来判断"薄壁还是实心"。
- **自适应**：全局均匀做到 1e-3 是立方爆炸（本机 5.4e9 体素不可行）。正确做法是对小特征取局部盒 + 0.5 mm 余量，`h_loc = clamp(r/12, 0.05, 0.2)`，局部重新体素化——体素数只随表面积增长，等效 0.05 mm。

### 23.5 形状层：拓扑（连接与通/盲）

- 边→两面：`IEdge.GetTwoAdjacentFaces2()`；面→边环：`GetFirstLoop/GetEdges`。面的指针映射用 `Marshal.GetIUnknownForObject(face)`，COM 包装对象不能用 `==` 比较。
- **闭合圆边没有顶点**：`GetStartVertex/EndVertex` 返回空。别因此跳过——边仍带"两侧邻接面"，端点用 `IEdge.GetCurve()` 求值。
- 通/盲：圆柱面两端边环各邻接什么面——外表面=开口、垂直于轴的平面=底（盲）、内孔面=通、另一圆柱=交孔。
- 孔要**先按同轴分组**：`R2.75 沉孔` 与 `R1.5 孔` 同轴 = "Ø5.5 沉孔 + Ø3 通孔"；按半径分组会把一个孔拆成两种"特征"。
- 射线法只作交叉验证，**起点必须在孔内空气中**（轴线 + 沉孔底/开口位置）；输出"两侧受阻"八成是起点落在材料里。
- **`V−E+F=2−2g` 对 B-Rep 不可靠**：闭合圆边不是 CW 1-cell。实测 `51−125+68=−6`（g=4）与同轴分组数出的 ≥12 个通孔矛盾。**genus 用"通孔计数"直接数。**

### 23.6 形状层：分段（几何版特征树）

同面归组（同 `(n,d)` / 同轴同径）→ 连通合并 → 签名分类：圆柱+端环+底=盲孔；两端开口=通孔；平面底+侧壁环+圆角=凹坑；窄条+两侧相切=圆角/倒角；同径同轴阵列=孔系（查 60°/120° 是否均布）。产出特征表，与历史树对账：树能解释几何就采信，解释不了以几何为准。

### 23.7 元件层：特征与草图

- `FirstFeature` → `GetNextFeature` 遍历；草图同时出现在顶层与子特征里，别重复计数。
- 按**类型白名单**过滤：`Extrusion`、**`ICE`（SW2026 的切除）**、`Chamfer`、`Fillet`、`ProfileFeature`、`RefPlane`、`OriginProfileFeature`；Folder/Light 一律跳过。
- 草图：`GetSketchSegments` → `GetType()`（0 线 / 1 弧·圆 / 3 样条）。**`ISketchSegment` 没有 `GetStartPoint2`**，要转 `ISketchLine` / `ISketchArc`；圆是"起点=终点"的合并端点弧。`ConstructionGeometry` 是作者用来定位的基准，逆向时要一起抄。
- **轮廓判据**：草图有重复/重叠线段时 `GetSketchContours` 会返回开链（实测 4 条轮廓、边数 5/1/1/1、全部 `closed=False`），但下游拉伸照样成功。**已消费草图的 contours/closed 不可信，以 B-Rep / 体积为准。**

### 23.8 元件层：参数与逐特征体积

- `GetDefinition() as IExtrudeFeatureData2` → `GetEndCondition(true/false)`、`GetDepth`、`IsThinFeature`。
- **`GetDepth` 只在 Blind 下有意义**：贯穿/到下一面一律返回 0。
- 枚举：0 Blind / 1 ThroughAll / 2 ThroughNext / 3 UpToVertex / 4 UpToSurface / 5 OffsetFromSurface / 6 MidPlane / 7 UpToBody / 9 ThroughAllBoth / 10 UpToSelection / 11 UpToNext。
- **逐特征体积**：从末尾往前 `SetSuppression(0)` + `EditRebuild3()` 读体积，再从前往后 `SetSuppression(1)`；差值即净去除量。**`SetSuppression2` 在部分文件上返回 True 但静默不生效。**
- 校验：`Σ(各特征增量) = 成品体积`；只剩基体时 `体积 ÷ 拉伸长度 = 轮廓面积`，与草图鞋带公式对账。对不上多半是切除用了**「所选轮廓」**、只切掉一个子区域。

### 23.9 校验

体积（≤1%）、面积（≤2%）、截面面积积分（±2%）、**通孔数 vs 声明的 genus**、对称性、跨通道一致（向量尺寸 vs 体素实测）。任何一门不过，回退到对应层，不要用"大概对"放过。

### 23.10 坑表

| 现象 | 真因 |
|---|---|
| `ISketchSegment.GetStartPoint2` 编译不过 | 方法在 `ISketchLine`/`ISketchArc` 上 |
| 找不到切除特征 | SW2026 类型名是 `ICE`，不是 `Cut` |
| `GetDepth` 总是 0 | 端条件是贯穿/到下一面 |
| 轮廓 `closed=0` 但拉伸成功 | 重复/重叠线段，轮廓 API 返回开链；以 B-Rep/体积为准 |
| `SetSuppression2` 返回 True 但没抑制 | 用 `SetSuppression` |
| 圆读成"起点=终点" | 圆就是合并端点的圆弧 |
| `GetEdgeCount=125` 只导出 72 条 | 闭合圆边无顶点，被跳过 |
| `V−E+F` 的 genus 与目测不符 | 闭合边不是 CW 1-cell，欧拉公式失效 |
| 径向孔判成 blind / both-blocked | 射线起点 `c−radial·r` 落在材料里 |
| 沉孔+通孔读成两个盲坑 | 没按同轴分组 |
| 体素体积偏差大 | 网格不水密 / STL 导出带平移 / 分辨率不足 |
| 三角形"一半重复" | 解析把面号当顶点（TRI 行首列是 fid） |

### 23.11 案例：转向内圈轴承座

68 面（`GetFaceCount` 与枚举双证）；228,410 三角面；0.25 mm 体素 7.1M；体积 12,594.8 vs 12,535.5（+0.47%）。正确结构：**Ø56 内孔（三处 120° 平面）+ 12 个径向沉头通孔（Ø5.5 沉孔 + Ø3 通孔，上下两层各 6，60° 均布）+ 3 个 Ø2.5 + Ø63.5 法兰带**，高 27.4。两个教训都写进了上面：射线起点错 → 12 通孔误判成盲坑；Euler 不可用 → 改用通孔计数。
