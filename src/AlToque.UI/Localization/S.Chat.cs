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
}
