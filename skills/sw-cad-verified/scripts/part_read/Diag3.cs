using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

public static class Diag3
{
    public static string Run(string path)
    {
        var sb = new System.Text.StringBuilder();
        SldWorks sw = null;
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("SLDWORKS").Length > 0)
            {
                System.Console.Error.WriteLine("REFUSED: SLDWORKS.exe already running: this legacy dumper would take over that instance and close it on exit. Close SolidWorks first.");
                System.Environment.Exit(42);
            }
            sw = new SldWorks(); sw.Visible = false;
            int err=0, warn=0;
            IModelDoc2 m = sw.OpenDoc6(path,1,1,"",ref err,ref warn) as IModelDoc2;
            if (m==null) { sb.AppendLine("openfail "+err); return sb.ToString(); }
            IPartDoc pd = m as IPartDoc;
            object[] bodies = pd.GetBodies2(0,false) as object[];
            sb.AppendLine("bodies=" + (bodies==null?-1:bodies.Length));
            int bi=0;
            if (bodies!=null) foreach(object o in bodies)
            {
                IBody2 b = o as IBody2; if(b==null) continue; bi++;
                int fc=b.GetFaceCount(), ec=b.GetEdgeCount(), vc=b.GetVertexCount();
                object[] faces=b.GetFaces() as object[];
                sb.AppendLine(string.Format("body{0}: GetFaceCount={1} enumFaces={2} GetEdgeCount={3} GetVertexCount={4}", bi, fc, faces==null?-1:faces.Length, ec, vc));
                // unique edges via face edges
                var set = new HashSet<IntPtr>();
                if (faces!=null) foreach(object fo in faces)
                {
                    IFace2 f=fo as IFace2; if(f==null) continue;
                    object[] es=f.GetEdges() as object[];
                    if (es!=null) foreach(object eo in es) set.Add(Marshal.GetIUnknownForObject(eo));
                }
                sb.AppendLine("unique edges via faces=" + set.Count);
                // per-face edge counts (first 10)
                if (faces!=null) for(int i=0;i<Math.Min(10,faces.Length);i++)
                {
                    IFace2 f=faces[i] as IFace2;
                    object[] es=f.GetEdges() as object[];
                    sb.AppendLine(string.Format("  face{0} edges={1}", i+1, es==null?0:es.Length));
                }
            }
            sw.CloseDoc(m.GetTitle());
        }
        catch(Exception e){ sb.AppendLine("ERR "+e.Message); }
        finally { try{ if(sw!=null) sw.ExitApp(); }catch{} }
        return sb.ToString();
    }
}
