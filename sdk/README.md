# Plugin SDK

This folder is the editor-side mirror of the public plugin SDK. The full SDK is
published from a separate repository so plugin authors can version against a
stable API surface without tying their build to the main app checkout.

## What the SDK covers

- manifest schema examples and packaging rules
- the native ABI mirror in `sdk/native/wosm_plugin.h`
- process-plugin JSON-RPC request and response shapes
- helper templates for addon, Python, and Java support packages

## Default plugin workflow

1. Create the plugin in its own repository or worktree.
2. Choose a package kind:
   - `addon` for declarative, no-code extensions
   - `process` for Python, Java, or other sandboxed bridges
   - `native` only when in-process integration is unavoidable
3. Package `plugin.json5`, `icon`, `description`, and the runtime entry inside
   the plugin root.
4. Build the package into `artifacts/plugins/<plugin-name>/` or publish it as a
   standalone archive.
5. Install it from the Plugins window, or drop a supported archive on the
   window when the UI accepts drag-and-drop.

## Python support plugin

Python support should be shipped as a separate process plugin. It should expose
its own bridge API for:

- loading Python entry points
- resolving package metadata and icon data
- forwarding commands and hooks over JSON-RPC

The Python runtime and any native extensions must stay inside the sandboxed
process package.

## Java support plugin

Java support should also be a separate process plugin. Its first target is JOSM
compatibility, not a generic JAR loader.

Recommended rollout:

1. parse package metadata and icons
2. discover plugin descriptors and capabilities
3. map commands, hooks, and actions to the bridge API
4. expand the supported JOSM surface gradually

When Java support is installed, the installer should accept `.jar` packages as
a source format. The Java bridge still owns class loading and JOSM-specific
translation.
