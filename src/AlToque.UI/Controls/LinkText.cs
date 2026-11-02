using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace AlToque.Controls;

/// <summary>
/// Texto de un mensaje con los enlaces (http, https y www.) subrayados y clicables: abren el navegador del sistema.
/// El texto se sigue pudiendo seleccionar y copiar; un clic sin arrastrar sobre un enlace lo abre.
/// Uso: <c>&lt;SelectableTextBlock c:LinkText.Text="{Binding Text}" /&gt;</c>.
/// </summary>
public static partial class LinkText
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, string?>("Text", typeof(LinkText));

    private static readonly AttachedProperty<IReadOnlyList<Link>?> LinksProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, IReadOnlyList<Link>?>("Links", typeof(LinkText));

    private sealed record Link(int Start, int Length, Uri Uri);

    static LinkText()
    {
        TextProperty.Changed.AddClassHandler<SelectableTextBlock>(OnTextChanged);
    }

    public static string? GetText(SelectableTextBlock block) => block.GetValue(TextProperty);

    public static void SetText(SelectableTextBlock block, string? value) => block.SetValue(TextProperty, value);

    // Sin espacios ni comillas/ángulos; la puntuación final (un punto, una coma…) no es parte del enlace.
    [GeneratedRegex(@"\b(?:https?://|www\.)[^\s<>""'`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>Los enlaces de un texto: dónde empiezan, cuánto miden y a qué dirección van (solo http/https).</summary>
    internal static IReadOnlyList<(int Start, int Length, Uri Uri)> Find(string text)
    {
        var list = new List<(int, int, Uri)>();
        foreach (Match m in UrlPattern().Matches(text))
        {
            var value = TrimEnd(m.Value);
            var address = value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0)
                list.Add((m.Index, value.Length, uri));
        }
        return list;
    }

    private static string TrimEnd(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (".,;:!?¡¿".Contains(last))
                url = url[..^1];
            // Un paréntesis de cierre se queda solo si abre otro dentro del enlace (Wikipedia, por ejemplo).
            else if (last == ')' && url.Count(c => c == '(') < url.Count(c => c == ')'))
                url = url[..^1];
            else if (last is ']' or '}' or '»' or '”')
                url = url[..^1];
            else
                break;
        }
        return url;
    }

    private static void OnTextChanged(SelectableTextBlock block, AvaloniaPropertyChangedEventArgs e)
    {
        var text = e.NewValue as string ?? "";
        var found = Find(text);
        block.SetValue(LinksProperty, found.Select(f => new Link(f.Start, f.Length, f.Uri)).ToList());
        if (found.Count == 0)
        {
            block.Inlines?.Clear();
            block.Text = text;
            return;
        }

        var inlines = new InlineCollection();
        var at = 0;
        foreach (var (start, length, _) in found)
        {
            if (start > at)
                inlines.Add(new Run(text[at..start]));
            inlines.Add(new Run(text.Substring(start, length)) { TextDecorations = TextDecorations.Underline });
            at = start + length;
        }
        if (at < text.Length)
            inlines.Add(new Run(text[at..]));
        block.Inlines = inlines;

        if (block.Tag is not "links")
        {
            block.Tag = "links";
            block.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            block.AddHandler(InputElement.PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    /// <summary>El enlace bajo el puntero: el que tiene algún trozo (puede ocupar varias líneas) en ese punto.</summary>
    private static Link? LinkAt(SelectableTextBlock block, Point point)
    {
        if (block.GetValue(LinksProperty) is not { Count: > 0 } links)
            return null;
        var p = new Point(point.X - block.Padding.Left, point.Y - block.Padding.Top);
        return links.FirstOrDefault(l => block.TextLayout.HitTestTextRange(l.Start, l.Length).Any(r => r.Contains(p)));
    }

    private static Link? LinkAt(SelectableTextBlock block, int index) =>
        block.GetValue(LinksProperty) is { Count: > 0 } links ? links.FirstOrDefault(l => index >= l.Start && index < l.Start + l.Length) : null;

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is SelectableTextBlock block)
            block.Cursor = LinkAt(block, e.GetPosition(block)) is null ? new Cursor(StandardCursorType.Ibeam) : new Cursor(StandardCursorType.Hand);
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Solo un clic: si se arrastró para seleccionar texto, no se abre nada.
        if (sender is not SelectableTextBlock block || e.InitialPressMouseButton != MouseButton.Left || block.SelectionStart != block.SelectionEnd)
            return;
        // Lo que hay bajo el puntero; si no, el carácter donde el clic dejó el cursor de texto.
        if ((LinkAt(block, e.GetPosition(block)) ?? LinkAt(block, block.SelectionStart)) is not { } link)
            return;
        if (TopLevel.GetTopLevel(block)?.Launcher is { } launcher)
            _ = launcher.LaunchUriAsync(link.Uri);
    }
}
