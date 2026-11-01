using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using AlToque.Core.Agent;

namespace AlToque.Platform;

/// <summary>
/// Lista de saltos del botón de la barra de tareas: clic derecho › «Salir de Al Toque» ejecuta <c>AlToque.exe quit</c>.
/// Es la forma de cerrar Al Toque, porque cerrar la ventana solo la minimiza. COM generado por código (NativeAOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class JumpList
{
    private static readonly Guid ClsidDestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    private static readonly Guid IidCustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    private static readonly Guid ClsidEnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    private static readonly Guid IidObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    private static readonly Guid IidObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    /// <summary>PKEY_Title: el texto que muestra la tarea en la lista.</summary>
    private static readonly PropertyKey TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    private const uint ClsctxInprocServer = 0x1;
    private const uint CoinitApartmentThreaded = 0x2;
    private const ushort VtLpwstr = 31;

    public static void SetQuitTask(string exePath, string title)
    {
        var init = CoInitializeEx(0, CoinitApartmentThreaded);
        nint listPtr = 0, collectionPtr = 0, linkPtr = 0, removedPtr = 0;
        try
        {
            var wrappers = new StrategyBasedComWrappers();
            Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidDestinationList, 0, ClsctxInprocServer, IidCustomDestinationList, out listPtr));
            var list = (ICustomDestinationList)wrappers.GetOrCreateObjectForComInstance(listPtr, CreateObjectFlags.None);
            list.BeginList(out _, IidObjectArray, out removedPtr);

            Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidEnumerableObjectCollection, 0, ClsctxInprocServer, IidObjectCollection, out collectionPtr));
            var collection = (IObjectCollection)wrappers.GetOrCreateObjectForComInstance(collectionPtr, CreateObjectFlags.None);

            Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidShellLink, 0, ClsctxInprocServer, IidShellLinkW, out linkPtr));
            var link = (IShellLinkW)wrappers.GetOrCreateObjectForComInstance(linkPtr, CreateObjectFlags.None);
            link.SetPath(exePath);
            link.SetArguments("quit");
            link.SetIconLocation(exePath, 0);
            link.SetDescription(title);
            var store = (IPropertyStore)link;
            var value = new PropVariant { Type = VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(title) };
            try
            {
                store.SetValue(TitleKey, value);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
            }

            collection.AddObject(linkPtr);
            list.AddUserTasks(collectionPtr);
            list.CommitList();
            ((ComObject)(object)link).FinalRelease();
            ((ComObject)(object)collection).FinalRelease();
            ((ComObject)(object)list).FinalRelease();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            // Sin lista de saltos se sigue pudiendo salir desde el ícono de la bandeja o desde Acerca de.
            AgentLog.Write("No se pudo crear la lista de saltos: " + ex.Message);
        }
        finally
        {
            foreach (var ptr in new[] { removedPtr, linkPtr, collectionPtr, listPtr })
            {
                if (ptr != 0)
                    Marshal.Release(ptr);
            }
            if (init >= 0)
                CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>PROPVARIANT con un texto (VT_LPWSTR); 24 bytes, el tamaño en 64 bits.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Pointer;
    }

    [GeneratedComInterface]
    [Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    internal partial interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMinSlots, in Guid riid, out nint ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, nint poa);
        void AppendKnownCategory(int category);
        void AddUserTasks(nint poa);
        void CommitList();
        void GetRemovedDestinations(in Guid riid, out nint ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? pszAppID);
        void AbortList();
    }

    [GeneratedComInterface]
    [Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
    internal partial interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, in Guid riid, out nint ppv);

        // IObjectCollection
        void AddObject(nint punk);
        void AddFromArray(nint poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [GeneratedComInterface]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    internal partial interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(in PropertyKey key, out PropVariant pv);
        void SetValue(in PropertyKey key, in PropVariant propvar);
        void Commit();
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}
