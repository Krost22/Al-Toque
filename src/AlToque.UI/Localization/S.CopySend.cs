namespace AlToque.Localization;

/// <summary>Textos de «Enviar lo copiado» (atajo Ctrl+Alt+C y botón al copiar).</summary>
public static partial class S
{
    public static string SectionCopySend => T("Enviar lo copiado", "Send what you copy");
    public static string CopySendSetting(string shortcut) => T($"Enviar lo copiado con {shortcut}", $"Send what you copy with {shortcut}");
    public static string CopySendSettingHint(string shortcut) => T(
        $"Copia un texto, un enlace, una imagen o archivos y pulsa {shortcut} en cualquier app: Al Toque lo manda sin abrir nada.",
        $"Copy a text, a link, an image or files and press {shortcut} in any app: Al Toque sends it without opening anything.");
    public static string CopySendTargetTitle => T("Enviar a", "Send to");
    public static string CopySendTargetHint => T("Siempre a la misma persona, o elegir cada vez en una lista corta.",
        "Always to the same person, or pick each time from a short list.");
    public static string CopySendAskEachTime => T("Preguntar cada vez", "Ask each time");
    public static string CopySendOfferSetting => T("Ofrecer al copiar", "Offer when copying");
    public static string CopySendOfferHint => T(
        "Al copiar algo aparece unos segundos un botón para enviarlo; si no lo tocas, se va solo. Lo que copian los gestores de contraseñas nunca se ofrece.",
        "When you copy something, a button to send it shows up for a few seconds and goes away on its own. Anything copied from password managers is never offered.");
    public static string CopySendTray(string shortcut) => T($"Enviar lo copiado ({shortcut})", $"Send what you copy ({shortcut})");

    public static string CopySendHotkeyTaken => T("Otro programa ya usa este atajo: ciérralo o cambia su atajo para usarlo aquí.",
        "Another program already uses this shortcut: close it or change its shortcut to use it here.");
    public static string CopySendHotkeyFailed => T("Este equipo no dejó registrar el atajo. El botón al copiar sigue funcionando.",
        "This computer didn't allow the shortcut. The button when copying still works.");
    public static string CopySendHotkeyWayland => T(
        "En Wayland las apps no pueden tener atajos globales. Usa «Ofrecer al copiar» o crea un atajo del sistema que ejecute «altoque clipboard».",
        "On Wayland apps can't have global shortcuts. Use “Offer when copying” or create a system shortcut that runs “altoque clipboard”.");

    public static string CopySendOfferTitle => T("¿Enviar lo que copiaste?", "Send what you copied?");
    public static string CopySendPickTitle => T("¿A quién se lo envías?", "Who do you send it to?");
    public static string CopySendTo(string name) => T($"Enviar a {name}", $"Send to {name}");
    public static string CopySendChoose => T("Enviar…", "Send…");
    public static string CopySendOther => T("Elegir a otra persona", "Pick someone else");
    public static string CopySendImage => T("Imagen", "Image");
    public static string CopySendOnline => T("En línea", "Online");
    public static string CopySendLater => T("Le llega al conectarse", "Gets it when online");
    public static string CopySendNothing => T("No hay nada copiado para enviar", "There's nothing copied to send");
    public static string CopySendNoPeers => T("Aún no tienes compañeros: empareja un equipo primero", "No teammates yet: pair a device first");
    public static string CopySendSent(string name) => T($"Enviado a {name}", $"Sent to {name}");
    public static string CopySendQueued(string name) => T($"Le llegará a {name} al conectarse", $"{name} will get it when online");
    public static string CopySendSending(string name) => T($"Enviando a {name}…", $"Sending to {name}…");
    public static string CopySendFailed => T("No se pudo preparar lo copiado", "Couldn't prepare what you copied");
    public static string CopiedTextName(DateTime when) => T($"Texto copiado {when:yyyy-MM-dd HH.mm.ss}.txt", $"Copied text {when:yyyy-MM-dd HH.mm.ss}.txt");
}
