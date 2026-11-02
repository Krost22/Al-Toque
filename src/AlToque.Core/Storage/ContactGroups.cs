using AlToque.Core.Security;

namespace AlToque.Core.Storage;

/// <summary>
/// Un grupo de compañeros (equipo, sesión, familia…): una lista local de este equipo, que no se comparte. Sirve para
/// filtrar el panel y para enviar a todos de una vez. Archivado, no se muestra pero se conserva.
/// </summary>
public sealed record ContactGroup
{
    public const int MaxNameLength = 32;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<string> Members { get; init; } = [];
    public bool Archived { get; init; }
}

/// <summary>Operaciones sobre la lista de grupos de los ajustes: cada una devuelve una lista nueva.</summary>
public static class ContactGroups
{
    public const int MaxGroups = 30;
    public const int MaxMembers = 200;

    public static string? CleanName(string? name) =>
        TextLimits.CleanShortText(name?.Replace('\n', ' '), ContactGroup.MaxNameLength) is { Length: > 0 } clean ? clean : null;

    /// <summary>Crea un grupo (con sus primeros miembros, si los hay). Devuelve null si el nombre no sirve o hay demasiados.</summary>
    public static (List<ContactGroup> Groups, ContactGroup? Created) Create(IReadOnlyList<ContactGroup>? groups, string name,
        IEnumerable<string>? members = null)
    {
        var list = Copy(groups);
        if (CleanName(name) is not { } clean || list.Count >= MaxGroups)
            return (list, null);
        var group = new ContactGroup
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = clean,
            Members = (members ?? []).Where(PeerKeys.IsValidDeviceId).Distinct(StringComparer.Ordinal).Take(MaxMembers).ToList(),
        };
        list.Add(group);
        return (list, group);
    }

    public static List<ContactGroup> Rename(IReadOnlyList<ContactGroup>? groups, string id, string name) =>
        CleanName(name) is { } clean ? Map(groups, id, g => g with { Name = clean }) : Copy(groups);

    public static List<ContactGroup> SetArchived(IReadOnlyList<ContactGroup>? groups, string id, bool archived) =>
        Map(groups, id, g => g with { Archived = archived });

    public static List<ContactGroup> Delete(IReadOnlyList<ContactGroup>? groups, string id) =>
        Copy(groups).Where(g => g.Id != id).ToList();

    public static List<ContactGroup> AddMember(IReadOnlyList<ContactGroup>? groups, string id, string deviceId) =>
        !PeerKeys.IsValidDeviceId(deviceId) ? Copy(groups)
        : Map(groups, id, g => g.Members.Contains(deviceId) || g.Members.Count >= MaxMembers ? g : g with { Members = [.. g.Members, deviceId] });

    public static List<ContactGroup> RemoveMember(IReadOnlyList<ContactGroup>? groups, string id, string deviceId) =>
        Map(groups, id, g => g with { Members = g.Members.Where(m => m != deviceId).ToList() });

    /// <summary>El mismo compañero con otra identidad (reinstaló): ocupa su lugar en cada grupo donde estaba.</summary>
    public static List<ContactGroup> ReplaceMember(IReadOnlyList<ContactGroup>? groups, string oldId, string newId) =>
        Copy(groups).Select(g => g.Members.Contains(oldId)
            ? g with { Members = g.Members.Select(m => m == oldId ? newId : m).Distinct(StringComparer.Ordinal).ToList() }
            : g).ToList();

    /// <summary>Quita de todos los grupos a quien ya no es compañero (se quitó o se bloqueó).</summary>
    public static List<ContactGroup> Prune(IReadOnlyList<ContactGroup>? groups, Func<string, bool> isContact) =>
        Copy(groups).Select(g => g with { Members = g.Members.Where(isContact).ToList() }).ToList();

    /// <summary>Lo leído de disco, en orden: nombres limpios, ids y miembros válidos, sin repetidos y con topes.</summary>
    public static List<ContactGroup>? Normalize(IReadOnlyList<ContactGroup>? groups)
    {
        if (groups is null)
            return null;
        var result = new List<ContactGroup>();
        foreach (var g in groups)
        {
            if (g is null || !IsValidId(g.Id) || CleanName(g.Name) is not { } name || result.Any(r => r.Id == g.Id))
                continue;
            result.Add(g with
            {
                Name = name,
                Members = (g.Members ?? []).Where(PeerKeys.IsValidDeviceId).Distinct(StringComparer.Ordinal).Take(MaxMembers).ToList(),
            });
            if (result.Count >= MaxGroups)
                break;
        }
        return result.Count > 0 ? result : null;
    }

    private static bool IsValidId(string? id) => id is { Length: 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static List<ContactGroup> Copy(IReadOnlyList<ContactGroup>? groups) =>
        groups is null ? [] : groups.Select(g => g with { Members = [.. g.Members] }).ToList();

    private static List<ContactGroup> Map(IReadOnlyList<ContactGroup>? groups, string id, Func<ContactGroup, ContactGroup> change) =>
        Copy(groups).Select(g => g.Id == id ? change(g) : g).ToList();
}
