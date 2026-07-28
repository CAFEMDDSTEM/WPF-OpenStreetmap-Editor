package org.wosm.javabridge;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.PrintWriter;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.jar.JarFile;
import java.util.jar.Manifest;
import java.util.stream.Stream;

public final class WosmJavaPluginBridge {
    private final String pluginDirectoryArgument;
    private final String josmCoreDirectoryArgument;
    private Path packageDirectory;
    private List<JosmPluginInfo> plugins = List.of();

    private WosmJavaPluginBridge(String[] args) {
        var pluginDirectory = "josm-plugins";
        var josmCoreDirectory = "josm-core";
        for (var index = 0; index < args.length; index++) {
            if ("--plugins".equals(args[index]) && index + 1 < args.length) {
                pluginDirectory = args[++index];
            } else if ("--josm-core".equals(args[index]) && index + 1 < args.length) {
                josmCoreDirectory = args[++index];
            }
        }
        pluginDirectoryArgument = pluginDirectory;
        josmCoreDirectoryArgument = josmCoreDirectory;
    }

    public static void main(String[] args) throws IOException {
        new WosmJavaPluginBridge(args).run();
    }

    private void run() throws IOException {
        try (var reader = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
             var writer = new PrintWriter(System.out, true, StandardCharsets.UTF_8)) {
            String line;
            while ((line = reader.readLine()) != null) {
                writer.println(handle(line));
            }
        }
    }

    private String handle(String requestJson) {
        Object id = null;
        try {
            var request = Json.parseObject(requestJson);
            id = request.get("id");
            var method = asString(request.get("method"));
            var params = asObject(request.get("params"));
            return success(id, switch (method) {
                case "initialize" -> initialize(params);
                case "hook" -> handleHook(params);
                case "command.execute" -> executeCommand(params);
                case "shutdown" -> actions();
                default -> throw new RpcException(-32601, "Unsupported method: " + method);
            });
        } catch (RpcException ex) {
            return error(id, ex.code, ex.getMessage());
        } catch (Exception ex) {
            return error(id, -32603, ex.getMessage() == null ? ex.getClass().getName() : ex.getMessage());
        }
    }

    private Map<String, Object> initialize(Map<String, Object> params) throws IOException {
        var plugin = asObject(params.get("plugin"));
        packageDirectory = Path.of(asString(plugin.get("packageDirectory"))).toAbsolutePath().normalize();
        plugins = discoverPlugins(packageDirectory);
        return actions();
    }

    private Map<String, Object> handleHook(Map<String, Object> params) {
        var name = asString(params.get("name"));
        if ("application.stopping".equals(name)) {
            plugins = List.of();
        }
        return actions();
    }

    private Map<String, Object> executeCommand(Map<String, Object> params) {
        var command = asString(params.get("command"));
        if (!"josm.inspect".equals(command)) {
            return actions();
        }

        var message = new StringBuilder();
        if (plugins.isEmpty()) {
            message.append("No JOSM plugins were discovered in this package.");
        } else {
            message.append("Java/JOSM bridge loaded ").append(plugins.size()).append(" plugin(s):");
            for (var plugin : plugins) {
                message.append(System.lineSeparator())
                    .append("- ")
                    .append(plugin.displayName())
                    .append(" [")
                    .append(plugin.className())
                    .append("] ");
                message.append(plugin.classLoaded() ? "class linked" : "metadata only");
                if (!plugin.error().isBlank()) {
                    message.append(" (").append(plugin.error()).append(")");
                }
            }
        }

        var action = new LinkedHashMap<String, Object>();
        action.put("type", "showMessage");
        action.put("arguments", Map.of("message", message.toString()));
        return actions(List.of(action));
    }

    private List<JosmPluginInfo> discoverPlugins(Path root) throws IOException {
        var pluginDirectory = root.resolve(pluginDirectoryArgument).normalize();
        if (!pluginDirectory.startsWith(root) || !Files.isDirectory(pluginDirectory)) {
            return List.of();
        }

        var pluginJars = listJars(pluginDirectory);
        var classPath = new ArrayList<Path>();
        var josmCoreDirectory = root.resolve(josmCoreDirectoryArgument).normalize();
        if (josmCoreDirectory.startsWith(root) && Files.isDirectory(josmCoreDirectory)) {
            classPath.addAll(listJars(josmCoreDirectory));
        }
        classPath.addAll(pluginJars);

        var urls = new URL[classPath.size()];
        for (var index = 0; index < classPath.size(); index++) {
            urls[index] = classPath.get(index).toUri().toURL();
        }

        try (var loader = new URLClassLoader(urls, ClassLoader.getPlatformClassLoader())) {
            var discovered = new ArrayList<JosmPluginInfo>();
            for (var jar : pluginJars) {
                discovered.add(readPluginInfo(jar, loader));
            }
            return discovered;
        }
    }

    private static List<Path> listJars(Path directory) throws IOException {
        try (Stream<Path> stream = Files.list(directory)) {
            return stream
                .filter(path -> Files.isRegularFile(path) && path.getFileName().toString().endsWith(".jar"))
                .sorted()
                .toList();
        }
    }

    private static JosmPluginInfo readPluginInfo(Path jar, ClassLoader loader) {
        try (var jarFile = new JarFile(jar.toFile())) {
            var manifest = jarFile.getManifest();
            if (manifest == null) {
                return new JosmPluginInfo(jar.getFileName().toString(), "", "", false, "missing manifest");
            }

            var attributes = manifest.getMainAttributes();
            var className = stringAttribute(attributes.getValue("Plugin-Class"));
            var description = stringAttribute(attributes.getValue("Plugin-Description"));
            if (className.isBlank()) {
                return new JosmPluginInfo(displayName(jar, className), className, description, false, "missing Plugin-Class");
            }

            try {
                Class.forName(className, false, loader);
                return new JosmPluginInfo(displayName(jar, className), className, description, true, "");
            } catch (LinkageError | ClassNotFoundException ex) {
                return new JosmPluginInfo(displayName(jar, className), className, description, false, ex.getClass().getSimpleName() + ": " + ex.getMessage());
            }
        } catch (IOException ex) {
            return new JosmPluginInfo(jar.getFileName().toString(), "", "", false, ex.getMessage());
        }
    }

    private static String displayName(Path jar, String className) {
        if (!className.isBlank()) {
            var offset = className.lastIndexOf('.');
            var simpleName = offset >= 0 ? className.substring(offset + 1) : className;
            return simpleName.endsWith("Plugin")
                ? simpleName.substring(0, simpleName.length() - "Plugin".length())
                : simpleName;
        }
        var fileName = jar.getFileName().toString();
        return fileName.endsWith(".jar") ? fileName.substring(0, fileName.length() - 4) : fileName;
    }

    private static String stringAttribute(String value) {
        return value == null ? "" : value.trim();
    }

    private static String asString(Object value) {
        if (value instanceof String string) return string;
        throw new RpcException(-32602, "Expected string parameter.");
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> asObject(Object value) {
        if (value instanceof Map<?, ?> map) return (Map<String, Object>) map;
        throw new RpcException(-32602, "Expected object parameter.");
    }

    private static String success(Object id, Map<String, Object> result) {
        return Json.stringify(Map.of("jsonrpc", "2.0", "id", id, "result", result));
    }

    private static String error(Object id, int code, String message) {
        return Json.stringify(Map.of(
            "jsonrpc", "2.0",
            "id", id == null ? 0 : id,
            "error", Map.of("code", code, "message", message)));
    }

    private static Map<String, Object> actions() {
        return actions(List.of());
    }

    private static Map<String, Object> actions(List<Map<String, Object>> actions) {
        return Map.of("actions", actions);
    }

    private record JosmPluginInfo(
        String displayName,
        String className,
        String description,
        boolean classLoaded,
        String error) {
    }

    private static final class RpcException extends RuntimeException {
        private final int code;

        private RpcException(int code, String message) {
            super(message);
            this.code = code;
        }
    }

    private static final class Json {
        private Json() {
        }

        static Map<String, Object> parseObject(String text) {
            var parser = new Parser(text);
            var value = parser.parseValue();
            parser.skipWhitespace();
            if (!parser.isAtEnd()) throw new RpcException(-32700, "Unexpected trailing JSON.");
            return asObject(value);
        }

        static String stringify(Object value) {
            var builder = new StringBuilder();
            writeJson(builder, value);
            return builder.toString();
        }

        private static void writeJson(StringBuilder builder, Object value) {
            if (value == null) {
                builder.append("null");
            } else if (value instanceof String string) {
                writeString(builder, string);
            } else if (value instanceof Number || value instanceof Boolean) {
                builder.append(value);
            } else if (value instanceof Map<?, ?> map) {
                builder.append('{');
                var first = true;
                for (var entry : map.entrySet()) {
                    if (!first) builder.append(',');
                    first = false;
                    writeString(builder, String.valueOf(entry.getKey()));
                    builder.append(':');
                    writeJson(builder, entry.getValue());
                }
                builder.append('}');
            } else if (value instanceof Iterable<?> iterable) {
                builder.append('[');
                var first = true;
                for (var item : iterable) {
                    if (!first) builder.append(',');
                    first = false;
                    writeJson(builder, item);
                }
                builder.append(']');
            } else {
                writeString(builder, String.valueOf(value));
            }
        }

        private static void writeString(StringBuilder builder, String value) {
            builder.append('"');
            for (var index = 0; index < value.length(); index++) {
                var character = value.charAt(index);
                switch (character) {
                    case '"' -> builder.append("\\\"");
                    case '\\' -> builder.append("\\\\");
                    case '\b' -> builder.append("\\b");
                    case '\f' -> builder.append("\\f");
                    case '\n' -> builder.append("\\n");
                    case '\r' -> builder.append("\\r");
                    case '\t' -> builder.append("\\t");
                    default -> {
                        if (character < 0x20) {
                            builder.append("\\u%04x".formatted((int) character));
                        } else {
                            builder.append(character);
                        }
                    }
                }
            }
            builder.append('"');
        }

        private static final class Parser {
            private final String text;
            private int offset;

            Parser(String text) {
                this.text = text;
                if (!text.isEmpty() && text.charAt(0) == '\ufeff') {
                    offset = 1;
                }
            }

            Object parseValue() {
                skipWhitespace();
                if (isAtEnd()) throw new RpcException(-32700, "Unexpected end of JSON.");
                return switch (text.charAt(offset)) {
                    case '{' -> parseObjectValue();
                    case '[' -> parseArray();
                    case '"' -> parseString();
                    case 't' -> parseLiteral("true", Boolean.TRUE);
                    case 'f' -> parseLiteral("false", Boolean.FALSE);
                    case 'n' -> parseLiteral("null", null);
                    default -> parseNumber();
                };
            }

            private Map<String, Object> parseObjectValue() {
                offset++;
                var result = new LinkedHashMap<String, Object>();
                skipWhitespace();
                if (consume('}')) return result;
                while (true) {
                    skipWhitespace();
                    var name = parseString();
                    skipWhitespace();
                    expect(':');
                    result.put(name, parseValue());
                    skipWhitespace();
                    if (consume('}')) return result;
                    expect(',');
                }
            }

            private List<Object> parseArray() {
                offset++;
                var result = new ArrayList<Object>();
                skipWhitespace();
                if (consume(']')) return result;
                while (true) {
                    result.add(parseValue());
                    skipWhitespace();
                    if (consume(']')) return result;
                    expect(',');
                }
            }

            private String parseString() {
                expect('"');
                var result = new StringBuilder();
                while (!isAtEnd()) {
                    var character = text.charAt(offset++);
                    if (character == '"') return result.toString();
                    if (character != '\\') {
                        result.append(character);
                        continue;
                    }
                    if (isAtEnd()) throw new RpcException(-32700, "Invalid JSON escape.");
                    var escape = text.charAt(offset++);
                    switch (escape) {
                        case '"', '\\', '/' -> result.append(escape);
                        case 'b' -> result.append('\b');
                        case 'f' -> result.append('\f');
                        case 'n' -> result.append('\n');
                        case 'r' -> result.append('\r');
                        case 't' -> result.append('\t');
                        case 'u' -> result.append(parseUnicodeEscape());
                        default -> throw new RpcException(-32700, "Invalid JSON escape.");
                    }
                }
                throw new RpcException(-32700, "Unterminated JSON string.");
            }

            private char parseUnicodeEscape() {
                if (offset + 4 > text.length()) throw new RpcException(-32700, "Invalid unicode escape.");
                var value = Integer.parseInt(text.substring(offset, offset + 4), 16);
                offset += 4;
                return (char) value;
            }

            private Object parseLiteral(String literal, Object value) {
                if (!text.startsWith(literal, offset)) throw new RpcException(-32700, "Invalid JSON literal.");
                offset += literal.length();
                return value;
            }

            private Number parseNumber() {
                var start = offset;
                if (isAtEnd() || (text.charAt(offset) != '-' && !Character.isDigit(text.charAt(offset)))) {
                    var token = isAtEnd() ? "<eof>" : Character.toString(text.charAt(offset));
                    throw new RpcException(-32700, "Unexpected JSON token '" + token + "' at offset " + offset + ".");
                }
                if (text.charAt(offset) == '-') offset++;
                while (!isAtEnd() && Character.isDigit(text.charAt(offset))) offset++;
                if (!isAtEnd() && text.charAt(offset) == '.') {
                    offset++;
                    while (!isAtEnd() && Character.isDigit(text.charAt(offset))) offset++;
                }
                if (!isAtEnd() && (text.charAt(offset) == 'e' || text.charAt(offset) == 'E')) {
                    offset++;
                    if (!isAtEnd() && (text.charAt(offset) == '+' || text.charAt(offset) == '-')) offset++;
                    while (!isAtEnd() && Character.isDigit(text.charAt(offset))) offset++;
                }
                try {
                    var value = text.substring(start, offset);
                    if (value.contains(".") || value.contains("e") || value.contains("E")) {
                        return Double.parseDouble(value);
                    }
                    return Long.parseLong(value);
                } catch (NumberFormatException ex) {
                    throw new RpcException(-32700, "Invalid JSON number.");
                }
            }

            void skipWhitespace() {
                while (!isAtEnd() && Character.isWhitespace(text.charAt(offset))) offset++;
            }

            boolean isAtEnd() {
                return offset >= text.length();
            }

            private boolean consume(char expected) {
                if (!isAtEnd() && text.charAt(offset) == expected) {
                    offset++;
                    return true;
                }
                return false;
            }

            private void expect(char expected) {
                if (!consume(expected)) throw new RpcException(-32700, "Expected '" + expected + "'.");
            }
        }
    }
}
