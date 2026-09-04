# File Analysis Tool

Scans local directories, extracts file content, analyzes it with a local Ollama model, and indexes
everything in PostgreSQL for full-text + semantic search — queryable from the CLI or from Claude via
an MCP server. See [PLAN.md](PLAN.md) for architecture and design decisions.

## Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL + pgvector)
- [Ollama](https://ollama.com) running locally, with models pulled:
  ```
  ollama pull qwen3:14b
  ollama pull nomic-embed-text
  ```

## Setup

```
docker compose -f docker/docker-compose.yml up -d
dotnet ef database update --project src/FileAnalysis.Infrastructure --startup-project src/FileAnalysis.CLI
```

(`scan` also applies pending migrations automatically on non-dry-run invocations.)

## CLI usage

```
dotnet run --project src/FileAnalysis.CLI -- scan --path "C:\some\folder" [--verbose] [--dry-run] [--skip-ai] [--reanalyze] [--skip-survey] [--auto-confirm] [--survey-only]
dotnet run --project src/FileAnalysis.CLI -- search "<query>" [--limit 10] [--type pdf] [--tag foo] [--category code] [--since 2024-01-01]
dotnet run --project src/FileAnalysis.CLI -- show <file-id>
dotnet run --project src/FileAnalysis.CLI -- similar <file-id> [--limit 10]
dotnet run --project src/FileAnalysis.CLI -- stats
```

`--skip-ai` skips Ollama analysis/embedding (metadata + content extraction only, fast). `--reanalyze`
forces re-analysis of unchanged files that already have an analysis.

### Directory survey (runs automatically before indexing)

Before touching any file, `scan` walks the directory structure and decides what's even worth indexing:
directories matching the static `Scanner.ExcludePatterns` list (`node_modules`, `bin`, `obj`, `.git`, ...)
are excluded automatically with no AI involved; large directories (200+ files or 100+ MB by default,
see `DirectorySurvey` in `appsettings.json`) that don't match the static list get a single AI
classification call (structure only — names/counts/extensions, never file contents) to guess whether
they're a build artifact/dependency cache or real content. High-confidence "junk" verdicts are
auto-excluded and reported; anything the AI is unsure about is shown to you to confirm interactively
before the real scan proceeds. A directory's decision is never re-asked or overwritten on later scans of
the same path.

- `--skip-survey` — old behavior, static exclude list only, no structure survey or AI classification.
- `--auto-confirm` — non-interactive: accept the AI's suggested classification for undecided directories
  instead of prompting (useful for cron/unattended runs).
- `--survey-only` — run the survey (and any confirmation) and stop, without indexing a single file. Handy
  for just seeing what's actually inside a directory tree before committing to a full scan.

## Cleanup suggestions

Once files are indexed, find what's actually safe to remove — real duplicate detection (exact hash +
semantic near-duplicates), low AI-usefulness scores, and staleness — through a persistent, resumable,
human-approved workflow. Nothing is ever deleted or moved without an explicit `apply-cleanup` run, and
`apply-cleanup` only **moves** files into a quarantine directory (`Cleanup.QuarantineRoot` in
`appsettings.json`, mirroring the original directory tree) — never a real delete.

```
dotnet run --project src/FileAnalysis.CLI -- suggest-cleanup [--path <dir>] [--older-than 365] [--min-usefulness 3] [--similarity-threshold 0.97] [--dry-run]
dotnet run --project src/FileAnalysis.CLI -- list-cleanup [--status pending|approved|rejected|applied] [--path <prefix>]
dotnet run --project src/FileAnalysis.CLI -- approve-cleanup <id|--all --reason <r>|--path <prefix>>
dotnet run --project src/FileAnalysis.CLI -- reject-cleanup <id|--all --reason <r>|--path <prefix>>
dotnet run --project src/FileAnalysis.CLI -- apply-cleanup <id|--all --reason <r>|--path <prefix>> [--dry-run]
```

`<id>` accepts a full suggestion ID or an unambiguous prefix (the short IDs `list-cleanup` prints).
`--all` always requires `--reason` or `--path` to scope it — there's no bare "decide everything" flag, on
purpose. Re-running `suggest-cleanup` never overwrites a suggestion you've already approved or rejected.

`suggest_cleanup`, `list_cleanup_suggestions`, `approve_cleanup`, and `reject_cleanup` are also available
as MCP tools (conversational review via `chat` or Claude). `apply-cleanup` is deliberately **CLI-only** —
the one step that actually touches your filesystem is never reachable from a chat/LLM context.

## MCP Server (Claude Desktop / Claude Code)

Build once:

```
dotnet publish src/FileAnalysis.MCP -c Release
```

Then register it in `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "file-analysis": {
      "command": "dotnet",
      "args": ["C:\\code\\chomadev\\file-analysis\\src\\FileAnalysis.MCP\\bin\\Release\\net8.0\\FileAnalysis.MCP.dll"]
    }
  }
}
```

The server reads its own `appsettings.json` (next to the built DLL) for the Postgres connection string
and Ollama settings — edit that file if your database or Ollama instance isn't on localhost.

Exposed tools: `search_files`, `get_file_analysis`, `find_similar`, `list_categories`, `list_tags`, `get_stats`,
`suggest_cleanup`, `list_cleanup_suggestions`, `approve_cleanup`, `reject_cleanup`.

Only stdio transport is implemented (what Claude Desktop uses). HTTP/SSE transport for a remote-hosted
server was listed as optional in the plan and has not been built.

## Published binaries (plug-and-play)

Self-contained, single-file `win-x64` builds (no .NET SDK/runtime required to run them) live at:

```
C:\tools\file-analysis\cli\FileAnalysis.CLI.exe
C:\tools\file-analysis\mcp\FileAnalysis.MCP.exe
```

Rebuild after any code change:

```
dotnet publish src/FileAnalysis.CLI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o C:\tools\file-analysis\cli
dotnet publish src/FileAnalysis.MCP -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o C:\tools\file-analysis\mcp
```

A Claude Code skill wrapping the published CLI (and documenting MCP registration) is installed at
`~/.claude/skills/file-analysis/SKILL.md` — Claude picks it up automatically for tasks like
"clean up this old folder" or "find duplicates in X" without needing this repo open.

## Terminal chat with a local model (no Open WebUI needed)

For talking to a local Ollama model (with native tool-calling, e.g. `qwen3:14b`) wired directly to the
file-analysis tools, entirely from the terminal:

```
dotnet publish src/FileAnalysis.MCP -c Release   # once, or after any FileAnalysis.MCP change
dotnet run --project src/FileAnalysis.CLI -- chat [--model qwen3:14b] [--mcp-dll <path>]
```

This spawns the MCP server as a subprocess over stdio and drives the Ollama `/api/chat` tool-calling
loop directly — no `mcpo`, no Open WebUI, no extra services. Type `sair` to exit.
