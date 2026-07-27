using WPF_OpenStreetmap_Editor.Services;

namespace WPF_OpenStreetmap_Editor.Tests.Services;

public sealed class HelpContentServiceTests {
    [Fact]
    public void Create_IncludesHelpSectionsAndProgramInfo() {
        var content = HelpContentService.Create();

        Assert.Equal("WPF OpenStreetmap Editor", content.ProgramName);
        Assert.Contains(content.Sections, section => section.Title == "快捷键");
        Assert.Contains(content.ProgramInfo, item =>
            item.Name == "许可证" &&
            item.Value.Contains("GPL v3", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_LoadsEmbeddedGplV3License() {
        var content = HelpContentService.Create();

        Assert.Contains("GNU GENERAL PUBLIC LICENSE", content.LicenseText);
        Assert.Contains("Version 3, 29 June 2007", content.LicenseText);
    }
}
