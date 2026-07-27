using System.Runtime.InteropServices;

namespace WPF_OpenStreetmap_Editor.Services;

internal static partial class ConsoleAttachment {
    public static void DetachFromConsole() {
        if (OperatingSystem.IsWindows()) FreeConsole();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();
}
