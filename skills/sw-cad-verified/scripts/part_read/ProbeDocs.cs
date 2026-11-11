using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Read-only: list documents already loaded in the running SolidWorks instance
// and dump mass/bbox for any part whose title matches a filter. Never opens,
// saves, closes or modifies anything.
public static class ProbeDocs
{
    static StringBuilder L;
    static void Log(string s) { L.AppendLine(s); }

    public static string Run(string filter, string outFile)
    {
        L = new StringBuilder();
        bool created = false;
        ISldWorks sw = SwKit.Acquire(out created, false);
        try
        {
            Log("created = " + created + "  (must be False: we must not own the user's instance)");
            object[] docs = sw.GetDocuments() as object[];
            Log("open documents = " + (docs == null ? 0 : docs.Length));
            if (docs == null) return L.ToString();

            foreach (object o in docs)
            {
                IModelDoc2 d = o as IModelDoc2;
                if (d == null) continue;
                string title = "";
                string path = "";
                int type = -1;
                bool dirty = false;
                try { title = d.GetTitle(); } catch { }
                try { path = d.GetPathName(); } catch { }
                try { type = d.GetType(); } catch { }
                try { dirty = d.GetSaveFlag(); } catch { }
                Log("--- " + title + "  type=" + type + " dirty=" + dirty + "\n    path=" + path);

                if (filter.Length > 0 && title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                try
                {
                    IPartDoc pd = d as IPartDoc;
                    if (pd == null) { Log("    (not a part)"); continue; }
                    double[] pb = pd.GetPartBox(true) as double[];
                    Log("    box mm  x[" + pb[0] * 1000 + "," + pb[3] * 1000 + "]"
                        + " y[" + pb[1] * 1000 + "," + pb[4] * 1000 + "]"
                        + " z[" + pb[2] * 1000 + "," + pb[5] * 1000 + "]");
                    int st = 0;
                    double[] mp = d.Extension.GetMassProperties2(1, out st, false) as double[];
                    if (mp != null)
                        Log("    vol=" + (mp[3] * 1e9).ToString("F2") + " mm3  area=" + (mp[4] * 1e6).ToString("F2")
                            + " mm2  mass=" + (mp[5] * 1000).ToString("F3") + " g");
                    object[] bodies = pd.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
                    Log("    bodies=" + (bodies == null ? 0 : bodies.Length));
                    if (bodies != null && bodies.Length > 0)
                    {
                        IBody2 b = bodies[0] as IBody2;
                        Log("    faces=" + b.GetFaceCount() + " edges=" + b.GetEdgeCount() + " verts=" + b.GetVertexCount());
                        object[] faces = b.GetFaces() as object[];
                        if (faces != null)
                        {
                            int nCyl = 0, nPlane = 0;
                            StringBuilder cy = new StringBuilder();
                            foreach (object fo in faces)
                            {
                                IFace2 fc = fo as IFace2;
                                if (fc == null) continue;
                                ISurface su = fc.GetSurface() as ISurface;
                                if (su == null) continue;
                                if (su.IsCylinder())
                                {
                                    nCyl++;
                                    double[] cp = su.CylinderParams as double[];
                                    cy.Append("      cyl R=" + (cp[6] * 1000).ToString("F2")
                                        + " a=(" + (cp[3] * 1000).ToString("F2") + "," + (cp[4] * 1000).ToString("F2") + "," + (cp[5] * 1000).ToString("F2")
                                        + ") p=(" + (cp[0] * 1000).ToString("F2") + "," + (cp[1] * 1000).ToString("F2") + "," + (cp[2] * 1000).ToString("F2") + ")\n");
                                }
                                else if (su.IsPlane()) nPlane++;
                            }
                            Log("    cyl=" + nCyl + " plane=" + nPlane);
                            if (cy.Length > 0 && cy.Length < 6000) Log(cy.ToString());
                        }
                    }
                }
                catch (Exception e) { Log("    read failed: " + e.Message); }
            }
        }
        catch (Exception e) { Log("EXCEPTION: " + e.Message); }
        finally
        {
            try { File.WriteAllText(outFile, L.ToString(), new UTF8Encoding(true)); } catch { }
        }
        return L.ToString();
    }
}
