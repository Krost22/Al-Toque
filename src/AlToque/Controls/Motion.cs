using System.Diagnostics;

namespace AlToque.Controls;

/// <summary>
/// Reloj común de las animaciones dibujadas a mano (Chasqui, avatares, pista de progreso). La herramienta de
/// capturas lo congela para obtener fotogramas exactos.
/// </summary>
public static class AnimClock
{
    /// <summary>Si tiene valor, el tiempo queda detenido en esos segundos.</summary>
    public static double? Frozen { get; set; }

    public static double Now => Frozen ?? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

/// <summary>Curvas del sistema de movimiento (sección "Movimiento" del diseño).</summary>
public static class Ease
{
    /// <summary>Entrar: rápido al salir, lento al llegar · cubic-bezier(0, 0, 0, 1).</summary>
    public static double Enter(double t) => Bezier(0, 0, 0, 1, t);

    /// <summary>Salir · cubic-bezier(1, 0, 1, 1).</summary>
    public static double Exit(double t) => Bezier(1, 0, 1, 1, t);

    /// <summary>Rebote, solo para Chasqui y logros · cubic-bezier(.34, 1.56, .64, 1).</summary>
    public static double Bounce(double t) => Bezier(0.34, 1.56, 0.64, 1, t);

    /// <summary>Salida suave para resortes y aterrizajes.</summary>
    public static double OutCubic(double t) => 1 - Math.Pow(1 - Clamp01(t), 3);

    public static double InOutSine(double t) => -(Math.Cos(Math.PI * Clamp01(t)) - 1) / 2;

    /// <summary>Rebote elástico corto (squash and stretch): oscila y se asienta.</summary>
    public static double Elastic(double t)
    {
        t = Clamp01(t);
        if (t is 0 or 1)
            return t;
        const double c4 = 2 * Math.PI / 3;
        return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    }

    /// <summary>Un pulso 0 → 1 → 0 dentro de [start, start + length].</summary>
    public static double Pulse(double t, double start, double length)
    {
        if (t < start || t > start + length)
            return 0;
        return Math.Sin((t - start) / length * Math.PI);
    }

    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Progreso dentro de un tramo: 0 antes, 1 después.</summary>
    public static double Seg(double t, double start, double length) => Clamp01((t - start) / length);

    /// <summary>Evalúa una curva cúbica de Bézier como las de CSS (x en función del tiempo).</summary>
    public static double Bezier(double x1, double y1, double x2, double y2, double t)
    {
        t = Clamp01(t);
        if (t is 0 or 1)
            return t;
        // x(u) es monótona con x1, x2 en [0, 1]: la bisección siempre converge, incluso con tangentes planas.
        double lo = 0, hi = 1, u = t;
        for (int i = 0; i < 24; i++)
        {
            u = (lo + hi) / 2;
            if (Cubic(x1, x2, u) < t)
                lo = u;
            else
                hi = u;
        }
        return Cubic(y1, y2, u);
    }

    private static double Cubic(double p1, double p2, double u) =>
        3 * (1 - u) * (1 - u) * u * p1 + 3 * (1 - u) * u * u * p2 + u * u * u;
}
