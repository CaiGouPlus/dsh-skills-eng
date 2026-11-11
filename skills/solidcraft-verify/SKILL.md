---
name: solidcraft-verify
description: 用 SolidCraft 当「第二内核」给 SolidWorks 工作做数值验算——面积/体积/质心对拍、参数敏感性试算、怀疑 SW 布尔残留污染包围盒时复核。当 SW 侧的数值对不上手算、改参数怕 SW 弹框挂死、或想在不碰 SW 的前提下先算一遍时使用。｜机械线六件套，分工见正文「相关技能」节
whenToUse: SW 建完件体积/面积/包围盒和手算对不上；怀疑 bbox 被布尔残留污染；想在不开 SW 的情况下试算参数改动；需要第二个独立内核交叉验证一个尺寸或一个齿廓/轮廓的面积；SW 正被占用或 COM 一时连不上而要一个数值答案
---

# SolidCraft 旁路验算器（SW 项目的第二内核）

**一句话**：不替代 SolidWorks，换一把尺子量。SW 负责出图与落地，SolidCraft 负责**在你不敢确定的地方给一个独立数字**。

## 第 0 步：先读它自带的技能（必做）

```
skill_reference {"skill":"solidcraft-modeling"}     # 内置文档，不需要文件权限
skill_reference {"skill":"solidcraft-checking"}     # 验收与回归
```

它的 `reference.md` 已经写好了下面这些，**本技能不重复，也不许自己重新摸索**：

| 已覆盖 | 内容 |
|---|---|
| 草图平面轴 | `XY`→(+X,+Y)、`XZ`→(+X,**−Z**)、`YZ`→(+Y,+Z) |
| id 规则 | 每个创建器返回 `created{points,curves,...}`，计数器可预测 |
| 错误原文 → 修法 | 整张 Errors 表：`unknown sketch entity 'p1'`、`the corner must join exactly two lines`… |
| `measure` 容差 | 解析与实测差 **0.001–0.01%** 视为一致；**又长又浅的圆弧约 0.1%**（R1000 跨 20°） |
| 静默失败 | 「A cut that misses reports success」——切除报成功也要量体积 |
| 布尔限制 | 碰到曲面会报 `planar faces only`，改走草图轮廓 + 拉伸切除 |

外面（不在 SolidCraft 的对话里）就读仓库 `skills/solidcraft-modeling/reference.md`。

## 判定表：什么时候值得动它

| 症状 | 用？ | 动作 |
|---|---|---|
| 轮廓/齿廓/型腔面积和手算对不上 | ✅ 首选 | 建草图 → `inspect_design` 读 `profile.area_mm2` |
| 怀疑 SW 的体积/bbox 被布尔残留污染 | ✅ | 独立内核重算体积 + COM，与 SW 对拍 |
| 改一个参数要先跑一轮 SW，怕挂死 | ✅ | headless 试算，改完回 SW 落地 |
| SW 正被占用 / COM 连不上，但要一个数 | ✅ | 它能给数值，给不了工程图 |
| 要出工程图 / GD&T / CAM | ❌ | 回 SW |
| 要建**斜齿** | ❌ 当前不通 | 见「不做」 |
| 相交圆角 / 抽壳 / 复杂放样 | ⚠️ 未验证 | 小样本先试水，别整件上 |

## 三种用法

### A. 轮廓面积即刻验算（性价比最高，先试这个）

建一个只有轮廓的草图，读它自报的面积：

```
sketch.create plane=XY → sketch.arc.center_point ×4（或 line/circle）
inspect_design {"measure":false,"joints":false}
   → sketches[].profiles[].area_mm2
```

实测：齿槽 4 弧轮廓，SolidCraft **41.4364** vs SW 沿弧采样 **41.4369** → 差 **1.2×10⁻⁵**。
开 SW 走一轮 COM 要几分钟还可能弹框；这里 1 秒。**调齿廓/型腔参数时先用它筛，最后再回 SW 确认。**

### B. 体积 / 质心对拍（1e-3 级）

- SW 侧：`IPartDoc.GetMassProperties2(1, out st, false)[3]`（体积）、`GetPartBox(true)`（外形）
- SolidCraft 侧：`measure {"summary":true}`
- **两边近似手段必须一致**，否则差的是构造不是内核：
  - 都用两截面放样 → 可比
  - 一边螺旋扫掠、一边放样 → 差 2.7%，那是构造差异，不是 bug
  - SW 的斜齿用「终止草图平移 12.915」近似旋转，SolidCraft 复刻时也要平移同样的量

容差按下面的表定，**不要拿 1e-5 当判据**。

### C. 参数敏感性试算

```
set_parameter {"name":"L","value":"120 mm"}      # 真改
check_parameters {"set":{"L":"140 mm"}}          # 试完自动还原，给体积/圆柱/报错
```
拿来回答「把齿宽从 60 改到 70，体积会变多少、孔位会不会跑」这类问题，不必开 SW。

## 数值容差（这张表决定你的判据）

| 对象 | 误差 | 用法 |
|---|---|---|
| 多面体（长方体、棱柱） | **0，精确** | 可当精确基准 |
| 圆柱 / 回转体 | −6×10⁻⁵ … −1.1×10⁻⁴ | 容差 ≥ 1e-3 |
| 圆弧截面棱柱（含浅弧） | **+8×10⁻⁴ 偏高**（官方：浅弧可到 0.1%） | 容差 ≥ 1e-2，或按系统偏差校正 |
| 草图轮廓面积 | 1.2×10⁻⁵ | **截面级强判据，优先用它** |

> **「对到 1e-3」可行；「对到 1e-5」是工具底噪，不是模型错。**

## 四条硬规则（其余见官方技能）

1. **`solidcraft-cli mcp` 必加 `--headless`**。不带时只要 GUI 开着就自动桥接过去，`new_design` / `open` 会**覆盖用户正在看的设计**。要驱动 GUI 才用 `--connect 127.0.0.1:<port>`，且先问用户。
2. **失败先打错误原文再判断**。`isError:true` 且 **0.00 s 返回** = 参数被拒（多半是名字写错），不是功能缺失。实例：`must be a point [x, y]` 说明 `sketch.rectangle.two_point` 要 `p0/p1`，不是 `corner1/corner2`。
3. **参数名、特征名先查再写**。`solidcraft-cli commands > cmds.json` 看 `params`；特征名从 `inspect_design` 的 timeline 取（`Extrude1` / `Loft1` / `CircularPattern1`）。已知易错：`solid.combine` 用 `target` + `tools`，不是 `bodies`；**平移轮廓要连圆心一起移**（只移端点 → `GapB has no closed profiles`）。
4. **客户端跑完 `os._exit(0)`**。残留 `solidcraft-cli` 各烧 400 s CPU，会把后续正常调用拖成"卡死"。

## 不做（写死，别试探）

- **斜齿**：两截面放样做的齿槽，单齿切除比直齿多 **2.7%**（放样不保截面积，而螺旋扫掠按 Cavalieri 应不变）；**×104 阵列超 3 分钟不返回**。直齿（一次拉伸切除 + 阵列 104）只要 **17.8 s**。
- 工程图 / GD&T / CAM：没有。
- 替代 SW 主线：不要。它只做数值旁路。

## 本机部署

| 项 | 值 |
|---|---|
| CLI | `%DSH_ROOT%\downloads\solidcraft-bin\app\solidcraft-0.30.1-windows-x64-portable\solidcraft-cli.exe` |
| 无头 | `solidcraft-cli.exe mcp --headless --allow-path <目录>` |
| 驱动 GUI | 先 `solidcraft.exe --control 7878`，再 `mcp --connect 127.0.0.1:7878` |
| 文件沙箱 | 只能读写 `--allow-path` 给的目录（home/temp 内非隐藏目录之外都要显式给） |

## 脚本

- `scripts/probe.py` —— MCP 探针骨架：跑一段 JSON 命令流，逐步打印 `isError`、**错误原文**、耗时，结尾 `os._exit(0)`。

```bash
python scripts/probe.py --exe <solidcraft-cli.exe> --work <目录> --commands build.json
python scripts/probe.py --exe <...> --work <...> --mode connect --port 7878 --commands build.json
python scripts/probe.py --exe <...> --work <...> --selftest   # 三条回归用例
```

---

## 相关技能（互相引用）

- **机械这条线的另外四个**：`engineering-problem-solving`（解题总控）/ `problem-image-reading`（题图转写）/
  `engineering-mechanics`（符号约定与必出图）/ `engineering-visualization`（出图规则）。
  本技能只负责"再算一遍，给一个独立数字"；要不要算、算完怎么讲、图怎么画，交回它们。
- **`sw-cad-verified`** —— SolidWorks 建模/装配的已验证动作集。本技能是它的"数值对拍"补充：SW 回的 bbox/体积可疑时，用 SolidCraft 算一遍再判断。
  - 特别提醒（2026-11 踩过）：SW 里 `GetPartBox` 回读的跨度与设计值差 1000 倍 = **单位坑**（草图坐标是米，程序里按 mm 传了），先去查 `sw-cad-verified` 第 15 节，不要当成实体异常。
- **`%DSH_WORKSPACE%\sw-skill\solidworks-automation-skill-main`** —— 更完整的 SW API 手册（references/assembly.md、part-modeling.md）。
- **真实参考装配体**：`D:\南工小樱\舵\轮组V1.5.SLDASM`（板 6/10 mm、轴承 61804-2Z、M3/M4 紧固件）。
- **TiddlyWiki**：`SolidWorks 自动化·配合与单位踩坑`。
