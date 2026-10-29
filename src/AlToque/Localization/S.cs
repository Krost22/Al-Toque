using System.Globalization;

namespace AlToque.Localization;

/// <summary>
/// Textos de la interfaz en español neutro e inglés. El idioma se fija al arrancar (ajuste "auto", "es" o "en");
/// cambiarlo requiere reiniciar la app.
/// </summary>
public static class S
{
    public static bool Spanish { get; private set; } = true;

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("es");

    public static void Initialize(string language)
    {
        var ui = CultureInfo.CurrentUICulture;
        Spanish = language switch
        {
            "es" => true,
            "en" => false,
            _ => ui.TwoLetterISOLanguageName != "en",
        };
        if (Spanish)
            Culture = ui.TwoLetterISOLanguageName == "es" ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo("es");
        else
            Culture = ui.TwoLetterISOLanguageName == "en" ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo("en-US");
    }

    private static string T(string es, string en) => Spanish ? es : en;

    // ── General ──────────────────────────────────────────────────────────────────
    public static string AppName => "Al Toque";
    public static string Tagline => T("Clic derecho, tu compañero, listo.", "Right-click, your teammate, done.");
    public static string Settings => T("Ajustes", "Settings");
    public static string Close => T("Cerrar", "Close");
    public static string Cancel => T("Cancelar", "Cancel");
    public static string Accept => T("Aceptar", "Accept");
    public static string Decline => T("Rechazar", "Decline");
    public static string Send => T("Enviar", "Send");
    public static string Retry => T("Reintentar", "Retry");
    public static string Change => T("Cambiar", "Change");
    public static string Save => T("Guardar", "Save");
    public static string Quit => T("Salir", "Quit");
    public static string Open => T("Abrir", "Open");
    public static string OpenFolder => T("Abrir carpeta", "Open folder");
    public static string OpenFile => T("Abrir archivo", "Open file");
    public static string Verified => T("Verificado", "Verified");
    public static string Unverified => T("No verificado", "Not verified");
    public static string Trusted => T("De confianza", "Trusted");

    // ── Bandeja ──────────────────────────────────────────────────────────────────
    public static string TrayOpen => T("Abrir Al Toque", "Open Al Toque");
    public static string TrayReceiveFrom => T("Recibir de", "Receive from");
    public static string TrayPair => T("Emparejar un compañero…", "Pair a teammate…");
    public static string TrayConnect => T("Conectar por IP o código…", "Connect by IP or code…");
    public static string TrayHistory => T("Historial y ajustes…", "History and settings…");
    public static string TrayTooltipIdle => T("Al Toque · listo", "Al Toque · ready");
    public static string TrayTooltipPaused => T("Al Toque · recepción en pausa", "Al Toque · receiving paused");

    // ── Modos de recepción ───────────────────────────────────────────────────────
    public static string ModeNobody => T("Nadie", "No one");
    public static string ModeTrusted => T("Solo compañeros de confianza", "Trusted teammates only");
    public static string ModeTrustedShort => T("Solo confianza", "Trusted only");
    public static string ModeEveryone => T("Todos durante 10 minutos", "Everyone for 10 minutes");
    public static string ModeEveryoneShort => T("Todos (10 min)", "Everyone (10 min)");
    public static string ReceiveFromLabel => T("Recibir de:", "Receive from:");
    public static string EveryoneRemaining(TimeSpan left) => T($"Todos · quedan {left:m\\:ss}", $"Everyone · {left:m\\:ss} left");

    // ── Panel rápido ─────────────────────────────────────────────────────────────
    public static string Searching => T("Buscando compañeros…", "Looking for teammates…");
    public static string NobodyFound => T("No veo a nadie en esta red. ¿Tu compañero tiene Al Toque abierto?",
        "I can't see anyone on this network. Does your teammate have Al Toque open?");
    public static string DropHere => T("Suelta archivos aquí o sobre alguien", "Drop files here or on someone");
    public static string Recent => T("Recientes", "Recent");
    public static string NoRecent => T("Todavía no hay envíos.", "Nothing sent yet.");
    public static string ConnectByCode => T("Conectar por código", "Connect by code");
    public static string UseIp => T("Usar IP", "Use IP");
    public static string PairButton => T("Emparejar", "Pair");
    public static string MyCode => T("Mi código", "My code");
    public static string Checking => T("verificando…", "checking…");
    public static string Offline => T("desconectado", "offline");
    public static string Available => T("disponible", "available");
    public static string Busy => T("ocupado", "busy");
    public static string WantsToPair => T("quiere emparejar", "wants to pair");
    public static string PickTitle(int count, string size) => T($"Enviar {Items(count)} ({size}) a…", $"Send {Items(count)} ({size}) to…");
    public static string MessagePlaceholder => T("Mensaje opcional (\"Aquí va el render final\")", "Optional message (\"Here's the final render\")");
    public static string NetworkPublicPaused => T("Esta red es pública: por seguridad no estoy recibiendo archivos ni emparejamientos. Si confías en esta red, puedes recibir igual.",
        "This network is public: for safety I'm not receiving files or pairings. If you trust this network, you can receive anyway.");
    public static string AllowPublicNetwork => T("Recibir igual en esta red", "Receive anyway on this network");
    public static string PairPublicNetwork => T("Esta PC está en una red pública y no puede recibir el emparejamiento. Las dos PC deben permitirlo.",
        "This PC is on a public network and can't receive the pairing. Both PCs must allow it.");
    public static string SettingAllowPublic => T("Recibir también en redes públicas", "Also receive on public networks");
    public static string SettingAllowPublicHint => T("Úsalo solo en redes de confianza. Mejor aún: marca la red como privada en Windows (Configuración → Red e Internet).",
        "Only on networks you trust. Better: mark the network as private in Windows (Settings → Network & internet).");
    public static string ModeNobodyHint => T("Estás invisible: nadie puede enviarte archivos.", "You're invisible: no one can send you files.");
    public static string TrustedOnlyHint => T("Solo tus compañeros emparejados pueden enviarte archivos.", "Only paired teammates can send you files.");
    public static string EmptyTrustedHint => T("Empareja a tus compañeros o activa \"Todos (10 min)\" para recibir.",
        "Pair your teammates or turn on \"Everyone (10 min)\" to receive.");

    // ── Solicitud entrante ───────────────────────────────────────────────────────
    public static string WantsToSend(int count) => T($"quiere enviarte {Items(count)}", $"wants to send you {Items(count)}");
    public static string SaveIn => T("Guardar en:", "Save to:");
    public static string ExecutableWarning => T("Es un programa. Ábrelo solo si lo esperabas.", "This is a program. Only open it if you expected it.");
    public static string MisleadingWarning => T("El nombre parece engañoso: fíjate en la extensión real.", "The name looks misleading: check the real extension.");
    public static string HiddenCharsWarning => T("El nombre tenía caracteres ocultos que se quitaron.", "The name had hidden characters that were removed.");
    public static string UnverifiedWarning => T("No es un compañero emparejado: comprueba con esa persona que sea quien dice ser.",
        "Not a paired teammate: check with them that they are who they say.");
    public static string DeclineAndBlock => T("Rechazar y bloquear", "Decline and block");
    public static string ExpiresIn(TimeSpan left) => T($"Vence en {left:m\\:ss}", $"Expires in {left:m\\:ss}");
    public static string AndMore(int n) => T($"y {n} más", $"and {n} more");

    // ── Transferencias ───────────────────────────────────────────────────────────
    public static string Preparing => T("Preparando…", "Preparing…");
    public static string ConnectingTo(string who) => T($"Conectando con {who}…", $"Connecting to {who}…");
    public static string WaitingFor(string who, TimeSpan left) => T($"Esperando que {who} acepte… ({left:m\\:ss})", $"Waiting for {who} to accept… ({left:m\\:ss})");
    public static string SendingTo(string who, int pct, string? eta) =>
        T($"Enviando a {who} · {pct} %{(eta is null ? "" : " · quedan " + eta)}", $"Sending to {who} · {pct}%{(eta is null ? "" : " · " + eta + " left")}");
    public static string ReceivingFrom(string who, int pct, string? eta) =>
        T($"Recibiendo de {who} · {pct} %{(eta is null ? "" : " · quedan " + eta)}", $"Receiving from {who} · {pct}%{(eta is null ? "" : " · " + eta + " left")}");
    public static string SentOk(string who, string what) => T($"¡Listo! {who} recibió {what}", $"Done! {who} received {what}");
    public static string ReceivedOk(string what, string who) => T($"Listo: {what} de {who}", $"Done: {what} from {who}");
    public static string Declined(string who) => T($"{who} rechazó el envío.", $"{who} declined.");
    public static string Cancelled => T("Envío cancelado.", "Transfer cancelled.");
    public static string ReceiveCancelled => T("Recepción cancelada.", "Receiving cancelled.");
    public static string Partial(int ok, int failed) => T($"Llegaron {ok}; fallaron {failed}.", $"{ok} arrived; {failed} failed.");

    // ── Errores (sección 8.8) ────────────────────────────────────────────────────
    public static string ErrorText(string? code, string who, string? detail) => code switch
    {
        "E_DECLINED" => Declined(who),
        "E_ACCEPT_TIMEOUT" => T($"{who} no respondió en 2 minutos. Puedes intentarlo de nuevo.", $"{who} didn't answer within 2 minutes. You can try again."),
        "E_UNREACHABLE" => T($"No pude conectar con {who}. ¿Está encendida y en la misma red? Si la red es pública, en esa PC elige \"Recibir igual en esta red\" y permite el firewall.",
            $"I couldn't reach {who}. Is it on and on the same network? If the network is public, choose \"Receive anyway on this network\" on that PC and allow the firewall."),
        "E_IDENTITY_MISMATCH" => T($"Cuidado: este \"{who}\" tiene una clave distinta a la que conoces. Por seguridad, no envié nada.",
            $"Careful: this \"{who}\" has a different key than the one you know. For safety, nothing was sent."),
        "E_POLICY" => T($"{who} solo recibe archivos de compañeros de confianza. Pídele que active \"Todos (10 min)\" o emparejen sus equipos.",
            $"{who} only accepts files from trusted teammates. Ask them to turn on \"Everyone (10 min)\" or pair with them."),
        "E_DISK_FULL" => detail is { } d && long.TryParse(d, out var missing)
            ? T($"A {who} le faltan {Format.Bytes(missing)} de espacio.", $"{who} needs {Format.Bytes(missing)} more free space.")
            : T($"{who} se quedó sin espacio en disco.", $"{who} ran out of disk space."),
        "E_HASH_MISMATCH" => T("Un archivo llegó dañado.", "A file arrived damaged."),
        "E_NETWORK_PUBLIC" => T($"{who} está en una red pública y no recibe archivos.", $"{who} is on a public network and isn't receiving files."),
        "E_BAD_MANIFEST" => T("El envío tenía nombres de archivo no válidos y se rechazó por seguridad.", "The transfer had invalid file names and was rejected for safety."),
        "E_BUSY" => T($"{who} está ocupado con otros envíos. Prueba en un momento.", $"{who} is busy with other transfers. Try again shortly."),
        "E_RATE_LIMIT" => T("Demasiados intentos seguidos. Espera un minuto.", "Too many attempts in a row. Wait a minute."),
        "E_CANCELLED" => T($"Se canceló el envío.", "The transfer was cancelled."),
        "E_TIMEOUT" => T($"{who} dejó de responder.", $"{who} stopped responding."),
        "E_SOURCE_CHANGED" => T("Un archivo cambió mientras se enviaba.", "A file changed while it was being sent."),
        "E_TOO_LARGE" => detail ?? T("Demasiados archivos.", "Too many files."),
        "E_IO" => detail is not null ? T("No pude leer o escribir: ", "I couldn't read or write: ") + detail : T("Error de disco.", "Disk error."),
        _ => T("Algo salió mal. Puedes reintentar.", "Something went wrong. You can retry."),
    };

    // ── Emparejamiento ───────────────────────────────────────────────────────────
    public static string PairTitle => T("Emparejar un compañero", "Pair a teammate");
    public static string PairWaiting => T("Pídele a tu compañero que abra Al Toque y te elija en su lista. Esta ventana se cierra sola en 2 minutos.",
        "Ask your teammate to open Al Toque and pick you from their list. This window closes by itself in 2 minutes.");
    public static string PairVisibleAs => T("Te ven como:", "They see you as:");
    public static string PairConnecting(string who) => T($"Conectando con {who}…", $"Connecting to {who}…");
    public static string PairCompare => T("¿Ves el mismo código en la otra pantalla?", "Do you see the same code on the other screen?");
    public static string PairMatches => T("Sí, coincide", "Yes, it matches");
    public static string PairDoesNotMatch => T("No coincide", "It doesn't match");
    public static string PairWaitingOther => T("Esperando a que tu compañero confirme…", "Waiting for your teammate to confirm…");
    public static string PairDone(string who) => T($"¡Listo! {who} ya es de confianza.", $"Done! {who} is now trusted.");
    public static string PairOutcome(string outcome) => outcome switch
    {
        "Rejected" or "RemoteRejected" => T("Los códigos no coincidieron. No se guardó nada.", "The codes didn't match. Nothing was saved."),
        "Tampered" => T("Algo interfirió en la conexión. No se guardó nada.", "Something interfered with the connection. Nothing was saved."),
        "Timeout" => T("Se acabó el tiempo.", "Time ran out."),
        "Locked" => T("Demasiados intentos. Espera 5 minutos.", "Too many attempts. Wait 5 minutes."),
        "NotPairing" => T("Ese equipo no tiene abierta la ventana de emparejamiento.", "That device doesn't have its pairing window open."),
        _ => T("No se pudo emparejar.", "Pairing failed."),
    };
    public static string PairOpenHint => T("Abre \"Emparejar\" en el otro equipo y elígelo aquí:", "Open \"Pair\" on the other device and pick it here:");
    public static string PairNoCandidates => T("Nadie tiene abierta la ventana de emparejamiento todavía.", "No one has their pairing window open yet.");
    public static string PairWithThis => T("Emparejar con este equipo", "Pair with this device");

    // ── Conectar ─────────────────────────────────────────────────────────────────
    public static string ConnectTitle => T("Conectar por IP o código", "Connect by IP or code");
    public static string ConnectHint => T("Escribe el código que muestra el otro equipo (por ejemplo K7Q2-9XM4) o su dirección IP.",
        "Type the code shown on the other device (e.g. K7Q2-9XM4) or its IP address.");
    public static string ConnectBlocked => T("Tu red no deja que los equipos se encuentren solos (pasa en el Wi-Fi de invitados). Conéctate con un código.",
        "Your network doesn't let devices find each other (common on guest Wi-Fi). Connect with a code.");
    public static string ConnectProbe => T("Buscar", "Find");
    public static string ConnectFound => T("Encontré a:", "Found:");
    public static string ConnectNotFound => T("No encontré Al Toque en esa dirección. Revisa que la otra PC tenga la app abierta y, si la red es pública, que haya elegido \"Recibir igual en esta red\". Algunos Wi-Fi públicos aíslan los equipos: ahí usa el punto de acceso del celular.",
        "I couldn't find Al Toque at that address. Check the other PC has the app open and, on a public network, chose \"Receive anyway on this network\". Some public Wi-Fi isolates devices: use a phone hotspot instead.");
    public static string ConnectInvalid => T("Ese código o dirección no es válido.", "That code or address isn't valid.");
    public static string ConnectWrongCode => T("Ese equipo no corresponde al código. Revisa que esté bien escrito.", "That device doesn't match the code. Check it's typed correctly.");
    public static string ConnectRejected => T("Ese equipo no acepta archivos de desconocidos. Pídele que active \"Todos (10 min)\" o empareja.",
        "That device doesn't accept files from strangers. Ask them to turn on \"Everyone (10 min)\" or pair.");
    public static string ShowMyCode => T("Mostrar mi código", "Show my code");
    public static string MyCodeHint => T("Díctale este código a tu compañero:", "Read this code to your teammate:");
    public static string ChooseFiles => T("Elegir archivos…", "Choose files…");
    public static string SendFilesTo(string who) => T($"Enviar a {who}", $"Send to {who}");

    // ── Ventana principal ────────────────────────────────────────────────────────
    public static string TabPeers => T("Compañeros", "Teammates");
    public static string TabHistory => T("Historial", "History");
    public static string TabSettings => T("Ajustes", "Settings");
    public static string NoTrustedPeers => T("Aún no tienes compañeros de confianza.", "You don't have trusted teammates yet.");
    public static string PairNew => T("Emparejar uno nuevo", "Pair a new one");
    public static string Favorite => T("Favorito", "Favorite");
    public static string AutoAccept => T("Aceptar automáticamente", "Accept automatically");
    public static string Rename => T("Nombre local", "Local name");
    public static string Forget => T("Quitar confianza", "Remove trust");
    public static string PairedOn(DateTimeOffset when) => T($"Emparejado el {when.ToLocalTime().ToString("d", Culture)}", $"Paired on {when.ToLocalTime().ToString("d", Culture)}");
    public static string ClearHistory => T("Borrar historial", "Clear history");
    public static string HistoryEmpty => T("El historial está vacío.", "History is empty.");
    public static string SettingAlias => T("Tu nombre en la red", "Your name on the network");
    public static string SettingAliasHint => T("Es lo que ven tus compañeros. No uses datos personales que no quieras compartir.",
        "It's what your teammates see. Don't use personal data you don't want to share.");
    public static string SettingFolder => T("Carpeta de destino", "Destination folder");
    public static string SettingConflicts => T("Si el nombre ya existe", "If the name already exists");
    public static string ConflictRename => T("Renombrar (recomendado)", "Rename (recommended)");
    public static string ConflictReplace => T("Reemplazar", "Replace");
    public static string ConflictSkip => T("Omitir", "Skip");
    public static string SettingMode => T("Modo de recepción", "Receive mode");
    public static string SettingLanguage => T("Idioma (al reiniciar)", "Language (after restart)");
    public static string LanguageAuto => T("Automático", "Automatic");
    public static string SettingReduceMotion => T("Reducir animaciones", "Reduce animations");
    public static string SettingSounds => T("Sonidos", "Sounds");
    public static string SettingStartup => OperatingSystem.IsWindows()
        ? T("Iniciar con Windows", "Start with Windows")
        : T("Iniciar con la sesión", "Start at login");
    public static string SettingShell => T("Menú del clic derecho y \"Enviar a\"", "Right-click menu and \"Send to\"");
    public static string SettingShellHint => T("En Windows 11 aparece dentro de \"Mostrar más opciones\" (o Mayús + F10).",
        "On Windows 11 it appears under \"Show more options\" (or Shift + F10).");
    public static string SettingExcludeVirtual => T("Ignorar VPN y adaptadores virtuales", "Ignore VPNs and virtual adapters");
    public static string SettingFirewall => T("Permitir en el firewall (solo redes privadas)", "Allow through firewall (private networks only)");
    public static string SettingFirewallHint => T("Pide permiso de administrador una sola vez.", "Asks for administrator permission once.");
    public static string SettingIdentity => T("Identidad de este equipo", "This device's identity");
    public static string SettingIdentityHint => T("Tus compañeros te reconocen por este color y estos símbolos.", "Your teammates recognize you by this color and these symbols.");
    public static string ResetIdentity => T("Restablecer identidad…", "Reset identity…");
    public static string ResetIdentityConfirm => T("Se generará una clave nueva y tendrás que volver a emparejar con todos. ¿Continuar?",
        "A new key will be generated and you'll need to pair with everyone again. Continue?");
    public static string RestartNeeded => T("Reinicia Al Toque para aplicar el cambio.", "Restart Al Toque to apply the change.");
    public static string MotwUnsupported => T("Esta carpeta está en un disco FAT32/exFAT: los archivos recibidos no llevarán la marca de origen que activa SmartScreen.",
        "This folder is on a FAT32/exFAT drive: received files won't carry the origin mark that triggers SmartScreen.");
    public static string TamperedTrust => T("La lista de compañeros de confianza se modificó fuera de Al Toque y se descartó por seguridad. Vuelve a emparejar.",
        "The trusted teammate list was modified outside Al Toque and was discarded for safety. Pair again.");
    public static string Version(string v) => T($"Versión {v}", $"Version {v}");
    public static string OpenExecutableConfirm(string name) => T($"\"{name}\" es un programa recibido por la red. ¿Seguro que quieres abrirlo?",
        $"\"{name}\" is a program received over the network. Are you sure you want to open it?");
    public static string Yes => T("Sí", "Yes");
    public static string No => T("No", "No");

    // ── Bienvenida ───────────────────────────────────────────────────────────────
    public static string WelcomeTitle => T("¡Hola! Soy Chasqui.", "Hi! I'm Chasqui.");
    public static string WelcomeBody => T("Llevo tus archivos a la PC de al lado, cifrados y sin internet. ¿Cómo quieres que te vean tus compañeros?",
        "I carry your files to the PC next door, encrypted and without internet. How should your teammates see you?");
    public static string WelcomeStart => T("Empezar", "Get started");

    // ── Formatos ─────────────────────────────────────────────────────────────────
    public static string Items(int n) => Spanish ? (n == 1 ? "1 archivo" : $"{n:N0} archivos") : (n == 1 ? "1 file" : $"{n:N0} files");
    public static string Sent => T("Enviado", "Sent");
    public static string Received => T("Recibido", "Received");
    public static string Yesterday => T("ayer", "yesterday");
    public static string Now => T("ahora", "now");
    public static string MinutesAgo(int m) => T($"hace {m} min", $"{m} min ago");
    public static string HoursAgo(int h) => T($"hace {h} h", $"{h} h ago");
}

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 || value >= 100 ? "0" : value >= 10 ? "0.#" : "0.#";
        return value.ToString(format, S.Culture) + " " + units[unit];
    }

    public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60)
            return $"{Math.Max(1, (int)t.TotalSeconds)} s";
        if (t.TotalMinutes < 60)
            return $"{(int)Math.Round(t.TotalMinutes)} min";
        return $"{(int)t.TotalHours} h {t.Minutes} min";
    }

    public static string Ago(DateTimeOffset when)
    {
        var d = DateTimeOffset.Now - when;
        if (d.TotalMinutes < 1)
            return S.Now;
        if (d.TotalHours < 1)
            return S.MinutesAgo((int)d.TotalMinutes);
        if (when.Date == DateTimeOffset.Now.Date)
            return S.HoursAgo((int)d.TotalHours);
        if (when.Date == DateTimeOffset.Now.Date.AddDays(-1))
            return S.Yesterday;
        return when.ToLocalTime().ToString("d MMM", S.Culture);
    }
}
