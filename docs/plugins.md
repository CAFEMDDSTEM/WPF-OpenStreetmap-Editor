# Plugin architecture

WOSM discovers plugins under `%LOCALAPPDATA%/WPF-OpenStreetmap-Editor/Plugins`.
Each plugin is a directory containing `plugin.json5` at its root. The plugin
manager can install that directory by selecting its manifest, or install a
`.wosm-plugin`/`.zip` archive whose root contains the manifest.

Every plugin and addon package must contain three metadata files:

```text
org.example.plugin/
  plugin.json5       # JSON5 configuration and declaration
  icon.png           # PNG, ICO, or JPEG; declared by `icon`
  description.md     # UTF-8 Markdown or text; declared by `descriptionFile`
```

The icon and description paths must remain inside the package and cannot pass
through symbolic links or reparse points. Icons are limited to 2 MB, 4096
pixels per side, and 16,777,216 pixels per frame. Descriptions are limited to
64 KB and cannot be empty. The plugin manager displays the selected package's
icon and description.

The schema version is currently `1`. Plugin IDs and command IDs are stable API
identifiers; display labels can change between releases.

## Plugin kinds

- `native`: an in-process Windows DLL using the WOSM C ABI. A `.lib` import
  library and C/C++ headers may be shipped for plugin development, but `.lib`
  files are link-time artifacts and are not loaded at runtime.
- `process`: an executable speaking JSON-RPC 2.0 over standard input/output.
  Java/JOSM and Python compatibility runtimes belong here after being packaged
  as executables, for example with GraalVM native-image, a single-process
  jpackage launcher, or PyInstaller.
- `addon`: a declarative, no-code extension. It can add menu commands composed
  from restricted host actions. Addons do not subscribe to runtime hooks.

Process plugins run in a per-plugin Windows AppContainer without declared
capabilities. WOSM copies the package into a temporary AppContainer-owned
session directory and exposes only redirected standard input, output, and
error. A Job object limits the plugin to one process, enforces the configured
memory limit, blocks desktop and clipboard access, and terminates the process
when the job closes. Process plugins therefore do not require the native-code
trust confirmation, but they can affect the editor through the host actions
declared in their manifest.

The AppContainer has no network capability and cannot read files outside its
own profile. Its environment is rebuilt from a small system allowlist so host
tokens and other inherited variables are not exposed. WOSM fails closed if
sandbox setup fails and never falls back to normal user permissions.

Native plugins are different: they execute inside the editor process with the
current user's operating-system permissions and can read process memory, read
credentials, modify user files, access the network, or crash the editor. The
plugin manager requires an explicit warning confirmation for native plugins
and stores a SHA-256 fingerprint covering every file in the package. Any file
change invalidates that confirmation. Copying a native plugin into the plugin
directory manually does not bypass the confirmation.

Language-level restrictions are not security boundaries. Python imports,
reflection, and native extensions, or Java file, network, process, and JNI
APIs, can bypass function blacklists. Such runtimes must remain inside the
process-plugin AppContainer; they must not be loaded as native plugins merely
to avoid sandbox compatibility work.

Packages are limited to 10,000 files and 512 MB of uncompressed content. A
manifest is limited to 256 KB. Archives with paths outside their package root,
or directory packages containing symbolic links/reparse points, are rejected.

## Addon example

```json5
{
  schemaVersion: 1,
  id: 'org.example.imagery-addon',
  name: 'Example imagery',
  version: '1.0.0',
  icon: 'icon.png',
  descriptionFile: 'description.md',
  kind: 'addon',
  contributions: {
    menus: [
      { location: 'tools', label: 'Open example imagery', command: 'open-imagery' },
    ],
    commands: [
      {
        id: 'open-imagery',
        actions: [
          {
            type: 'addImagery',
            arguments: {
              type: 'xyz',
              url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            },
          },
        ],
      },
    ],
  },
}
```

`openUrl` accepts only absolute HTTP/HTTPS URLs. `showMessage` accepts `title`
and `message`. `addImagery` accepts `type` and a required `url`. The built-in
OpenStreetMap transfer addon also uses the host-owned actions `downloadOsm`,
`uploadOsm`, and `manageOsmAccounts`; these actions do not accept arbitrary
arguments or executable code.

Toolbar contributions use `location: 'main'`, a valid
`MahApps.Metro.IconPacks.Lucide` icon identifier such as `Download`, a tooltip,
and an order from `-10000` through `10000`. Menu contributions currently use
`location: 'tools'`.

## Built-in OpenStreetMap transfer

WOSM installs `org.openstreetmap.transfer` as a built-in addon package under
the same plugin directory as third-party packages. It contributes toolbar and
Tools menu commands for:

- downloading OSM XML for a bounding box selected in the download window
- uploading reviewed create/modify/delete changes from the current document
- managing OSM accounts

The host implements those actions directly. The addon manifest only wires UI
commands to host-owned behavior; it does not contain network or credential
code. OSM account metadata is stored in `%LOCALAPPDATA%`, while bearer tokens
are stored in Windows Credential Manager.

## Process bridge

```json5
{
  schemaVersion: 1,
  id: 'org.example.josm-bridge',
  name: 'JOSM compatibility bridge',
  version: '0.1.0',
  icon: 'icon.png',
  descriptionFile: 'description.md',
  kind: 'process',
  runtime: {
    entry: 'josm-bridge.exe',
    arguments: ['--plugins', 'josm-plugins'],
    hostActions: ['showMessage', 'openUrl', 'addImagery'],
    timeoutMilliseconds: 10000,
    memoryLimitMegabytes: 1536,
  },
  hooks: ['application.started', 'mainWindow.loaded', 'application.stopping'],
  contributions: {
    menus: [
      { location: 'tools', label: 'JOSM plugins', command: 'josm.manage' },
    ],
  },
}
```

Standard input and output contain exactly one UTF-8 JSON-RPC message per line.
Diagnostics belong on standard error. WOSM sends these methods:

- `initialize`: protocol version, host identity, plugin identity, and package
  directory.
- `hook`: a subscribed hook name and its payload.
- `command.execute`: a contributed command ID and payload.
- `shutdown`: final notification before the bridge is stopped.

`runtime.hostActions` is the bridge's host API capability list. Every action
returned over RPC must be supported by WOSM and explicitly listed there;
undeclared actions reject the entire response. RPC lines are limited to 1 MB.
`memoryLimitMegabytes` is clamped to 128-4096 MB. The `packageDirectory`
sent to `initialize` is the AppContainer copy, not the original installed
package path.

Every call requires a JSON-RPC response with the same integer ID. For
compatibility with older bridges, WOSM skips up to 31 non-protocol startup
lines on standard output and records the first one in the load error if no
response follows. Bridges must not rely on this tolerance; diagnostics belong
on standard error.

Successful `hook` and `command.execute` responses may return host actions.
`initialize` and `shutdown` should return an empty action list:

```json
{"jsonrpc":"2.0","id":2,"result":{"actions":[{"type":"showMessage","arguments":{"message":"Ready"}}]}}
```

A JOSM bridge owns Java startup, JOSM core/API version selection, Swing event
dispatch, JAR discovery, and translation between JOSM objects and WOSM RPC
objects. WOSM does not load arbitrary JOSM JARs directly. This separation is
intentional: existing JOSM plugins link against `org.openstreetmap.josm.*` and
cannot be made compatible by treating a JAR as a generic archive.

JARs are executable code, not safe addon data. Bundle bridge-managed JARs in
the process plugin package so WOSM copies them into the AppContainer session.
The bridge must not load JARs from locations outside that session or request
host actions that turn an external JAR into unsandboxed native code.

## Native ABI

Native plugins export the following C ABI with `cdecl` calling convention. All
JSON uses UTF-8 and the same JSON-RPC methods as process plugins.
The canonical declaration is [`sdk/native/wosm_plugin.h`](../sdk/native/wosm_plugin.h).

```text
org.example.native/
  plugin.json5
  icon.png
  description.md
  example-plugin.dll
  example-plugin.lib    # optional import library for native consumers
```

```json5
{
  schemaVersion: 1,
  id: 'org.example.native',
  name: 'Example native plugin',
  version: '1.0.0',
  icon: 'icon.png',
  descriptionFile: 'description.md',
  kind: 'native',
  runtime: {
    entry: 'example-plugin.dll',
    hostActions: ['showMessage'],
    timeoutMilliseconds: 5000,
  },
  hooks: ['application.started', 'application.stopping'],
}
```

```c
#define WOSM_PLUGIN_ABI_VERSION 1

__declspec(dllexport) int wosm_plugin_abi_version(void);
__declspec(dllexport) char* wosm_plugin_invoke(
    const unsigned char* request_utf8,
    int request_length);
__declspec(dllexport) void wosm_plugin_free(char* response_utf8);
```

`wosm_plugin_invoke` returns a null-terminated response allocated by the plugin.
WOSM calls `wosm_plugin_free` exactly once for every non-null response. Native
plugins run in process: an access violation, deadlock, or ABI mismatch can bring
down the editor. Prefer a process bridge unless direct in-process integration is
necessary.

## Hooks

Schema version 1 defines:

- `application.started`
- `mainWindow.loaded`
- `application.stopping`

Hook names are versioned host API. Unknown hook names are rejected so spelling
errors do not silently produce inactive plugins.
