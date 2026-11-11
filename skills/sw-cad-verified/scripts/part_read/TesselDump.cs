using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

/// <summary>Dump tessellation triangles with face index and normal (mm).</summary>
public static class TesselDump
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
            foreach (object o in bodies)
            {
                IBody2 b = o as IBody2; if (b == null) continue;
                ITessellation tess = null;
                try { tess = b.GetTessellation(null) as ITessellation; } catch { }
                if (tess == null) { SB.AppendLine("NOTESS"); continue; }
                try { tess.ImprovedQuality = true; } catch { }
                try { tess.NeedVertexNormal = true; } catch { }
                try { tess.NeedFaceFacetMap = true; } catch { }
                try { tess.MaxFacetWidth = 0.0005; } catch { }
                
                
                bool ok = false; try { ok = tess.Tessellate(); } catch { }
                SB.AppendLine("TESSINFO|" + ok + "|" + tess.GetFacetCount());
                object[] faces = b.GetFaces() as object[];
                foreach (object fo in faces)
                {
                    fi++;
                    IFace2 f = fo as IFace2; if (f == null) continue;
                    int[] facets = null;
                    try { facets = ToInts(tess.GetFaceFacets(f)); } catch { }
                    if (facets == null) continue;
                    foreach (int id in facets)
                    {
                        int[] fins = null;
                        try { fins = ToInts(tess.GetFacetFins(id)); } catch { }
                        if (fins == null || fins.Length < 3) continue;
                        int[] vs = new int[3];
                        for (int k = 0; k < 3; k++)
                        {
                            int fin = fins[k];
                            int[] vv = null;
                            try { vv = ToInts(tess.GetFinVertices(fin)); } catch { }
                            if (vv == null || vv.Length < 2) { break; }
                            vs[k] = vv[0];
                        }
                        double[] p0 = null, p1 = null, p2 = null, n0 = null;
                        try { p0 = tess.GetVertexPoint(vs[0]) as double[]; } catch { }
                        try { p1 = tess.GetVertexPoint(vs[1]) as double[]; } catch { }
                        try { p2 = tess.GetVertexPoint(vs[2]) as double[]; } catch { }
                        try { n0 = tess.GetVertexNormal(vs[0]) as double[]; } catch { }
                        if (p0 == null || p1 == null || p2 == null) continue;
                        SB.Append(fi).Append('|')
                          .Append(F(p0)).Append('|').Append(F(p1)).Append('|').Append(F(p2)).Append('|')
                          .Append(n0 == null ? "0,0,0" : F(n0)).AppendLine();
                    }
                }
            }
            try { sw.CloseDoc(m.GetTitle()); } catch { }
        }
        catch (Exception e) { SB.AppendLine("ERR|" + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static int[] ToInts(object o)
    {
        if (o == null) return null;
        if (o is int[]) return (int[])o;
        if (o is short[]) { short[] s = (short[])o; int[] r = new int[s.Length]; for (int i=0;i<s.Length;i++) r[i]=s[i]; return r; }
        if (o is object[]) { object[] a = (object[])o; int[] r = new int[a.Length]; for (int i=0;i<a.Length;i++) r[i]=Convert.ToInt32(a[i]); return r; }
        return null;
    }

    static string F(double[] p)
    {
        return (p[0]*1000).ToString("F4",CI) + "," + (p[1]*1000).ToString("F4",CI) + "," + (p[2]*1000).ToString("F4",CI);
    }
}
