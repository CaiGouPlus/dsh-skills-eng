using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Read an assembly's top-level component list (read-only, silent open).
// Used to find where 5015 seat sits and what it is mated with.
public static class FindCmp
{
    static StringBuilder L;
    static void Log(string s) { L.AppendLine(s); }

    public static string Run(string asmPath, string outFile)
    {
        L = new StringBuilder();
        bool created = false;
        ISldWorks sw = SwKit.Acquire(out created, true);
        IModelDoc2 doc = null;
        try
        {
            Log("sw rev = " + sw.RevisionNumber());
            Log("created = " + created);
            int err = 0, warn = 0;
            doc = sw.OpenDoc6(asmPath,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "", ref err, ref warn) as IModelDoc2;
            Log("open err=" + err + " warn=" + warn + " doc=" + (doc == null ? "NULL" : "ok"));
            if (doc == null) return L.ToString();

            IAssemblyDoc ad = doc as IAssemblyDoc;
            if (ad == null) { Log("not an assembly"); return L.ToString(); }

            try { ad.ResolveAllLightWeightComponents(false); Log("resolved lightweight"); }
            catch (Exception e) { Log("resolve failed: " + e.Message); }

            object[] comps = ad.GetComponents(false) as object[];
            Log("top-level components = " + (comps == null ? 0 : comps.Length));
            if (comps == null) return L.ToString();

            int i = 0;
            foreach (object o in comps)
            {
                IComponent2 c = o as IComponent2;
                if (c == null) { Log("[" + i + "] <not a component>"); i++; continue; }
                string nm = "";
                string path = "";
                try { nm = c.Name2; } catch { }
                try { path = c.GetPathName(); } catch { }
                bool supp = false;
                try { supp = (c.GetSuppression() != 0); } catch { }
                string s = "[" + (i++) + "] " + nm + "   supp=" + supp + "\n      path=" + path;
                try
                {
                    IMathTransform xf = c.Transform2 as IMathTransform;
                    if (xf != null)
                    {
                        double[] a = xf.ArrayData as double[];
                        s += "\n      rot=" + F(a, 0, 3) + " | " + F(a, 3, 3) + " | " + F(a, 6, 3)
                           + "  t=" + F(a, 9, 3);
                    }
                }
                catch { }
                Log(s);
            }
        }
        catch (Exception e)
        {
            Log("EXCEPTION: " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            try { if (doc != null) sw.CloseDoc(doc.GetTitle()); } catch { }
            try { File.WriteAllText(outFile, L.ToString(), new UTF8Encoding(true)); } catch { }
        }
        return L.ToString();
    }

    static string F(double[] a, int from, int n)
    {
        if (a == null) return "?";
        StringBuilder b = new StringBuilder("(");
        for (int k = 0; k < n; k++)
        {
            if (from + k >= a.Length) { b.Append("?"); break; }
            if (k > 0) b.Append(",");
            b.Append((a[from + k] * 1000.0).ToString("F2"));
        }
        b.Append(")");
        return b.ToString();
    }
}
