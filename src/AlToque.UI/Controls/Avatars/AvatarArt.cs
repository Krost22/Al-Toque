using Avalonia;
using Avalonia.Media;

namespace AlToque.Controls.Avatars;

/// <summary>Cómo se pinta una capa del personaje.</summary>
internal enum Paint
{
    /// <summary>Volumen con sombra proyectada, luz arriba a la izquierda y borde más oscuro.</summary>
    Body,

    /// <summary>Volumen sin sombra proyectada (detalles grandes sobre el cuerpo).</summary>
    Soft,

    /// <summary>Color plano (manchas, narices, puntos).</summary>
    Flat,

    /// <summary>Trazo redondeado.</summary>
    Line,
}

/// <summary>
/// Una capa del personaje en la caja de 100 × 100. Las que tienen <see cref="Wiggle"/> se mueven alrededor de
/// <see cref="Pivot"/> cuando el personaje está animado (orejas, antenas, tentáculos).
/// </summary>
internal sealed record Layer(Geometry Geo, Color Color, Paint Paint = Paint.Body, double Width = 0)
{
    public Point Pivot { get; init; }
    public double Rotate { get; init; }
    public double Wiggle { get; init; }
    public double Phase { get; init; }
    public double Bob { get; init; }
}

internal enum MouthStyle
{
    Smile,

    /// <summary>Forma de "w", de gato o perro.</summary>
    Cat,

    /// <summary>Sin boca visible (máscara, trompa).</summary>
    None,
}

/// <summary>Dónde van los ojos y la boca, y de qué color.</summary>
internal sealed record FaceSpec(Point LeftEye, Point RightEye, double EyeSize, Point Mouth, double MouthWidth)
{
    public Color EyeInk { get; init; } = AvatarArt.Ink;
    public Color MouthInk { get; init; } = AvatarArt.Ink;
    public Color? EyeWhite { get; init; }
    public double EyeWhiteSize { get; init; }
    public double EyeTall { get; init; } = 1.15;
    public Color Blush { get; init; } = Color.FromRgb(0xFF, 0x8F, 0xA3);
    public double BlushAlpha { get; init; } = 0.42;
    public MouthStyle Mouth2 { get; init; } = MouthStyle.Smile;
}

internal sealed record Character(string Id, Layer[] Layers, FaceSpec Face);

/// <summary>
/// Los personajes de los avatares: ilustración plana con un toque neo-skeuomórfico (volumen suave, luz cenital,
/// sombra proyectada sobre la ficha). Todo es geometría vectorial, así que se ven nítidos a cualquier tamaño.
/// </summary>
internal static class AvatarArt
{
    public static readonly Color Ink = Color.FromRgb(0x24, 0x22, 0x2B);

    private static Geometry E(double cx, double cy, double rx, double ry) => new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));

    private static Geometry C(double cx, double cy, double r) => E(cx, cy, r, r);

    private static Geometry RR(double x, double y, double w, double h, double r) => new RectangleGeometry(new Rect(x, y, w, h), r, r);

    private static Geometry P(string path) => Geometry.Parse(path);

    private static Geometry Union(params Geometry[] parts)
    {
        var g = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var p in parts)
            g.Children.Add(p);
        return g;
    }

    private static Color H(string hex) => ColorUtil.Hex(hex);

    public static readonly Character[] All =
    [
        Cat(), Dog(), Fox(), Bear(), Polar(), Panda(), Bunny(), Frog(), Whale(), Jelly(), Ghost(), Mushroom(),
        Cactus(), Cloud(), Snail(), Teapot(), Axolotl(), Penguin(), Lion(), Robot(), Ninja(), Rock(), Elephant(), Toast(),
    ];

    private static Character Cat()
    {
        var fur = H("#F7F2EA");
        var pink = H("#F5B3AC");
        return new Character("gato",
        [
            new(P("M16,62 L20,21 Q21,14 27,17 L52,40 Z"), fur) { Pivot = new Point(26, 48), Wiggle = 9 },
            new(P("M84,62 L80,21 Q79,14 73,17 L48,40 Z"), fur) { Pivot = new Point(74, 48), Wiggle = 9, Phase = 0.5 },
            new(P("M23,46 L25,27 Q26,23 30,25 L41,36 Z"), pink, Paint.Flat) { Pivot = new Point(26, 48), Wiggle = 9 },
            new(P("M77,46 L75,27 Q74,23 70,25 L59,36 Z"), pink, Paint.Flat) { Pivot = new Point(74, 48), Wiggle = 9, Phase = 0.5 },
            new(RR(14, 34, 72, 100, 34), fur),
            new(E(50, 76.5, 2.8, 2), H("#F09A9A"), Paint.Flat),
        ],
        new FaceSpec(new Point(37, 68), new Point(63, 68), 5, new Point(50, 80), 9) { Mouth2 = MouthStyle.Cat });
    }

    private static Character Dog()
    {
        var fur = H("#F8F5F0");
        var tan = H("#D9B48C");
        return new Character("perro",
        [
            new(RR(19, 28, 62, 104, 30), fur),
            new(E(64, 56, 11, 10), H("#EED7BC"), Paint.Soft),
            new(P("M31,32 C18,30 7,45 9,66 C10,77 20,80 25,71 C29,61 31,48 36,39 Z"), tan) { Pivot = new Point(31, 34), Wiggle = 12 },
            new(P("M69,32 C82,30 93,45 91,66 C90,77 80,80 75,71 C71,61 69,48 64,39 Z"), tan) { Pivot = new Point(69, 34), Wiggle = 12, Phase = 0.4 },
            new(E(50, 70, 5.6, 4), H("#2C2A31"), Paint.Soft),
        ],
        new FaceSpec(new Point(38, 58), new Point(62, 58), 4.8, new Point(50, 78), 9) { Mouth2 = MouthStyle.Cat });
    }

    private static Character Fox()
    {
        var orange = H("#F6A35A");
        var cream = H("#FDF6EE");
        return new Character("zorro",
        [
            new(P("M13,58 L19,14 Q20,8 26,12 L50,34 Z"), orange) { Pivot = new Point(24, 46), Wiggle = 8 },
            new(P("M87,58 L81,14 Q80,8 74,12 L50,34 Z"), orange) { Pivot = new Point(76, 46), Wiggle = 8, Phase = 0.5 },
            new(P("M20,42 L23,20 Q24,16 28,19 L38,30 Z"), H("#4A332C"), Paint.Flat) { Pivot = new Point(24, 46), Wiggle = 8 },
            new(P("M80,42 L77,20 Q76,16 72,19 L62,30 Z"), H("#4A332C"), Paint.Flat) { Pivot = new Point(76, 46), Wiggle = 8, Phase = 0.5 },
            new(E(50, 74, 39, 36), orange),
            new(P("M11,76 Q30,66 50,84 Q70,66 89,76 Q88,110 50,112 Q12,110 11,76 Z"), cream, Paint.Soft),
            new(E(50, 81, 3.8, 2.8), Ink, Paint.Soft),
        ],
        new FaceSpec(new Point(36, 68), new Point(64, 68), 4.6, new Point(50, 88), 8));
    }

    private static Character Bear()
    {
        var honey = H("#F5C063");
        return new Character("oso",
        [
            new(C(25, 36, 13), honey) { Pivot = new Point(28, 44), Wiggle = 6 },
            new(C(75, 36, 13), honey) { Pivot = new Point(72, 44), Wiggle = 6, Phase = 0.5 },
            new(C(25, 36, 7), H("#E3A548"), Paint.Flat) { Pivot = new Point(28, 44), Wiggle = 6 },
            new(C(75, 36, 7), H("#E3A548"), Paint.Flat) { Pivot = new Point(72, 44), Wiggle = 6, Phase = 0.5 },
            new(RR(12, 32, 76, 100, 38), honey),
            new(E(50, 80, 15, 11), H("#FCEACB"), Paint.Soft),
            new(E(50, 75, 5, 3.6), H("#3B2B24"), Paint.Soft),
        ],
        new FaceSpec(new Point(36, 64), new Point(64, 64), 4.6, new Point(50, 85), 7));
    }

    private static Character Polar()
    {
        var white = H("#F4F6F9");
        return new Character("polar",
        [
            new(C(27, 38, 10), white) { Pivot = new Point(30, 44), Wiggle = 6 },
            new(C(73, 38, 10), white) { Pivot = new Point(70, 44), Wiggle = 6, Phase = 0.5 },
            new(C(27, 38, 5), H("#DDE2EA"), Paint.Flat) { Pivot = new Point(30, 44), Wiggle = 6 },
            new(C(73, 38, 5), H("#DDE2EA"), Paint.Flat) { Pivot = new Point(70, 44), Wiggle = 6, Phase = 0.5 },
            new(RR(13, 33, 74, 100, 35), white),
            new(E(50, 74, 14, 10.5), H("#FFFFFF"), Paint.Soft),
            new(E(50, 69.5, 6, 4.2), H("#2A2A30"), Paint.Soft),
        ],
        new FaceSpec(new Point(37, 60), new Point(63, 60), 4.2, new Point(50, 79), 7));
    }

    private static Character Panda()
    {
        var white = H("#F8F8F8");
        var black = H("#2A2A31");
        return new Character("panda",
        [
            new(C(24, 36, 12.5), black) { Pivot = new Point(28, 44), Wiggle = 6 },
            new(C(76, 36, 12.5), black) { Pivot = new Point(72, 44), Wiggle = 6, Phase = 0.5 },
            new(RR(13, 33, 74, 100, 35), white),
            new(E(37, 63, 9, 11.5), black, Paint.Soft) { Pivot = new Point(37, 63), Rotate = -28 },
            new(E(63, 63, 9, 11.5), black, Paint.Soft) { Pivot = new Point(63, 63), Rotate = 28 },
            new(E(50, 75, 4.6, 3.3), black, Paint.Soft),
        ],
        new FaceSpec(new Point(38, 62), new Point(62, 62), 3.7, new Point(50, 82), 7) { EyeInk = H("#FFFFFF") });
    }

    private static Character Bunny()
    {
        var fur = H("#F8EFE2");
        var pink = H("#F6B8B3");
        return new Character("conejo",
        [
            new(RR(25, -4, 17, 60, 8.5), fur) { Pivot = new Point(34, 50), Rotate = -9, Wiggle = 7 },
            new(RR(29.5, 4, 8, 42, 4), pink, Paint.Flat) { Pivot = new Point(34, 50), Rotate = -9, Wiggle = 7 },
            new(RR(58, -4, 17, 60, 8.5), fur) { Pivot = new Point(66, 50), Rotate = 24, Wiggle = 9, Phase = 0.35 },
            new(RR(62.5, 4, 8, 42, 4), pink, Paint.Flat) { Pivot = new Point(66, 50), Rotate = 24, Wiggle = 9, Phase = 0.35 },
            new(RR(15, 44, 70, 92, 35), fur),
            new(P("M47.5,75 Q50,73 52.5,75 Q50,79 47.5,75 Z"), H("#F08F98"), Paint.Flat),
        ],
        new FaceSpec(new Point(38, 69), new Point(62, 69), 4.4, new Point(50, 80), 7) { Mouth2 = MouthStyle.Cat });
    }

    private static Character Frog()
    {
        var green = H("#ABDD83");
        return new Character("rana",
        [
            new(C(29, 42, 16), green) { Pivot = new Point(29, 52), Wiggle = 5 },
            new(C(71, 42, 16), green) { Pivot = new Point(71, 52), Wiggle = 5, Phase = 0.5 },
            new(RR(6, 46, 88, 90, 40), green),
        ],
        new FaceSpec(new Point(29, 41), new Point(71, 41), 4.4, new Point(50, 76), 24)
        {
            EyeWhite = H("#FFFFFF"),
            EyeWhiteSize = 9.5,
            BlushAlpha = 0.5,
        });
    }

    private static Character Whale()
    {
        var blue = H("#BEDDF2");
        return new Character("ballena",
        [
            new(P("M47,24 C44,16 46,9 50,6 C54,9 56,16 53,24 Z"), H("#DDF0FB"), Paint.Soft) { Pivot = new Point(50, 26), Wiggle = 10, Bob = 2 },
            new(P("M42,22 C36,18 33,13 34,8 C39,10 43,15 45,21 Z"), H("#DDF0FB"), Paint.Soft) { Pivot = new Point(44, 24), Wiggle = 14, Bob = 2, Phase = 0.3 },
            new(P("M58,22 C64,18 67,13 66,8 C61,10 57,15 55,21 Z"), H("#DDF0FB"), Paint.Soft) { Pivot = new Point(56, 24), Wiggle = 14, Bob = 2, Phase = 0.6 },
            new(P("M-12,124 L-12,74 C-12,46 16,30 50,30 C86,30 112,50 112,82 L112,124 Z"), blue),
            new(P("M4,124 C8,98 30,88 54,90 C80,92 98,106 102,124 Z"), H("#E6F3FB"), Paint.Soft),
        ],
        new FaceSpec(new Point(38, 60), new Point(64, 60), 4.2, new Point(51, 72), 16) { BlushAlpha = 0.35 });
    }

    private static Character Jelly()
    {
        var lilac = H("#CDB9F3");
        var tentacle = H("#DCCDF8");
        Layer T(double x, double phase) =>
            new(P(FormattableString.Invariant($"M{x},58 C{x - 6},70 {x + 6},80 {x},92 C{x - 4},100 {x + 3},106 {x},112")), tentacle, Paint.Line, 5)
            {
                Pivot = new Point(x, 58),
                Wiggle = 9,
                Phase = phase,
            };
        return new Character("medusa",
        [
            T(30, 0), T(43, 0.25), T(57, 0.5), T(70, 0.75),
            new(P("M16,58 C16,30 32,15 50,15 C68,15 84,30 84,58 C78,63 72,56 66,61 C60,66 55,58 50,62 C45,58 40,66 34,61 C28,56 22,63 16,58 Z"), lilac) { Bob = 2.5 },
            new(E(36, 30, 6, 3.5), H("#E6DBFB"), Paint.Flat) { Pivot = new Point(36, 30), Rotate = -30, Bob = 2.5 },
        ],
        new FaceSpec(new Point(40, 42), new Point(60, 42), 3.8, new Point(50, 50), 7));
    }

    private static Character Ghost()
    {
        var white = H("#F9F6F2");
        return new Character("fantasma",
        [
            new(P("M20,104 L20,50 C20,27 33,14 50,14 C67,14 80,27 80,50 L80,104 C76,97 71,97 67,103 C63,109 58,109 55,103 C52,97 48,97 45,103 C42,109 37,109 33,103 C29,97 24,97 20,104 Z"), white) { Bob = 3 },
            new(E(15, 66, 6, 9), white, Paint.Soft) { Pivot = new Point(20, 62), Rotate = -30, Wiggle = 16, Bob = 3 },
            new(E(85, 66, 6, 9), white, Paint.Soft) { Pivot = new Point(80, 62), Rotate = 30, Wiggle = 16, Phase = 0.5, Bob = 3 },
        ],
        new FaceSpec(new Point(40, 46), new Point(60, 46), 4.6, new Point(50, 60), 7) { EyeTall = 1.45 });
    }

    private static Character Mushroom()
    {
        var cap = H("#F3DDB8");
        var stem = H("#FBF5EA");
        return new Character("hongo",
        [
            new(RR(28, 48, 44, 84, 18), stem),
            new(P("M5,58 C5,29 25,11 50,11 C75,11 95,29 95,58 C95,65 88,67 80,65 C62,60 38,60 20,65 C12,67 5,65 5,58 Z"), cap) { Pivot = new Point(50, 60), Wiggle = 4 },
            new(E(29, 36, 6.5, 5), H("#FBF0DD"), Paint.Flat) { Pivot = new Point(50, 60), Wiggle = 4 },
            new(E(56, 24, 7.5, 5), H("#FBF0DD"), Paint.Flat) { Pivot = new Point(50, 60), Wiggle = 4 },
            new(E(75, 44, 5, 4), H("#FBF0DD"), Paint.Flat) { Pivot = new Point(50, 60), Wiggle = 4 },
        ],
        new FaceSpec(new Point(41, 78), new Point(59, 78), 4, new Point(50, 87), 6.5));
    }

    private static Character Cactus()
    {
        var mint = H("#BEE8AE");
        var spine = H("#8FC67E");
        var petal = H("#FF95B5");
        Geometry Petal(double angle)
        {
            var a = angle * Math.PI / 180;
            return C(50 + Math.Cos(a) * 5.5, 20 + Math.Sin(a) * 5.5, 4.6);
        }
        return new Character("cactus",
        [
            new(RR(9, 38, 15, 38, 7.5), mint) { Pivot = new Point(22, 72), Wiggle = 6 },
            new(RR(14, 62, 24, 13, 6.5), mint, Paint.Soft) { Pivot = new Point(22, 72), Wiggle = 6 },
            new(RR(76, 28, 15, 34, 7.5), mint) { Pivot = new Point(78, 60), Wiggle = 6, Phase = 0.5 },
            new(RR(62, 50, 26, 13, 6.5), mint, Paint.Soft) { Pivot = new Point(78, 60), Wiggle = 6, Phase = 0.5 },
            new(RR(31, 24, 38, 112, 19), mint),
            new(P("M36,40 l-3,-2 M64,44 l3,-2 M38,92 l-3,2 M62,96 l3,2 M15,48 l-3,-1 M85,38 l3,-1"), spine, Paint.Line, 2),
            new(Union(Petal(-90), Petal(-18), Petal(54), Petal(126), Petal(198)), petal, Paint.Soft) { Pivot = new Point(50, 20), Wiggle = 25 },
            new(C(50, 20, 3.4), H("#FFD66B"), Paint.Flat) { Pivot = new Point(50, 20), Wiggle = 25 },
        ],
        new FaceSpec(new Point(42, 56), new Point(58, 56), 3.7, new Point(50, 64), 6.5));
    }

    private static Character Cloud()
    {
        var white = H("#FAFAFC");
        return new Character("nube",
        [
            new(Union(C(29, 64, 19), C(50, 47, 24), C(72, 60, 20), RR(10, 58, 80, 38, 19)), white) { Bob = 2.5 },
        ],
        new FaceSpec(new Point(40, 66), new Point(60, 66), 4.2, new Point(50, 75), 8));
    }

    private static Character Snail()
    {
        var body = H("#F6EAD3");
        var shell = H("#F3CB8F");
        return new Character("caracol",
        [
            new(P("M84,40 L80,22"), body, Paint.Line, 4) { Pivot = new Point(84, 42), Wiggle = 12 },
            new(P("M92,40 L96,24"), body, Paint.Line, 4) { Pivot = new Point(92, 42), Wiggle = 12, Phase = 0.4 },
            new(C(80, 21, 6), body, Paint.Soft) { Pivot = new Point(84, 42), Wiggle = 12 },
            new(C(96, 23, 6), body, Paint.Soft) { Pivot = new Point(92, 42), Wiggle = 12, Phase = 0.4 },
            new(P("M2,104 C2,88 14,84 28,84 L76,84 C82,84 82,78 82,68 L82,46 C82,36 98,36 98,46 L98,76 C98,96 88,104 72,104 Z"), body),
            new(C(44, 62, 27), shell),
            new(P("M44,62 C44,58 50,58 50,62 C50,70 38,70 38,62 C38,52 56,52 56,62 C56,76 32,76 32,62 C32,46 62,46 62,62"), H("#DDA45E"), Paint.Line, 3.6),
        ],
        new FaceSpec(new Point(80, 21), new Point(96, 23), 2.9, new Point(90, 60), 6) { BlushAlpha = 0.3 });
    }

    private static Character Teapot()
    {
        var orange = H("#F39463");
        return new Character("tetera",
        [
            new(P("M24,64 C13,61 8,52 5,44 L11,41 C15,48 19,55 28,57 Z"), orange) { Pivot = new Point(24, 60), Wiggle = 5 },
            new(P("M75,55 C93,53 95,82 75,84"), orange, Paint.Line, 7),
            new(E(48, 71, 32, 27), orange),
            new(P("M17,72 C30,78 66,78 79,72 C79,77 77,81 75,84 C62,88 34,88 21,84 C19,81 17,77 17,72 Z"), H("#F8B48C"), Paint.Soft),
            new(E(48, 46, 19, 6.5), H("#F7A578")) { Pivot = new Point(48, 46), Wiggle = 6, Bob = 1.5 },
            new(C(48, 38, 5), H("#F7A578"), Paint.Soft) { Pivot = new Point(48, 46), Wiggle = 6, Bob = 1.5 },
        ],
        new FaceSpec(new Point(39, 66), new Point(57, 66), 3.9, new Point(48, 75), 7));
    }

    private static Character Axolotl()
    {
        var pink = H("#FCDDE5");
        var gill = H("#F59BB6");
        Layer G(string path, Point pivot, double phase) => new(P(path), gill, Paint.Line, 6) { Pivot = pivot, Wiggle = 14, Phase = phase };
        return new Character("ajolote",
        [
            G("M20,54 L7,42", new Point(20, 56), 0), G("M17,64 L3,62", new Point(20, 64), 0.2), G("M20,74 L8,84", new Point(20, 72), 0.4),
            G("M80,54 L93,42", new Point(80, 56), 0.5), G("M83,64 L97,62", new Point(80, 64), 0.7), G("M80,74 L92,84", new Point(80, 72), 0.9),
            new(RR(20, 76, 60, 60, 26), pink),
            new(E(50, 64, 34, 27), pink),
        ],
        new FaceSpec(new Point(34, 63), new Point(66, 63), 4, new Point(50, 72), 11));
    }

    private static Character Penguin()
    {
        var dark = H("#2B2E3B");
        return new Character("pinguino",
        [
            new(RR(13, 26, 74, 110, 37), dark),
            new(P("M50,46 C42,35 23,37 23,57 C23,79 37,94 50,94 C63,94 77,79 77,57 C77,37 58,35 50,46 Z"), H("#F7F6F2"), Paint.Soft),
            new(P("M43.5,65 Q50,58.5 56.5,65 Q50,71 43.5,65 Z"), H("#FFAA4D"), Paint.Soft),
        ],
        new FaceSpec(new Point(38, 57), new Point(62, 57), 4, new Point(50, 77), 6));
    }

    private static Character Lion()
    {
        var mane = H("#F4BC63");
        var face = H("#FDEFD3");
        var parts = new List<Geometry>();
        for (int i = 0; i < 11; i++)
        {
            var a = i / 11.0 * Math.PI * 2;
            parts.Add(C(50 + Math.Cos(a) * 31, 66 + Math.Sin(a) * 31, 14));
        }
        parts.Add(C(50, 66, 34));
        return new Character("leon",
        [
            new(Union([.. parts]), mane) { Pivot = new Point(50, 66), Wiggle = 4 },
            new(C(31, 45, 7.5), H("#F9DDB0"), Paint.Soft),
            new(C(69, 45, 7.5), H("#F9DDB0"), Paint.Soft),
            new(E(50, 67, 27, 25), face),
            new(E(50, 76, 10, 7), H("#FFF8EA"), Paint.Soft),
            new(P("M46,70 Q50,67.5 54,70 Q50,75 46,70 Z"), H("#6B4434"), Paint.Flat),
        ],
        new FaceSpec(new Point(40, 62), new Point(60, 62), 3.8, new Point(50, 80), 7) { Mouth2 = MouthStyle.Cat });
    }

    private static Character Robot()
    {
        var shell = H("#EEF1F6");
        var metal = H("#C9CED8");
        return new Character("robot",
        [
            new(P("M50,30 L50,15"), metal, Paint.Line, 3.5) { Pivot = new Point(50, 30), Wiggle = 14 },
            new(C(50, 12, 5.5), H("#FF6F6F"), Paint.Soft) { Pivot = new Point(50, 30), Wiggle = 14 },
            new(RR(6, 52, 10, 22, 4), metal),
            new(RR(84, 52, 10, 22, 4), metal),
            new(RR(12, 28, 76, 80, 24), shell),
            new(RR(21, 40, 58, 40, 15), H("#262A36"), Paint.Soft),
        ],
        new FaceSpec(new Point(39, 57), new Point(61, 57), 4.2, new Point(50, 69), 11)
        {
            EyeInk = H("#86F2FF"),
            MouthInk = H("#86F2FF"),
            Blush = H("#86F2FF"),
            BlushAlpha = 0.18,
        });
    }

    private static Character Ninja()
    {
        var black = H("#25252D");
        var red = H("#F0565B");
        return new Character("ninja",
        [
            new(P("M86,42 C92,36 96,30 100,24 L102,32 C98,36 94,40 88,46 Z"), red, Paint.Soft) { Pivot = new Point(86, 44), Wiggle = 16 },
            new(P("M86,46 C92,46 98,48 104,52 L100,58 C96,54 92,52 86,50 Z"), red, Paint.Soft) { Pivot = new Point(86, 48), Wiggle = 16, Phase = 0.4 },
            new(RR(13, 26, 74, 110, 37), black),
            new(RR(13, 39, 74, 8, 4), red, Paint.Soft),
            new(RR(21, 53, 58, 22, 11), H("#F7D7B5"), Paint.Soft),
        ],
        new FaceSpec(new Point(39, 64), new Point(61, 64), 3.8, new Point(50, 80), 6)
        {
            Mouth2 = MouthStyle.None,
            BlushAlpha = 0.3,
        });
    }

    private static Character Rock()
    {
        var gray = H("#CBCFD8");
        return new Character("piedra",
        [
            new(P("M8,104 C4,76 18,48 44,44 C60,42 68,32 82,40 C96,50 98,80 93,104 Z"), gray),
            new(E(30, 82, 3, 2), H("#B3B8C4"), Paint.Flat),
            new(E(76, 58, 2.5, 1.8), H("#B3B8C4"), Paint.Flat),
            new(E(80, 88, 3.2, 2.2), H("#B3B8C4"), Paint.Flat),
            new(P("M64,40 C62,30 66,22 74,20 C76,28 72,36 64,40 Z"), H("#8FD27B"), Paint.Soft) { Pivot = new Point(64, 40), Wiggle = 20 },
        ],
        new FaceSpec(new Point(46, 68), new Point(64, 68), 3.6, new Point(55, 77), 6.5));
    }

    private static Character Elephant()
    {
        var gray = H("#D9D6E8");
        var ear = H("#CAC5DD");
        return new Character("elefante",
        [
            new(E(19, 60, 18, 24), ear) { Pivot = new Point(30, 56), Wiggle = 12 },
            new(E(81, 60, 18, 24), ear) { Pivot = new Point(70, 56), Wiggle = 12, Phase = 0.5 },
            new(E(19, 61, 10, 15), H("#EEC6D4"), Paint.Flat) { Pivot = new Point(30, 56), Wiggle = 12 },
            new(E(81, 61, 10, 15), H("#EEC6D4"), Paint.Flat) { Pivot = new Point(70, 56), Wiggle = 12, Phase = 0.5 },
            new(RR(23, 30, 54, 90, 27), gray),
            new(P("M43,68 C43,84 45,98 56,102 C63,104 67,99 64,95 C56,93 57,84 57,68 Z"), gray, Paint.Soft) { Pivot = new Point(50, 70), Wiggle = 10 },
        ],
        new FaceSpec(new Point(39, 56), new Point(61, 56), 3.8, new Point(50, 80), 6) { Mouth2 = MouthStyle.None });
    }

    private static Character Toast()
    {
        var crust = H("#E4AA6C");
        var bread = H("#FCE6BC");
        return new Character("tostada",
        [
            new(P("M13,104 L13,46 C4,41 5,22 23,18 C34,15 42,23 50,23 C58,23 66,15 77,18 C95,22 96,41 87,46 L87,104 Z"), crust),
            new(P("M20,104 L20,50 C13,46 13,29 26,26 C35,23 42,30 50,30 C58,30 65,23 74,26 C87,29 87,46 80,50 L80,104 Z"), bread, Paint.Soft),
            new(RR(40, 38, 19, 12, 3.5), H("#FFE27A")) { Pivot = new Point(49, 44), Rotate = -8, Wiggle = 8, Bob = 1.5 },
        ],
        new FaceSpec(new Point(40, 64), new Point(60, 64), 3.9, new Point(50, 73), 7));
    }
}
