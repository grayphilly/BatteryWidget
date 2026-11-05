// FactoryHelper.cs
// IClassFactory boilerplate so the widget host can COM-activate our provider.
// Adapted from the Microsoft widget provider walkthrough.

using Microsoft.Windows.Widgets.Providers;
using System.Runtime.InteropServices;
using WinRT;

namespace COM;

internal static class Guids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport, ComVisible(false), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid(Guids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int LockServer(bool fLock);
}

[ComVisible(true)]
internal class WidgetProviderFactory<T> : IClassFactory
    where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = -2147221232;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            return CLASS_E_NOAGGREGATION;
        }

        IntPtr provider = IntPtr.Zero;
        try
        {
            provider = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return Marshal.QueryInterface(provider, in riid, out ppvObject);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return Marshal.GetHRForException(exception);
        }
        finally
        {
            if (provider != IntPtr.Zero)
            {
                Marshal.Release(provider);
            }
        }
    }

    int IClassFactory.LockServer(bool fLock) => 0;
}
