namespace AlToque.Localization;

/// <summary>Textos de «Cerca de ti» y de las solicitudes de conexión.</summary>
public static partial class S
{
    public static string NearbyHeader => T("Cerca de ti", "Nearby");
    public static string NearbyConnect => T("Conectar", "Connect");
    public static string NearbyTapToConnect => T("Toca para conectar", "Tap to connect");
    public static string NearbyNeedsPairing => T("Que abra «Emparejar»", "Ask them to open “Pair”");
    public static string NearbyConnectTip(string name) => T($"Pedirle a {name} conectarse", $"Ask {name} to connect");

    // ── Solicitud que llega ─────────────────────────────────────────────────────
    public static string PairRequestTitle(string name) => T($"{name} quiere conectarse contigo", $"{name} wants to connect with you");
    public static string PairRequestBody => T("Si aceptas, podrán enviarse archivos sin preguntar y escribirse.",
        "If you accept, you'll be able to send each other files without asking and chat.");
    public static string PairRequestCheck(string name) => T($"Comprueba que {name} ve este mismo código:", $"Check that {name} sees this same code:");
    public static string PairRequestAccept => T("Conectar", "Connect");
    public static string PairRequestDecline => T("Ahora no", "Not now");
    public static string PairRequestWaiting(string name) => T($"Esperando a que {name} confirme el código…", $"Waiting for {name} to confirm the code…");
    public static string PairRequestDone(string name) => T($"¡Listo! {name} ya es tu compañero.", $"Done! {name} is now your teammate.");
    public static string PairRequestOpenChat => T("Escribirle", "Message");
    public static string PairRequestFailed => T("No se conectaron.", "You didn't connect.");

    // ── Ajustes ─────────────────────────────────────────────────────────────────
    public static string VisibleNearby => T("Visible para equipos cercanos", "Visible to nearby devices");
    public static string VisibleNearbyHint => T("En redes privadas, los demás ven tu nombre y avatar para pedirte conectar. Solo tus compañeros pueden enviarte archivos.",
        "On private networks, others see your name and avatar to ask to connect. Only your teammates can send you files.");
}
