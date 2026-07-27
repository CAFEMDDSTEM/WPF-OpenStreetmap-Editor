using System.Windows;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class LocExtension : DynamicResourceExtension {
    public LocExtension() {
    }

    public LocExtension(string key)
        : base(key) {
    }
}
