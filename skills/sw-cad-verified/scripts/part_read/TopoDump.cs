using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

/// <summary>Unified dump: faces, edges (with adjacent faces), tessellation (with face ids + normals).</summary>
public static class TopoDump
{
    static StringBuilder SB = new StringBuilder();
    static CultureInfo CI = CultureInfo.InvariantCulture;
    static Dictionary<IntPtr, int> FaceIndex = new Dictionary<IntPtr, int>();

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
            int st = 0;
            double[] mp = m.Extension.GetMassProperties2(1, out st, false) as double[];
            double[] pb = pd.GetPartBox(true) as double[];
            SB.AppendLine(string.Format("META|{0}|{1}|{2}|{3}|{4}|{5}|{6}", CI, mp != null ? (mp[3]*1e9).ToString("F3",CI) : "-",
                (pb[0]*1000).ToString("F3",CI), (pb[3]*1000).ToString("F3",CI),
                (pb[1]*1000).ToString("F3",CI), (pb[4]*1000).ToString("F3",CI),
                (pb[2]*1000).ToString("F3",CI), (pb[5]*1000).ToString("F3",CI)));
            int fidBase = 0;
            foreach (object o in bodies)
            {
                IBody2 b = o as IBody2; if (b == null) continue;
                object[] faces = b.GetFaces() as object[];
                if (faces == null) continue;
                FaceIndex.Clear();
                for (int i = 0; i < faces.Length; i++)
                {
                    FaceIndex[Marshal.GetIUnknownForObject(faces[i])] = fidBase + i + 1;
                    DumpFace(fidBase + i + 1, faces[i] as IFace2);
                }
                // edges
                object[] edges = b.GetEdges() as object[];
                int eid = 0;
                if (edges != null)
                    foreach (object eo in edges)
                    {
                        eid++;
                        IEdge e = eo as IEdge; if (e == null) continue;
                        int fA = 0, fB = 0;
                        try
                        {
                            object two = e.GetTwoAdjacentFaces2();
                            object[] arr = two as object[];
                            if (arr != null && arr.Length >= 2) { fA = Look(arr[0]); fB = Look(arr[1]); }
                            else if (two != null) { fA = Look(two); }
                        }
                        catch { }
                        double[] p0 = null, p1 = null;
                        try { object v0 = e.GetStartVertex(); IVertex vt = v0 as IVertex; if (vt != null) p0 = ToDoubles(vt.GetPoint()); } catch { }
                        try { object v1 = e.GetEndVertex(); IVertex vt = v1 as IVertex; if (vt != null) p1 = ToDoubles(vt.GetPoint()); } catch { }
                        SB.AppendLine("E|" + eid + "|" + fA + "|" + fB + "|" + (p0 == null ? "-" : P(p0)) + "|" + (p1 == null ? "-" : P(p1)));
                    }
                // tessellation
                ITessellation tess = null;
                try { tess = b.GetTessellation(null) as ITessellation; } catch { }
                if (tess != null)
                {
                    try { tess.NeedVertexNormal = true; } catch { }
                    try { tess.NeedFaceFacetMap = true; } catch { }
                    try { tess.MaxFacetWidth = 0.0005; } catch { }
                    try { tess.Tessellate(); } catch { }
                    foreach (object fo in faces)
                    {
                        IFace2 f = fo as IFace2; if (f == null) continue;
                        int fid = 0; if (!FaceIndex.TryGetValue(Marshal.GetIUnknownForObject(f), out fid)) continue;
                        int[] facets = null;
                        try { facets = ToInts(tess.GetFaceFacets(f)); } catch { }
                        if (facets == null) continue;
                        foreach (int id in facets)
                        {
                            int[] fins = null;
                            try { fins = ToInts(tess.GetFacetFins(id)); } catch { }
                            if (fins == null || fins.Length < 3) continue;
                            int[] vs = new int[3];
                            bool good = true;
                            for (int k = 0; k < 3; k++)
                            {
                                int[] vv = null;
                                try { vv = ToInts(tess.GetFinVertices(fins[k])); } catch { }
                                if (vv == null || vv.Length < 2) { good = false; break; }
                                vs[k] = vv[0];
                            }
                            if (!good) continue;
                            double[] p0 = null, p1 = null, p2 = null, n0 = null;
                            try { p0 = tess.GetVertexPoint(vs[0]) as double[]; } catch { }
                            try { p1 = tess.GetVertexPoint(vs[1]) as double[]; } catch { }
                            try { p2 = tess.GetVertexPoint(vs[2]) as double[]; } catch { }
                            try { n0 = tess.GetVertexNormal(vs[0]) as double[]; } catch { }
                            if (p0 == null || p1 == null || p2 == null) continue;
                            SB.Append("TRI|").Append(fid).Append('|').Append(P(p0)).Append('|').Append(P(p1)).Append('|').Append(P(p2)).Append('|')
                              .Append(n0 == null ? "0,0,0" : N(n0)).AppendLine();
                        }
                    }
                }
                fidBase += faces.Length;
            }
            try { sw.CloseDoc(m.GetTitle()); } catch { }
        }
        catch (Exception e) { SB.AppendLine("ERR|" + e.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return SB.ToString();
    }

    static void DumpFace(int fid, IFace2 f)
    {
        if (f == null) return;
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
                vals = string.Join(",", new string[] { pp[0].ToString("F6",CI), pp[1].ToString("F6",CI), pp[2].ToString("F6",CI),
                    (pp[3]*1000).ToString("F4",CI), (pp[4]*1000).ToString("F4",CI), (pp[5]*1000).ToString("F4",CI) });
            }
            else if (su != null && su.IsCylinder())
            {
                double[] cp = su.CylinderParams as double[];
                code = "C";
                vals = string.Join(",", new string[] { (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI), (cp[6]*1000).ToString("F4",CI) });
            }
            else if (su != null && su.IsCone())
            {
                double[] cp = su.ConeParams as double[];
                code = "K";
                vals = string.Join(",", new string[] { (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI), (cp[6]*1000).ToString("F4",CI), cp[7].ToString("F6",CI) });
            }
            else if (su != null && su.IsSphere())
            {
                double[] cp = su.SphereParams as double[];
                code = "S";
                vals = string.Join(",", new string[] { (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI), (cp[3]*1000).ToString("F4",CI) });
            }
            else if (su != null && su.IsTorus())
            {
                double[] cp = su.TorusParams as double[];
                code = "T";
                vals = string.Join(",", new string[] { (cp[0]*1000).ToString("F4",CI), (cp[1]*1000).ToString("F4",CI), (cp[2]*1000).ToString("F4",CI),
                    cp[3].ToString("F6",CI), cp[4].ToString("F6",CI), cp[5].ToString("F6",CI), (cp[6]*1000).ToString("F4",CI), (cp[7]*1000).ToString("F4",CI) });
            }
        }
        catch { }
        SB.Append("F|").Append(fid).Append('|').Append(code).Append('|').Append(area.ToString("F3",CI)).Append('|').Append(vals).Append('|').Append(bs).AppendLine();
    }

    static double[] ToDoubles(object o)
    {
        if (o == null) return null;
        if (o is double[]) return (double[])o;
        if (o is object[]) { object[] a = (object[])o; double[] r = new double[a.Length]; for (int i=0;i<a.Length;i++) r[i]=Convert.ToDouble(a[i]); return r; }
        return null;
    }

    static int Look(object faceObj)
    {
        try { return FaceIndex[Marshal.GetIUnknownForObject(faceObj)]; } catch { return 0; }
    }
    static int[] ToInts(object o)
    {
        if (o == null) return null;
        if (o is int[]) return (int[])o;
        if (o is short[]) { short[] s = (short[])o; int[] r = new int[s.Length]; for (int i=0;i<s.Length;i++) r[i]=s[i]; return r; }
        if (o is object[]) { object[] a = (object[])o; int[] r = new int[a.Length]; for (int i=0;i<a.Length;i++) r[i]=Convert.ToInt32(a[i]); return r; }
        return null;
    }
    static string P(double[] p) { return (p[0]*1000).ToString("F4",CI)+","+(p[1]*1000).ToString("F4",CI)+","+(p[2]*1000).ToString("F4",CI); }
    static string N(double[] p) { return p[0].ToString("F4",CI)+","+p[1].ToString("F4",CI)+","+p[2].ToString("F4",CI); }
}
