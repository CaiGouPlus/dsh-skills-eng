using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

public static class FaceDump
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;
    static List<string> Rows = new List<string>();

    public static string Run(string path)
    {
        LogPath = Path.ChangeExtension(path, ".faces.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        SldWorks sw = null;
        try
        {
            sw = new SldWorks();
            sw.Visible = true;
            int err = 0, warn = 0;
            IModelDoc2 model = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
            IPartDoc pd = model as IPartDoc;
            object[] bodies = pd.GetBodies2(0, false) as object[];
            if (bodies == null) { L("no bodies"); return SB.ToString(); }
            foreach (object o in bodies)
            {
                IBody2 b = o as IBody2;
                if (b == null) continue;
                object[] faces = b.GetFaces() as object[];
                if (faces == null) continue;
                L("faces = " + faces.Length);
                int i = 0;
                foreach (object fo in faces)
                {
                    i++;
                    IFace2 face = fo as IFace2;
                    if (face == null) continue;
                    ISurface su = face.GetSurface() as ISurface;
                    if (su == null) continue;
                    double area = -1; try { area = face.GetArea() * 1e6; } catch { }
                    string kind = "other";
                    double[] pp = null;
                    try { if (su.IsPlane()) { kind = "plane"; pp = su.PlaneParams as double[]; } else if (su.IsCylinder()) kind = "cyl"; } catch { }
                    string s = string.Format("face {0,3} {1,-6} area={2,10:F2} mm2", i, kind, area);
                    if (pp != null && pp.Length >= 6)
                        s += string.Format("  n=({0:F3},{1:F3},{2:F3}) p=({3:F2},{4:F2},{5:F2})", pp[0], pp[1], pp[2], pp[3] * 1000, pp[4] * 1000, pp[5] * 1000);
                    Rows.Add(s);
                }
            }
            // sort by area desc
            Rows.Sort(delegate(string a, string b) { return AreaOf(b).CompareTo(AreaOf(a)); });
            foreach (string r in Rows) L(r);
            sw.CloseDoc(model.GetTitle());
        }
        catch (Exception e) { L("!! " + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static double AreaOf(string s)
    {
        int k = s.IndexOf("area=");
        if (k < 0) return -1;
        int k2 = s.IndexOf(" mm2", k);
        string num = s.Substring(k + 5, k2 - (k + 5)).Trim();
        double v; double.TryParse(num, out v); return v;
    }

    static void L(string s) { SB.AppendLine(s); try { File.AppendAllText(LogPath, s + System.Environment.NewLine, new UTF8Encoding(true)); } catch { } }
}
