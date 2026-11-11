using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;

public static class Diag
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;

    public static string Run(string path)
    {
        LogPath = Path.ChangeExtension(path, ".diag.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        SldWorks sw = null;
        try
        {
            sw = new SldWorks();
            sw.Visible = true;
            int err = 0, warn = 0;
            IModelDoc2 model = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (model == null) { L("open failed"); return SB.ToString(); }
            Feature f = null;
            Feature it = model.FirstFeature() as Feature;
            while (it != null) { string tn = ""; try { tn = it.GetTypeName2(); } catch { } if (tn == "Extrusion") { f = it; break; } it = it.GetNextFeature() as Feature; }
            if (f == null) { L("no extrusion"); return SB.ToString(); }
            L("feature = " + f.Name + " sup=" + f.IsSuppressed() + " needsRebuild=" + model.Extension.NeedsRebuild2);
            L("model vol = " + ModelVol(model) + " bodyCount=" + BodyCount(model) + " bodyVol=" + BodyVol(model));

            bool ok = f.SetSuppression2(0, 0, null);
            L("sup set ok=" + ok + " sup=" + f.IsSuppressed() + " needsRebuild=" + model.Extension.NeedsRebuild2);
            L("after set, vol = " + ModelVol(model) + " bodyCount=" + BodyCount(model) + " bodyVol=" + BodyVol(model));

            bool rb = model.EditRebuild3();
            L("EditRebuild3=" + rb + " sup=" + f.IsSuppressed() + " vol=" + ModelVol(model) + " bodyCount=" + BodyCount(model) + " bodyVol=" + BodyVol(model));

            bool frb = model.ForceRebuild3(true);
            L("ForceRebuild3=" + frb + " sup=" + f.IsSuppressed() + " vol=" + ModelVol(model) + " bodyCount=" + BodyCount(model) + " bodyVol=" + BodyVol(model));

            bool ok2 = f.SetSuppression2(1, 0, null);
            bool rb2 = model.ForceRebuild3(true);
            L("unsup ok=" + ok2 + " rebuild=" + rb2 + " sup=" + f.IsSuppressed() + " vol=" + ModelVol(model) + " bodyCount=" + BodyCount(model) + " bodyVol=" + BodyVol(model));

            sw.CloseDoc(model.GetTitle());
        }
        catch (Exception e) { L("!! " + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static double ModelVol(IModelDoc2 m)
    {
        try { int st = 0; double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[]; if (mp != null) return Math.Round(mp[3] * 1e9, 2); } catch { }
        return -1;
    }

    static int BodyCount(IModelDoc2 m)
    {
        try { object[] b = (m as IPartDoc).GetBodies2(0, false) as object[]; return b == null ? -1 : b.Length; } catch { return -2; }
    }

    static double BodyVol(IModelDoc2 m)
    {
        try
        {
            object[] bs = (m as IPartDoc).GetBodies2(0, false) as object[];
            if (bs == null) return -1;
            double v = 0;
            foreach (object o in bs) { IBody2 b = o as IBody2; if (b != null) { double[] mp = b.GetMassProperties(1000) as double[]; if (mp != null) v += mp[3]; } }
            return Math.Round(v * 1e9, 2);
        }
        catch { return -2; }
    }

    static void L(string s) { SB.AppendLine(s); try { File.AppendAllText(LogPath, s + System.Environment.NewLine, new UTF8Encoding(true)); } catch { } }
}
