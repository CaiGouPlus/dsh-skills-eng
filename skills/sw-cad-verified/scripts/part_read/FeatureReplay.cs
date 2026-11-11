// FeatureReplay.cs - measure what each feature actually contributes.
//
// Two independent measurements, both by suppression (no geometry edits):
//   A) PROGRESSIVE  : suppress everything, then unsuppress one feature at a time
//                     in tree order -> volume/area/bbox after each step.
//                     This is the build history, measured instead of assumed.
//   B) SOLO REMOVAL : from the finished part, suppress exactly one feature ->
//                     delta tells how much material it added (or removed).
//
// Why this exists: reading extrude depth + direction from the API is unreliable
// (Dir/Flip/Sd semantics, offset-plane normal, derived sketches). Measuring is not.

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

public static class FeatureReplay
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    static ISldWorks sw;
    static IModelDoc2 model;

    static void L(string s) { SB.AppendLine(s); try { File.AppendAllText(LogPath, s + "\r\n"); } catch { } }
    static string F(double v, int d) { return v.ToString("F" + d, C); }

    // features we care about, in tree order
    static List<IFeature> Build = new List<IFeature>();
    static List<string> BuildName = new List<string>();
    static List<string> BuildType = new List<string>();

    public static string Run(string partPath, string outDir)
    {
        LogPath = Path.Combine(outDir, "slide.replay.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        bool created = false;
        try
        {
            sw = SwKit.Acquire(out created, true);
            int err = 0, warn = 0;
            model = sw.OpenDoc6(partPath, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (model == null) { L("!! open failed"); return SB.ToString(); }

            // ---- collect solid features in build order ----
            IFeature f = model.FirstFeature() as IFeature;
            while (f != null)
            {
                string tn = ""; try { tn = f.GetTypeName2(); } catch { }
                if (IsSolidFeature(tn))
                {
                    Build.Add(f);
                    BuildName.Add(f.Name);
                    BuildType.Add(tn);
                }
                f = f.GetNextFeature() as IFeature;
            }
            L("solid features in tree order (" + Build.Count + "):");
            for (int i = 0; i < Build.Count; i++) L("  " + (i + 1) + ". [" + BuildType[i] + "] " + BuildName[i]);

            double v0, a0; double[] b0;
            Measure(out v0, out a0, out b0);
            L("");
            L("=== baseline (nothing touched) ===");
            L(Row(0, "as-is", v0, a0, b0));

            // ---- B) solo removal ----
            L("");
            L("=== SOLO REMOVAL (suppress one feature of the finished part) ===");
            for (int i = 0; i < Build.Count; i++)
            {
                SuppressAll(false);
                SetSupp(i, true);
                model.EditRebuild3();
                double v, a; double[] bb;
                Measure(out v, out a, out bb);
                L(Row(i + 1, "suppress " + BuildName[i], v, a, bb) + "   dV=" + F(v - v0, 3) + " mm3");
                SetSupp(i, false);
            }
            model.EditRebuild3();
            double vv, aa; double[] bbb; Measure(out vv, out aa, out bbb);
            L("restored V=" + F(vv, 3) + "  (baseline " + F(v0, 3) + ")");

            // ---- A) progressive ----
            L("");
            L("=== PROGRESSIVE REBUILD (unsuppress one at a time, in tree order) ===");
            SuppressAll(true);
            model.EditRebuild3();
            double vp, ap; double[] bp; Measure(out vp, out ap, out bp);
            L("  0. (all suppressed)  V=" + F(vp, 3));
            double prev = vp;
            for (int i = 0; i < Build.Count; i++)
            {
                SetSupp(i, false);
                model.EditRebuild3();
                double v, a; double[] bb;
                Measure(out v, out a, out bb);
                string st = "";
                try { st = (model as IPartDoc) == null ? "" : ""; } catch { }
                L(Row(i + 1, BuildName[i], v, a, bb) + "   dV=" + F(v - prev, 3) + " mm3");
                prev = v;
            }
        }
        catch (Exception ex) { L("!! " + ex.Message + "\n" + ex.StackTrace); }
        finally { try { if (model != null) sw.CloseDoc(model.GetTitle()); } catch { } SwKit.Release(sw, created); }
        return SB.ToString();
    }

    static bool IsSolidFeature(string tn)
    {
        if (string.IsNullOrEmpty(tn)) return false;
        string[] skip = { "RefPlane", "OriginProfileFeature", "ProfileFeature", "FavoriteFolder",
                          "HistoryFolder", "SelectionSetFolder", "SensorFolder", "DocsFolder",
                          "DetailCabinet", "InkMarkupFolder", "EnvFolder", "SolidBodyFolder",
                          "SurfaceBodyFolder", "CommentsFolder", "EqnFolder", "MaterialFolder",
                          "NotesAreaFtrFolder", "AnnotationViewFeat", "FtrFolder" };
        foreach (string s in skip) if (s == tn) return false;
        return true;
    }

    static void SuppressAll(bool suppress)
    {
        for (int i = 0; i < Build.Count; i++) SetSupp(i, suppress);
    }

    static void SetSupp(int i, bool suppress)
    {
        try
        {
            IFeature f = Build[i];
            if (f == null) return;
            // SuppressionType: 0 suppress, 1 unsuppress; ConfigOption: 2 all configs
            f.SetSuppression2(suppress ? 0 : 1, 2, null);
        }
        catch (Exception e) { L("  !! SetSuppression " + BuildName[i] + " : " + e.Message); }
    }

    static void Measure(out double vol, out double area, out double[] bbox)
    {
        vol = 0; area = 0; bbox = new double[6];
        try
        {
            IPartDoc pd = model as IPartDoc;
            object[] bodies = pd.GetBodies2(0, false) as object[];
            double v = 0, a = 0;
            double[] bb = null;
            if (bodies != null)
            {
                foreach (object ob in bodies)
                {
                    IBody2 b = ob as IBody2;
                    if (b == null) continue;
                    object mp = b.GetMassProperties(1.0);
                    double[] m = mp as double[];
                    if (m != null && m.Length >= 5) { v += m[3]; a += m[4]; }
                    double[] bx = b.GetBodyBox() as double[];
                    if (bx != null && bx.Length >= 6)
                    {
                        if (bb == null) { bb = new double[6]; Array.Copy(bx, bb, 6); }
                        else
                        {
                            if (bx[0] < bb[0]) bb[0] = bx[0];
                            if (bx[1] < bb[1]) bb[1] = bx[1];
                            if (bx[2] < bb[2]) bb[2] = bx[2];
                            if (bx[3] > bb[3]) bb[3] = bx[3];
                            if (bx[4] > bb[4]) bb[4] = bx[4];
                            if (bx[5] > bb[5]) bb[5] = bx[5];
                        }
                    }
                }
            }
            vol = v * 1e9;      // m3 -> mm3
            area = a * 1e6;     // m2 -> mm2
            if (bb != null) bbox = bb;
        }
        catch (Exception e) { L("  !! Measure: " + e.Message); }
    }

    static string Row(int i, string name, double vol, double area, double[] bb)
    {
        return "  " + i.ToString().PadLeft(2) + ". " + name.PadRight(14)
             + "  V=" + F(vol, 3).PadLeft(11)
             + "  A=" + F(area, 2).PadLeft(10)
             + "  box x[" + F(bb[0] * 1000, 3) + "," + F(bb[3] * 1000, 3) + "]"
             + " y[" + F(bb[1] * 1000, 3) + "," + F(bb[4] * 1000, 3) + "]"
             + " z[" + F(bb[2] * 1000, 3) + "," + F(bb[5] * 1000, 3) + "]";
    }

    // unused but kept for parity with other probes
    static string Pt(ISketchPoint p) { return p == null ? "?" : "(" + F(p.X * 1000, 3) + "," + F(p.Y * 1000, 3) + ")"; }
}
