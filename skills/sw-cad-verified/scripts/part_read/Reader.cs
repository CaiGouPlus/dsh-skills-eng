using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

/// <summary>
/// Read a part: feature tree, sketches, bodies, cylinders, mass props, views.
/// ASCII-only file (PowerShell 5.1 reads .cs as GBK without BOM).
/// </summary>
public static class Reader
{
    static StringBuilder SB = new StringBuilder();
    static string LogPath;
    static string PartPath;

    public static string Run(string partPath)
    {
        PartPath = partPath;
        LogPath = Path.ChangeExtension(partPath, ".read.log");
        if (File.Exists(LogPath)) File.Delete(LogPath);
        IModelDoc2 model = null;
        SldWorks sw = null;
        try
        {
            sw = new SldWorks();
            sw.Visible = true;
            L("revision = " + sw.RevisionNumber());
            int err = 0, warn = 0;
            model = sw.OpenDoc6(partPath, 1, 1, "", ref err, ref warn) as IModelDoc2;
            L("open err=" + err + " warn=" + warn);
            if (model == null) { L("!! open failed"); return SB.ToString(); }
            L("title = " + model.GetTitle());
            L("path  = " + model.GetPathName());
            L("doctype = " + model.GetType());

            Safe("configs", delegate { Configs(model); });
            Safe("props", delegate { Props(model); });
            Safe("tree", delegate { Tree(model); });
            Safe("detail", delegate { Detail(model); });
            Safe("bodies", delegate { Bodies(model); });
            Safe("mass", delegate { Mass(model); });
            Safe("views", delegate { Views(model); });
            Safe("close", delegate { sw.CloseDoc(model.GetTitle()); });
        }
        catch (Exception e) { L("!! " + e.Message); }
        finally
        {
            try { if (sw != null) sw.ExitApp(); } catch { }
        }
        return SB.ToString();
    }

    static void Safe(string tag, Action a)
    {
        try { a(); }
        catch (Exception e) { L("!! " + tag + " : " + e.Message); }
    }

    static void Configs(IModelDoc2 model)
    {
        object o = model.GetConfigurationNames();
        object[] arr = o as object[];
        string s = "";
        if (arr != null) foreach (object x in arr) s += (s == "" ? "" : " | ") + x;
        L("configs = " + s);
    }

    static void Props(IModelDoc2 model)
    {
        ICustomPropertyManager cpm = model.Extension.get_CustomPropertyManager("") as ICustomPropertyManager;
        if (cpm == null) { L("props = (no manager)"); return; }
        object[] names = cpm.GetNames() as object[];
        if (names == null || names.Length == 0) { L("props = (none)"); return; }
        foreach (object n in names)
        {
            string nm = n.ToString();
            string val = "", res = ""; bool was = false;
            try { cpm.Get5(nm, false, out val, out res, out was); } catch { }
            L("prop  " + nm + " = " + val + (was ? "  [resolved: " + res + "]" : ""));
        }
    }

    static void Tree(IModelDoc2 model)
    {
        Feature f = model.FirstFeature() as Feature;
        int n = 0;
        while (f != null)
        {
            Walk(f, 0, ref n);
            f = f.GetNextFeature() as Feature;
        }
        L("features = " + n);
    }

    static void Walk(Feature f, int depth, ref int n)
    {
        n++;
        string ind = new string(' ', depth * 2);
        string tn = "?"; try { tn = f.GetTypeName2(); } catch { }
        string nm = f.Name;
        bool sup = false; try { sup = f.IsSuppressed(); } catch { }
        L("F" + n.ToString("00") + ind + "[" + tn + "] " + nm + (sup ? "  (SUPPRESSED)" : ""));

        ISketch sk = null;
        try { sk = f.GetSpecificFeature2() as ISketch; } catch { }
        if (sk != null) SketchSum(sk, ind + "  ");

        Feature sub = null;
        try { sub = f.GetFirstSubFeature() as Feature; } catch { }
        while (sub != null)
        {
            Walk(sub, depth + 1, ref n);
            sub = sub.GetNextSubFeature() as Feature;
        }
    }

    static void SketchSum(ISketch sk, string ind)
    {
        object[] segs = sk.GetSketchSegments() as object[];
        int lines = 0, arcs = 0, splines = 0, cons = 0, other = 0;
        if (segs != null)
            foreach (object o in segs)
            {
                ISketchSegment sg = o as ISketchSegment;
                if (sg == null) continue;
                bool c = false; try { c = sg.ConstructionGeometry; } catch { }
                if (c) cons++;
                int t = 0; try { t = sg.GetType(); } catch { }
                if (t == 0) lines++; else if (t == 1) arcs++; else if (t == 3) splines++; else other++;
            }
        object[] cs = sk.GetSketchContours() as object[];
        int cnum = cs == null ? 0 : cs.Length;
        int closed = 0;
        if (cs != null) foreach (object o in cs) { ISketchContour sc = o as ISketchContour; if (sc != null && sc.IsClosed()) closed++; }
        int st = -1; try { st = sk.GetConstrainedStatus(); } catch { }
        L(ind + "sketch: segs=" + (segs == null ? 0 : segs.Length) + " line=" + lines + " arc=" + arcs + " spline=" + splines + " other=" + other + " construction=" + cons + " contours=" + cnum + " closed=" + closed + " status=" + st);
    }

    static void Detail(IModelDoc2 model)
    {
        Feature f = model.FirstFeature() as Feature;
        while (f != null)
        {
            string tn = ""; try { tn = f.GetTypeName2(); } catch { }
            ISketch sk = null; try { sk = f.GetSpecificFeature2() as ISketch; } catch { }
            if (sk != null && tn != "OriginProfileFeature") DumpSketch(f.Name, sk);
            if (tn == "Extrusion" || tn == "ICE" || tn == "Cut")
            {
                try
                {
                    IExtrudeFeatureData2 ed = f.GetDefinition() as IExtrudeFeatureData2;
                    if (ed != null)
                    {
                        int ec1 = ed.GetEndCondition(true);
                        int ec2 = ed.GetEndCondition(false);
                        L("  " + f.Name + " : thin=" + ed.IsThinFeature()
                            + " ec1=" + ec1 + "(" + EndName(ec1) + ")"
                            + " ec2=" + ec2 + "(" + EndName(ec2) + ")"
                            + " depth1=" + (ed.GetDepth(true) * 1000).ToString("F2") + "mm"
                            + " depth2=" + (ed.GetDepth(false) * 1000).ToString("F2") + "mm"
                            + " reverse=" + ed.ReverseDirection + " both=" + ed.BothDirections);
                    }
                    else L("  " + f.Name + " : definition is not IExtrudeFeatureData2");
                }
                catch (Exception e) { L("  " + f.Name + " : " + e.Message); }
            }
            f = f.GetNextFeature() as Feature;
        }
    }

    static void DumpSketch(string name, ISketch sk)
    {
        object[] segs = sk.GetSketchSegments() as object[];
        if (segs == null) return;
        L("  =sketch " + name + " segments=" + segs.Length);
        try
        {
            MathTransform mt = sk.ModelToSketchTransform as MathTransform;
            if (mt != null)
            {
                double[] tr = mt.ArrayData as double[];
                if (tr != null) L("   plane transform = [" + string.Join(", ", Array.ConvertAll(tr, d => d.ToString("F6"))) + "]");
            }
        }
        catch { }
        int i = 0;
        foreach (object o in segs)
        {
            i++;
            if (i > 40) { L("  ..."); break; }
            ISketchSegment sg = o as ISketchSegment;
            if (sg == null) continue;
            int t = 0; try { t = sg.GetType(); } catch { }
            bool c = false; try { c = sg.ConstructionGeometry; } catch { }
            string s = "   seg" + i + (c ? " [constr]" : "") + " type=" + t;
            if (t == 0)
            {
                try
                {
                    ISketchLine ln = sg as ISketchLine;
                    if (ln != null)
                    {
                        ISketchPoint p1 = ln.GetStartPoint2() as ISketchPoint;
                        ISketchPoint p2 = ln.GetEndPoint2() as ISketchPoint;
                        if (p1 != null && p2 != null)
                            s += string.Format(" ({0:F2},{1:F2}) -> ({2:F2},{3:F2})", p1.X * 1000, p1.Y * 1000, p2.X * 1000, p2.Y * 1000);
                    }
                }
                catch { }
            }
            if (t == 1)
            {
                try
                {
                    ISketchArc ar = sg as ISketchArc;
                    if (ar != null)
                    {
                        ISketchPoint p1 = ar.GetStartPoint2() as ISketchPoint;
                        ISketchPoint p2 = ar.GetEndPoint2() as ISketchPoint;
                        ISketchPoint ctr = ar.GetCenterPoint2() as ISketchPoint;
                        if (p1 != null && p2 != null)
                            s += string.Format(" ({0:F2},{1:F2}) -> ({2:F2},{3:F2})", p1.X * 1000, p1.Y * 1000, p2.X * 1000, p2.Y * 1000);
                        s += string.Format(" R={0:F2}", ar.GetRadius() * 1000);
                        if (ctr != null) s += string.Format(" C=({0:F2},{1:F2})", ctr.X * 1000, ctr.Y * 1000);
                    }
                }
                catch { }
            }
            L(s);
        }
    }

    static void Bodies(IModelDoc2 model)
    {
        IPartDoc pd = model as IPartDoc;
        object[] bodies = null;
        try { bodies = pd.GetBodies2(0, false) as object[]; } catch { }
        if (bodies == null || bodies.Length == 0) { try { bodies = pd.GetBodies2(1, false) as object[]; } catch { } }
        L("bodies = " + (bodies == null ? -1 : bodies.Length));
        if (bodies == null) return;
        int bi = 0;
        foreach (object o in bodies)
        {
            IBody2 b = o as IBody2;
            if (b == null) continue;
            bi++;
            int fc = -1, ec = -1, vc = -1;
            try { fc = b.GetFaceCount(); } catch { }
            try { ec = b.GetEdgeCount(); } catch { }
            try { vc = b.GetVertexCount(); } catch { }
            Dictionary<double, int> cyl = new Dictionary<double, int>();
            Dictionary<string, int> surf = new Dictionary<string, int>();
            List<string> cylLines = new List<string>();
            object[] faces = null;
            try { faces = b.GetFaces() as object[]; } catch { }
            if (faces != null)
                foreach (object fo in faces)
                {
                    IFace2 face = fo as IFace2;
                    if (face == null) continue;
                    ISurface su = null; try { su = face.GetSurface() as ISurface; } catch { }
                    if (su == null) continue;
                    string kind = "?";
                    try
                    {
                        if (su.IsCylinder()) { kind = "cylinder"; double[] cp = su.CylinderParams as double[]; if (cp != null) { double r = Math.Round(cp[6] * 1000.0, 1); if (!cyl.ContainsKey(r)) cyl[r] = 0; cyl[r]++; double[] fb = null; try { fb = face.GetBox() as double[]; } catch { } string bb = fb != null ? string.Format(" box x[{0:F1},{1:F1}] y[{2:F1},{3:F1}] z[{4:F1},{5:F1}]", fb[0] * 1000, fb[3] * 1000, fb[1] * 1000, fb[4] * 1000, fb[2] * 1000, fb[5] * 1000) : ""; cylLines.Add(string.Format("    cyl R{0:F2} at ({1:F2},{2:F2},{3:F2}) dir ({4:F2},{5:F2},{6:F2}){7}", cp[6] * 1000, cp[0] * 1000, cp[1] * 1000, cp[2] * 1000, cp[3], cp[4], cp[5], bb)); } }
                        else if (su.IsPlane()) kind = "plane";
                        else if (su.IsCone()) kind = "cone";
                        else if (su.IsSphere()) kind = "sphere";
                        else if (su.IsTorus()) kind = "torus";
                        else kind = "freeform";
                    }
                    catch { }
                    if (!surf.ContainsKey(kind)) surf[kind] = 0;
                    surf[kind]++;
                }
            L("body " + bi + " faces=" + fc + " edges=" + ec + " verts=" + vc);
            string ss = "";
            foreach (KeyValuePair<string, int> kv in surf) ss += (ss == "" ? "" : ", ") + kv.Key + ":" + kv.Value;
            L("  surfaces: " + ss);
            if (cyl.Count > 0)
            {
                string cs = "";
                foreach (KeyValuePair<double, int> kv in cyl) cs += (cs == "" ? "" : ", ") + "R" + kv.Key.ToString("F1") + "mm x" + kv.Value;
                L("  cylinders: " + cs);
                foreach (string cl in cylLines) L(cl);
            }
        }
    }

    static void Mass(IModelDoc2 model)
    {
        IPartDoc pd = model as IPartDoc;
        double[] b = pd.GetPartBox(true) as double[];
        if (b != null)
            L(string.Format("partbox = {0:F2} x {1:F2} x {2:F2} mm  (x:{3:F2}..{4:F2} y:{5:F2}..{6:F2} z:{7:F2}..{8:F2})",
                (b[3] - b[0]) * 1000, (b[4] - b[1]) * 1000, (b[5] - b[2]) * 1000,
                b[0] * 1000, b[3] * 1000, b[1] * 1000, b[4] * 1000, b[2] * 1000, b[5] * 1000));
        int st = 0;
        double[] mp = model.Extension.GetMassProperties2(1, out st, false) as double[];
        if (mp != null && mp.Length >= 4)
            L(string.Format("volume = {0:F2} mm3   com = ({1:F2}, {2:F2}, {3:F2}) mm", mp[3] * 1e9, mp[0] * 1000, mp[1] * 1000, mp[2] * 1000));
    }

    static void Views(IModelDoc2 model)
    {
        string dir = Path.GetDirectoryName(PartPath);
        try { model.ShowNamedView2("*Isometric", 7); model.ViewZoomtofit2(); model.ViewDisplayShaded(); model.SaveBMP(Path.Combine(dir, "iso.bmp"), 1400, 1000); } catch (Exception e) { L("!! iso view: " + e.Message); }
        try { model.ShowNamedView2("*Front", 1); model.ViewZoomtofit2(); model.ViewDisplayShaded(); model.SaveBMP(Path.Combine(dir, "front.bmp"), 1400, 1000); } catch (Exception e) { L("!! front view: " + e.Message); }
        try { model.ShowNamedView2("*Top", 5); model.ViewZoomtofit2(); model.ViewDisplayShaded(); model.SaveBMP(Path.Combine(dir, "top.bmp"), 1400, 1000); } catch (Exception e) { L("!! top view: " + e.Message); }
    }

    static string EndName(int v)
    {
        switch (v)
        {
            case 0: return "Blind";
            case 1: return "ThroughAll";
            case 2: return "ThroughNext";
            case 3: return "UpToVertex";
            case 4: return "UpToSurface";
            case 5: return "OffsetFromSurface";
            case 6: return "MidPlane";
            case 7: return "UpToBody";
            case 9: return "ThroughAllBoth";
            case 10: return "UpToSelection";
            case 11: return "UpToNext";
            default: return "?";
        }
    }

    static void L(string s)
    {
        SB.AppendLine(s);
        try { File.AppendAllText(LogPath, s + System.Environment.NewLine, new UTF8Encoding(true)); } catch { }
    }
}
