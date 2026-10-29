using AlToque.Core.Protocol;
using AlToque.Core.Security;
using AlToque.Core.Storage;
using AlToque.Core.Transfer;

namespace AlToque.Core.Tests;

public class ReceivePlanTests
{
    private static readonly string Session = Base32.Encode(new byte[16]);

    private static OfferMessage Offer(params (string Id, string Path, long Size)[] files) => new()
    {
        SessionId = Session,
        Files = files.Select(f => new OfferFile { FileId = f.Id, Path = f.Path, Size = f.Size }).ToList(),
        TotalSize = files.Sum(f => f.Size),
    };

    [Fact]
    public void BuildsPlanInsideRoot()
    {
        using var dir = new TempDir();
        var plan = ReceivePlan.Build(Offer(("1", "Proyecto/a.txt", 3), ("2", "Proyecto/sub/b.bin", 5)), dir.Path, ConflictPolicy.Rename, true);
        Assert.Equal(2, plan.Files.Count);
        Assert.All(plan.Files, f => Assert.True(NameSanitizer.IsInside(dir.Path, f.FinalPath)));
        Assert.Equal(8, plan.AcceptedBytes);
        Assert.Equal(Path.Combine(plan.Root, "Proyecto"), plan.SingleTopFolder);
    }

    [Fact]
    public void RejectsDuplicateIdsAndPaths()
    {
        using var dir = new TempDir();
        Assert.Equal(ErrorCodes.BadManifest,
            Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("1", "a.txt", 1), ("1", "b.txt", 1)), dir.Path, ConflictPolicy.Rename, true)).Code);
        // Mismo identificador o misma ruta: la lección de CVE-2024-10668.
        Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("1", "a.txt", 1), ("2", "A.TXT", 1)), dir.Path, ConflictPolicy.Rename, true));
        Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("1", "a", 1), ("2", "a/b", 1)), dir.Path, ConflictPolicy.Rename, true));
    }

    [Fact]
    public void RejectsInconsistentSizesAndBadIds()
    {
        using var dir = new TempDir();
        var offer = new OfferMessage
        {
            SessionId = Session,
            Files = [new OfferFile { FileId = "1", Path = "a.txt", Size = 10 }],
            TotalSize = 5,
        };
        Assert.Throws<TransferException>(() => ReceivePlan.Build(offer, dir.Path, ConflictPolicy.Rename, true));
        Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("1", "a.txt", -1)), dir.Path, ConflictPolicy.Rename, true));
        Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("X/../1", "a.txt", 1)), dir.Path, ConflictPolicy.Rename, true));
        Assert.Throws<TransferException>(() => ReceivePlan.Build(Offer(("1", "../a.txt", 1)), dir.Path, ConflictPolicy.Rename, true));
    }

    [Fact]
    public void RenamesTopLevelOnConflict()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "foto.jpg"), "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "Proyecto"));
        var plan = ReceivePlan.Build(Offer(("1", "foto.jpg", 1), ("2", "Proyecto/a.txt", 1)), dir.Path, ConflictPolicy.Rename, true);
        Assert.Equal("foto (1).jpg", plan.Files[0].Name);
        Assert.Equal("Proyecto (1)/a.txt", plan.Files[1].DisplayPath);
    }

    [Fact]
    public void SkipPolicyMarksExisting()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "foto.jpg"), "x");
        var plan = ReceivePlan.Build(Offer(("1", "foto.jpg", 1), ("2", "otra.jpg", 4)), dir.Path, ConflictPolicy.Skip, true);
        Assert.True(plan.Files[0].Skip);
        Assert.False(plan.Files[1].Skip);
        Assert.Equal(4, plan.AcceptedBytes);
    }

    [Fact]
    public void FlagsDangerousAndMisleadingFiles()
    {
        using var dir = new TempDir();
        var plan = ReceivePlan.Build(Offer(("1", "instalador.exe", 1), ("2", "factura.pdf.exe", 1), ("3", "nota‮txt.exe", 1), ("4", "foto.png", 1)),
            dir.Path, ConflictPolicy.Rename, true);
        Assert.True(plan.Files[0].Dangerous);
        Assert.False(plan.Files[0].MisleadingName);
        Assert.True(plan.Files[1].MisleadingName);
        Assert.True(plan.Files[2].HadHiddenCharacters);
        Assert.False(plan.Files[3].Dangerous);
        Assert.True(plan.HasDangerous);
    }
}
