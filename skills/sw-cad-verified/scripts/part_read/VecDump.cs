using System;
using System.IO;
using System.Text;
using System.Globalization;
using SolidWorks.Interop.sldworks;

public static class VecDump
{
    static StringBuilder SB = new StringBuilder();
    static CultureInfo CI = CultureInfo.InvariantCulture;

    public static string Run(string path)
    {
        SldWorks sw = null;
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("SLDWORKS").Length > 0)
            {
                System.Console.Error.WriteLine("REFUSED: SLDWORKS.exe already running: this legacy dumper would take over that instance and close it on exit. Close SolidWorks first.");
                System.Environment.Exit(42);
            }
            sw = new SldWorks();
            sw.Visible = false;
            int err = 0, warn = 0;
            IModelDoc2 m = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (m == null) { SB.AppendLine("OPENFAIL|" + err); return SB.ToString(); }
            IPartDoc pd = m as IPartDoc;
            object[] bodies = pd.GetBodies2(0, false) as object[];
            int fi = 0;
            try {
                int st = 0;
                double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[];
                double[] pb = (pd.GetPartBox(true) as double[]);
                SB.AppendLine(string.Format("META|0|{0}|{1},{2},{3},{4},{5},{6}", mp != null ? (mp[3]*1e9).ToString("F3",CI) : "-", 
                    (pb[0]*1000).ToString("F2",CI),(pb[3]*1000).ToString("F2",CI),(pb[1]*1000).ToString("F2",CI),(pb[4]*1000).ToString("F2",CI),(pb[2]*1000).ToString("F2",CI),(pb[5]*1000).ToString("F2",CI)));
            } catch {}
            if (bodies != null)
                foreach (object o in bodies)
                {
                    IBody2 b = o as IBody2; if (b == null) continue;
                    object[] fs = null; try { fs = b.GetFaces() as object[]; } catch { }
                    if (fs == null) continue;
                    foreach (object fo in fs)
                    {
                        fi++;
                        IFace2 f = fo as IFace2; if (f == null) continue;
                        double area = 0; try { area = f.GetArea() * 1e6; } catch { }
                        double[] box = null; try { box = f.GetBox() as double[]; } catch { }
                        string bs = box == null ? "-" : string.Join(",", new string[] {
                            (box[0]*1000).ToString("F3",CI), (box[3]*1000).ToString("F3",CI),
                            (box[1]*1000).ToString("F3",CI), (box[4]*1000).ToString("F3",CI),
                            (box[2]*1000).ToString("F3",CI), (box[5]*1000).ToString("F3",CI) });
                        ISurface su = null; try { su = f.GetSurface() as ISurface; } catch { }
                        string code = "X"; string vals = "0";
                        try
                        {
                            if (su != null && su.IsPlane())
                            {
                                double[] pp = su.PlaneParams as double[];
                                code = "P";
                                vals = string.Join(",", new string[] {
                                    pp[0].ToString("F6",CI), pp[1].ToString("F6",CI), pp[2].ToString("F6",CI),
                                    (pp[3]*1000).ToString("F4",CI), (pp[4]*1000).ToString("F4",CI), (pp[5]*1000).ToString("F4",CI) });
                            }
                            else if (su != null && su.IsCylinder())
                            {
                                double[] cp = su.CylinderParams as double[];
                                code = "C";
                                vals = string.Join(",", new string[] {
                                    (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI),
                                    (cp[6]*1000).ToString("F4",CI) });
                            }
                            else if (su != null && su.IsCone())
                            {
                                double[] cp = su.ConeParams as double[];
                                code = "K";
                                vals = string.Join(",", new string[] {
                                    (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI),
                                    (cp[6]*1000).ToString("F4",CI), cp[7].ToString("F6",CI) });
                            }
                            else if (su != null && su.IsSphere())
                            {
                                double[] cp = su.SphereParams as double[];
                                code = "S";
                                vals = string.Join(",", new string[] {
                                    (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                                    (cp[3]*1000).ToString("F4",CI) });
                            }
                            else if (su != null && su.IsTorus())
                            {
                                double[] cp = su.TorusParams as double[];
                                code = "T";
                                vals = string.Join(",", new string[] {
                                    (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI),
                                    (cp[6]*1000).ToString("F4",CI), (cp[7]*1000).ToString("F4",CI) });
                            }
                        }
                        catch { }
                        string nrm = "";
                        try {
                            object no = f.Normal;
                            double[] na = no as double[];
                            if (na == null) { MathVector mv = no as MathVector; if (mv != null) na = mv.ArrayData as double[]; }
                            if (na != null && na.Length >= 3) nrm = string.Join(",", new string[] { na[0].ToString("F4",CI), na[1].ToString("F4",CI), na[2].ToString("F4",CI) });
                        } catch { }
                        SB.AppendLine(code + "|" + fi.ToString() + "|" + area.ToString("F3",CI) + "|" + vals + "|" + bs + "|" + nrm);
                    }
                }
            try { sw.CloseDoc(m.GetTitle()); } catch { }
        }
        catch (Exception e) { SB.AppendLine("ERR|" + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }
}
