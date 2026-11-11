using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;

public static class Diag2
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;

    public static string Run(string path)
    {
        LogPath = Path.ChangeExtension(path, ".diag2.log");
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
            L("name=" + f.Name + " sup=" + f.IsSuppressed() + " vol=" + Vol(model));

            // route 1: SetSuppression(0)
            bool r1 = false; try { r1 = f.SetSuppression(0); } catch (Exception e) { L("SetSuppression err " + e.Message); }
            L("SetSuppression(0) -> " + r1 + " sup=" + f.IsSuppressed());
            model.EditRebuild3();
            L("after rebuild vol=" + Vol(model));
            try { f.SetSuppression(1); } catch { }
            model.EditRebuild3();

            // route 2: select + EditSuppress2
            model.ClearSelection2(true);
            bool sel = model.Extension.SelectByID2(f.Name, "BODYFEATURE", 0, 0, 0, false, 0, null, 0);
            L("SelectByID2 -> " + sel + " count=" + ((ISelectionMgr)model.SelectionManager).GetSelectedObjectCount2(-1));
            bool sup2 = false; try { sup2 = model.EditSuppress2(); } catch (Exception e) { L("EditSuppress2 err " + e.Message); }
            L("EditSuppress2 -> " + sup2 + " sup=" + f.IsSuppressed());
            model.EditRebuild3();
            L("after rebuild vol=" + Vol(model) + " bodies=" + Bodies(model));
            try { model.EditUnsuppress2(); } catch { }
            model.EditRebuild3();
            L("restored sup=" + f.IsSuppressed() + " vol=" + Vol(model));

            sw.CloseDoc(model.GetTitle());
        }
        catch (Exception e) { L("!! " + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static double Vol(IModelDoc2 m) { try { int st = 0; double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[]; return mp == null ? -1 : Math.Round(mp[3] * 1e9, 2); } catch { return -1; } }
    static int Bodies(IModelDoc2 m) { try { object[] b = (m as IPartDoc).GetBodies2(0, false) as object[]; return b == null ? -1 : b.Length; } catch { return -2; } }
    static void L(string s) { SB.AppendLine(s); try { File.AppendAllText(LogPath, s + System.Environment.NewLine, new UTF8Encoding(true)); } catch { } }
}
