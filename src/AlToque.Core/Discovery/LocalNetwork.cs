using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AlToque.Core.Discovery;

public sealed record LocalInterface(string Name, string Description, int Index, IPAddress Address, IPAddress Mask)
{
    public IPAddress Broadcast
    {
        get
        {
            var a = Address.GetAddressBytes();
            var m = Mask.GetAddressBytes();
            var b = new byte[4];
            for (int i = 0; i < 4; i++)
                b[i] = (byte)(a[i] | ~m[i]);
            return new IPAddress(b);
        }
    }

    public bool Contains(IPAddress other)
    {
        if (other.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var a = Address.GetAddressBytes();
        var o = other.GetAddressBytes();
        var m = Mask.GetAddressBytes();
        for (int i = 0; i < 4; i++)
        {
            if ((a[i] & m[i]) != (o[i] & m[i]))
                return false;
        }
        return true;
    }
}

/// <summary>Interfaces de red local aptas para descubrir y recibir: sin VPN ni adaptadores virtuales por defecto.</summary>
public static class LocalNetwork
{
    /// <summary>Para pruebas en un mismo equipo: incluye la interfaz de loopback.</summary>
    public static bool IncludeLoopback { get; set; } =
        Environment.GetEnvironmentVariable("ALTOQUE_DISCOVERY_LOOPBACK") == "1";

    private static readonly string[] VirtualDescriptionHints =
    [
        "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "wsl", "docker", "tap-windows", "tap adapter",
        "wireguard", "tailscale", "zerotier", "vpn", "openvpn", "nordlynx", "fortinet", "anyconnect", "npcap",
        "bluetooth", "teredo", "isatap", "pangp", "wintun", "radmin", "hamachi",
    ];

    private static readonly string[] VirtualNamePrefixes =
        ["docker", "br-", "veth", "virbr", "vboxnet", "vmnet", "tun", "tap", "wg", "tailscale", "zt", "utun", "llw", "awdl", "anpi", "bridge"];

    public static bool IsVirtual(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            return true;
        var desc = nic.Description.ToLowerInvariant();
        var name = nic.Name.ToLowerInvariant();
        if (VirtualDescriptionHints.Any(h => desc.Contains(h, StringComparison.Ordinal) || name.Contains(h, StringComparison.Ordinal)))
            return true;
        return !OperatingSystem.IsWindows() && VirtualNamePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    private static readonly Lock CacheLock = new();
    private static (long Stamp, bool ExcludeVirtual, IReadOnlyList<LocalInterface> List)? _cache;

    /// <summary>Versión con caché de unos segundos, para el camino caliente de conexiones entrantes.</summary>
    public static IReadOnlyList<LocalInterface> GetIPv4InterfacesCached(bool excludeVirtual)
    {
        lock (CacheLock)
        {
            if (_cache is { } c && c.ExcludeVirtual == excludeVirtual && Environment.TickCount64 - c.Stamp < 5000)
                return c.List;
        }
        var list = GetIPv4Interfaces(excludeVirtual);
        lock (CacheLock)
            _cache = (Environment.TickCount64, excludeVirtual, list);
        return list;
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
            _cache = null;
    }

    public static IReadOnlyList<LocalInterface> GetIPv4Interfaces(bool excludeVirtual)
    {
        var list = new List<LocalInterface>();
        NetworkInterface[] nics;
        try
        {
            nics = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return list;
        }
        foreach (var nic in nics)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                var isLoopback = nic.NetworkInterfaceType == NetworkInterfaceType.Loopback;
                if (isLoopback && !IncludeLoopback)
                    continue;
                if (!isLoopback && excludeVirtual && IsVirtual(nic))
                    continue;
                var props = nic.GetIPProperties();
                var v4 = props.GetIPv4Properties();
                if (v4 is null)
                    continue;
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var mask = ua.IPv4Mask is { } m && !m.Equals(IPAddress.Any) ? m : IPAddress.Parse("255.255.255.0");
                    list.Add(new LocalInterface(nic.Name, nic.Description, v4.Index, ua.Address, mask));
                }
            }
            catch (NetworkInformationException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
        }
        return list;
    }

    /// <summary>¿La dirección local de una conexión entrante pertenece a una interfaz admitida?</summary>
    public static bool IsEligibleLocalAddress(IPAddress local, bool excludeVirtual)
    {
        if (local.IsIPv4MappedToIPv6)
            local = local.MapToIPv4();
        if (IPAddress.IsLoopback(local))
            return IncludeLoopback;
        return GetIPv4InterfacesCached(excludeVirtual).Any(i => i.Address.Equals(local));
    }

    /// <summary>Dirección IPv4 principal, para el código de conexión.</summary>
    public static IPAddress? PrimaryAddress(bool excludeVirtual)
    {
        var all = GetIPv4Interfaces(excludeVirtual).Where(i => !IPAddress.IsLoopback(i.Address)).ToList();
        return (all.FirstOrDefault(i => IsPrivate(i.Address)) ?? all.FirstOrDefault())?.Address;
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }
}
