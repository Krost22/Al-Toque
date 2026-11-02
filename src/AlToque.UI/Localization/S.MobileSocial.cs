namespace AlToque.Localization;

/// <summary>Textos del teléfono para chats, grupos, enviar a varios y "Mantener conectado".</summary>
public static partial class S
{
    public static string MobileKeepTitle => T("Al Toque está conectado", "Al Toque is connected");
    public static string MobileKeepBody => T("Recibes mensajes y archivos aunque cierres la app.", "You get messages and files even with the app closed.");
    public static string MobileMessagesChannel => T("Mensajes", "Messages");
    public static string MobileKeepConnected => T("Mantener conectado", "Stay connected");
    public static string MobileKeepConnectedHint => T(
        "Recibe mensajes y archivos con la app cerrada. Queda una notificación fija y gasta un poco más de batería.",
        "Get messages and files with the app closed. A permanent notification stays and it uses a bit more battery.");
    public static string MobileMore(string name) => T($"Más opciones para {name}", $"More options for {name}");
    public static string MobileGroupMore => T("Opciones del grupo", "Group options");
    public static string MobileFavorite => T("Marcar como favorito", "Mark as favorite");
    public static string MobileUnfavorite => T("Quitar de favoritos", "Remove from favorites");
    public static string MobileRemovePeer => T("Quitar de mis compañeros", "Remove from my teammates");
    public static string MobileHidePeer => T("Ocultar en esta sesión", "Hide for this session");
    public static string MobileGroupsOf(string name) => T($"Grupos de {name}", $"{name}'s groups");
    public static string MobileNoGroups => T("Aún no tienes grupos.", "You have no groups yet.");
    public static string MobileYes => T("Sí", "Yes");
    public static string MobileNo => T("No", "No");
    public static string MobileSave => T("Guardar", "Save");
    public static string MobileConnectRequestTitle(string name) => T($"{name} quiere conectarse contigo", $"{name} wants to connect with you");
}
