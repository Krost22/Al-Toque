using System.Security.Cryptography;
using System.Text;
using AlToque.Localization;

namespace AlToque.Controls.Avatars;

/// <summary>Personajes disponibles, sus nombres y el avatar por defecto de cada equipo.</summary>
public static class AvatarCatalog
{
    /// <summary>El avatar clásico: la inicial sobre el color de la identidad.</summary>
    public const string InitialId = "inicial";

    private static readonly Dictionary<string, Character> ById = AvatarArt.All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    /// <summary>Todos los ids en el orden del selector; la inicial va al final.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. AvatarArt.All.Select(c => c.Id), InitialId];

    internal static Character? Find(string? id) => id is not null && ById.TryGetValue(id, out var c) ? c : null;

    public static bool IsKnown(string? id) => id == InitialId || (id is not null && ById.ContainsKey(id));

    /// <summary>Personaje estable para un equipo que aún no eligió (o que eligió uno que esta versión no conoce).</summary>
    public static string DefaultFor(string deviceId)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes("altoque-avatar:" + deviceId));
        return AvatarArt.All[((h[0] << 8) | h[1]) % AvatarArt.All.Length].Id;
    }

    public static string Resolve(string? chosen, string deviceId) => IsKnown(chosen) ? chosen! : DefaultFor(deviceId);

    public static string NameOf(string id) => id switch
    {
        "gato" => S.Pick("Gato", "Cat"),
        "perro" => S.Pick("Perro", "Dog"),
        "zorro" => S.Pick("Zorro", "Fox"),
        "oso" => S.Pick("Oso", "Bear"),
        "polar" => S.Pick("Oso polar", "Polar bear"),
        "panda" => S.Pick("Panda", "Panda"),
        "conejo" => S.Pick("Conejo", "Bunny"),
        "rana" => S.Pick("Rana", "Frog"),
        "ballena" => S.Pick("Ballena", "Whale"),
        "medusa" => S.Pick("Medusa", "Jellyfish"),
        "fantasma" => S.Pick("Fantasma", "Ghost"),
        "hongo" => S.Pick("Hongo", "Mushroom"),
        "cactus" => S.Pick("Cactus", "Cactus"),
        "nube" => S.Pick("Nube", "Cloud"),
        "caracol" => S.Pick("Caracol", "Snail"),
        "tetera" => S.Pick("Tetera", "Teapot"),
        "ajolote" => S.Pick("Ajolote", "Axolotl"),
        "pinguino" => S.Pick("Pingüino", "Penguin"),
        "leon" => S.Pick("León", "Lion"),
        "robot" => S.Pick("Robot", "Robot"),
        "ninja" => S.Pick("Ninja", "Ninja"),
        "piedra" => S.Pick("Piedra", "Rock"),
        "elefante" => S.Pick("Elefante", "Elephant"),
        "tostada" => S.Pick("Tostada", "Toast"),
        _ => S.Pick("Inicial", "Initial"),
    };
}
