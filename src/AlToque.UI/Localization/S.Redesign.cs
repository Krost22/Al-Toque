namespace AlToque.Localization;

/// <summary>Textos del rediseño (panel, tarjetas, emparejar, ajustes y avatares).</summary>
public static partial class S
{
    // ── Panel rápido ─────────────────────────────────────────────────────────────
    public static string ReadyTitle => T("Listo para enviar", "Ready to send");
    public static string ReadyBody => T("Arrastra archivos aquí o usa el clic derecho.", "Drag files here or use right-click.");
    public static string ChooseFilesShort => T("Elegir archivos", "Choose files");
    public static string PeersHeader => T("Compañeros", "Teammates");
    public static string InThisNetwork(int n) => T($" · {n} en esta red", $" · {n} on this network");
    public static string PairShort => T("Emparejar", "Pair");
    public static string SegNobody => T("Nadie", "No one");
    public static string SegTrusted => T("Confianza", "Trusted");
    public static string SegEveryone => T("Todos", "Everyone");
    public static string SeeHistory => T("Ver historial", "See history");
    public static string WhoCanSend => T("Quién puede enviarte", "Who can send you files");
    /// <param name="visibleNearby">Los equipos cercanos ven el nombre para pedir conectarse (lo normal en redes privadas).</param>
    public static string ModeHintTrusted(int n, bool visibleNearby = false) => (n, visibleNearby) switch
    {
        (0, true) => T("Aún no tienes compañeros. Los equipos cercanos te ven y pueden pedirte conectar.",
            "No teammates yet. Nearby devices can see you and ask to connect."),
        (0, false) => T("Aún no tienes compañeros de confianza. Empareja uno para recibir.", "You have no trusted teammates yet. Pair one to receive."),
        (1, true) => T("Solo tu compañero puede enviarte. Los equipos cercanos ven tu nombre para pedirte conectar.",
            "Only your teammate can send you files. Nearby devices see your name to ask to connect."),
        (1, false) => T("Solo tu compañero emparejado. Los demás ni siquiera ven tu nombre.", "Only your paired teammate. Others can't even see your name."),
        (_, true) => T($"Solo tus {n} compañeros pueden enviarte. Los equipos cercanos ven tu nombre para pedirte conectar.",
            $"Only your {n} teammates can send you files. Nearby devices see your name to ask to connect."),
        _ => T($"Solo tus {n} compañeros emparejados. Los demás ni siquiera ven tu nombre.", $"Only your {n} paired teammates. Others can't even see your name."),
    };
    public static string ModeHintEveryone(TimeSpan? left) => left is { } l
        ? T($"Cualquiera en esta red puede pedirte permiso. Vuelve a «Confianza» en {l:m\\:ss}.", $"Anyone on this network can ask you. Back to “Trusted” in {l:m\\:ss}.")
        : T("Cualquiera en esta red puede pedirte permiso.", "Anyone on this network can ask you.");
    public static string DragTitle => T("Suéltalos sobre un compañero", "Drop them on a teammate");
    public static string DropForLater => T("O suéltalos aquí", "Or drop them here");
    public static string DropForLaterHint => T("para elegir después a quién y añadir un mensaje", "to choose who later and add a message");
    public static string DropToSend => T("Soltar para enviar", "Drop to send");
    public static string NobodyTitle => T("No veo a nadie en esta red", "I can't see anyone on this network");
    public static string StillSearching(string? network, int seconds) => network is { Length: > 0 }
        ? T($"Sigo buscando en «{network}» · {seconds} s", $"Still looking on “{network}” · {seconds} s")
        : T($"Sigo buscando · {seconds} s", $"Still looking · {seconds} s");
    public static string NobodyVisibleTrusted => T("Tu PC está visible para tus compañeros de confianza.", "Your PC is visible to your trusted teammates.");
    public static string NobodyVisibleNearby => T("Tu PC está visible para los equipos cercanos.", "Your PC is visible to nearby devices.");
    public static string NobodyVisibleEveryone => T("Tu PC está visible para todos en esta red.", "Your PC is visible to everyone on this network.");
    public static string NobodyInvisible => T("Tu PC está invisible: elegiste «Nadie».", "Your PC is invisible: you chose “Nobody”.");
    public static string NobodyBlocking => T("Esta red puede estar bloqueando la búsqueda.", "This network may be blocking discovery.");
    public static string NobodyBlockingHint => T("Pasa mucho en Wi-Fi de invitados. Con un código sí funciona.", "Common on guest Wi-Fi. A code still works.");
    public static string NobodyOpen => T("¿Tu compañero tiene Al Toque abierto?", "Does your teammate have Al Toque open?");
    public static string NobodyOpenHint => T("Y conectado a la misma red que tú.", "And on the same network as you.");
    public static string FileCountSize(int n, string size) => $"{Items(n)} · {size}";
    public static string ToPeer(string name) => T($"Para {name}", $"To {name}");
    public static string FromPeer(string name) => T($"De {name}", $"From {name}");
    public static string RecentFailed(string name) => T($"No llegó a {name}", $"Didn't reach {name}");
    public static string RecentDeclined(string name) => T($"{name} lo rechazó", $"{name} declined");
    public static string RecentCancelled => T("Cancelado", "Cancelled");
    public static string RecentYouDeclined(string name) => T($"Rechazaste lo de {name}", $"You declined {name}'s files");
    public static string RecentPartial => T("Llegó solo una parte", "Only part arrived");
    public static string ItemsNamed(string first, int n) => n <= 1 ? first : T($"{first} y {n - 1} más", $"{first} and {n - 1} more");

    // ── Solicitud entrante ───────────────────────────────────────────────────────
    public static string WantsToSendSized(int n, string size) => T($"quiere enviarte {Items(n)} · {size}", $"wants to send you {Items(n)} · {size}");
    public static string WantsToSendNamed(string name, string size) => T($"quiere enviarte {name} · {size}", $"wants to send you {name} · {size}");
    public static string ChipUnverified => T("No verificado", "Not verified");
    public static string ChipTrusted => T("De confianza", "Trusted");
    public static string UnverifiedExplain(string name) =>
        T($"No es un compañero emparejado. Antes de aceptar, pregúntale a {name} si en su Al Toque aparecen estos símbolos:",
          $"This isn't a paired teammate. Before accepting, ask {name} whether their Al Toque shows these symbols:");
    public static string PairInstead => T("Emparejar en su lugar", "Pair instead");
    public static string ProgramUnchecked => T("Es un programa. Lo dejé sin marcar: márcalo solo si lo esperabas.", "It's a program. I left it unchecked: check it only if you expected it.");
    public static string SaveInShort => T("Guardar en", "Save to");
    public static string AcceptCount(int n, int total) => n == total
        ? T("Aceptar", "Accept")
        : T($"Aceptar {Items(n)}", $"Accept {Items(n)}");
    public static string AlwaysAcceptFrom(string name) => T($"Aceptar siempre de {name}", $"Always accept from {name}");
    public static string NothingChecked => T("No marcaste ningún archivo.", "No files are checked.");

    // ── Tarjetas de envío ────────────────────────────────────────────────────────
    public static string WaitingAccept(string name) => T($"Esperando que {name} acepte…", $"Waiting for {name} to accept…");
    public static string AutoCancelIn(TimeSpan left) => T($"Se cancela solo en {left:m\\:ss}", $"Cancels itself in {left:m\\:ss}");
    public static string SendingTo2(string name) => T($"Enviando a {name}", $"Sending to {name}");
    public static string ReceivingFrom2(string name) => T($"Recibiendo de {name}", $"Receiving from {name}");
    public static string FileOf(string file, int i, int n) => n <= 1 ? file : T($"{file} · archivo {i} de {n}", $"{file} · file {i} of {n}");
    public static string BytesOf(string done, string total) => T($"{done} de {total}", $"{done} of {total}");
    public static string Left(string eta) => T($"quedan {eta}", $"{eta} left");
    public static string ReceivedTitle(int n, string name) => T($"Listo: {Items(n)} de {name}", $"Done: {Items(n)} from {name}");
    public static string SentTitle(string name) => T($"Listo: llegó a {name}", $"Done: delivered to {name}");
    public static string SavedIn(string where) => T($"Guardados en {where}", $"Saved to {where}");
    public static string DeclinedTitle(string name) => T($"{name} rechazó el envío", $"{name} declined");
    public static string DeclinedBody => T("No se envió nada. Puedes intentarlo de nuevo cuando quieras.", "Nothing was sent. You can try again whenever you like.");
    public static string CouldNotConnect(string name) => T($"No pude conectar con {name}", $"I couldn't reach {name}");
    public static string SameNetworkQuestion => T("¿Está encendida y en la misma red?", "Is it on and on the same network?");
    public static string OpenNamed(string name) => T($"Abrir {name}", $"Open {name}");
    public static string Pause => T("Pausar", "Pause");
    public static string Resume => T("Reanudar", "Resume");
    public static string Paused => T("En pausa", "Paused");

    // ── Emparejar ────────────────────────────────────────────────────────────────
    public static string Step(int n, string what) => T($"Paso {n} de 3 · {what}", $"Step {n} of 3 · {what}");
    public static string StepChoose => T("Elegir el equipo", "Choose the device");
    public static string StepCompare => T("Comparar el código", "Compare the code");
    public static string StepDone => T("Listo", "Done");
    public static string PairWith(string name) => T($"Emparejar con {name}", $"Pair with {name}");
    public static string You => T("Tú", "You");
    public static string YourTeammate => T("Tu compañero", "Your teammate");
    public static string SeesThisCode(string name) => T($"¿{name} ve exactamente este código en su pantalla?", $"Does {name} see exactly this code on their screen?");
    public static string ReadItAloud => T("Léanlo en voz alta. Si no coincide, alguien podría estar en medio y no se empareja nada.",
        "Read it out loud. If it doesn't match, someone could be in the middle and nothing gets paired.");
    public static string NoMatch => T("No coincide", "Doesn't match");
    public static string YesMatch => T("Sí, coincide", "Yes, it matches");
    public static string NowTrusted(string name) => T($"{name} ya es de confianza", $"{name} is now trusted");
    public static string NowTrustedBody => T("Ahora aparece en clic derecho › Enviar con Al Toque, y sus envíos te llegan como verificados.",
        "It now shows up in right-click › Send with Al Toque, and its transfers arrive verified.");
    public static string AcceptWithoutAsking => T("Aceptar sus envíos sin preguntar", "Accept its transfers without asking");
    public static string AcceptWithoutAskingHint => T("Recomendado solo para equipos tuyos.", "Recommended only for your own devices.");
    public static string Done => T("Listo", "Done");

    // ── Ventana principal ────────────────────────────────────────────────────────
    public static string NavAbout => T("Acerca de", "About");
    public static string YourDevice => T("Tu equipo en la red", "Your device on the network");
    public static string YourName => T("Tu nombre", "Your name");
    public static string YourNameHint => T("Es lo que ven tus compañeros. Evita datos personales.", "It's what your teammates see. Avoid personal data.");
    public static string YourFingerprint(string code) => T($"Tu huella · {code}", $"Your fingerprint · {code}");
    public static string ChangeAvatar => T("Cambiar avatar", "Change avatar");
    public static string PickAvatarTitle => T("Elige tu avatar", "Choose your avatar");
    public static string PickAvatarHint => T("Así te verán tus compañeros. El color sale de tu identidad.", "This is how your teammates will see you. The color comes from your identity.");
    public static string SectionReceive => T("Recibir", "Receive");
    public static string DestFolder => T("Carpeta de destino", "Destination folder");
    public static string ChangeEllipsis => T("Cambiar…", "Change…");
    public static string WhoCanSendHint => T("«Todos» vuelve sola a «Confianza» a los 10 minutos.", "“Everyone” goes back to “Trusted” after 10 minutes.");
    public static string IfExists => T("Si el archivo ya existe", "If the file already exists");
    public static string IfExistsHint => T("Renombrar guarda «foto (2).jpg» y no pisa nada.", "Rename saves “photo (2).jpg” and overwrites nothing.");
    public static string RenameRecommended => T("Renombrar (recomendado)", "Rename (recommended)");
    public static string SectionSystem => OperatingSystem.IsWindows() ? "Windows" : T("Sistema", "System");
    public static string StartWithSystem => OperatingSystem.IsWindows() ? T("Iniciar con Windows", "Start with Windows") : T("Iniciar con la sesión", "Start with the session");
    public static string StartHint => T("Para que el clic derecho funcione sin abrir nada.", "So right-click works without opening anything.");
    public static string ShellTitle => T("Clic derecho y «Enviar a»", "Right-click and “Send to”");
    public static string ShellHint2 => T("En Windows 11 está dentro de «Mostrar más opciones» (Mayús + F10).", "On Windows 11 it's under “Show more options” (Shift + F10).");
    public static string SectionGeneral => "General";
    public static string Sounds => T("Sonidos", "Sounds");
    public static string SoundsHint => T("Un toque corto al llegar una solicitud y al terminar. Respeta «No molestar».", "A short chime when a request arrives and when done. Respects “Do not disturb”.");
    public static string Animations => T("Animaciones", "Animations");
    public static string AnimationsHint => T("Con «Reducidas», Chasqui usa poses fijas y fundidos, sin confeti.", "With “Reduced”, Chasqui uses still poses and fades, no confetti.");
    public static string AnimSystem => OperatingSystem.IsWindows() ? T("Como en Windows", "Like Windows") : T("Como el sistema", "Like the system");
    public static string AnimFull => T("Completas", "Full");
    public static string AnimReduced => T("Reducidas", "Reduced");
    public static string Language => T("Idioma", "Language");
    public static string LanguageHint => T("Se aplica al reiniciar Al Toque.", "Applies after restarting Al Toque.");
    public static string LanguageSpanish => "Español";
    public static string LanguageEnglish => "English";
    public static string On => T("Activado", "On");
    public static string Off => T("Desactivado", "Off");
    public static string SectionNetwork => T("Red y seguridad", "Network and security");
    public static string AboutBody => T("Pasa archivos entre tus equipos de la red local, cifrado de punta a punta y sin nube.",
        "Send files between your devices on the local network, end-to-end encrypted and with no cloud.");
    public static string License => T("Código abierto con licencia MIT.", "Open source under the MIT license.");
    public static string PeersPageHint => T("Tus equipos de confianza. Sus envíos llegan verificados.", "Your trusted devices. Their transfers arrive verified.");
}
