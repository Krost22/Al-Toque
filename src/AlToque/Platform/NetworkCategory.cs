using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace AlToque.Platform;

/// <summary>
/// Categoría de red de Windows (pública, privada o de dominio) con <c>INetworkListManager</c>. Si todas las redes
/// conectadas son públicas, la recepción se pausa (sección 9.1.5).
/// </summary>
public static partial class NetworkCategory
{
    private static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private static readonly Guid IidNetworkListManager = new("DCB00000-570F-4A9B-8D69-199FDBA5723B");
    private const int NlmEnumNetworkConnected = 0x01;
    private const int NlmCategoryPublic = 0;

    public static bool IsOnPublicNetwork() => OperatingSystem.IsWindows() && QueryWindows();

    [SupportedOSPlatform("windows")]
    private static bool QueryWindows()
    {
        var init = CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */);
        try
        {
            if (CoCreateInstance(ClsidNetworkListManager, 0, 0x17 /* CLSCTX_ALL */, IidNetworkListManager, out var ptr) < 0)
                return false;
            try
            {
                var wrappers = new StrategyBasedComWrappers();
                var manager = (INetworkListManager)wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
                manager.GetNetworks(NlmEnumNetworkConnected, out var enumPtr);
                ((ComObject)(object)manager).FinalRelease();
                if (enumPtr == 0)
                    return false;
                try
                {
                    var networks = (IEnumNetworks)wrappers.GetOrCreateObjectForComInstance(enumPtr, CreateObjectFlags.None);
                    int total = 0, publicCount = 0;
                    while (total < 64)
                    {
                        networks.Next(1, out var netPtr, out var fetched);
                        if (fetched == 0 || netPtr == 0)
                            break;
                        try
                        {
                            var network = (INetwork)wrappers.GetOrCreateObjectForComInstance(netPtr, CreateObjectFlags.None);
                            network.GetCategory(out var category);
                            ((ComObject)(object)network).FinalRelease();
                            total++;
                            if (category == NlmCategoryPublic)
                                publicCount++;
                        }
                        finally
                        {
                            Marshal.Release(netPtr);
                        }
                    }
                    ((ComObject)(object)networks).FinalRelease();
                    return total > 0 && publicCount == total;
                }
                finally
                {
                    Marshal.Release(enumPtr);
                }
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (init >= 0)
                CoUninitialize();
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}

// Interfaces duales (derivan de IDispatch): los cuatro primeros métodos reservan el lugar de IDispatch en la vtable.

[GeneratedComInterface]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetworkListManager
{
    void GetTypeInfoCount(out uint count);
    void GetTypeInfo(uint index, uint lcid, out nint info);
    void GetIDsOfNames(in Guid riid, nint names, uint count, uint lcid, nint dispIds);
    void Invoke(int dispId, in Guid riid, uint lcid, ushort flags, nint parameters, nint result, nint exceptionInfo, nint argError);
    void GetNetworks(int flags, out nint enumNetworks);
}

[GeneratedComInterface]
[Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface IEnumNetworks
{
    void GetTypeInfoCount(out uint count);
    void GetTypeInfo(uint index, uint lcid, out nint info);
    void GetIDsOfNames(in Guid riid, nint names, uint count, uint lcid, nint dispIds);
    void Invoke(int dispId, in Guid riid, uint lcid, ushort flags, nint parameters, nint result, nint exceptionInfo, nint argError);
    void GetNewEnum(out nint enumVariant);

    [PreserveSig]
    int Next(uint celt, out nint network, out uint fetched);
}

[GeneratedComInterface]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetwork
{
    void GetTypeInfoCount(out uint count);
    void GetTypeInfo(uint index, uint lcid, out nint info);
    void GetIDsOfNames(in Guid riid, nint names, uint count, uint lcid, nint dispIds);
    void Invoke(int dispId, in Guid riid, uint lcid, ushort flags, nint parameters, nint result, nint exceptionInfo, nint argError);
    void GetName(out nint name);
    void SetName(nint name);
    void GetDescription(out nint description);
    void SetDescription(nint description);
    void GetNetworkId(out Guid id);
    void GetDomainType(out int domainType);
    void GetNetworkConnections(out nint connections);
    void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);
    void GetIsConnectedToInternet(out short connected);
    void GetIsConnected(out short connected);
    void GetConnectivity(out int connectivity);
    void GetCategory(out int category);
}
