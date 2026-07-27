namespace WPF_OpenStreetmap_Editor.Plugins;

public static partial class BuiltInPluginCatalog {
    private const string BetterImeManifest = """
        {
          schemaVersion: 1,
          id: 'org.wosm.better-ime',
          name: 'Better IME For WOSM',
          version: '1.0.0',
          icon: 'icon.jpg',
          descriptionFile: 'description.md',
          kind: 'addon',
          contributions: {
            commands: [
              {
                id: 'enable',
                actions: [{ type: 'enableNonTextInputImeGuard', arguments: {} }]
              }
            ]
          }
        }
        """;

    private const string BetterImeDescription = """
        # Better IME For WOSM

        Automatically disables IME while focus is outside editable text fields so WOSM shortcuts stay available.
        """;
}
