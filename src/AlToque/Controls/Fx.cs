using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace AlToque.Controls;

/// <summary>Preferencia de movimiento de toda la app: con "reducido" solo hay fundidos y cambios de color.</summary>
public static class MotionPrefs
{
    private static bool _reduced;

    public static bool Reduced
    {
        get => _reduced;
        set
        {
            if (_reduced == value)
                return;
            _reduced = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;
}

/// <summary>Curva arbitraria como <see cref="Easing"/> de Avalonia (admite rebasar 1 para los rebotes).</summary>
public sealed class FnEasing(Func<double, double> f) : Easing
{
    public override double Ease(double progress) => f(progress);
}

/// <summary>
/// Animaciones de interfaz (entradas, salidas, saltitos y sacudidas) con las duraciones y curvas del sistema de
/// movimiento. Conviene aplicarlas a contenedores, no a controles que ya tengan transiciones propias.
/// </summary>
public static class Fx
{
    public static readonly Easing EnterEasing = new FnEasing(Ease.Enter);
    public static readonly Easing ExitEasing = new FnEasing(Ease.Exit);
    public static readonly Easing BounceEasing = new FnEasing(Ease.Bounce);
    private static readonly Easing Linear = new LinearEasing();

    /// <summary>Entra desde abajo (16 px) con opacidad, en 250 ms con la curva Entrar.</summary>
    public static Task Enter(Visual v, int delayMs = 0, double dy = 16, int ms = 250) =>
        MotionPrefs.Reduced
            ? Run(v, ms, delayMs, EnterEasing, (0, 0, "none"), (1, 1, "none"))
            : Run(v, ms, delayMs, EnterEasing, (0, 0, I($"translateY({dy}px)")), (1, 1, "translateY(0px)"));

    /// <summary>Aparece creciendo con rebote: para logros, insignias y personajes.</summary>
    public static Task Pop(Visual v, int delayMs = 0, double from = 0.5, int ms = 420) =>
        MotionPrefs.Reduced
            ? Run(v, 167, delayMs, EnterEasing, (0, 0, "none"), (1, 1, "none"))
            : Run(v, ms, delayMs, BounceEasing, (0, 0, I($"scale({from})")), (0.35, 1, I($"scale({Mid(from, 1, 0.8)})")), (1, 1, "scale(1)"));

    /// <summary>Sale hacia abajo con opacidad en 167 ms (curva Salir).</summary>
    public static Task Exit(Visual v, double dy = 8, int ms = 167) =>
        MotionPrefs.Reduced
            ? Run(v, ms, 0, ExitEasing, (0, 1, "none"), (1, 0, "none"), forward: true)
            : Run(v, ms, 0, ExitEasing, (0, 1, "translateY(0px)"), (1, 0, I($"translateY({dy}px)")), forward: true);

    /// <summary>"No": sacudida horizontal que se amortigua.</summary>
    public static Task Shake(Visual v, double amp = 9)
    {
        if (MotionPrefs.Reduced)
            return Task.CompletedTask;
        return Run(v, 460, 0, Linear,
            (0, 1, "translateX(0px)"), (0.12, 1, I($"translateX({-amp}px)")), (0.28, 1, I($"translateX({amp * 0.8}px)")),
            (0.44, 1, I($"translateX({-amp * 0.55}px)")), (0.6, 1, I($"translateX({amp * 0.35}px)")),
            (0.78, 1, I($"translateX({-amp * 0.15}px)")), (1, 1, "translateX(0px)"));
    }

    /// <summary>Entradas en cascada: cada elemento 45 ms después del anterior.</summary>
    public static void Stagger(IEnumerable<Visual> items, int stepMs = 45, int firstDelayMs = 0, double dy = 12)
    {
        var i = 0;
        foreach (var v in items)
            _ = Enter(v, firstDelayMs + i++ * stepMs, dy);
    }

    private static double Mid(double a, double b, double t) => a + (b - a) * t;

    private static string I(FormattableString s) => FormattableString.Invariant(s);

    private static Task Run(Visual v, int ms, int delayMs, Easing easing, params (double Cue, double Opacity, string Transform)[] frames) =>
        Run(v, ms, delayMs, easing, frames, forward: false);

    private static Task Run(Visual v, int ms, int delayMs, Easing easing, (double Cue, double Opacity, string Transform) a,
        (double Cue, double Opacity, string Transform) b, bool forward) => Run(v, ms, delayMs, easing, [a, b], forward);

    private static Task Run(Visual v, int ms, int delayMs, Easing easing, (double Cue, double Opacity, string Transform)[] frames, bool forward)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            Easing = easing,
            FillMode = forward ? FillMode.Both : FillMode.Backward,
        };
        foreach (var (cue, opacity, transform) in frames)
        {
            var kf = new KeyFrame { Cue = new Cue(cue) };
            kf.Setters.Add(new Setter(Visual.OpacityProperty, opacity));
            kf.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(transform)));
            anim.Children.Add(kf);
        }
        try
        {
            return anim.RunAsync(v, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }
}
