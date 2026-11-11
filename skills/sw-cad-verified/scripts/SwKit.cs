// SwKit.cs - SolidWorks instance lifecycle helper (2026-11-10).
//
// WHY THIS EXISTS
//   `new SldWorks()` does NOT tell you whether it created a new instance or
//   attached to the instance the user is already working in. Scripts that did
//   `sw.Visible = false` hid the user's own window, and scripts that called
//   `sw.ExitApp()` in a finally block closed the user's session.
//   When the outer PowerShell was killed by a timeout, the finally never ran
//   and a hidden instance was orphaned, squatting on the single-instance lock:
//   afterwards every double-click opened the file into an invisible window.
//
// RULE
//   - Probe the process list BEFORE touching COM. If SLDWORKS.exe was already
//     running, treat it as "not ours": never change Visible, never ExitApp.
//   - Only an instance this process created may be closed.
//
// USAGE
//   bool created;
//   ISldWorks sw = SwKit.Acquire(out created);        // visibleIfCreated: false by default
//   try { ... } finally { SwKit.Release(sw, created); }
//
// Compile with:
//   Add-Type -Path '<install>\SolidWorks.Interop.sldworks.dll'
//   Add-Type -Path 'SwKit.cs' -ReferencedAssemblies $pia

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

public static class SwKit
{
    /// <summary>True if at least one SLDWORKS.exe was already running.</summary>
    public static bool InstanceAlreadyRunning()
    {
        try { return Process.GetProcessesByName("SLDWORKS").Length > 0; }
        catch { return true; }   // on doubt, assume yes: safer (we then never exit it)
    }

    /// <summary>
    /// Attach to a running instance when possible, otherwise create one.
    /// created == true  => we own it, caller MUST call Release().
    /// created == false => it was already there, leave Visible/ExitApp alone.
    /// </summary>
    public static ISldWorks Acquire(out bool created, bool visibleIfCreated = false)
    {
        created = false;

        bool existed = InstanceAlreadyRunning();
        if (existed)
        {
            // Somebody else's instance: attach, never mutate it.
            try
            {
                object o = Marshal.GetActiveObject("SldWorks.Application");
                if (o != null) return (ISldWorks)o;
            }
            catch { }
            // Not in the ROT. `new SldWorks()` still connects to the singleton.
            // Mark as NOT created so Release() never closes the user's session.
            return new SldWorks();
        }

        // Nothing was running: this one is ours.
        ISldWorks sw = new SldWorks();
        created = true;
        try { sw.Visible = visibleIfCreated; } catch { }
        return sw;
    }

    /// <summary>
    /// Close the app only if this process created it.
    /// closeDocs=true is only safe for instances we own (unsaved user work is
    /// never involved because we created a fresh session).
    /// </summary>
    public static void Release(ISldWorks sw, bool created, bool closeDocs = true)
    {
        if (sw == null || !created) return;
        try { if (closeDocs) sw.CloseAllDocuments(false); } catch { }
        try { sw.ExitApp(); } catch { }
    }
}
