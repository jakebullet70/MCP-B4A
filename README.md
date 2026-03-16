![mcp-b4a](https://github.com/user-attachments/assets/115aa63b-3a12-4408-8ae1-479290cfca2f)


# MCP Server for B4A

Bridges [Claude Code](https://claude.ai/claude-code) (and any MCP-compatible client) with the [B4A](https://www.b4x.com/b4a.html) (Basic4Android) ecosystem.

Exposes 20 tools for compiling projects, reading/modifying layouts, exploring libraries, deploying APKs, and debugging via ADB — all without leaving your AI coding assistant.

---

## Tools

### Configuration

| Tool | Description |
|------|-------------|
| `b4a_get_config` | Returns current paths and config sources (auto-detected vs explicit) |
| `b4a_set_config` | Updates a configuration value |

### Build & Deploy

| Tool | Description |
|------|-------------|
| `b4a_build` | Compiles a B4A project via B4ABuilder.exe (release/debug/bundle) |
| `b4a_get_build_log` | Returns the log from the last build |
| `b4a_get_signing_info` | Returns keystore path, alias, and signing status (passwords are never exposed) |
| `b4a_install_apk` | Installs an APK on a connected device via ADB |

### Project

| Tool | Description |
|------|-------------|
| `b4a_read_project` | Reads project metadata: libraries, modules, build configs |
| `b4a_list_project_files` | Lists source files, layouts, and assets |
| `b4a_project_context` | Single-call overview: app info, libraries, modules, and last build error |
| `b4a_list_recent_projects` | Lists recently opened projects from the B4A IDE history |

### Layouts

| Tool | Description |
|------|-------------|
| `b4a_read_layout` | Converts binary .bal/.bil to JSON |
| `b4a_write_layout` | Writes JSON back to .bal/.bil (with validation and backup) |
| `b4a_list_layouts` | Lists all layout files in a project directory |

### Libraries

| Tool | Description |
|------|-------------|
| `b4a_list_libraries` | Lists available B4A libraries with version info |
| `b4a_get_library_docs` | Returns formatted method/property/event documentation for a library |
| `b4a_search_library` | Searches across all library documentation |

### Manifest

| Tool | Description |
|------|-------------|
| `b4a_read_manifest` | Extracts the Manifest Editor block from a .b4a file |
| `b4a_write_manifest` | Updates the Manifest Editor block |

### ADB / Debugging

| Tool | Description |
|------|-------------|
| `b4a_get_logcat` | Returns logcat output filtered by the B4A tag |
| `b4a_list_devices` | Lists connected ADB devices |

---

## Prerequisites

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or use the self-contained build)
- [B4A IDE](https://www.b4x.com/b4a.html) installed (required for compilation)
- [Android SDK Platform Tools](https://developer.android.com/tools/releases/platform-tools) (required for ADB tools)

---

## Installation

### 1. Build

```powershell
cd B4aMcp
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../publish
```

### 2. Configure Claude Code

```bash
claude mcp add b4a C:\path\to\publish\B4aMcp.exe
```

Or add manually to your MCP settings:

```json
{
  "mcpServers": {
    "b4a": {
      "command": "C:\\path\\to\\publish\\B4aMcp.exe",
      "args": []
    }
  }
}
```

### 3. First-time setup

Run `b4a_get_config` to check auto-detected paths. If B4A is installed in a non-standard location, configure manually:

```
b4a_set_config(key="b4aPath", value="C:\\B4A")
b4a_set_config(key="additionalLibrariesPath", value="C:\\B4A\\SharedLibs")
```

The server auto-detects paths from the B4A IDE settings file (`b4xV5.ini`) when possible. Config is stored at `%APPDATA%\mcp-b4a\config.json`.

---

## Layout Files (.bal / .bil)

B4A stores UI layouts in a proprietary binary format. This server includes a full port of the official [BalConverter](https://github.com/B4X-Community/B4X-BalConverter) to VB.NET, providing lossless binary-to-JSON roundtrip conversion.

- `.bal` — standard layout (Activity layouts)
- `.bil` — internal layout variant (RECT32/CNULL entries stripped on write)

**Roundtrip safety:** `b4a_write_layout` validates JSON structure and always creates a `.bak` backup before writing.

### EditText Required Properties

EditText controls in layout JSON require specific properties that differ from other control types. The B4A runtime does **not** null-guard all of them, so missing properties cause runtime crashes.

| Property | Type | Required | Default | Notes |
|----------|------|----------|---------|-------|
| `inputType` | string | Yes | — | `TEXT`, `NUMBERS`, `DECIMAL_NUMBERS`, `PHONE`, `NONE` |
| `password` | boolean | **Yes** | — | No null guard in B4A runtime — **crashes if missing** |
| `hint` | string | No | `""` | Placeholder text |
| `hintColor` | color | No | `0xFFF0F0F0` | Uses `ValueType: 6` format |
| `singleLine` | boolean | No | `true` | Text wrapping behavior |
| `wrap` | boolean | No | `true` | Has default in runtime |
| `forceDone` | boolean | No | `false` | Has default in runtime |

> **`inputType` values must be string constants, NOT integers.** B4A uses Java reflection (`getField("INPUT_TYPE_" + value)`) internally, so numeric values like `1` or `129` will crash at runtime with `NoSuchFieldException`.

`b4a_write_layout` automatically injects missing `password` and `singleLine` with safe defaults, and converts numeric `inputType` values to their named equivalents when possible. Warnings are included in the response.

---

## Security

- `b4a_build` only accepts paths to existing `.b4a` files — no arbitrary command execution.
- All file writes create `.bak` backups first.
- ADB tools are read-only (`logcat`, `devices`) except for `b4a_install_apk` which only runs `adb install`.
- Signing passwords from `b4xV5.ini` are never exposed through `b4a_get_signing_info`.

---

## Caching

The server caches results to avoid redundant I/O:

- **File-based cache** (mtime-invalidated): layout conversions, project parsing, library docs
- **TTL cache** (60s): device list, library listings
- **Simple store**: last build log (no expiry, replaced on each build)

Cache is invalidated automatically when source files change.

---

## Development

```powershell
cd B4aMcp
dotnet build
dotnet run
```

The server communicates via **stdio** (MCP standard). It does not open any network ports.

### Project Structure

```
B4aMcp/
├── Program.vb              # Server entry point
├── Models/
│   ├── B4aProject.vb       # Project metadata model
│   └── McpConfig.vb        # Configuration model
├── Tools/
│   ├── AdbTools.vb          # logcat, devices, install APK
│   ├── BuildTools.vb        # compile, build log, signing info
│   ├── ConfigTools.vb       # get/set configuration
│   ├── LayoutTools.vb       # read/write/list layouts + EditText validation
│   ├── LibraryTools.vb      # list, docs, search libraries
│   ├── ManifestTools.vb     # read/write manifest block
│   └── ProjectTools.vb      # project metadata, file listing, context
└── Utils/
    ├── AppConfig.vb          # Config management + B4A IDE auto-detection
    ├── B4aParser.vb          # .b4a project file parser
    ├── BalConverter.vb        # Binary .bal/.bil ↔ JSON converter
    └── CacheManager.vb       # Mtime-based + TTL caching
```

### Tech Stack

- **VB.NET** targeting **.NET 8**
- [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) SDK (0.9.0-preview.2)
- [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) for serialization

---

## License

MIT
