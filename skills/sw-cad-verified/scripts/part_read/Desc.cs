using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;

public static class Desc
{
    public static string Run(string listPath)
    {
        StringBuilder SB = new StringBuilder();
        string[] paths = File.ReadAllLines(listPath, Encoding.UTF8);
        if (System.Diagnostics.Process.GetProcessesByName("SLDWORKS").Length > 0)
        {
            System.Console.Error.WriteLine("REFUSED: SLDWORKS.exe already running: this legacy dumper would take over that instance and close it on exit. Close SolidWorks first.");
            System.Environment.Exit(42);
        }
        SldWorks sw = new SldWorks();
        sw.Visible = false;
        foreach (string raw in paths)
        {
            string p = raw.Trim();
            if (p == "") continue;
            try { SB.AppendLine(One(sw, p)); }
            catch (Exception e) { SB.AppendLine(Path.GetFileName(p) + "|ERR|" + e.Message); }
        }
        try { sw.ExitApp(); } catch { }
        return SB.ToString();
    }

    static string One(SldWorks sw, string path)
    {
        int err = 0, warn = 0;
        IModelDoc2 m = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
        if (m == null) return Path.GetFileName(path) + "|OPENFAIL|" + err;
        IPartDoc pd = m as IPartDoc;
        object[] bodies = pd.GetBodies2(0, false) as object[];
        double vol = -1;
        try { int st = 0; double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[]; if (mp != null) vol = mp[3] * 1e9; } catch { }
        double[] bb = pd.GetPartBox(true) as double[];
        double area = 0; int faces = 0, edges = 0, verts = 0, plane = 0, cyl = 0, other = 0;
        double[] hist = new double[7];
        if (bodies != null)
            foreach (object o in bodies)
            {
                IBody2 b = o as IBody2; if (b == null) continue;
                try { edges += b.GetEdgeCount(); } catch { }
                try { verts += b.GetVertexCount(); } catch { }
                object[] fs = null; try { fs = b.GetFaces() as object[]; } catch { }
                if (fs == null) continue;
                faces += fs.Length;
                foreach (object fo in fs)
                {
                    IFace2 f = fo as IFace2; if (f == null) continue;
                    double a = 0; try { a = f.GetArea() * 1e6; } catch { }
                    area += a;
                    ISurface su = null; try { su = f.GetSurface() as ISurface; } catch { }
                    if (su == null) { other++; continue; }
                    bool isP = false; try { isP = su.IsPlane(); } catch { }
                    if (isP)
                    {
                        plane++;
                        double[] pp = null; try { pp = su.PlaneParams as double[]; } catch { }
                        if (pp != null)
                        {
                            double ax = Math.Abs(pp[0]), ay = Math.Abs(pp[1]), az = Math.Abs(pp[2]);
                            if (ax > 0.9) hist[pp[0] > 0 ? 0 : 1] += a;
                            else if (ay > 0.9) hist[pp[1] > 0 ? 2 : 3] += a;
                            else if (az > 0.9) hist[pp[2] > 0 ? 4 : 5] += a;
                            else hist[6] += a;
                        }
                    }
                    else
                    {
                        bool isC = false; try { isC = su.IsCylinder(); } catch { }
                        if (isC) cyl++; else other++;
                    }
                }
            }
        try { sw.CloseDoc(m.GetTitle()); } catch { }
        string[] parts = new string[] {
            Path.GetFileName(path), vol.ToString("F2"),
            bb != null ? ((bb[3]-bb[0])*1000).ToString("F2") : "0",
            bb != null ? ((bb[4]-bb[1])*1000).ToString("F2") : "0",
            bb != null ? ((bb[5]-bb[2])*1000).ToString("F2") : "0",
            area.ToString("F2"), faces.ToString(), edges.ToString(), verts.ToString(),
            plane.ToString(), cyl.ToString(), other.ToString(),
            hist[0].ToString("F1"), hist[1].ToString("F1"), hist[2].ToString("F1"),
            hist[3].ToString("F1"), hist[4].ToString("F1"), hist[5].ToString("F1"), hist[6].ToString("F1")
        };
        return string.Join("|", parts);
    }
}
