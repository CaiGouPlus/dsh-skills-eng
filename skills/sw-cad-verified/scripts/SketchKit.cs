using System;
using System.Text;
using SolidWorks.Interop.sldworks;

/// <summary>
/// 尺寸/关系驱动的草图工具。
/// 原则：几何必须由「几何关系 + 尺寸」约束到完全定义，不靠坐标。
/// 选择一律走实体自带的 GetID() 名字，绕开本地化中文名和 SelectByRay 的不可靠。
/// </summary>
public static class SketchKit
{
    public const string HORIZONTAL = "sgHORIZONTAL", VERTICAL = "sgVERTICAL",
                        COINCIDENT = "sgCOINCIDENT", TANGENT = "sgTANGENT",
                        PARALLEL = "sgPARALLEL", PERPENDICULAR = "sgPERPENDICULAR",
                        CONCENTRIC = "sgCONCENTRIC", EQUAL = "sgEQUAL", FIXED = "sgFIXED";

    public static ISldWorks Sw;
    public static IModelDoc2 Model;
    static StringBuilder log = new StringBuilder();

    /// <summary>日志落盘路径。默认放临时目录，可由调用方改成项目目录。</summary>
    public static string LogPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "sw_sketchkit.log");

    /// <summary>逐行落盘：SW 挂死时，最后一行就是现场。不要只在最后一次性打印。</summary>
    public static void L(string s)
    {
        log.AppendLine(s);
        try { System.IO.File.WriteAllText(LogPath, log.ToString()); } catch { }
    }
    public static string Text() { return log.ToString(); }

    // ---------- 尺寸输入框开关 ----------
    // swUserPreferenceToggle_e.swInputDimValOnCreate = 10
    // 打开时 AddDimension2 会弹「修改」输入框等回车，把 SW 挂住（自动化必关）
    public const int PREF_INPUT_DIM_VALUE = 10;
    public static bool _prefWasOn = false;
    public static bool _prefSaved = false;

    public static void QuietDimEntry()
    {
        try
        {
            _prefWasOn = Sw.GetUserPreferenceToggle(PREF_INPUT_DIM_VALUE);
            _prefSaved = true;
            Sw.SetUserPreferenceToggle(PREF_INPUT_DIM_VALUE, false);
            L("   dim-entry popup: " + (_prefWasOn ? "WAS ON -> off" : "already off"));
        }
        catch (Exception e) { L("   QuietDimEntry err: " + e.Message); }
    }

    public static void RestoreDimEntry()
    {
        if (!_prefSaved) return;
        try { Sw.SetUserPreferenceToggle(PREF_INPUT_DIM_VALUE, _prefWasOn); L("   dim-entry popup restored to " + _prefWasOn); }
        catch { }
    }

    public static Feature FindOrigin()
    {
        Feature f = (Feature)Model.FirstFeature();
        while (f != null)
        {
            if (f.GetTypeName2() == "OriginProfileFeature") return f;
            f = (Feature)f.GetNextFeature();
        }
        return null;
    }

    // ---------- 草图生命周期 ----------
    public static void SelectPlane(string planeName)
    {
        Model.ClearSelection2(true);
        if (!string.IsNullOrEmpty(planeName) &&
            Model.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0)) return;
        Feature f = (Feature)Model.FirstFeature();
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane") { f.Select2(false, 0); return; }
            f = (Feature)f.GetNextFeature();
        }
    }

    public static ISketch Begin()
    {
        Model.SketchManager.InsertSketch(true);
        return Model.SketchManager.ActiveSketch;
    }

    public static Feature End()
    {
        Model.SketchManager.InsertSketch(true);
        return Model.FeatureByPositionReverse(0) as Feature;
    }

    // ---------- 实体 ID ----------
    public static string IdOf(object ent)
    {
        try
        {
            object o = null;
            SketchSegment sg = ent as SketchSegment;
            if (sg != null) o = sg.GetID();
            else { ISketchPoint p = ent as ISketchPoint; if (p != null) o = p.GetID(); }
            object[] a = o as object[];
            if (a != null && a.Length > 1) return a[1] as string;
        }
        catch { }
        return null;
    }

    public static bool SelectEnt(object ent, bool append)
    {
        try
        {
            SketchSegment sg = ent as SketchSegment;
            if (sg != null) { sg.Select4(append, null); return true; }
            ISketchPoint p = ent as ISketchPoint;
            if (p != null) { p.Select4(append, null); return true; }
        }
        catch (Exception e) { L("   SelectEnt err: " + e.Message); return false; }
        L("   SelectEnt: unsupported entity type");
        return false;
    }

    // ---------- 取端点 ----------
    public static ISketchPoint SP(SketchSegment sg, bool start)
    {
        if (sg == null) return null;
        try
        {
            int t = sg.GetType();
            if (t == 0) { ISketchLine l = sg as ISketchLine; return (start ? l.GetStartPoint2() : l.GetEndPoint2()) as ISketchPoint; }
            if (t == 1) { ISketchArc a = sg as ISketchArc; return (start ? a.GetStartPoint2() : a.GetEndPoint2()) as ISketchPoint; }
        }
        catch { }
        return null;
    }

    /// 重建/改动后必须重新取线段引用，旧的 COM 对象会静默失效
    public static object[] Segments(ISketch sk)
    {
        try { return sk.GetSketchSegments() as object[]; } catch { return null; }
    }

    // ---------- 几何关系 ----------
    public static bool Relate(object[] ents, string gcs)
    {
        Model.ClearSelection2(true);
        int ok = 0;
        for (int i = 0; i < ents.Length; i++) if (SelectEnt(ents[i], i > 0)) ok++;
        if (ok == 0) { L("   relate " + gcs + ": nothing selected"); return false; }
        try
        {
            int before = RelCount(ents[0]);
            Model.SketchAddConstraints(gcs);   // 返回 void，靠关系计数判断是否真加上
            Model.ClearSelection2(true);
            int after = RelCount(ents[0]);
            bool applied = after > before;
            if (!applied) L("   relate " + gcs + ": NO EFFECT (relations " + before + " -> " + after + ")");
            return applied;
        }
        catch (Exception e) { L("   relate " + gcs + " err: " + e.Message); Model.ClearSelection2(true); return false; }
    }

    // ---------- 尺寸 ----------
    public static IDisplayDimension DimOn(object ent, double tx, double ty)
    {
        Model.ClearSelection2(true);
        if (!SelectEnt(ent, false)) { L("   dim: select miss"); return null; }
        try
        {
            IDisplayDimension dd = Model.AddDimension2(tx, ty, 0) as IDisplayDimension;
            Model.ClearSelection2(true);
            return dd;
        }
        catch (Exception e) { L("   dim err: " + e.Message); Model.ClearSelection2(true); return null; }
    }

    /// 建尺寸并显式赋米制值，避免留下未确认的输入框
    public static IDisplayDimension DimVal(object ent, double tx, double ty, double valueMeters, bool angular)
    {
        IDisplayDimension dd = DimOn(ent, tx, ty);
        if (dd == null) return null;
        try
        {
            IDimension d = dd.GetDimension2(0) as IDimension;
            if (d != null)
            {
                d.SystemValue = valueMeters;   // 不在这里 Rebuild：重建会让旧 SketchSegment 引用失效
                L(string.Format("   dim set = {0:F4} (readback {1:F4})", valueMeters * 1000, d.SystemValue * 1000));
            }
        }
        catch (Exception e) { L("   dim set err: " + e.Message); }
        Model.ClearSelection2(true);
        return dd;
    }

    // swConstrainedStatus_e: 1=Unknown 2=Under 3=Fully 4=Over 5=NoSolution 6=Invalid 7=AutosolveOff
    public static string StatusName(int st)
    {
        switch (st)
        {
            case 2: return "under-defined";
            case 3: return "FULLY DEFINED";
            case 4: return "OVER-DEFINED";
            case 5: return "no-solution";
            case 7: return "autosolve-off";
            default: return "unknown(" + st + ")";
        }
    }

    static int RelCount(object ent)
    {
        try
        {
            SketchSegment sg = ent as SketchSegment;
            if (sg != null) return sg.GetRelationsCount();
            ISketchPoint p = ent as ISketchPoint;
            if (p != null) return p.GetRelationsCount();
        }
        catch { }
        return -1;
    }

    public static string Report(ISketch sk, string tag)
    {
        int st = -1;
        try { st = sk.GetConstrainedStatus(); } catch { }
        int segs = 0; try { object[] a = sk.GetSketchSegments() as object[]; segs = a == null ? 0 : a.Length; } catch { }
        string line = "   " + tag + ": segments=" + segs + "  " + StatusName(st);
        L(line);
        return line;
    }
}
