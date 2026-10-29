using System.Net;
using System.Reflection;

namespace AlToque.Core;

/// <summary>Constantes de producto y de protocolo.</summary>
public static class AppInfo
{
    public const string Name = "Al Toque";

    /// <summary>Identificador sin espacios: carpetas, registro, tubería IPC.</summary>
    public const string Id = "AlToque";

    public const string ExecutableName = "AlToque";

    /// <summary>Versión del protocolo, negociada por ALPN.</summary>
    public const int ProtocolVersion = 1;

    public const string Alpn = "altoque/1";

    /// <summary>Puerto TCP de sesiones por defecto (no registrado en IANA).</summary>
    public const int DefaultTcpPort = 45831;

    /// <summary>Puerto UDP de descubrimiento (no registrado en IANA).</summary>
    public const int DiscoveryPort = 45832;

    /// <summary>Grupo multicast de ámbito local (239.255.0.0/16).</summary>
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.45.83");

    /// <summary>Extensión de los archivos a medio recibir.</summary>
    public const string PartExtension = ".altoque-part";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static string OsName =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "other";
}
