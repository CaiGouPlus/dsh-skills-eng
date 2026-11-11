using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

/// <summary>
/// Per-feature volume deltas via SetSuppression (old API - works on this file).
/// </summary>
public static class SuppressTest
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;

    public static string Run(string path)
    {
        LogPath = Path.ChangeExtension(path, ".suppress.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        SldWorks sw = null;
        try
        {
            sw = new SldWorks();
            sw.Visible = true;
            int err = 0, warn = 0;
            IModelDoc2 model = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (model == null) { L("open failed"); return SB.ToString(); }

            List<Feature> solid = new List<Feature>();
            Feature f = model.FirstFeature() as Feature;
            while (f != null)
            {
                string tn = ""; try { tn = f.GetTypeName2(); } catch { }
                if (tn == "Extrusion" || tn == "ICE" || tn == "Chamfer" || tn == "Fillet") solid.Add(f);
                f = f.GetNextFeature() as Feature;
            }
            L("solid features = " + solid.Count);
            foreach (Feature ff in solid) { string t = ""; try { t = ff.GetTypeName2(); } catch { } L("  " + ff.Name + " [" + t + "]"); }
            L("volume0 = " + Vol(model));

            for (int i = solid.Count - 1; i >= 0; i--)
            {
                bool ok = false;
                try { ok = solid[i].SetSuppression(0); } catch (Exception e) { L("  err " + e.Message); }
                try { model.EditRebuild3(); } catch { }
                L("suppress " + solid[i].Name + " ok=" + ok + " sup=" + solid[i].IsSuppressed() + " vol=" + Vol(model));
            }
            L("--- base only ---");
            for (int i = 0; i < solid.Count; i++)
            {
                bool ok = false;
                try { ok = solid[i].SetSuppression(1); } catch (Exception e) { L("  err " + e.Message); }
                try { model.EditRebuild3(); } catch { }
                L("unsuppress " + solid[i].Name + " ok=" + ok + " sup=" + solid[i].IsSuppressed() + " vol=" + Vol(model));
            }
            try { sw.CloseDoc(model.GetTitle()); } catch { }
        }
        catch (Exception e) { L("!! " + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static double Vol(IModelDoc2 m)
    {
        try
        {
            object[] bs = (m as IPartDoc).GetBodies2(0, false) as object[];
            if (bs == null || bs.Length == 0) return 0;
            int st = 0;
            double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[];
            if (mp != null) return Math.Round(mp[3] * 1e9, 2);
        }
        catch { }
        return -1;
    }

    static void L(string s)
    {
        SB.AppendLine(s);
        try { File.AppendAllText(LogPath, s + System.Environment.NewLine, new UTF8Encoding(true)); } catch { }
    }
}
