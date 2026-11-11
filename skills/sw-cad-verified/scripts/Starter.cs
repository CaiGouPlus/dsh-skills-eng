using System;
using System.IO;
using SolidWorks.Interop.sldworks;

/// <summary>
/// 通用零件模板：连接 → 新建 → 完全定义草图 → 拉伸 → B-Rep 回读 → 存盘。
/// 这个文件是**骨架**，不是成品。换零件时只改标注 TODO 的三处，其余保持不动。
///
/// 编译（两个文件必须一起编译，分开编译会互相看不见）：
///   $pia = 'D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
///   Add-Type -Path $pia
///   Add-Type -Path @('SketchKit.cs','Starter.cs') -ReferencedAssemblies $pia
///   [Starter]::Run('', 'D:\out\demo.SLDPRT')
/// </summary>
public static class Starter
{
    const string TPL_FALLBACK = @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_part.prtdot";

    // ================= TODO 1：改你的几何 =================
    const double R_OUT = 0.050;   // 毛坯半径 m
    const double WIDTH = 0.020;   // 厚度 m
    // ======================================================

    public static string Run(string templatePath, string savePath)
    {
        try
        {
            // ---- 连接：C# PIA 强类型是唯一稳的路（PowerShell New-Object -ComObject 会 0x8002802B）----
            SketchKit.Sw = new SldWorks();
            SketchKit.Sw.Visible = true;
            SketchKit.LogPath = Path.ChangeExtension(savePath, ".log");
            SketchKit.L("connect OK  " + SketchKit.Sw.RevisionNumber());

            // ---- 关掉尺寸输入框，否则 AddDimension2 会弹框等回车，整个 SW 挂死 ----
            SketchKit.QuietDimEntry();

            // ---- 新建：模板要么显式给，要么按常见路径探测 ----
            string tpl = !string.IsNullOrEmpty(templatePath) ? templatePath : FindTemplate();
            if (!File.Exists(tpl)) { SketchKit.L("!! 模板不存在: " + tpl); return SketchKit.Text(); }
            SketchKit.L("template = " + tpl);
            SketchKit.Model = SketchKit.Sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            if (SketchKit.Model == null) { SketchKit.L("!! NewDocument failed"); return SketchKit.Text(); }

            ISketchManager mgr = SketchKit.Model.SketchManager;
            IModelDoc2 model = SketchKit.Model;

            // ---- 草图 1：毛坯。以原点为圆心建圆 → SW 自动带「与原点重合」，再加一个直径尺寸即完全定义 ----
            SketchKit.SelectPlane(null);
            ISketch sk = SketchKit.Begin();

            object circle = mgr.CreateCircleByRadius(0, 0, 0, R_OUT);
            SketchKit.L("circle = " + (circle != null));
            if (circle == null) { SketchKit.L("!! CreateCircleByRadius null"); return SketchKit.Text(); }

            SketchKit.DimVal(circle, R_OUT * 0.7, R_OUT * 0.7, 2 * R_OUT, false);

            int st = sk.GetConstrainedStatus();
            SketchKit.L("   sketch1 status = " + st + "  " + SketchKit.StatusName(st));
            if (st != 3) { SketchKit.L("!! 草图未完全定义，停手（不靠坐标硬编码，先补几何关系）"); return SketchKit.Text(); }

            SketchKit.End();

            // ---- 特征：拉伸。第 3 参 Dir 必须 true，给错是静默 null ----
            Feature skf = model.FeatureByPositionReverse(0) as Feature;
            skf.Select2(false, 0);
            Feature body = model.FeatureManager.FeatureExtrusion2(
                true, false, false, 0, 0, WIDTH, WIDTH,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as Feature;
            SketchKit.L("extrude = " + (body != null ? "OK" : "FAIL"));
            if (body == null) { SketchKit.L("!! 拉伸失败，先查草图是否 contours==1，不要改参数重试"); return SketchKit.Text(); }

            // ---- 验收：B-Rep 回读 + 理论值对比 ----
            double v = Volume();
            double vTheory = Math.PI * R_OUT * R_OUT * WIDTH * 1e9;
            SketchKit.L(string.Format("volume = {0:F2} mm3   theory = {1:F2} mm3   err = {2:F3}%",
                v, vTheory, Math.Abs(v - vTheory) / vTheory * 100));

            double[] b = BBox();
            if (b != null) SketchKit.L(string.Format("bbox = {0:F2} x {1:F2} x {2:F2} mm", b[0], b[1], b[2]));

            // ---- 出图：屏幕截图在多屏下不可靠，用 SaveBMP ----
            model.ShowNamedView2("*Isometric", 7);
            model.ViewZoomtofit2();
            model.ViewDisplayShaded();
            try { model.SaveBMP(Path.ChangeExtension(savePath, ".bmp"), 1600, 1000); } catch { }

            if (!string.IsNullOrEmpty(savePath))
            {
                model.EditRebuild3();
                model.SaveAs(savePath);
                SketchKit.L("saved = " + File.Exists(savePath) + "  " + (File.Exists(savePath) ? new FileInfo(savePath).Length : 0));
            }
        }
        catch (Exception e) { SketchKit.L("!! " + e.Message); }
        finally { SketchKit.RestoreDimEntry(); }
        return SketchKit.Text();
    }

    /// 体积 mm³。GetMassProperties2 的返回数组里 [3] 是体积，单位 m³。
    static double Volume()
    {
        try
        {
            int st;
            double[] mp = SketchKit.Model.Extension.GetMassProperties2(1, out st, false) as double[];
            if (mp != null) return mp[3] * 1e9;
        }
        catch { }
        return -1;
    }

    /// 外形尺寸 mm³：GetPartBox 返回 [xmin,ymin,zmin,xmax,ymax,zmax]，单位 m。
    static double[] BBox()
    {
        try
        {
            IPartDoc pd = SketchKit.Model as IPartDoc;
            double[] b = pd.GetPartBox(true) as double[];
            if (b != null) return new double[] { (b[3] - b[0]) * 1000, (b[4] - b[1]) * 1000, (b[5] - b[2]) * 1000 };
        }
        catch { }
        return null;
    }

    /// 模板探测：先看本机 SolidWorks 的用户默认模板，再按 ProgramData 扫。
    static string FindTemplate()
    {
        if (File.Exists(TPL_FALLBACK)) return TPL_FALLBACK;
        try
        {
            string baseDir = @"C:\ProgramData\SOLIDWORKS";
            // 目录名形如 "SOLIDWORKS 2026"，取版本号最大的那个
            string best = null; int bestVer = -1;
            foreach (string d in Directory.GetDirectories(baseDir))
            {
                string nm = Path.GetFileName(d);
                if (!nm.StartsWith("SOLIDWORKS")) continue;
                int ver = 0; int.TryParse(nm.Replace("SOLIDWORKS", "").Trim(), out ver);
                string p = Path.Combine(d, @"templates\gb_part.prtdot");
                if (ver > bestVer && File.Exists(p)) { bestVer = ver; best = p; }
            }
            if (best != null) return best;
        }
        catch { }
        return TPL_FALLBACK;
    }
}
