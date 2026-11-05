// Program.cs
// Two roles in one executable:
//   * Widget provider  — when the Widgets host COM-activates us (launched with
//     "-Embedding"), or with explicit "--provider".
//   * Battery monitor  — otherwise (the package startup task launches us with no
//     args at logon; also "--monitor"). Runs the always-on alert loop.

using System.Runtime.InteropServices;
using BatteryWidget;
using BatteryWidget.Notifications;
using COM;

bool explicitProvider = args.Any(a => a.Equals("--provider", StringComparison.OrdinalIgnoreCase));
bool explicitMonitor = args.Any(a => a.Equals("--monitor", StringComparison.OrdinalIgnoreCase));
bool isEmbedding = args.Any(a =>
    a.Equals("-Embedding", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("/Embedding", StringComparison.OrdinalIgnoreCase));

bool runProvider = explicitProvider || (isEmbedding && !explicitMonitor);

if (!runProvider)
{
    // Battery monitor role. Single-instance: if another monitor already holds
    // the mutex (startup task + a manual launch, say), exit so toasts aren't
    // duplicated.
    using var singleton = new Mutex(initiallyOwned: true, @"Local\BatteryWidgetMonitor", out bool isOnly);
    if (!isOnly)
    {
        return;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    await new BatteryMonitor().RunAsync(cts.Token);
    return;
}

// --- Widget provider role (COM server) ---

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("ole32.dll")]
static extern int CoRegisterClassObject(
    [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
    [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
    uint dwClsContext,
    uint flags,
    out uint lpdwRegister);

[DllImport("ole32.dll")]
static extern int CoRevokeClassObject(uint dwRegister);

// Must match the com:Class Id / CreateInstance ClassId in Package.appxmanifest.
Guid CLSID_Factory = Guid.Parse("55489218-3DF6-4B99-8940-1BD2EA27B12A");

const uint CLSCTX_LOCAL_SERVER = 0x4;
const uint REGCLS_MULTIPLEUSE = 0x1;

WinRT.ComWrappersSupport.InitializeComWrappers();

var factory = new WidgetProviderFactory<WidgetProvider>();
Marshal.ThrowExceptionForHR(
    CoRegisterClassObject(CLSID_Factory, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out uint cookie));

try
{
    if (GetConsoleWindow() != IntPtr.Zero)
    {
        Console.WriteLine("Battery widget provider registered. Press ENTER to exit.");
        Console.ReadLine();
    }
    else
    {
        WidgetProvider.GetEmptyWidgetListEvent().WaitOne();
    }
}
finally
{
    int revokeResult = CoRevokeClassObject(cookie);
    if (revokeResult < 0)
    {
        System.Diagnostics.Trace.TraceError($"CoRevokeClassObject failed: 0x{revokeResult:X8}");
    }
    GC.KeepAlive(factory);
}
