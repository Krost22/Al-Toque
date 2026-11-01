namespace AlToque.Localization;

/// <summary>Textos de la ventana principal, las tarjetas de compañeros y el tutorial corto.</summary>
public static partial class S
{
    public static string Minimize => T("Minimizar", "Minimize");
    public static string KeepOnTop => T("Mantener encima de las demás ventanas", "Keep on top of other windows");
    public static string QuitApp => T("Salir de Al Toque", "Quit Al Toque");
    public static string QuitAppHint => T("Al Toque deja de recibir hasta que lo vuelvas a abrir.", "Al Toque stops receiving until you open it again.");
    public static string StillRunning => T("Al Toque sigue en la barra de tareas y recibiendo. Para salir: clic derecho en su botón › Salir de Al Toque.",
        "Al Toque stays in the taskbar and keeps receiving. To quit: right-click its button › Quit Al Toque.");

    public static string MakeFavorite(string name) => T($"Marcar a {name} como favorito", $"Mark {name} as a favorite");
    public static string Unfavorite(string name) => T($"Quitar a {name} de favoritos", $"Remove {name} from favorites");
    public static string RemovePeerTip(string name) => T($"Quitar a {name} de tus compañeros", $"Remove {name} from your teammates");
    public static string HidePeerTip(string name) => T($"Ocultar a {name}", $"Hide {name}");
    public static string RemovePeerConfirm(string name) => T(
        $"¿Quitar a {name} de tus compañeros? Ya no podrá enviarte sin pedir permiso. Pueden volver a conectarse cuando quieran.",
        $"Remove {name} from your teammates? They won't be able to send you files without asking. You can connect again any time.");
    public static string HidePeerConfirm(string name) => T(
        $"¿Ocultar a {name}? No aparecerá ni podrá enviarte nada hasta que reinicies Al Toque.",
        $"Hide {name}? They won't show up or be able to send you anything until you restart Al Toque.");

    // ── Tutorial corto (3 pasos) ─────────────────────────────────────────────────
    public static string Tour1Title => T("Tus compañeros", "Your teammates");
    public static string Tour1Body => T("Toca a alguien para enviarle archivos, o suéltalos encima. ★ lo deja primero y × lo quita.",
        "Tap someone to send them files, or drop files on them. ★ keeps them first and × removes them.");
    public static string Tour1Nobody => T("Cuando otro equipo de tu red abra Al Toque, aparecerá aquí. Si la red bloquea la búsqueda, conéctense con un código.",
        "When another device on your network opens Al Toque, it shows up here. If the network blocks discovery, connect with a code.");
    public static string Tour2Title => T("Conecta un equipo nuevo", "Connect a new device");
    public static string Tour2Body => T("Pulsen «Emparejar» en los dos y comparen el código de 6 cifras: desde entonces se envían sin preguntar.",
        "Press “Pair” on both and compare the 6-digit code: from then on you send to each other without asking.");
    public static string Tour3Title => T("Siempre a mano", "Always at hand");
    public static string Tour3Body => T("Al Toque vive en la barra de tareas: «—» lo minimiza y sigue recibiendo. Para salir, clic derecho en su botón › Salir de Al Toque.",
        "Al Toque lives in the taskbar: “—” minimizes it and it keeps receiving. To quit, right-click its button › Quit Al Toque.");
}
