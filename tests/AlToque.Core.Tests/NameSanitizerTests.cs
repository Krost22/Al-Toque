using AlToque.Core.Transfer;

namespace AlToque.Core.Tests;

public class NameSanitizerTests
{
    [Theory]
    [InlineData("Render_final.mp4")]
    [InlineData("Proyecto/notas.txt")]
    [InlineData("Fotos 2026/Día 1/IMG_0001.JPG")]
    [InlineData("a.b.c")]
    [InlineData("consola.txt/x")]
    [InlineData("日本語/ファイル.txt")]
    public void AcceptsNormalPaths(string path)
    {
        Assert.True(NameSanitizer.TryValidate(path, windowsRules: true, out var result, out var error), error);
        Assert.False(result.HadHiddenCharacters);
        Assert.Equal(path.Normalize(System.Text.NormalizationForm.FormC), string.Join('/', result.Components));
    }

    [Theory]
    [InlineData("../../Inicio/malo.exe")]
    [InlineData("a/../../b")]
    [InlineData("./a")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/system32/x.dll")]
    [InlineData("C:x.txt")]
    [InlineData("//server/share/x")]
    [InlineData("a\\..\\b")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("")]
    [InlineData("file.txt:Zone.Identifier")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("Carpeta/NUL.log")]
    [InlineData("COM1")]
    [InlineData("lpt9.tar.gz")]
    [InlineData("CON .txt")]
    [InlineData("termina.")]
    [InlineData("termina ")]
    [InlineData("pregunta?.txt")]
    [InlineData("estrella*.txt")]
    [InlineData("tub|o.txt")]
    [InlineData("menor<.txt")]
    [InlineData("comillas\".txt")]
    [InlineData("nulo\0.txt")]
    [InlineData("salto\nlinea.txt")]
    [InlineData("tab\t.txt")]
    [InlineData("del\u007f.txt")]
    [InlineData("c1\u0085.txt")]
    [InlineData("parcial.altoque-part")]
    [InlineData("\u202e")]
    public void RejectsDangerousPaths(string path)
    {
        Assert.False(NameSanitizer.TryValidate(path, windowsRules: true, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RemovesBidiControlsAndFlagsThem()
    {
        // "factura_‮fdp.exe" se ve como "factura_exe.pdf" pero es un programa.
        var name = "factura_\u202Efdp.exe";
        Assert.True(NameSanitizer.TryValidate(name, true, out var result, out _));
        Assert.True(result.HadHiddenCharacters);
        Assert.Equal("factura_fdp.exe", result.Components[0]);
        Assert.True(FileKinds.IsDangerous(result.Components[0]));
    }

    [Fact]
    public void RejectsTooLongComponentAndTooDeep()
    {
        Assert.False(NameSanitizer.TryValidate(new string('a', 256), true, out _, out _));
        Assert.True(NameSanitizer.TryValidate(new string('a', 255), true, out _, out _));
        // 128 caracteres de 2 bytes en UTF-8 = 256 bytes.
        Assert.False(NameSanitizer.TryValidate(new string('ñ', 128), true, out _, out _));
        var deep = string.Join('/', Enumerable.Repeat("d", 65));
        Assert.False(NameSanitizer.TryValidate(deep, true, out _, out _));
        var ok = string.Join('/', Enumerable.Repeat("d", 64));
        Assert.True(NameSanitizer.TryValidate(ok, true, out _, out _));
    }

    [Fact]
    public void NonWindowsRulesAllowColonButNotTraversal()
    {
        Assert.True(NameSanitizer.TryValidate("Foto 10:30.jpg", windowsRules: false, out _, out _));
        Assert.True(NameSanitizer.TryValidate("CON", windowsRules: false, out _, out _));
        Assert.False(NameSanitizer.TryValidate("../x", windowsRules: false, out _, out _));
        Assert.False(NameSanitizer.TryValidate("a\\b", windowsRules: false, out _, out _));
    }

    [Fact]
    public void NormalizesToNfc()
    {
        var decomposed = "Cafe\u0301.txt";
        Assert.True(NameSanitizer.TryValidate(decomposed, true, out var result, out _));
        Assert.Equal("Café.txt", result.Components[0]);
    }

    [Fact]
    public void CombineStaysInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "altoque-root");
        var combined = NameSanitizer.Combine(root, ["a", "b.txt"]);
        Assert.True(NameSanitizer.IsInside(root, combined));
        Assert.False(NameSanitizer.IsInside(root, root));
        Assert.False(NameSanitizer.IsInside(root, root + "-otro" + Path.DirectorySeparatorChar + "x"));
        Assert.Throws<InvalidOperationException>(() => NameSanitizer.Combine(root, ["..", "x"]));
    }

    [Theory]
    [InlineData("factura.pdf.exe", true)]
    [InlineData("fotos.jpg.scr", true)]
    [InlineData("instalador.exe", false)]
    [InlineData("informe.pdf", false)]
    public void DetectsMisleadingDoubleExtensions(string name, bool expected) =>
        Assert.Equal(expected, FileKinds.HasMisleadingDoubleExtension(name));
}
