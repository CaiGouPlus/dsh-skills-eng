#!/usr/bin/env python3
"""SolidCraft MCP probe — 通用探针骨架。

用法:
  python probe.py --exe <solidcraft-cli.exe> --work <dir> --commands build.json
  python probe.py --exe <...> --work <...> --mode connect --port 7878 --commands build.json
  python probe.py --exe <...> --work <...> --selftest        # 四条回归用例

设计要点（都是踩过的坑）:
  * 默认 --mode headless（加 --headless），不会覆盖用户正在看的 GUI
  * 每一步都打印 isError / **错误原文** / 耗时 —— 0.00s 返回的 isError 是"参数被拒"，不是功能缺失
  * 结束用 os._exit(0)，避免解释器退出时挂住；残留进程会烧 CPU 把后续调用拖成"卡死"
"""
import argparse
import json
import math
import os
import subprocess
import sys
import threading
import time

try:
    sys.stdout.reconfigure(line_buffering=True)   # os._exit 不会 flush 缓冲区
except Exception:
    pass


class MCP:
    def __init__(self, argv, work):
        self.p = subprocess.Popen(argv + ["--allow-path", work],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, text=True,
                                  encoding="utf-8", bufsize=1)
        self.n = 0
        self.work = work

    def _req(self, method, params=None):
        self.n += 1
        rid = self.n
        self.p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": rid, "method": method,
                                       "params": params or {}}) + "\n")
        self.p.stdin.flush()
        while True:
            line = self.p.stdout.readline()
            if not line:
                raise SystemExit("server closed (exit %s)" % self.p.poll())
            m = json.loads(line)
            if m.get("id") == rid:
                if "error" in m:
                    return {"__rpc_error__": m["error"]}
                return m["result"]

    def notify(self, method, params=None):
        self.p.stdin.write(json.dumps({"jsonrpc": "2.0", "method": method,
                                       "params": params or {}}) + "\n")
        self.p.stdin.flush()

    def init(self):
        r = self._req("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                     "clientInfo": {"name": "probe", "version": "0"}})
        self.notify("notifications/initialized")
        return r

    def call(self, tool, args):
        t0 = time.time()
        r = self._req("tools/call", {"name": tool, "arguments": args})
        dt = time.time() - t0
        if not isinstance(r, dict) or r.get("__rpc_error__"):
            return True, r, dt            # JSON-RPC 层错误（未知工具/参数不符 schema）
        txt = [c.get("text", "") for c in r.get("content", []) if c.get("type") == "text"]
        try:
            v = json.loads(txt[0]) if txt else None
        except Exception:
            v = txt[0] if txt else None
        return r.get("isError", False), v, dt


def show(tag, err, v, dt):
    s = v if isinstance(v, str) else json.dumps(v, ensure_ascii=False)
    s = (s or "null")
    print("%-22s err=%-5s %6.2fs  %s" % (tag, err, dt, s[:600]), flush=True)


def run_commands(m, path):
    data = json.load(open(path, encoding="utf-8"))
    steps = data["commands"] if isinstance(data, dict) else data
    err, v, dt = m.call("batch", {"commands": steps, "verbose": False})
    if err:
        print("BATCH FAILED  %s" % json.dumps(v, ensure_ascii=False)[:800])
        # batch 回执里已经带 failed.index / failed.command / failed.error
        return False
    err, v, dt = m.call("measure", {"summary": True})
    print("measure  %s" % json.dumps(v, ensure_ascii=False)[:500])
    return True


def selftest(m):
    ok = True

    def fresh(commands, tag):
        m.call("new_design", {})
        e, v, dt = m.call("batch", {"commands": commands, "verbose": False})
        if e:
            print("!! %s batch failed: %s" % (tag, json.dumps(v, ensure_ascii=False)[:400]))
            return None
        e, v, dt = m.call("measure", {"summary": True})
        return v["total"]["volume_mm3"] if isinstance(v, dict) and v.get("total") else None

    # 1) 长方体 100^3 —— 必须是精确 1e6
    V = fresh([{"command": "sketch.create", "params": {"plane": "XY", "name": "S"}},
               {"command": "sketch.rectangle.two_point", "params": {"p0": [0, 0], "p1": [100, 100]}},
               {"command": "sketch.finish", "params": {}},
               {"command": "solid.extrude", "params": {"distance": 100}}], "box")
    if V is None:
        ok = False
    else:
        good = abs(V - 1e6) < 1e-6
        ok &= good
        print("[1] box 100^3        V=%.6f   %s" % (V, "OK 精确" if good else "!! 不精确"))

    # 2) 圆柱 d100 h100 —— 落在 -1.1e-4 附近
    V = fresh([{"command": "sketch.create", "params": {"plane": "XY", "name": "S"}},
               {"command": "sketch.circle.center", "params": {"center": [0, 0], "diameter": 100}},
               {"command": "sketch.finish", "params": {}},
               {"command": "solid.extrude", "params": {"distance": 100}}], "cyl")
    exact = math.pi / 4 * 1e4 * 100
    if V is None:
        ok = False
    else:
        rel = V / exact - 1
        good = -3.0e-4 < rel < 0.0
        ok &= good
        print("[2] cyl d100x100     V=%.4f  rel=%+.3e   %s"
              % (V, rel, "OK 落在底噪内" if good else "!! 超出预期"))

    # 3) 轮廓面积：腰形槽（两弧两线）解析 = w*(L-w) + pi*(w/2)^2
    m.call("new_design", {})
    L, W = 150.0, 100.0
    m.call("batch", {"commands": [
        {"command": "sketch.create", "params": {"plane": "XY", "name": "S"}},
        {"command": "sketch.slot.overall", "params": {"p0": [0, 0], "p1": [L, 0], "width": W}},
        {"command": "sketch.finish", "params": {}}], "verbose": False})
    e, d, dt = m.call("inspect_design", {"measure": False, "joints": False})
    A = None
    for sk in (d or {}).get("sketches", []):
        for pr in (sk.get("profiles") or []):
            if isinstance(pr, dict) and "area_mm2" in pr:
                A = pr["area_mm2"]
    A_exact = W * (L - W) + math.pi * (W / 2) ** 2
    if A is None:
        ok = False
        print("[3] slot area        !! 没读到 profile.area_mm2")
    else:
        good = abs(A - A_exact) / A_exact < 1e-4
        ok &= good
        print("[3] slot area        A=%.4f  exact=%.4f  rel=%.2e   %s"
              % (A, A_exact, A / A_exact - 1, "OK" if good else "!! 偏差过大"))

    # 4) 反向用例：参数名写错，必须得到可操作的错误
    e, v, dt = m.call("batch", {"commands": [
        {"command": "sketch.create", "params": {"plane": "XY", "name": "S"}},
        {"command": "sketch.rectangle.two_point",
         "params": {"corner1": [0, 0], "corner2": [10, 10]}}], "verbose": False})
    txt = json.dumps(v, ensure_ascii=False)
    good = e and ("p0" in txt) and dt < 5
    ok &= good
    print("[4] 反向(错参数名)   err=%s %.2fs  %s" % (e, dt, ("OK 报了 " + txt[:120]) if good
                                                    else "!! 没给出可操作错误"))

    print("\nSELFTEST %s" % ("PASS" if ok else "FAIL"))
    return ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--mode", choices=["headless", "connect"], default="headless")
    ap.add_argument("--port", default="7878")
    ap.add_argument("--commands")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()

    if not os.path.isdir(a.work):
        raise SystemExit("--work 目录不存在: %s" % a.work)

    argv = [a.exe, "mcp"]                       # 子命令必须在最前
    if a.mode == "connect":
        argv += ["--connect", "127.0.0.1:" + a.port]
    else:
        argv += ["--headless"]

    m = MCP(argv, a.work)
    try:
        info = m.init()
        print("server: %s" % json.dumps((info or {}).get("serverInfo", {}), ensure_ascii=False),
              flush=True)
        if a.selftest:
            selftest(m)
        elif a.commands:
            run_commands(m, a.commands)
        else:
            e, v, dt = m.call("inspect_design", {"measure": False, "sketches": False})
            show("inspect_design", e, v, dt)
    except SystemExit as exc:
        print("ABORT: %s" % (exc.code,), flush=True)
    except Exception as exc:                     # 不留哑失败
        import traceback
        traceback.print_exc()
        print("ABORT: %r" % (exc,), flush=True)
    finally:
        print("done", flush=True)
        os._exit(0)          # 关键：别让解释器退出时挂住


if __name__ == "__main__":
    main()
