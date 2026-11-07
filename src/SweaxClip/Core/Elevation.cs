using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SweaxClip.Core;

/// <summary>
/// "Work over administrator windows". Windows keeps the keys typed into an administrator window (Task Manager, an
/// installer, an elevated terminal) away from ordinary apps, so SweaxClip only sees Win+V there when it has
/// administrator rights itself. A scheduled task that starts it at sign-in "with highest privileges" gives it those
/// rights without asking each time: Windows asks once, when the task is made.
/// </summary>
public static class Elevation
{
    public const string TaskName = "SweaxClip";

    /// <summary>This copy runs with administrator rights.</summary>
    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SweaxClip.exe");

    public static bool TaskExists() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>
    /// Registers the sign-in task, with administrator rights or without (used once, to hand back to an ordinary copy).
    /// Windows asks for permission unless this copy already has administrator rights. False if the user said no.
    /// </summary>
    public static async Task<bool> RegisterTask(bool admin)
    {
        string xml = Path.Combine(Path.GetTempPath(), $"sweaxclip-task-{Environment.ProcessId}.xml");
        File.WriteAllText(xml, TaskXml(admin), Encoding.Unicode);
        try
        {
            string args = $"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F";
            if (IsElevated || !admin) return await Task.Run(() => Schtasks(args)) == 0;
            var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch (Win32Exception) { return false; } // the permission prompt was declined
        catch (Exception ex) { Paths.Log("task: " + ex.Message); return false; }
        finally { try { File.Delete(xml); } catch { } }
    }

    /// <summary>Starts the task now: a copy of SweaxClip with the task's rights, which takes over from this one.</summary>
    public static bool RunTask() => Schtasks($"/Run /TN \"{TaskName}\"") == 0;

    public static bool DeleteTask() => Schtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

    static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            return p.WaitForExit(8000) ? p.ExitCode : -1;
        }
        catch (Exception ex) { Paths.Log("schtasks: " + ex.Message); return -1; }
    }

    /// <summary>
    /// The task: at this user's sign-in, start SweaxClip in the background and let it take over from any copy already
    /// running. Unlike schtasks' defaults it is never stopped after 72 hours and runs at normal priority (Task Scheduler
    /// otherwise starts programs below normal), and it runs on battery too.
    /// </summary>
    internal static string TaskXml(bool admin)
    {
        string user = WindowsIdentity.GetCurrent().User!.Value;
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>SweaxClip clipboard history. Started at sign-in{(admin ? " with administrator rights, so Win+V also works over administrator windows" : "")}.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>{(admin ? "HighestAvailable" : "LeastPrivilege")}</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Exe)}</Command><Arguments>--background --replace</Arguments></Exec></Actions>
            </Task>
            """;
    }
}

/// <summary>
/// The named mutex and events that keep SweaxClip to one copy and let a second launch (--toggle, --grab, --quit, a
/// plain start) reach the running one. They are created open to this user whatever the rights of either copy: by
/// default an object made by an administrator copy refuses an ordinary one, and the other way round.
/// </summary>
public static class Shared
{
    const uint ERROR_ALREADY_EXISTS = 183;

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr sd, out uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateMutex(ref SECURITY_ATTRIBUTES sa, bool initialOwner, string name);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateEvent(ref SECURITY_ATTRIBUTES sa, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr mem);

    /// <summary>Full access for the system, administrators and this user; medium integrity, so ordinary copies may signal it.</summary>
    static string Sddl => $"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{WindowsIdentity.GetCurrent().User!.Value})S:(ML;;NW;;;ME)";

    sealed class Native : WaitHandle
    {
        public Native(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    [DllImport("kernel32.dll")]
    static extern bool ReleaseMutex(SafeWaitHandle mutex);

    /// <summary>Gives up the single-instance mutex (before handing over to another copy).</summary>
    public static void Release(WaitHandle mutex)
    {
        try
        {
            if (mutex is System.Threading.Mutex m) m.ReleaseMutex();
            else ReleaseMutex(mutex.SafeWaitHandle);
        }
        catch { }
        mutex.Dispose();
    }

    /// <summary>
    /// Asks the running copy to quit and waits for its mutex, so this copy can take its place (a copy started with or
    /// without administrator rights replacing the other kind).
    /// </summary>
    public static bool TakeOver(WaitHandle mutex, string quitEvent)
    {
        try { EventWaitHandle.OpenExisting(quitEvent).Set(); } catch { return false; }
        try { return mutex.WaitOne(8000); }
        catch (AbandonedMutexException) { return true; } // it exited without letting go: the mutex is ours now
    }

    /// <summary>The single-instance mutex; <paramref name="first"/> when this copy made it (and owns it).</summary>
    public static WaitHandle? Mutex(string name, out bool first) => Create(name, true, out first);

    /// <summary>An auto-reset event others set with EventWaitHandle.OpenExisting(name).Set().</summary>
    public static WaitHandle Event(string name) =>
        Create(name, false, out _) ?? new EventWaitHandle(false, EventResetMode.AutoReset, name);

    static WaitHandle? Create(string name, bool mutex, out bool first)
    {
        first = false;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl, 1, out IntPtr sd, out _))
            return mutex ? Fallback(name, out first) : null;
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            IntPtr h = mutex ? CreateMutex(ref sa, true, name) : CreateEvent(ref sa, false, false, name);
            if (h == IntPtr.Zero) return mutex ? Fallback(name, out first) : null;
            first = Marshal.GetLastWin32Error() != ERROR_ALREADY_EXISTS;
            return new Native(h);
        }
        finally { LocalFree(sd); }
    }

    /// <summary>An older copy made the mutex with the default (stricter) access: it exists, so this is not the first copy.</summary>
    static WaitHandle? Fallback(string name, out bool first)
    {
        try { var m = new System.Threading.Mutex(true, name, out first); return m; }
        catch (UnauthorizedAccessException) { first = false; return null; }
    }
}
