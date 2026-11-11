using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

/// <summary>
/// Read a part in depth: metadata, feature tree + dimensions, sketches,
/// B-Rep faces grouped (planes / cylinders / cones / tori), mass props,
/// bounding box, and four viewport bitmaps. Read-only.
/// ASCII-only source (PowerShell 5.1 reads *.cs as GBK when there is no BOM).
/// </summary>
public static class PartAnalyze
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;
    static ISldWorks sw;
    static IModelDoc2 model;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    static void L(string s)
    {
        SB.AppendLine(s);
        try { File.AppendAllText(LogPath, s + "\r\n"); } catch { }
    }
    static string F(double v, int d) { return v.ToString("F" + d, C); }
    static string Md5(string path)
    {
        using (System.Security.Cryptography.MD5 md = System.Security.Cryptography.MD5.Create())
        using (FileStream fs = File.OpenRead(path))
        {
            byte[] h = md.ComputeHash(fs);
            StringBuilder sb = new StringBuilder();
            foreach (byte x in h) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
    static string MM(double v, int d) { return (v * 1000.0).ToString("F" + d, C); }
    static string Vec(double[] a, int d) { return "(" + MM(a[0], d) + ", " + MM(a[1], d) + ", " + MM(a[2], d) + ")"; }
    static void Safe(string tag, Action a)
    {
        try { a(); }
        catch (Exception e) { L("!! " + tag + " : " + e.Message); }
    }

    public static string Run(string partPath, string outDir, string expectMd5)
    {
        if (string.IsNullOrEmpty(outDir)) outDir = Path.GetDirectoryName(partPath);
        OutDir = outDir;
        Directory.CreateDirectory(outDir);
        LogPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(partPath) + ".analyze.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        bool created = false;
        string diskMd5 = "";
        try { diskMd5 = Md5(partPath); } catch (Exception e) { L("!! cannot hash input: " + e.Message); }
        L("md5(on disk, before open) = " + diskMd5 + "  (caller expected " + expectMd5 + ")");
        if (!string.IsNullOrEmpty(expectMd5) && !string.Equals(diskMd5, expectMd5, StringComparison.OrdinalIgnoreCase))
        {
            L("!! MD5 MISMATCH before open - the path on disk is not the file the caller meant");
            return SB.ToString();
        }
        try
        {
            sw = SwKit.Acquire(out created, true);
            L("SW revision = " + sw.RevisionNumber());
            int err = 0, warn = 0;
            model = sw.OpenDoc6(partPath, 1, 1, "", ref err, ref warn) as IModelDoc2;
            L("open err=" + err + " warn=" + warn);
            if (model == null) { L("!! open failed"); return SB.ToString(); }
            string req = Path.GetFullPath(partPath);
            string got = Path.GetFullPath(model.GetPathName());
            L("requested = " + req);
            L("opened    = " + got);
            L("title     = " + model.GetTitle());
            if (!string.Equals(req, got, StringComparison.OrdinalIgnoreCase))
            {
                L("!! IDENTITY MISMATCH: asked for " + req + " but SolidWorks opened " + got);
                L("!! refusing to analyze the wrong document");
                throw new Exception("IDENTITY MISMATCH - abort");
            }
            L("doctype = " + model.GetType());
            Safe("configs", Configs);
            Safe("material", Material);
            Safe("props", Props);
            Safe("tree", Tree);
            Safe("bodies", Bodies);
            Safe("mass", MassProps);
            Safe("box", Box);
            Safe("faces", Faces);
            Safe("tess", DumpTess);
            Safe("views", Views);
        }
        catch (Exception e) { L("!! fatal " + e.Message); }
        finally
        {
            try { if (model != null && sw != null) sw.CloseDoc(model.GetTitle()); } catch { }
            try { SwKit.Release(sw, created); } catch { }
        }
        return SB.ToString();
    }

    static void Configs()
    {
        object[] a = model.GetConfigurationNames() as object[];
        string s = "";
        if (a != null) foreach (object x in a) s += (s == "" ? "" : " | ") + x.ToString();
        L("configs = " + s);
        L("active config = " + model.ConfigurationManager.ActiveConfiguration.Name);
    }

    static void Material()
    {
        IPartDoc pd = model as IPartDoc;
        string db = "";
        string nm = "";
        try { nm = pd.GetMaterialPropertyName2("", out db); } catch (Exception e) { nm = "?" + e.Message; }
        L("material = " + nm + "   (library: " + db + ")");

    }

    static void Props()
    {
        ICustomPropertyManager cpm = model.Extension.get_CustomPropertyManager("") as ICustomPropertyManager;
        if (cpm == null) { L("props = (no manager)"); return; }
        object[] names = cpm.GetNames() as object[];
        if (names == null || names.Length == 0) { L("props = (none)"); return; }
        foreach (object n in names)
        {
            string nm = n.ToString(), val = "", res = "";
            bool was = false;
            try { cpm.Get5(nm, false, out val, out res, out was); } catch { }
            L("prop  " + nm + " = " + val + (was ? "  [res=" + res + "]" : ""));
        }
    }

    // ---------------- feature tree ----------------
    static void Tree()
    {
        IFeature f = model.FirstFeature() as IFeature;
        int idx = 0;
        int shown = 0;
        while (f != null && idx < 400)
        {
            string tn = "";
            try { tn = f.GetTypeName2(); } catch { }
            string nm = "";
            try { nm = f.Name; } catch { }
            bool supp = false;
            try { supp = f.IsSuppressed(); } catch { }
            L("");
            L("[" + idx + "] <" + tn + ">  " + nm + (supp ? "   [SUPPRESSED]" : ""));
            if (tn != "OriginProfileFeature" && tn != "Folder" && tn != "Light" && tn != "MaterialFolder"
                && tn != "RefPlane" && tn != "RefAxis" && tn != "CoordSys" && tn != "SurfaceBodyFolder"
                && tn != "Comment")
            {
                if (shown < 120)
                {
                    Safe("def" + idx, delegate { Def(f, tn); });
                    Safe("dims" + idx, delegate { Dims(f); });
                    if (tn == "ProfileFeature" || tn == "3DProfileFeature")
                        Safe("sketch" + idx, delegate { SketchDump(f); });
                    Safe("sub" + idx, delegate { SubTree(f); });
                    shown++;
                }
            }
            f = f.GetNextFeature() as IFeature;
            idx++;
        }
        L("");
        L("feature count (top level) = " + idx);
    }

    static void SubTree(IFeature f)
    {
        IFeature s = f.GetFirstSubFeature() as IFeature;
        int n = 0;
        while (s != null && n < 40)
        {
            string tn = ""; try { tn = s.GetTypeName2(); } catch { }
            string nm = ""; try { nm = s.Name; } catch { }
            L("    sub <" + tn + "> " + nm);
            if (tn == "ProfileFeature" || tn == "3DProfileFeature") Safe("subsk", delegate { SketchDump(s); });
            s = s.GetNextSubFeature() as IFeature;
            n++;
        }
    }

    static void Def(IFeature f, string tn)
    {
        object o = f.GetDefinition();
        if (o == null) { L("    definition = null"); return; }
        L("    definition class = " + o.GetType().Name);
        IExtrudeFeatureData2 ex = o as IExtrudeFeatureData2;
        if (ex != null)
        {
            try
            {
                int ec1 = ex.GetEndCondition(true), ec2 = ex.GetEndCondition(false);
                L("    extrude: endCond " + EcName(ec1) + " / " + EcName(ec2)
                  + "   depth=" + MM(ex.GetDepth(true), 3) + " / " + MM(ex.GetDepth(false), 3)
                  + "  both=" + ex.BothDirections + " rev=" + ex.ReverseDirection
                  + " thin=" + ex.IsThinFeature());
                if (ex.IsThinFeature()) L("    thin wall type = " + ex.ThinWallType);
            }
            catch (Exception e) { L("    extrude read: " + e.Message); }
        }
        IChamferFeatureData2 ch = o as IChamferFeatureData2;
        if (ch != null)
        {
            try
            {
                L("    chamfer: type=" + ch.Type + " edges=" + ch.GetEdgeCount()
                  + " d1=" + MM(ch.GetEdgeChamferDistance(0), 3)
                  + " d2=" + MM(ch.GetEdgeChamferDistance(1), 3)
                  + " angle=" + F(ch.EdgeChamferAngle * 180.0 / Math.PI, 3) + "deg");
            }
            catch (Exception e) { L("    chamfer read: " + e.Message); }
        }
        ISimpleFilletFeatureData2 fi = o as ISimpleFilletFeatureData2;
        if (fi != null)
        {
            try { L("    fillet: edges=" + fi.GetEdgeCount() + " defaultRadius=" + MM(fi.DefaultRadius, 3) + " multi=" + fi.IsMultipleRadius); }
            catch (Exception e) { L("    fillet read: " + e.Message); }
        }
    }

    static string EcName(int e)
    {
        switch (e)
        {
            case 0: return "0:Blind";
            case 1: return "1:ThroughAll";
            case 2: return "2:ThroughNext";
            case 3: return "3:UpToVertex";
            case 4: return "4:UpToSurface";
            case 5: return "5:OffsetFromSurface";
            case 6: return "6:MidPlane";
            case 7: return "7:UpToBody";
            case 9: return "9:ThroughAllBoth";
            case 10: return "10:UpToSelection";
            case 11: return "11:UpToNext";
            default: return e.ToString();
        }
    }

    static void Dims(IFeature f)
    {
        object o = null;
        try { o = f.GetFirstDisplayDimension(); } catch { }
        IDisplayDimension dd = o as IDisplayDimension;
        int guard = 0;
        while (dd != null && guard++ < 80)
        {
            IDimension d = null;
            try { d = dd.GetDimension2(0) as IDimension; } catch { }
            if (d != null)
            {
                string fn = "?", kind = "?";
                double sv = 0, val = 0;
                int dt = -1;
                try { fn = d.FullName; } catch { }
                try { sv = d.SystemValue; } catch { }
                try { val = d.Value; } catch { }
                try { dt = d.GetType(); } catch { }
                if (Math.Abs(sv) > 1e-12)
                {
                    double asDeg = sv * 180.0 / Math.PI;
                    double asMm = sv * 1000.0;
                    if (Math.Abs(val - asDeg) < 1e-6 + 1e-6 * Math.Abs(asDeg)) kind = "ANG";
                    else if (Math.Abs(val - asMm) < 1e-3 + 1e-9 * Math.Abs(asMm)) kind = "LEN";
                }
                string disp = (kind == "ANG")
                    ? F(sv * 180.0 / Math.PI, 4) + " deg"
                    : MM(sv, 4) + " mm";
                L("    dim " + fn.PadRight(30) + " " + kind + "  " + disp + "   (sv=" + F(sv, 8) + ", val=" + F(val, 4) + ", dType=" + dt + ")");
            }
            try { dd = dd.GetNext3() as IDisplayDimension; } catch { break; }
        }
    }

    // ---------------- sketch ----------------
    static void SketchDump(IFeature f)
    {
        ISketch sk = null;
        try { sk = f.GetSpecificFeature2() as ISketch; } catch { }
        if (sk == null) { L("    (no ISketch)"); return; }
        // sketch plane in model space
        try
        {
            double[] t = ((double[])((IMathTransform)sk.ModelToSketchTransform).ArrayData);
            L("    sketch plane: X=(" + F(t[0], 4) + "," + F(t[1], 4) + "," + F(t[2], 4) + ")"
              + " Y=(" + F(t[3], 4) + "," + F(t[4], 4) + "," + F(t[5], 4) + ")"
              + " Z=(" + F(t[6], 4) + "," + F(t[7], 4) + "," + F(t[8], 4) + ")"
              + " O=" + Vec(new double[] { t[9], t[10], t[11] }, 3));
        }
        catch { }
        try
        {
            object[] segs = sk.GetSketchSegments() as object[];
            L("    segments = " + (segs == null ? 0 : segs.Length));
            if (segs == null) return;
            int nLine = 0, nArc = 0, nOther = 0;
            foreach (object o in segs)
            {
                ISketchSegment sg = o as ISketchSegment;
                if (sg == null) continue;
                int ty = -1;
                try { ty = sg.GetType(); } catch { }
                bool cg = false;
                try { cg = sg.ConstructionGeometry; } catch { }
                if (ty == 0) nLine++;
                else if (ty == 1) nArc++;
                else nOther++;
                if (cg) continue;   // skip construction for brevity
                if (ty == 0)
                {
                    ISketchLine ln = sg as ISketchLine;
                    if (ln == null) continue;
                    ISketchPoint p1 = ln.GetStartPoint2() as ISketchPoint;
                    ISketchPoint p2 = ln.GetEndPoint2() as ISketchPoint;
                    if (p1 == null || p2 == null) continue;
                    L("      line  " + Pt(p1) + " -> " + Pt(p2) + "   len=" + MM(Math.Sqrt(Sq(p2.X - p1.X) + Sq(p2.Y - p1.Y)), 4));
                }
                else if (ty == 1)
                {
                    ISketchArc ar = sg as ISketchArc;
                    if (ar == null) continue;
                    ISketchPoint c = ar.GetCenterPoint2() as ISketchPoint;
                    ISketchPoint a1 = ar.GetStartPoint2() as ISketchPoint;
                    ISketchPoint a2 = ar.GetEndPoint2() as ISketchPoint;
                    if (c == null) continue;
                    double r = ar.GetRadius();
                    bool full = (a1 != null && a2 != null && Math.Abs(a1.X - a2.X) < 1e-9 && Math.Abs(a1.Y - a2.Y) < 1e-9);
                    L("      " + (full ? "circle" : "arc   ") + " c=" + Pt(c) + " R=" + MM(r, 4) + " D=" + MM(2 * r, 4)
                      + (full ? "" : "  a1=" + Pt(a1) + " a2=" + Pt(a2)));
                }
                else L("      seg type " + ty);
            }
            L("    seg counts: line=" + nLine + " arc/circle=" + nArc + " other=" + nOther);
            ISketchContour[] cs = sk.GetSketchContours() as ISketchContour[];
            if (cs != null)
            {
                string s = "";
                foreach (ISketchContour c in cs) s += (s == "" ? "" : ", ") + (c.IsClosed() ? "closed" : "open");
                L("    contours = " + cs.Length + " [" + s + "]");
            }
        }
        catch (Exception e) { L("    sketch read: " + e.Message); }
    }

    static string Pt(ISketchPoint p) { return "(" + MM(p.X, 4) + ", " + MM(p.Y, 4) + ")"; }
    static double Sq(double x) { return x * x; }

    // ---------------- bodies ----------------
    static void Bodies()
    {
        IPartDoc pd = model as IPartDoc;
        object[] bodies = pd.GetBodies2(0, false) as object[];
        L("");
        L("bodies = " + (bodies == null ? 0 : bodies.Length));
        if (bodies == null) return;
        int bi = 0;
        foreach (object o in bodies)
        {
            IBody2 b = o as IBody2;
            if (b == null) continue;
            double[] box = b.GetBodyBox() as double[];
            L("  body[" + bi + "] faces=" + b.GetFaceCount() + " edges=" + Count(b.GetEdges())
              + " verts=" + b.GetVertexCount()
              + "  box=" + (box == null ? "?" : BoxStr(box)));
            double[] mp = b.GetMassProperties(1.0) as double[];
            if (mp != null && mp.Length > 5)
                L("           volume=" + F(mp[3] * 1e9, 2) + " mm3  area=" + F(mp[4] * 1e6, 2) + " mm2  mass@1kg/m3=" + F(mp[5] * 1000, 3) + " g");
            bi++;
        }
    }

    static int Count(object o)
    {
        object[] a = o as object[];
        return a == null ? 0 : a.Length;
    }

    static string BoxStr(double[] b)
    {
        return "x[" + MM(b[0], 2) + "," + MM(b[3], 2) + "] y[" + MM(b[1], 2) + "," + MM(b[4], 2) + "] z[" + MM(b[2], 2) + "," + MM(b[5], 2) + "]";
    }

    static void MassProps()
    {
        int st = 0;
        object o = model.Extension.GetMassProperties2(1, out st, false);
        double[] mp = o as double[];
        L("");
        L("mass props status=" + st);
        if (mp == null) { L("  (null)"); return; }
        string[] nm = { "cx", "cy", "cz", "volume", "area", "mass", "Ixx", "Iyy", "Izz", "Ixy", "Izx", "Iyz",
                        "Mx", "My", "Mz", "?", "?", "?", "?", "?" };
        for (int i = 0; i < mp.Length; i++)
        {
            string label = i < nm.Length ? nm[i] : ("[" + i + "]");
            string unit = "";
            double v = mp[i];
            if (i <= 2) { v *= 1000; unit = "mm"; }
            else if (i == 3) { v *= 1e9; unit = "mm3"; }
            else if (i == 4) { v *= 1e6; unit = "mm2"; }
            else if (i == 5) { v *= 1000; unit = "g"; }
            else { v *= 1e9; unit = "g*mm2?"; }
            L("  " + label.PadRight(8) + " = " + F(v, 4) + " " + unit + "   (raw " + F(mp[i], 12) + ")");
        }
    }

    static void Box()
    {
        IPartDoc pd = model as IPartDoc;
        double[] b = pd.GetPartBox(true) as double[];
        if (b == null) { L("part box = null"); return; }
        L("");
        L("partBox = " + BoxStr(b));
        L("  size = " + MM(b[3] - b[0], 3) + " x " + MM(b[4] - b[1], 3) + " x " + MM(b[5] - b[2], 3) + " mm (X x Y x Z)");
        L("  size = " + MM(b[3] - b[0], 3) + " (X)  " + MM(b[4] - b[1], 3) + " (Y)  " + MM(b[5] - b[2], 3) + " (Z)");
    }

    // ---------------- faces ----------------
    class Group
    {
        public string Key;
        public List<int> Faces = new List<int>();
        public double[] Vals;        // plane: n(3)+d   cylinder: p(3)+a(3)+r
        public double Area;
        public double Dot = double.NaN;
        public double TMin = double.MaxValue, TMax = double.MinValue;
        public double[] BMin = { 1e9, 1e9, 1e9 }, BMax = { -1e9, -1e9, -1e9 };
    }

    static void Grow(Group g, double[] bx)
    {
        if (bx == null) return;
        for (int i = 0; i < 3; i++)
        {
            if (bx[i] < g.BMin[i]) g.BMin[i] = bx[i];
            if (bx[i + 3] > g.BMax[i]) g.BMax[i] = bx[i + 3];
        }
    }

    static void Faces()
    {
        IPartDoc pd = model as IPartDoc;
        object[] bodies = pd.GetBodies2(0, false) as object[];
        if (bodies == null || bodies.Length == 0) { L("no bodies"); return; }
        IBody2 b = bodies[0] as IBody2;
        object[] faces = b.GetFaces() as object[];
        int nf = faces == null ? 0 : faces.Length;
        L("");
        L("faces total = " + nf);

        Dictionary<string, Group> planes = new Dictionary<string, Group>();
        List<Group> planeList = new List<Group>();
        Dictionary<string, Group> cyls = new Dictionary<string, Group>();
        List<Group> cylList = new List<Group>();
        int nCone = 0, nTorus = 0, nSphere = 0, nOther = 0;
        List<string> coneInfo = new List<string>();

        for (int i = 0; i < nf; i++)
        {
            IFace2 fc = faces[i] as IFace2;
            if (fc == null) continue;
            int fidx = i + 1;
            ISurface su = fc.GetSurface() as ISurface;
            if (su == null) continue;
            double area = 0; try { area = fc.GetArea(); } catch { }
            double[] bx = null; try { bx = fc.GetBox() as double[]; } catch { }

            if (su.IsPlane())
            {
                double[] pp = su.PlaneParams as double[];
                double[] n = Norm(pp[0], pp[1], pp[2]);
                double d = (n[0] * pp[3] + n[1] * pp[4] + n[2] * pp[5]) * 1000.0;
                string key = "n" + F(n[0], 3) + "|" + F(n[1], 3) + "|" + F(n[2], 3) + "|d" + F(d, 2);
                Group g;
                if (!planes.TryGetValue(key, out g))
                {
                    g = new Group(); g.Key = key; g.Vals = new double[] { n[0], n[1], n[2], d };
                    planes[key] = g; planeList.Add(g);
                }
                g.Faces.Add(fidx); g.Area += area; Grow(g, bx);
            }
            else if (su.IsCylinder())
            {
                double[] cp = su.CylinderParams as double[];
                double[] a = Norm(cp[3], cp[4], cp[5]);
                double r = cp[6];
                double[] p = new double[] { cp[0], cp[1], cp[2] };
                double tp = p[0] * a[0] + p[1] * a[1] + p[2] * a[2];
                double[] q = new double[] { p[0] - tp * a[0], p[1] - tp * a[1], p[2] - tp * a[2] };
                string key = "a" + F(a[0], 3) + "|" + F(a[1], 3) + "|" + F(a[2], 3)
                             + "|r" + F(r * 1000, 3)
                             + "|q" + F(q[0] * 1000, 2) + "|" + F(q[1] * 1000, 2) + "|" + F(q[2] * 1000, 2);
                Group g;
                if (!cyls.TryGetValue(key, out g))
                {
                    g = new Group(); g.Key = key; g.Vals = new double[] { p[0], p[1], p[2], a[0], a[1], a[2], r };
                    cyls[key] = g; cylList.Add(g);
                }
                g.Faces.Add(fidx); g.Area += area; Grow(g, bx);
                // axial extent from box corners
                if (bx != null)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        double x = ((k & 1) == 0) ? bx[0] : bx[3];
                        double y = ((k & 2) == 0) ? bx[1] : bx[4];
                        double z = ((k & 4) == 0) ? bx[2] : bx[5];
                        double t = x * a[0] + y * a[1] + z * a[2];
                        if (t < g.TMin) g.TMin = t;
                        if (t > g.TMax) g.TMax = t;
                    }
                }
                double dot = CylDot(fc, q, a);
                if (double.IsNaN(g.Dot)) g.Dot = dot;
                else if (!double.IsNaN(dot)) g.Dot = (g.Dot + dot) * 0.5;
            }
            else if (su.IsCone())
            {
                nCone++;
                double[] cp = su.ConeParams as double[];
                coneInfo.Add("  face[" + fidx + "] CONE area=" + F(area * 1e6, 2) + "mm2 params=[" + string.Join(",", Array.ConvertAll(cp, x => F(x, 5))) + "]");
            }
            else if (su.IsTorus()) nTorus++;
            else if (su.IsSphere()) nSphere++;
            else nOther++;
        }

        L("");
        L("--- PLANES (" + planeList.Count + " groups) ---");
        planeList.Sort(delegate (Group x, Group y) { return y.Area.CompareTo(x.Area); });
        foreach (Group g in planeList)
        {
            if (g.Area * 1e6 < 0.5) continue;
            L("  n=(" + F(g.Vals[0], 3) + "," + F(g.Vals[1], 3) + "," + F(g.Vals[2], 3) + ")"
              + " d=" + F(g.Vals[3], 2).PadLeft(9)
              + "  area=" + F(g.Area * 1e6, 1).PadLeft(9) + " mm2  faces=" + g.Faces.Count
              + "  bbox x[" + MM(g.BMin[0], 2) + "," + MM(g.BMax[0], 2) + "] y[" + MM(g.BMin[1], 2) + "," + MM(g.BMax[1], 2)
              + "] z[" + MM(g.BMin[2], 2) + "," + MM(g.BMax[2], 2) + "]");
        }

        L("");
        L("--- CYLINDERS (" + cylList.Count + " groups) ---");
        cylList.Sort(delegate (Group x, Group y) { return x.Vals[6].CompareTo(y.Vals[6]); });
        foreach (Group g in cylList)
        {
            string kind = double.IsNaN(g.Dot) ? "?" : (g.Dot < -0.5 ? "HOLE" : (g.Dot > 0.5 ? "SHAFT/BOSS" : "mixed/partial"));
            L("  D=" + F(g.Vals[6] * 2000, 3).PadLeft(8) + " mm  axis=(" + F(g.Vals[3], 3) + "," + F(g.Vals[4], 3) + "," + F(g.Vals[5], 3) + ")"
              + "  t=" + F(g.TMin * 1000, 3) + ".." + F(g.TMax * 1000, 3)
              + "  area=" + F(g.Area * 1e6, 1).PadLeft(9) + "  faces=" + g.Faces.Count
              + "  nRad=" + F(g.Dot, 2) + "  " + kind);
        }

        if (coneInfo.Count > 0)
        {
            L("");
            L("--- CONES ---");
            foreach (string s in coneInfo) L(s);
        }
        L("");
        L("cones=" + nCone + "  tori=" + nTorus + "  spheres=" + nSphere + "  other=" + nOther);
    }

    /// <summary>Sign test: average dot of tessellation vertex normal with the radial
    /// direction. Negative => normals point toward the axis => internal surface (hole).</summary>
    static double CylDot(IFace2 fc, double[] q, double[] a)
    {
        try
        {
            float[] tri = fc.GetTessTriangles(false) as float[];
            float[] nrm = fc.GetTessNorms() as float[];
            if (tri == null || nrm == null) return double.NaN;
            int nt = tri.Length / 9;
            double sum = 0; int cnt = 0;
            for (int t = 0; t < nt; t++)
            {
                double cx = (tri[9 * t] + tri[9 * t + 3] + tri[9 * t + 6]) / 3.0;
                double cy = (tri[9 * t + 1] + tri[9 * t + 4] + tri[9 * t + 7]) / 3.0;
                double cz = (tri[9 * t + 2] + tri[9 * t + 5] + tri[9 * t + 8]) / 3.0;
                double tr = ((cx - q[0]) * a[0] + (cy - q[1]) * a[1] + (cz - q[2]) * a[2]);
                double[] rad = { cx - q[0] - tr * a[0], cy - q[1] - tr * a[1], cz - q[2] - tr * a[2] };
                double rl = Math.Sqrt(rad[0] * rad[0] + rad[1] * rad[1] + rad[2] * rad[2]);
                if (rl < 1e-9) continue;
                double nx = nrm[9 * t] + nrm[9 * t + 3] + nrm[9 * t + 6];
                double ny = nrm[9 * t + 1] + nrm[9 * t + 4] + nrm[9 * t + 7];
                double nz = nrm[9 * t + 2] + nrm[9 * t + 5] + nrm[9 * t + 8];
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-9) continue;
                sum += (rad[0] * nx + rad[1] * ny + rad[2] * nz) / (rl * nl);
                cnt++;
            }
            if (cnt == 0) return double.NaN;
            return sum / cnt;
        }
        catch { return double.NaN; }
    }

    static double[] Norm(double x, double y, double z)
    {
        double m = Math.Sqrt(x * x + y * y + z * z);
        if (m < 1e-12) return new double[] { 0, 0, 0 };
        x /= m; y /= m; z /= m;
        // canonical sign: first non-zero positive
        double[] r = new double[] { x, y, z };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(r[i]) > 1e-6)
            {
                if (r[i] < 0) { r[0] = -r[0]; r[1] = -r[1]; r[2] = -r[2]; }
                break;
            }
        }
        return r;
    }

    // ---------------- tessellation ----------------
    static void DumpTess()
    {
        IPartDoc pd = model as IPartDoc;
        object[] bodies = pd.GetBodies2(0, false) as object[];
        if (bodies == null || bodies.Length == 0) { L("tess: no bodies"); return; }
        string fp = Path.Combine(OutDir, Path.GetFileNameWithoutExtension(model.GetPathName()) + ".tess.txt");
        StringBuilder sb = new StringBuilder();
        int total = 0;
        foreach (object o in bodies)
        {
            IBody2 b = o as IBody2;
            if (b == null) continue;
            object[] faces = b.GetFaces() as object[];
            int fi = 0;
            foreach (object fo in faces)
            {
                fi++;
                IFace2 fc = fo as IFace2;
                if (fc == null) continue;
                float[] tri = null; float[] nrm = null;
                try { tri = fc.GetTessTriangles(false) as float[]; } catch { }
                try { nrm = fc.GetTessNorms() as float[]; } catch { }
                if (tri == null) continue;
                int nt = tri.Length / 9;
                for (int t = 0; t < nt; t++)
                {
                    sb.Append(fi).Append('|');
                    sb.Append(F(tri[9*t]*1000,4)).Append(',').Append(F(tri[9*t+1]*1000,4)).Append(',').Append(F(tri[9*t+2]*1000,4)).Append('|');
                    sb.Append(F(tri[9*t+3]*1000,4)).Append(',').Append(F(tri[9*t+4]*1000,4)).Append(',').Append(F(tri[9*t+5]*1000,4)).Append('|');
                    sb.Append(F(tri[9*t+6]*1000,4)).Append(',').Append(F(tri[9*t+7]*1000,4)).Append(',').Append(F(tri[9*t+8]*1000,4)).Append('|');
                    if (nrm != null && nrm.Length >= 9*t+9)
                        sb.Append(F((nrm[9*t]+nrm[9*t+3]+nrm[9*t+6])/3.0,5)).Append(',').Append(F((nrm[9*t+1]+nrm[9*t+4]+nrm[9*t+7])/3.0,5)).Append(',').Append(F((nrm[9*t+2]+nrm[9*t+5]+nrm[9*t+8])/3.0,5));
                    else sb.Append("0,0,0");
                    sb.AppendLine();
                    total++;
                }
            }
        }
        File.WriteAllText(fp, sb.ToString());
        L("tess -> " + fp + "   triangles = " + total);
    }

    // ---------------- views ----------------
    static string OutDir;
    static void Views()
    {
        string dir = OutDir;
        string baseName = Path.GetFileNameWithoutExtension(model.GetPathName());
        string[][] v = {
            new string[]{"*Isometric","7","iso"},
            new string[]{"*Front","1","front"},
            new string[]{"*Right","4","right"},
            new string[]{"*Top","5","top"},
            new string[]{"*Bottom","6","bottom"},
            new string[]{"*Back","2","back"},
            new string[]{"*Left","3","left"},
        };
        L("");
        foreach (string[] it in v)
        {
            try
            {
                model.ShowNamedView2(it[0], int.Parse(it[1]));
                model.ViewZoomtofit2();
                model.ViewDisplayShaded();
                string p = Path.Combine(dir, baseName + "." + it[2] + ".bmp");
                if (File.Exists(p)) File.Delete(p);
                bool ok = model.SaveBMP(p, 1600, 1200);
                L("  view " + it[2] + " -> " + (ok ? p : "FAILED"));
            }
            catch (Exception e) { L("  view " + it[2] + " !! " + e.Message); }
        }
    }
}
