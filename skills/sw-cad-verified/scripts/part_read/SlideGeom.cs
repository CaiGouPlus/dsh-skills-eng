// SlideGeom.cs - dump exact planar-face outlines + every sketch segment
// (including construction geometry) for reverse-engineering 滑槽.SLDPRT.
// Read-only. SwKit lifecycle so it never closes a user instance.

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

public static class SlideGeom
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    static ISldWorks sw;
    static IModelDoc2 model;

    static void L(string s) { SB.AppendLine(s); try { File.AppendAllText(LogPath, s + "\r\n"); } catch { } }
    static string F(double v, int d) { return v.ToString("F" + d, C); }
    static string MM(double v) { return (v * 1000.0).ToString("F3", C); }
    static string P2(double a, double b) { return "(" + MM(a) + ", " + MM(b) + ")"; }

    public static string Run(string partPath, string outDir)
    {
        LogPath = Path.Combine(outDir, "slide.geom.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        bool created = false;
        try
        {
            sw = SwKit.Acquire(out created, true);
            int err = 0, warn = 0;
            model = sw.OpenDoc6(partPath, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (model == null) { L("!! open failed"); return SB.ToString(); }
            L("opened " + model.GetPathName());

            // ---------- 1. every sketch, all segments ----------
            L("");
            L("================ SKETCHES (construction marked CONST) ================");
            IFeature feat = model.FirstFeature() as IFeature;
            int fi = 0;
            while (feat != null)
            {
                fi++;
                string tn = "";
                try { tn = feat.GetTypeName2(); } catch { }
                if (tn == "ProfileFeature")
                {
                    L("");
                    L("[" + fi + "] " + feat.Name);
                    ISketch sk = null;
                    try { sk = feat.GetSpecificFeature2() as ISketch; } catch { }
                    if (sk != null) DumpSketch(sk, "    ");
                }
                feat = feat.GetNextFeature() as IFeature;
            }

            // ---------- 2. planar faces: exact loops ----------
            L("");
            L("================ PLANAR FACE LOOPS ================");
            IPartDoc pd = model as IPartDoc;
            object[] bodies = pd.GetBodies2(0, false) as object[];
            if (bodies == null) { L("no bodies"); return SB.ToString(); }
            foreach (object ob in bodies)
            {
                IBody2 b = ob as IBody2;
                if (b == null) continue;
                object[] faces = b.GetFaces() as object[];
                if (faces == null) continue;
                int idx = 0;
                foreach (object fo in faces)
                {
                    idx++;
                    IFace2 face = fo as IFace2;
                    if (face == null) continue;
                    ISurface su = face.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (!su.IsPlane()) continue;
                    double[] pp = su.PlaneParams as double[];
                    if (pp == null || pp.Length < 6) continue;
                    double area = 0; try { area = face.GetArea() * 1e6; } catch { }
                    L("");
                    L("face " + idx + "  plane n=(" + F(pp[0], 3) + "," + F(pp[1], 3) + "," + F(pp[2], 3) + ")"
                      + "  p=" + P2(pp[3], pp[4]) + "  area=" + F(area, 2) + " mm2");
                    // build in-plane 2D frame
                    double[] n = new double[] { pp[0], pp[1], pp[2] };
                    // pick a reference not parallel to n
                    double[] r = Math.Abs(n[0]) < 0.9 ? new double[] { 1, 0, 0 } : new double[] { 0, 1, 0 };
                    double[] u = Cross(r, n); VecNorm(u);
                    double[] v = Cross(n, u); VecNorm(v);
                    object[] loops = face.GetLoops() as object[];
                    if (loops == null) continue;
                    foreach (object lo in loops)
                    {
                        ILoop2 lp = lo as ILoop2;
                        if (lp == null) continue;
                        bool outer = false;
                        try { outer = lp.IsOuter(); } catch { }
                        object[] ed = lp.GetEdges() as object[];
                        L("  loop " + (outer ? "OUTER" : "inner") + "  edges=" + (ed == null ? 0 : ed.Length));
                        if (ed == null) continue;
                        foreach (object eo in ed)
                        {
                            IEdge e = eo as IEdge;
                            if (e == null) continue;
                            IVertex v1 = e.GetStartVertex() as IVertex;
                            IVertex v2 = e.GetEndVertex() as IVertex;
                            double[] a = v1 == null ? null : v1.GetPoint() as double[];
                            double[] bb = v2 == null ? null : v2.GetPoint() as double[];
                            string kind = "?";
                            string extra = "";
                            try
                            {
                                ICurve cv = e.GetCurve() as ICurve;
                                if (cv != null)
                                {
                                    if (cv.IsCircle())
                                    {
                                        double[] cp = cv.CircleParams as double[];
                                        if (cp != null && cp.Length >= 7)
                                        {
                                            // circle-arc vs full circle by comparing endpoints
                                            bool full = a != null && bb != null && Sq(a[0] - bb[0]) + Sq(a[1] - bb[1]) + Sq(a[2] - bb[2]) < 1e-12;
                                            kind = full ? "circle" : "arc";
                                            extra = "  R=" + MM(cp[6]) + " ctr=" + P2(Proj(cp, 0, u), Proj(cp, 0, v))
                                                  + " axis=(" + F(cp[3], 2) + "," + F(cp[4], 2) + "," + F(cp[5], 2) + ")";
                                        }
                                        else kind = "circle?";
                                    }
                                    else if (cv.IsLine()) kind = "line";
                                    else kind = "curve";
                                }
                                else kind = "nullcurve";
                            }
                            catch (Exception ex) { extra = " !! " + ex.Message; }
                            L("    " + kind.PadRight(6)
                              + " " + (a == null ? "?" : P2(Proj(a, 0, u), Proj(a, 0, v)))
                              + " -> " + (bb == null ? "?" : P2(Proj(bb, 0, u), Proj(bb, 0, v)))
                              + extra);
                        }
                    }
                }
            }
        }
        catch (Exception ex) { L("!! " + ex.Message); }
        finally { try { if (model != null) sw.CloseDoc(model.GetTitle()); } catch { } SwKit.Release(sw, created); }
        return SB.ToString();
    }

    static void DumpSketch(ISketch sk, string ind)
    {
        object[] segs = null;
        try { segs = sk.GetSketchSegments() as object[]; } catch { }
        L(ind + "segments = " + (segs == null ? 0 : segs.Length));
        if (segs == null) return;
        foreach (object o in segs)
        {
            ISketchSegment sg = o as ISketchSegment;
            if (sg == null) continue;
            int ty = -1; try { ty = sg.GetType(); } catch { }
            bool cg = false; try { cg = sg.ConstructionGeometry; } catch { }
            string tag = cg ? "CONST" : "      ";
            if (ty == 0)
            {
                ISketchLine ln = sg as ISketchLine;
                if (ln == null) { L(ind + tag + " line(cast failed)"); continue; }
                ISketchPoint p1 = ln.GetStartPoint2() as ISketchPoint;
                ISketchPoint p2 = ln.GetEndPoint2() as ISketchPoint;
                L(ind + tag + " line  " + (p1 == null ? "?" : P2(p1.X, p1.Y)) + " -> " + (p2 == null ? "?" : P2(p2.X, p2.Y)));
            }
            else if (ty == 1)
            {
                ISketchArc ar = sg as ISketchArc;
                if (ar == null) { L(ind + tag + " arc(cast failed)"); continue; }
                ISketchPoint c = ar.GetCenterPoint2() as ISketchPoint;
                ISketchPoint a1 = ar.GetStartPoint2() as ISketchPoint;
                ISketchPoint a2 = ar.GetEndPoint2() as ISketchPoint;
                double r = 0; try { r = ar.GetRadius(); } catch { }
                bool full = a1 != null && a2 != null && Math.Abs(a1.X - a2.X) < 1e-9 && Math.Abs(a1.Y - a2.Y) < 1e-9;
                L(ind + tag + " " + (full ? "circle" : "arc   ") + " c=" + (c == null ? "?" : P2(c.X, c.Y)) + " R=" + MM(r)
                  + (full ? "" : "  " + (a1 == null ? "?" : P2(a1.X, a1.Y)) + " -> " + (a2 == null ? "?" : P2(a2.X, a2.Y))));
            }
            else L(ind + tag + " segtype " + ty);
        }
    }

    static double Sq(double x) { return x * x; }
    static double Proj(double[] p, int baseIdx, double[] axis)
    {
        return p[baseIdx] * axis[0] + p[baseIdx + 1] * axis[1] + p[baseIdx + 2] * axis[2];
    }
    static void VecNorm(double[] a)
    {
        double m = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
        if (m > 1e-12) { a[0] /= m; a[1] /= m; a[2] /= m; }
    }
    static double[] Cross(double[] a, double[] b)
    {
        return new double[]
        {
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]
        };
    }
}
