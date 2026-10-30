namespace AlToque.Localization;

/// <summary>Textos de actualizaciones, créditos y tutorial guiado.</summary>
public static partial class S
{
    // ── Actualizaciones ──────────────────────────────────────────────────────────
    public static string UpdatesTitle => T("Actualizaciones", "Updates");
    public static string UpdateAvailable(string v) => T($"Al Toque {v} ya está aquí", $"Al Toque {v} is here");
    public static string UpdateAvailableHint => T("Se instala en segundos y se vuelve a abrir sola.", "It installs in seconds and reopens by itself.");
    public static string UpdatePortableHint => T("Esta copia es portable: descarga la nueva versión desde GitHub.", "This is a portable copy: download the new version from GitHub.");
    public static string UpdateDownloading(string v) => T($"Descargando Al Toque {v}…", $"Downloading Al Toque {v}…");
    public static string UpdateReady(string v) => T($"Al Toque {v} está lista", $"Al Toque {v} is ready");
    public static string UpdateReadyHint => T("Reinicia para estrenarla; si no, se instala al cerrar.", "Restart to use it now; otherwise it installs when you quit.");
    public static string UpdateNow => T("Actualizar", "Update");
    public static string UpdateRestart => T("Reiniciar y actualizar", "Restart and update");
    public static string UpdateOpenPage => T("Descargar", "Download");
    public static string UpdateCheck => T("Buscar actualizaciones", "Check for updates");
    public static string UpdateChecking => T("Buscando versiones nuevas…", "Looking for new versions…");
    public static string UpdateUpToDate(string v) => T($"Tienes la última versión ({v}).", $"You have the latest version ({v}).");
    public static string UpdateIdle(string v) => T($"Versión {v}.", $"Version {v}.");
    public static string UpdateFailed => T("No se pudo comprobar. Revisa la conexión a internet.", "Couldn't check. Check your internet connection.");
    public static string UpdateLater => T("Más tarde", "Later");
    public static string AutoUpdate => T("Actualizar automáticamente", "Update automatically");
    public static string AutoUpdateHint => T("Descarga las versiones nuevas en segundo plano y las instala al reiniciar o al cerrar.",
        "Downloads new versions in the background and installs them when you restart or quit.");
    public static string UpdateCutsTransfers => T("Hay envíos en curso. Si actualizas ahora se cortarán. ¿Actualizar igual?",
        "Transfers are in progress. Updating now will cut them off. Update anyway?");
    public static string UpdatedTo(string v) => T($"¡Listo! Al Toque se actualizó a la versión {v}.", $"Done! Al Toque was updated to version {v}.");
    public static string SeeWhatsNew => T("Ver novedades", "See what's new");
    public static string TrayUpdate(string v) => T($"Actualizar a {v}", $"Update to {v}");

    // ── Créditos ─────────────────────────────────────────────────────────────────
    public static string Credits => "KrostGames • Eduardo Mogollon Salcedo 2026";

    // ── Tutorial guiado ──────────────────────────────────────────────────────────
    public static string TourNext => T("Siguiente", "Next");
    public static string TourSkip => T("Saltar", "Skip");
    public static string TourDone => T("¡A enviar!", "Let's go!");
    public static string TourAgain => T("Ver el tutorial otra vez", "Show the tutorial again");
    public static string TourStepOf(int i, int n) => T($"{i} de {n}", $"{i} of {n}");
    public static string TourHelloTitle => T("¡Bienvenido a Al Toque!", "Welcome to Al Toque!");
    public static string TourHelloBody => T("Soy Chasqui. Te muestro lo esencial en medio minuto.", "I'm Chasqui. Let me show you the basics in half a minute.");
    public static string TourSendTitle => T("Envía archivos", "Send files");
    public static string TourSendBody => OperatingSystem.IsWindows()
        ? T("Arrástralos sobre este panel o usa «Elegir archivos». También puedes hacer clic derecho en cualquier archivo › Enviar con Al Toque.",
            "Drag them onto this panel or use “Choose files”. You can also right-click any file › Send with Al Toque.")
        : T("Arrástralos sobre este panel o usa «Elegir archivos».", "Drag them onto this panel or use “Choose files”.");
    public static string TourPeersTitle => T("Tus compañeros", "Your teammates");
    public static string TourPeersBody => T("Aquí aparecen las PC de tu red que tienen Al Toque abierto. Suelta los archivos sobre una para enviárselos.",
        "PCs on your network with Al Toque open show up here. Drop files on one to send them.");
    public static string TourNobodyBody => T("Cuando otra PC de tu red abra Al Toque, aparecerá aquí. Si la red bloquea la búsqueda, conéctense con un código.",
        "When another PC on your network opens Al Toque, it shows up here. If the network blocks discovery, connect with a code.");
    public static string TourPairTitle => T("Empareja tus equipos", "Pair your devices");
    public static string TourPairBody => T("Comparan un código de 6 dígitos una sola vez y quedan como de confianza: sus envíos llegan verificados.",
        "Compare a 6-digit code once and they become trusted: their transfers arrive verified.");
    public static string TourModeTitle => T("Quién puede enviarte", "Who can send you files");
    public static string TourModeBody => T("Tú decides: nadie, solo tus compañeros de confianza o todos durante 10 minutos.",
        "You decide: no one, only your trusted teammates, or everyone for 10 minutes.");
    public static string TourSettingsTitle => T("Tus ajustes", "Your settings");
    public static string TourSettingsBody => T("Cambia tu avatar, tu nombre, la carpeta donde se guardan los archivos y más.",
        "Change your avatar, your name, the folder where files are saved and more.");
    public static string TourEndTitle => T("¡Listo para enviar!", "Ready to send!");
    public static string TourEndBody => T("Ya sabes lo esencial. Puedes repetir este tutorial desde Ajustes › Acerca de.",
        "You know the essentials. You can replay this tutorial from Settings › About.");
}
