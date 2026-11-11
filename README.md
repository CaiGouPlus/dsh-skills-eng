# dsh-skills-eng

DSH（DeepSeek Harness）**工程 / CAE 类技能包**：机械零件建模流水线、机械设计优化、SolidCraft 数值验算、SolidWorks 代码驱动建模。

## 内容

| 技能 | 用途 | 依赖 |
| --- | --- | --- |
| `mech-part-pipeline` | 零件端到端流水线：需求 → 读真值 → 计划 → 建模 → 验收 → 合规分级 | SolidWorks / SolidCraft |
| `mech-design-optimize` | 机械设计优化流程：原理方案 → 零件材料 → 结构装配 → 公差链 → 工艺 → 制图 | 同上 |
| `solidcraft-verify` | 用 SolidCraft 做「第二内核」数值验算（面积/体积/质心对拍） | SolidCraft CLI |
| `sw-cad-verified` | 代码驱动 SolidWorks 2026 建模（C# PIA / Python comtypes）的已验证做法与硬边界 | SolidWorks 2026、.NET PIA |

## 依赖脚本（已随包）

`skills/sw-cad-verified/scripts/` 里除技能自带的 `SketchKit.cs` / `Starter.cs` / `run_starter.ps1`，还收了原本散落在 `D:\DSH\DSH\cae\` 的：

- `SwKit.cs`（COM 连接与实例所有权）
- `sw-run.ps1`（编译并跑 C# 驱动的入口）
- `part_read/`（读零件几何的探针脚本：Reader.cs / PartAnalyze.cs / minwall.py / 切片与拓扑工具等）

> 二进制与中间产物（`*.SLDPRT` / `*.bmp` / `*.npy` / 大体积 tess/topo 文本 / SolidWorks API 帮助 cab）**未入库**，按需本机生成。
> `SKILL.md` 里的绝对路径（`D:\DSH\DSH\cae\...`）是本机运行口径；恢复时把 `scripts/part_read/` 放回该位置即可。

## 许可与第三方

自研技能；无第三方代码。文中引用的 SolidWorks / SolidCraft 文档与 API 说明归各自权利人，见 `THIRD_PARTY_NOTICES.md`。

## 可见性

当前为 **Private**。判定依据见知识库 `CaiGouPlus/dsh-tiddlywiki` 的笔记「仓库拆分与隐私分级规范」：
涉及本机指纹、账号/订阅身份、IM 桥接、个人创作、NSFW 的内容一律私有。

## 恢复方式

把对应目录拷回本机后重启 DSH：
- 插件 → `D:\DSH\<插件名>`（`crosspet` 例外：`%LOCALAPPDATA%\CrossPet\dsh-plugin-crosspet`）
- 技能 → `%USERPROFILE%\.dsh\skills\<技能名>`
- 若插件在 profile 里以 `link:` 挂载，恢复后确认 `%USERPROFILE%\.dsh\profiles\desktop` 的链接仍指向上述路径。
