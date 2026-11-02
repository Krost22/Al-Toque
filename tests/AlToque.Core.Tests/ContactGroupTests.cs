using System.Security.Cryptography;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using Xunit;

namespace AlToque.Core.Tests;

public class ContactGroupTests
{
    private static string NewDeviceId() => PeerKeys.DeviceIdFromSpki(RandomNumberGenerator.GetBytes(91));

    [Fact]
    public void GroupsAreCreatedRenamedArchivedAndDeleted()
    {
        var laura = NewDeviceId();
        var andres = NewDeviceId();
        var (groups, oficina) = ContactGroups.Create(null, "  Oficina  ", [laura, laura, "no-es-un-id"]);
        Assert.NotNull(oficina);
        Assert.Equal("Oficina", oficina!.Name);
        Assert.Equal([laura], oficina.Members);

        groups = ContactGroups.AddMember(groups, oficina.Id, andres);
        groups = ContactGroups.AddMember(groups, oficina.Id, andres); // sin repetir
        Assert.Equal([laura, andres], groups.Single().Members);

        groups = ContactGroups.Rename(groups, oficina.Id, "Equipo de diseño");
        groups = ContactGroups.Rename(groups, oficina.Id, "   "); // un nombre vacío no cambia nada
        Assert.Equal("Equipo de diseño", groups.Single().Name);

        groups = ContactGroups.SetArchived(groups, oficina.Id, true);
        Assert.True(groups.Single().Archived);
        groups = ContactGroups.RemoveMember(groups, oficina.Id, laura);
        Assert.Equal([andres], groups.Single().Members);

        Assert.Empty(ContactGroups.Delete(groups, oficina.Id));
    }

    [Fact]
    public void OperationsNeverChangeTheListTheyReceive()
    {
        var (groups, g) = ContactGroups.Create(null, "Familia");
        var before = groups.Single();
        var after = ContactGroups.AddMember(groups, g!.Id, NewDeviceId());
        Assert.Empty(before.Members);
        Assert.Single(after.Single().Members);
    }

    [Fact]
    public void RemovedContactsLeaveEveryGroup()
    {
        var keep = NewDeviceId();
        var gone = NewDeviceId();
        var (groups, a) = ContactGroups.Create(null, "A", [keep, gone]);
        (groups, var b) = ContactGroups.Create(groups, "B", [gone]);
        groups = ContactGroups.Prune(groups, id => id == keep);
        Assert.Equal([keep], groups.Single(x => x.Id == a!.Id).Members);
        Assert.Empty(groups.Single(x => x.Id == b!.Id).Members);
    }

    [Fact]
    public void AReinstalledTeammateKeepsItsPlaceInEachGroup()
    {
        var before = NewDeviceId();
        var after = NewDeviceId();
        var other = NewDeviceId();
        var (groups, a) = ContactGroups.Create(null, "Oficina", [other, before]);
        (groups, var b) = ContactGroups.Create(groups, "Familia", [other]);
        groups = ContactGroups.ReplaceMember(groups, before, after);
        Assert.Equal([other, after], groups.Single(g => g.Id == a!.Id).Members);
        Assert.Equal([other], groups.Single(g => g.Id == b!.Id).Members);
    }

    [Fact]
    public void BadNamesAndTooManyGroupsAreRefused()
    {
        Assert.Null(ContactGroups.Create(null, "‮​").Created);
        Assert.Equal(ContactGroup.MaxNameLength, ContactGroups.Create(null, new string('x', 100)).Created!.Name.Length);
        List<ContactGroup>? groups = null;
        for (var i = 0; i < ContactGroups.MaxGroups; i++)
            groups = ContactGroups.Create(groups, "G" + i).Groups;
        Assert.Null(ContactGroups.Create(groups, "Uno más").Created);
    }

    [Fact]
    public void GroupsSurviveInTheSettingsAndAreCleanedOnLoad()
    {
        using var dir = new TempDir();
        var paths = AppPaths.Create(Path.Combine(dir.Path, "config"), false);
        var protector = new FilePermissionSecretProtector();
        var laura = NewDeviceId();
        var store = SettingsStore.Load(paths, Sealer.LoadOrCreate(paths, protector));
        var (groups, _) = ContactGroups.Create(null, "Oficina", [laura]);
        groups.Add(new ContactGroup { Id = "no-es-un-id", Name = "Roto" });
        store.Update(s => s.ContactGroups = groups);

        var reloaded = SettingsStore.Load(paths, Sealer.LoadOrCreate(paths, protector)).Current;
        var single = Assert.Single(reloaded.ContactGroups!);
        Assert.Equal("Oficina", single.Name);
        Assert.Equal([laura], single.Members);

        // Una copia de los ajustes no comparte la lista de miembros.
        var copy = reloaded.Clone();
        copy.ContactGroups![0].Members.Add(NewDeviceId());
        Assert.Single(reloaded.ContactGroups![0].Members);
    }
}
