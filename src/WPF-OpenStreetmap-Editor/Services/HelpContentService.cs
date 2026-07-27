using System.IO;
using System.Reflection;
using System.Text;

namespace WPF_OpenStreetmap_Editor.Services;

internal sealed record HelpContent(
    string ProgramName,
    string Version,
    string VersionText,
    string LicenseName,
    string LicenseText,
    IReadOnlyList<HelpSection> Sections,
    IReadOnlyList<ProgramInfoItem> ProgramInfo);

internal sealed record HelpSection(string Title, IReadOnlyList<string> Items);

internal sealed record ProgramInfoItem(string Name, string Value);

internal static class HelpContentService {
    private const string LicenseResourceName = "WPF_OpenStreetmap_Editor.LICENSE.txt";

    public static HelpContent Create() {
        var assembly = typeof(HelpContentService).Assembly;
        var version = GetVersionText(assembly);
        var l = LocalizationService.Instance;

        return new HelpContent(
            "WPF OpenStreetmap Editor",
            version,
            l.Format("Help.VersionFormat", version),
            "GNU General Public License v3.0",
            ReadLicenseText(assembly),
            [
                new HelpSection(l.GetString("Help.Section.GetStarted"), [
                    l.GetString("Help.GetStarted.Open"),
                    l.GetString("Help.GetStarted.Save"),
                    l.GetString("Help.GetStarted.Layers")
                ]),
                new HelpSection(l.GetString("Help.Section.MapEditing"), [
                    l.GetString("Help.MapEditing.Tools"),
                    l.GetString("Help.MapEditing.Zoom"),
                    l.GetString("Help.MapEditing.Selection")
                ]),
                new HelpSection(l.GetString("Help.Section.SourcesThemes"), [
                    l.GetString("Help.SourcesThemes.Settings"),
                    l.GetString("Help.SourcesThemes.Imagery"),
                    l.GetString("Help.SourcesThemes.Attribution")
                ]),
                new HelpSection(l.GetString("Help.Section.OsmPlugins"), [
                    l.GetString("Help.OsmPlugins.Plugins"),
                    l.GetString("Help.OsmPlugins.Accounts"),
                    l.GetString("Help.OsmPlugins.DownloadUpload")
                ]),
                new HelpSection(l.GetString("Help.Section.Keyboard"), [
                    l.GetString("Help.Keyboard.F1"),
                    l.GetString("Help.Keyboard.Save"),
                    l.GetString("Help.Keyboard.Edit"),
                    l.GetString("Help.Keyboard.Modes"),
                    l.GetString("Help.Keyboard.Transform"),
                    l.GetString("Help.Keyboard.TypedCommands"),
                    l.GetString("Help.Keyboard.Drag"),
                    l.GetString("Help.Keyboard.Nodes")
                ])
            ],
            [
                new ProgramInfoItem(l.GetString("Help.Info.Program"), "WPF OpenStreetmap Editor"),
                new ProgramInfoItem(l.GetString("Help.Info.Version"), version),
                new ProgramInfoItem(l.GetString("Help.Info.License"), "GPL v3"),
                new ProgramInfoItem(l.GetString("Help.Info.Runtime"), $".NET {Environment.Version}"),
                new ProgramInfoItem(l.GetString("Help.Info.Features"), l.GetString("Help.Info.FeaturesValue"))
            ]);
    }

    internal static string GetVersionText(Assembly assembly) {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion)) {
            return informationalVersion.Split('+', 2)[0];
        }

        var version = assembly.GetName().Version;
        if (version is null) return "0.1.0";

        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    internal static string ReadLicenseText(Assembly assembly) {
        using var stream = assembly.GetManifestResourceStream(LicenseResourceName);
        if (stream is null) {
            return "GPL v3 license text is not available in this build. See https://www.gnu.org/licenses/gpl-3.0.txt";
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
