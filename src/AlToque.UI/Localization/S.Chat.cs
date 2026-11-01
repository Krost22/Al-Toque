namespace AlToque.Localization;

/// <summary>Textos de los chats.</summary>
public static partial class S
{
    public static string ChatPlaceholder => T("Escribe un mensaje…", "Write a message…");
    public static string ChatSend => T("Enviar mensaje", "Send message");
    public static string ChatAttach => T("Enviar archivos", "Send files");
    public static string ChatBack => T("Volver", "Back");
    public static string ChatOnline => T("En línea", "Online");
    public static string ChatOffline => T("Sin conexión: tus mensajes le llegarán cuando vuelva", "Offline: your messages will reach them when they're back");
    public static string ChatEmpty(string name) => T($"Escríbele a {name}. Lo que se manden también queda aquí.",
        $"Write to {name}. Whatever you send each other shows up here too.");
    public static string ChatToday => T("Hoy", "Today");
    public static string ChatYesterday => T("Ayer", "Yesterday");
    public static string ChatPending => T("Pendiente: se entrega cuando se conecte", "Pending: delivered when they connect");
    public static string ChatDelivered => T("Entregado", "Delivered");
    public static string ChatReply => T("Responder", "Reply");
    public static string ChatUnread(int n) => n == 1 ? T("1 mensaje sin leer", "1 unread message") : T($"{n} mensajes sin leer", $"{n} unread messages");
    public static string ChatFiles(int n) => n == 1 ? T("1 archivo", "1 file") : T($"{n} archivos", $"{n} files");
    public static string ChatSending => T("Enviando…", "Sending…");
    public static string ChatReceiving => T("Recibiendo…", "Receiving…");
    public static string ChatFileDeclined => T("Rechazado", "Declined");
    public static string ChatFileCancelled => T("Cancelado", "Cancelled");
    public static string ChatFileFailed => T("No llegó", "Didn't arrive");
    public static string ChatFilePartial => T("Llegó solo una parte", "Only part arrived");
    public static string ChatShowInFolder => T("Mostrar en la carpeta", "Show in folder");
    public static string ChatMore => T("Más opciones", "More options");
    public static string ChatClear => T("Borrar conversación", "Delete conversation");
    public static string ChatClearConfirm(string name) => T(
        $"¿Borrar la conversación con {name} de este equipo? Los archivos recibidos se quedan en su carpeta.",
        $"Delete the conversation with {name} from this device? Received files stay in their folder.");
    public static string ChatOpenChat(string name) => T($"Abrir el chat con {name}", $"Open the chat with {name}");

    // ── Fotos ───────────────────────────────────────────────────────────────────
    public static string ChatPhoto => T("Foto", "Photo");
    public static string ChatPhotos(int n) => n == 1 ? ChatPhoto : T($"{n} fotos", $"{n} photos");
    public static string ChatPhotoMissing => T("Ya no está en este equipo", "No longer on this device");
    public static string ChatPhotoOpen => T("Ver la foto", "View photo");

    /// <summary>Nombre del archivo para una imagen pegada con Ctrl+V.</summary>
    public static string PastedImageName(DateTime when) =>
        T($"Imagen {when:yyyy-MM-dd HH.mm.ss}.png", $"Image {when:yyyy-MM-dd HH.mm.ss}.png");

    public static string ViewerOpenWith => T("Abrir con la app del sistema", "Open with the system app");
    public static string ViewerPrevious => T("Anterior", "Previous");
    public static string ViewerNext => T("Siguiente", "Next");
    public static string ViewerCounter(int index, int total) => $"{index} / {total}";
    public static string ViewerUnreadable => T("No se pudo mostrar esta imagen.", "This image couldn't be shown.");

    // ── Chat fijado en la esquina ───────────────────────────────────────────────
    public static string ChatPin => T("Fijar en la esquina de la pantalla", "Pin to the corner of the screen");
    public static string ChatUnpin => T("Devolver al panel", "Back to the panel");
    public static string ChatWindowTitle(string name) => T($"{name} · Al Toque", $"{name} · Al Toque");
}
