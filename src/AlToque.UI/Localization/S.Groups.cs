namespace AlToque.Localization;

/// <summary>Textos de los grupos de compañeros y de los envíos a varios.</summary>
public static partial class S
{
    // ── Fichas de grupos ────────────────────────────────────────────────────────
    public static string GroupAll => T("Todos", "All");
    public static string GroupNew => T("Grupo", "Group");
    public static string GroupNewTip => T("Crear un grupo (equipo, sesión, familia…)", "Create a group (team, session, family…)");
    public static string GroupNameTitle => T("Nombre del grupo", "Group name");
    public static string GroupNamePlaceholder => T("Por ejemplo: Oficina, Diseño, Familia", "For example: Office, Design, Family");
    public static string GroupCreate => T("Crear grupo", "Create group");
    public static string GroupRename => T("Renombrar…", "Rename…");
    public static string GroupArchive => T("Archivar", "Archive");
    public static string GroupUnarchive => T("Mostrar de nuevo", "Show again");
    public static string GroupDelete => T("Eliminar grupo", "Delete group");
    public static string GroupDeleteConfirm(string name) => T($"¿Eliminar el grupo «{name}»? Tus compañeros siguen siéndolo.",
        $"Delete the group “{name}”? Your teammates stay your teammates.");
    public static string GroupSendTo(string name) => T($"Enviar archivos a «{name}»…", $"Send files to “{name}”…");
    public static string GroupArchived => T("Grupos archivados", "Archived groups");
    public static string GroupEmpty(string name) => T($"Aún no hay nadie en «{name}». Arrastra aquí a un compañero o usa su menú (clic derecho).",
        $"No one in “{name}” yet. Drag a teammate here or use their menu (right-click).");
    public static string GroupDropHint(string name) => T($"Soltar para añadir a «{name}»", $"Drop to add to “{name}”");

    // ── Menú de un compañero ────────────────────────────────────────────────────
    public static string PeerMenuChat => T("Abrir chat", "Open chat");
    public static string PeerMenuSelect => T("Seleccionar", "Select");
    public static string PeerMenuUnselect => T("Quitar de la selección", "Unselect");
    public static string PeerMenuGroups => T("Grupos", "Groups");
    public static string PeerMenuNewGroup => T("Nuevo grupo con este compañero…", "New group with this teammate…");
    public static string PeerMenuSendFiles => T("Enviar archivos…", "Send files…");

    // ── Selección de varios ─────────────────────────────────────────────────────
    public static string SelectMany => T("Seleccionar", "Select");
    public static string SelectManyTip => T("Elegir varios compañeros para enviarles lo mismo (también con Ctrl+clic)",
        "Pick several teammates to send them the same thing (also Ctrl+click)");
    public static string SelectedCount(int n) => n == 1 ? T("1 seleccionado", "1 selected") : T($"{n} seleccionados", $"{n} selected");
    public static string SendToSelected(int n) => n == 1 ? T("Enviar a 1", "Send to 1") : T($"Enviar a {n}", $"Send to {n}");
    public static string SelectCancel => T("Cancelar", "Cancel");
    public static string SelectHint => T("Toca los compañeros a los que quieres enviar", "Tap the teammates you want to send to");

    // ── Envío a varios: una tarjeta con el progreso de todos ────────────────────
    public static string SendingToMany(int n) => T($"Enviando a {n} compañeros", $"Sending to {n} teammates");
    public static string SendingToGroup(string name) => T($"Enviando a «{name}»", $"Sending to “{name}”");
    public static string SentToMany(int n) => T($"Listo: llegó a los {n}", $"Done: delivered to all {n}");
    public static string SentToGroup(string name) => T($"Listo: llegó a todo «{name}»", $"Done: delivered to all of “{name}”");
    public static string SentToSome(int ok, int n) => T($"Llegó a {ok} de {n}", $"Delivered to {ok} of {n}");
    public static string ReadyOf(int ok, int n) => T($"{ok} de {n} listos", $"{ok} of {n} done");
    public static string RetryFailedOnes(int n) => n == 1 ? T("Reintentar con 1", "Retry 1") : T($"Reintentar con {n}", $"Retry {n}");
    public static string RecipientDone(string name) => T($"{name}: llegó", $"{name}: delivered");
    public static string RecipientFailed(string name) => T($"{name}: no llegó", $"{name}: not delivered");
    public static string RecipientWaiting(string name) => T($"{name}: esperando", $"{name}: waiting");
    public static string RecipientSending(string name) => T($"{name}: enviando", $"{name}: sending");
}
