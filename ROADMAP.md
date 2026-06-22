# MCP-B4A Roadmap

Planned features and improvements for the B4A MCP server. Today the server exposes
**31 tools** across build/deploy, project metadata, layouts, libraries, source modules,
manifest editing, device interaction, sprite cleanup, and ADB debugging.

This roadmap is organized by theme. Each item lists rough **effort** (S/M/L) and the
rationale. Priorities are grouped into phases at the bottom.

> **Guiding signal:** the sibling **B4J MCP server** already ships several capabilities
> this server lacks (`find_symbol`, `outline`, `doctor`, `create_module`, `clone_layout`,
> `multi_edit_bas`, `tail_log`, `run`/`stop`/`list_processes`, `open_ide`). Closing that
> parity gap is the single biggest opportunity, since the B4X languages and project
> formats are nearly identical.

---

## 1. Code Intelligence & Navigation (highest value)

The server can read and edit `.bas` files as plain text, but has no structural
understanding of B4A code. AI agents currently re-read whole modules to find a sub.

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Module outline** | `b4a_outline` | M | Return all `Sub`/`Public Sub`/`Private Sub`, globals, and `#Region` markers with line ranges. Lets an agent jump to a sub without reading the whole file. (B4J already has this.) |
| **Symbol search** | `b4a_find_symbol` | M | Find a sub/global definition and all call sites across every module in a project. Critical for safe refactors given B4A's case-insensitivity footgun. |
| **Find references / rename** | `b4a_rename_symbol` | L | Project-wide rename respecting B4A case-insensitivity. Guards against the #1 gotcha (local shadowing a global). |
| **Compile-error mapping** | `b4a_parse_build_errors` | S | Parse the last build log into structured `{module, line, message}` objects instead of a single grep'd line in `b4a_project_context`. |
| **Dependency graph** | `b4a_module_graph` | M | Which modules `Call`/reference which — helps scope changes and spot dead modules. |

## 2. Editing Ergonomics

`b4a_edit_bas` is single search-and-replace only. Multi-step edits require N round-trips,
each rewriting the file and re-creating a `.bak`.

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Batch edits** | `b4a_multi_edit_bas` | M | Apply an ordered list of find/replace ops in one transaction with a single backup. (B4J already has `multi_edit_bas`.) |
| **Module scaffolding** | `b4a_create_module` | M | Generate a new `.bas` (CodeModule / Class / Activity) with the correct boilerplate and register it in the `.b4a` file (`NumberOfFiles`, `ModuleN`). |
| **Insert/append sub** | `b4a_insert_sub` | S | Add a sub at a known location without hand-crafting a find/replace anchor. |
| **Sub-aware edit** | `b4a_edit_sub` | M | Replace an entire sub body by name — more robust than text matching when indentation varies. |
| **Lint pass** | `b4a_lint` | M | Static checks for the documented gotchas: reserved-word identifiers, local/global case collisions, `MediaPlayer` usage, `Colors.R/G/B`, hardcoded pixel dimensions. |

## 3. Device & Debug Loop

The build → install → launch → screenshot → logcat loop works but is fragmented and
dump-only.

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Live log streaming** | `b4a_tail_log` | M | Stream new logcat lines since a cursor (or follow for N seconds) instead of only `logcat -d`. Essential for watching a crash happen in real time. (B4J has `tail_log`.) |
| **One-shot run + watch** | `b4a_run` | M | Build + install + launch + capture the first crash/exception from logcat, returning a structured result. Mirrors B4J's `run`. |
| **Crash extraction** | `b4a_get_last_crash` | S | Pull the most recent stack trace / `Application_Error` entry and map it back to module + line. |
| **App lifecycle** | `b4a_uninstall`, `b4a_clear_data`, `b4a_stop_app`, `b4a_grant_permission` | S | Clean-slate testing without manual ADB. |
| **Emulator control** | `b4a_list_emulators`, `b4a_start_emulator` | M | Boot an AVD when no physical device is connected. |
| **Configurable timeouts** | (build/adb) | S | `b4a_build` hardcodes a 300s timeout; ADB shell calls hardcode 10–60s. Expose via config. |

## 4. Layouts & Assets

`BalConverter` gives lossless `.bal`/`.bil` ↔ JSON roundtrip — a strong foundation to
build higher-level layout tooling on.

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Clone layout** | `b4a_clone_layout` | S | Duplicate a layout file (and rename its views) as a starting point. (B4J has `clone_layout`.) |
| **Create layout** | `b4a_create_layout` | M | Generate a minimal valid `.bal` from scratch / from a view spec. |
| **Add/remove view** | `b4a_layout_add_view` | M | Insert a Button/Label/EditText/Panel into an existing layout JSON with correct defaults (extends the EditText auto-fix logic already in `ValidateAndFixEditTexts`). |
| **Layout diff** | `b4a_diff_layout` | S | Human-readable diff between two layouts or before/after an edit. |
| **Responsive audit** | `b4a_layout_check_anchors` | M | Flag hardcoded pixel positions vs `%x`/`%y` — enforces the DrawText/DPI rule from the README. |

### Sprite / Asset Pipeline

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Sprite-sheet slicing** | `b4a_sprite_slice` | M | Cut a grid sheet into individual frames (companion to existing `b4a_sprite_cleanup`). |
| **Sprite packing** | `b4a_sprite_pack` | M | Pack frames into an atlas + emit coordinate metadata. |
| **Asset registration** | `b4a_add_asset` | S | Copy a file into `Files/` and register it in the project's `Files` group. |

## 5. Cross-Platform & Quality (infrastructure)

> **Note:** the server is **Windows-only by design** — the B4A IDE and `B4ABuilder.exe`
> only run on Windows and the server always runs alongside them. No cross-platform work is
> planned; the `System.Drawing.Common` usage (and its CA1416 warnings) is acceptable as-is.

| Feature | Effort | Why |
|---------|--------|-----|
| **Shared ADB helper** ✅ | S | *Done.* `FindAdb()` + `ProcessStartInfo`/async-read boilerplate consolidated into `Utils/AdbRunner.vb`. |
| **Automated tests** | M | No tests today. Add unit tests for `B4aParser`, `BalConverter` (roundtrip), and `ValidateAndFixEditTexts`. The binary roundtrip especially needs a regression net. |
| **CI pipeline** | S | GitHub Actions: `dotnet build` + tests + `dotnet publish` on tag. |
| **Structured errors** | M | Tools return free-text `"Error: ..."` strings; agents must string-match (e.g. `b4a_build_and_install` greps for `"Completed successfully"`). Standardize a JSON `{ok, error, data}` envelope. |
| **Path validation hardening** | S | Centralize the "file exists + correct extension" checks repeated in every tool. |

## 6. Project & IDE Integration

| Feature | Tool | Effort | Why |
|---------|------|--------|-----|
| **Open in IDE** | `b4a_open_ide` | S | Launch the B4A IDE on a project/module/line. (B4J has `open_ide`.) |
| **Health check** | `b4a_doctor` | M | One call that verifies B4A path, ADB, JDK/Android SDK, keystore, and connected device — reports what's misconfigured. (B4J has `doctor`.) |
| **Version bump** | `b4a_bump_version` | S | Increment `VersionCode`/`VersionName` in the `.b4a` Project Attributes for release. |
| **Library resolver** | `b4a_check_libraries` | M | Cross-check a project's referenced `LibraryN` entries against installed libraries; flag missing/version-mismatched libs before a build fails. |

---

## Suggested Phasing

**Phase 1 — Parity & daily-driver wins** ✅ *done*
- `b4a_outline`, `b4a_find_symbol` (§1)
- `b4a_multi_edit_bas`, `b4a_create_module` (§2)
- `b4a_tail_log`, `b4a_get_last_crash` (§3)
- `b4a_doctor`, `b4a_open_ide` (§6)
- Shared ADB helper ✅ (structured-error envelope deferred — would change the response contract of all existing tools; do as a deliberate pass)

**Phase 2 — Refactor safety & robustness** ✅ *done*
- `b4a_rename_symbol`, `b4a_lint`, `b4a_parse_build_errors` (§1–2)
- `b4a_run` one-shot loop, app lifecycle tools — `b4a_uninstall`/`b4a_clear_data`/`b4a_stop_app`/`b4a_grant_permission` (§3)
- `b4a_clone_layout`, `b4a_create_layout`, `b4a_check_libraries` (§4, §6)
- Automated tests (xUnit) — `B4aParser`, `BalConverter` roundtrip, `BasAnalyzer`, `b4a_lint` (§5)

**Phase 3 — Reach & polish** ← *next*
- Layout view manipulation + responsive audit (§4)
- Sprite slice/pack pipeline (§4)
- Emulator control, CI pipeline (§3, §5)
- Structured-error envelope across all tools (deferred from Phase 1 — deliberate one-pass migration)

---

*Generated from a review of the current codebase (31 tools across 10 tool classes) on
2026-06-22. Effort estimates: S ≈ <½ day, M ≈ 1–2 days, L ≈ 3+ days.*
