# 踩过的坑（附错误原文）

按"会误导判断"的程度排序。**第 0 条最重要**。

## 0. 别把"调用被拒"当成"功能不可用"

```
pattern x104:  err=True   0.00s        ← 0.00 秒返回，是参数/名字错
loft cut:      err=True   0.00s
```

`isError:true` **且耗时接近 0** = 它在参数校验阶段就拒了，根本没跑。
我因此一度断言"104 齿阵列卡死/建不出来"，实际清场后 **17.8 秒**建完。

**规则：失败先看 `failed.index` + `failed.error` 原文，再下结论。**

## 1. `--headless` 不加，会覆盖用户正在看的设计

`solidcraft-cli mcp` 在 GUI 开着时会**自动桥接**到那个窗口。
我在写"headless 体积自检"时没加 `--headless`，`new_design` 直接把用户窗口里的零件冲掉了。

```
solidcraft-cli mcp --headless --allow-path <dir>     # 验算用这个
solidcraft-cli mcp --connect 127.0.0.1:7878          # 只有要看/驱动 GUI 时才用
```

## 2. 参数名和特征名不能猜

| 错误原文 | 真实原因 |
|---|---|
| `invalid parameters for \`sketch.rectangle.two_point\`: \`p0\` must be a point [x, y]` | 参数是 `p0/p1`，不是 `corner1/corner2` |
| `invalid parameters for \`solid.combine\`: \`target\` must be a body name` | 是 `target` + `tools`，不是 `bodies` |
| 阵列报找不到特征（0.00s 返回） | `features:["Extrude"]` 写错，真名是 `Extrude1` / `Loft1` |

先查：`solidcraft-cli commands > cmds.json`，读每条的 `params` 字段；
特征名从 `inspect_design` 的 `timeline[].name` 取。

## 3. 平移轮廓必须连圆心一起移

四段弧用 `center + start + end` 定义。整体平移时**只移端点、不移圆心**：

```
loft shift=-1.000   err=True   "GapB has no closed profiles"
```

错误信息本身很好（直接说没闭合），但现象会让人以为"放样坏了"。正确做法：圆心和端点一起加 `dy`。

## 4. 残留进程会把正常调用拖成"卡死"

被中断的 `solidcraft-cli` 会继续在后台算（每个烧 400 s CPU）。三个残留 + 一个新会话 =
新会话看起来"永远不返回"。

```powershell
Get-Process solidcraft-cli -ErrorAction SilentlyContinue | Stop-Process -Force
```

Python 客户端结尾一律 `os._exit(0)`（`probe.py` 已内置）；否则解释器退出时会挂住。

## 5. 斜齿：两截面放样是错的路

- 单齿：放样切除比直齿多 **2.7%**（放样不保截面面积；真正的螺旋扫掠按 Cavalieri 应不变）
- ×104：**超 3 分钟不返回**（直齿 17.8 s）

现在没验证 `solid.sweep` + 螺旋导轨能否替代，**别在生产任务里试**。

## 6. 别用 `body_topology` 全量表

25–45 KB，而且会把圆柱拆成两个半面。要数孔、核轴径用
`body_topology {"body":"X","faces":"cylinders"}`（几行），
要看位置用 `measure` 的 COM / bbox。

## 7. `list_commands {}` 不加过滤是 100 KB+

查命令用 `list_commands {"filter":"hole"}`，或直接读 `commands` 转出的 JSON 本地 grep。
