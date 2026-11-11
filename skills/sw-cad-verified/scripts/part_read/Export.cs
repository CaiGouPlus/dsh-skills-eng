using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;

public static class Export
{
    public static string Run(string path)
    {
        StringBuilder sb = new StringBuilder();
        SldWorks sw = null;
        try
        {
            sw = new SldWorks();
            sw.Visible = true;
            int err = 0, warn = 0;
            IModelDoc2 model = sw.OpenDoc6(path, 1, 1, "", ref err, ref warn) as IModelDoc2;
            if (model == null) { sb.AppendLine("open failed"); return sb.ToString(); }
            string stl = Path.ChangeExtension(path, ".stl");
            int e = 0, w = 0;
            bool ok = model.Extension.SaveAs(stl, 0, 1, null, ref e, ref w);
            sb.AppendLine("saveas stl ok=" + ok + " e=" + e + " w=" + w + " exists=" + File.Exists(stl) + " size=" + (File.Exists(stl) ? new FileInfo(stl).Length : -1));
            sw.CloseDoc(model.GetTitle());
        }
        catch (Exception ex) { sb.AppendLine("!! " + ex.Message); }
        finally { try { if (sw != null) sw.ExitApp(); } catch { } }
        return sb.ToString();
    }
}
