namespace AlToque.Localization;

/// <summary>Textos de la app para teléfonos.</summary>
public static partial class S
{
    public static string MobileReadyTitle => T("Listo para recibir", "Ready to receive");
    public static string MobileReadyBody => T("Mantén Al Toque abierto y elige a quién enviar desde aquí o desde «Compartir».",
        "Keep Al Toque open, and send from here or from “Share”.");
    public static string MobileReceivingPaused => T("No estás recibiendo: elegiste «Nadie».", "You're not receiving: you chose “No one”.");
    public static string MobileSendFiles => T("Enviar archivos", "Send files");
    public static string MobilePendingTitle(int n, string size) => T($"{Items(n)} · {size} listos para enviar", $"{Items(n)} · {size} ready to send");
    public static string MobilePendingHint => T("Toca a quién se los envías.", "Tap who to send them to.");
    public static string MobilePreparing => T("Preparando los archivos…", "Getting the files ready…");
    public static string MobileTapToSend => T("Toca para enviar", "Tap to send");
    public static string MobileNobodyHint => T("Los equipos con Al Toque abierto en tu misma red Wi-Fi aparecen aquí.",
        "Devices with Al Toque open on your Wi-Fi show up here.");
    public static string MobileSavedIn => T("Lo que recibes se guarda en Descargas › Al Toque.", "What you receive is saved to Downloads › Al Toque.");
    public static string MobileOpenDownloads => T("Abrir Descargas", "Open Downloads");
    public static string MobileUnreachable(string who) => T($"No encuentro a {who}. ¿Sigue con Al Toque abierto?", $"I can't find {who}. Is Al Toque still open there?");
    public static string MobileCodeTitle => T("Conectar por código", "Connect by code");
    public static string MobileCodeHint => T("Escribe el código que muestra el otro equipo en «Mostrar mi código».",
        "Type the code the other device shows under “Show my code”.");
    public static string MobileCodePlaceholder => T("Código o IP (K7Q2-9XM4)", "Code or IP (K7Q2-9XM4)");
    public static string MobileSendHere => T("Enviar archivos aquí", "Send files here");
    public static string MobileMyCodeTitle => T("Mi código", "My code");
    public static string MobileMyCodeHint => T("En el otro equipo: Conectar por código, y escribe este código.",
        "On the other device: Connect by code, and type this code.");
    public static string MobilePairTitle => T("Emparejar un equipo", "Pair a device");
    public static string MobilePairHint => T("En el otro equipo pulsa también «Emparejar». Luego tócalo aquí y comparen el código de 6 cifras.",
        "Tap “Pair” on the other device too. Then tap it here and compare the 6-digit code.");
    public static string MobilePairNobody => T("Todavía nadie tiene abierto «Emparejar».", "No one has “Pair” open yet.");
    public static string MobilePairOpenFor(TimeSpan left) => T($"Visible para emparejar · {left:m\\:ss}", $"Visible for pairing · {left:m\\:ss}");
    public static string MobileSettingsTitle => T("Tu equipo", "Your device");
    public static string MobileReceiveFolder => T("Lo recibido se guarda en", "Received files are saved in");
    public static string MobileDownloadsFolder => T("Descargas › Al Toque", "Downloads › Al Toque");
    public static string MobileKeepOpen => T("Para recibir, Al Toque tiene que estar abierto. Durante un envío sigue trabajando aunque cambies de app.",
        "To receive, keep Al Toque open. During a transfer it keeps working even if you switch apps.");
    public static string MobileUpdateTitle(string v) => T($"Al Toque {v} ya está aquí", $"Al Toque {v} is here");
    public static string MobileUpdateHint => T("Descarga el APK nuevo e instálalo encima: no pierdes tus compañeros.",
        "Download the new APK and install it over this one: you keep your teammates.");
    public static string MobileUpdateDownload => T("Descargar", "Download");
    public static string MobileCheckUpdates => T("Buscar versión nueva", "Check for a new version");
    public static string MobileUpToDate => T("Tienes la última versión.", "You're on the latest version.");
    public static string MobileUpdateFailed => T("No pude revisar GitHub. Prueba más tarde.", "I couldn't check GitHub. Try again later.");
    public static string MobileServiceTitle => T("Al Toque está pasando archivos", "Al Toque is moving files");
    public static string MobileServiceBody => T("Se detiene sola al terminar.", "It stops by itself when it's done.");
    public static string MobileServiceChannel => T("Transferencias", "Transfers");
}
