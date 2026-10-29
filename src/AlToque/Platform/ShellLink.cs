using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace AlToque.Platform;

/// <summary>Crea accesos directos .lnk con <c>IShellLinkW</c>, usando COM generado por código (compatible con NativeAOT).</summary>
[SupportedOSPlatform("windows")]
internal static partial class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private const uint ClsctxInprocServer = 0x1;
    private const uint CoinitApartmentThreaded = 0x2;

    /// <summary>Debe llamarse desde un hilo STA.</summary>
    public static void Create(string shortcutPath, string target, string arguments, string workingDirectory, string description)
    {
        var init = CoInitializeEx(0, CoinitApartmentThreaded);
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidShellLink, 0, ClsctxInprocServer, IidShellLinkW, out var ptr));
            try
            {
                var wrappers = new StrategyBasedComWrappers();
                var link = (IShellLinkW)wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                ((IPersistFile)link).Save(shortcutPath, true);
                // Se libera antes de CoUninitialize, no cuando lo decida el recolector.
                ((ComObject)(object)link).FinalRelease();
            }
            finally
            {
                Marshal.Release(ptr);
            }
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

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
    void GetIDList(out nint ppidl);
    void SetIDList(nint pidl);
    void GetDescription(nint pszName, int cch);
    void SetDescription(string pszName);
    void GetWorkingDirectory(nint pszDir, int cch);
    void SetWorkingDirectory(string pszDir);
    void GetArguments(nint pszArgs, int cch);
    void SetArguments(string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
    void SetIconLocation(string pszIconPath, int iIcon);
    void SetRelativePath(string pszPathRel, uint dwReserved);
    void Resolve(nint hwnd, uint fFlags);
    void SetPath(string pszFile);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load(string pszFileName, uint dwMode);
    void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted(string pszFileName);
    void GetCurFile(out nint ppszFileName);
}
