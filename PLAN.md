# File Analysis Tool — Implementation Plan

## Architecture

```
┌─────────────────────────────────────────┐     ┌──────────────────────────────┐
│         LOCAL MACHINE (files live here) │     │   REMOTE SERVER (optional)   │
│                                         │     │                              │
│  ┌─────────────────────────────────┐    │     │  ┌────────────────────────┐  │
│  │  file-analysis CLI              │    │     │  │  PostgreSQL + pgvector │  │
│  │  - crawls directories           │    │ TCP │  │  - all metadata        │  │
│  │  - extracts file content        │◄───┼─────┼──│  - AI analysis         │  │
│  │  - calls Ollama (local)         │    │     │  │  - embeddings          │  │
│  │  - writes to remote Postgres    │    │     │  └────────────────────────┘  │
│  └─────────────────────────────────┘    │     │                              │
│                                         │     │  ┌────────────────────────┐  │
│  ┌─────────────────────────────────┐    │     │  │  MCP Server            │  │
│  │  Ollama (local)                 │    │     │  │  - search_files        │  │
│  │  - llama3.2 (analysis)          │    │     │  │  - get_file_analysis   │  │
│  │  - nomic-embed-text (vectors)   │    │     │  │  - find_similar        │  │
│  └─────────────────────────────────┘    │     │  │  - list_categories     │  │
│                                         │     │  └────────────────────────┘  │
└─────────────────────────────────────────┘     └──────────────────────────────┘
                                                         ▲
                                                   Claude Desktop /
                                                   Claude Code
```

## Key Design Decisions

| Decision | Choice | Reason |
|----------|--------|--------|
| AI model | Ollama (local) | File content never leaves the machine |
| Analysis model | llama3.2:3b or mistral:7b | Good quality, runs on consumer hardware |
| Embeddings model | nomic-embed-text | Best local embedding model for semantic search |
| Image/OCR | llava (multimodal via Ollama) | Describes image content locally |
| DB | PostgreSQL 16 + pgvector | Vector + full-text search in one place |
| DB location | Configurable (local or remote) | CLI connects via connection string |
| MCP Server location | Configurable (co-located with DB recommended) | Queries DB locally for speed |
| ORM | EF Core 9 | Code-first migrations, strong .NET ecosystem |
| .NET version | .NET 10 | Per project requirements |

---

## Solution Structure

```
file-analysis/
├── src/
│   ├── FileAnalysis.Core/          # Domain models, interfaces, shared logic
│   ├── FileAnalysis.Infrastructure/ # EF Core, Ollama client, extractors
│   ├── FileAnalysis.CLI/           # Console app — scan, index, search commands
│   └── FileAnalysis.MCP/           # MCP Server — exposes DB to AI assistants
├── tests/
│   ├── FileAnalysis.Core.Tests/
│   ├── FileAnalysis.Infrastructure.Tests/
│   └── FileAnalysis.MCP.Tests/
├── docker/
│   └── docker-compose.yml          # PostgreSQL + pgvector for dev/remote
└── PLAN.md
```

---

## Phases & Tasks

### Phase 1 — Foundation & Database Schema

**Goal:** Crawl a directory, extract file metadata, store in PostgreSQL.

| Task | Description |
|------|-------------|
| 1.1 | Create solution + projects (Core, Infrastructure, CLI) with .NET 10 |
| 1.2 | Design PostgreSQL schema: `files`, `file_contents`, `file_analyses` tables + pgvector |
| 1.3 | EF Core setup: DbContext, migrations, connection string config (local or remote) |
| 1.4 | Docker Compose for PostgreSQL 16 + pgvector (dev environment) |
| 1.5 | Directory crawler: recursive enumeration with include/exclude glob patterns |
| 1.6 | File metadata extraction: path, name, extension, size, created/modified dates, MD5 hash |
| 1.7 | MIME type detection (by extension + magic bytes) |
| 1.8 | Persist metadata to DB; skip unchanged files by hash |
| 1.9 | CLI command: `analyze scan --path <dir>` with progress output |

---

### Phase 2 — Content Extraction

**Goal:** Read actual file content per type, store extracted text.

| Task | Description |
|------|-------------|
| 2.1 | Extractor interface + dispatcher (routes by MIME type) |
| 2.2 | Plain text extractor: .txt, .md, .csv, .log, .json, .xml, .yaml, .toml |
| 2.3 | Code file extractor: .cs, .js, .ts, .py, .go, .rs, .sql, .sh, etc. |
| 2.4 | PDF extractor (PdfPig library — fully managed, no native deps) |
| 2.5 | Word/Excel/PowerPoint extractor (DocumentFormat.OpenXml) |
| 2.6 | Image extractor via Ollama llava model (describes image content) |
| 2.7 | Content chunking for large files (>50KB split into overlapping chunks) |
| 2.8 | Store extracted text in `file_contents` table |

---

### Phase 3 — AI Analysis via Ollama

**Goal:** Send extracted content to local Ollama, store structured analysis.

| Task | Description |
|------|-------------|
| 3.1 | Ollama HTTP client (typed client via IHttpClientFactory) |
| 3.2 | Analysis prompt: returns summary, tags[], category, language, topics[], usefulness_score (1-10), is_duplicate_candidate |
| 3.3 | Parse structured JSON response from Ollama |
| 3.4 | Embedding generation via `nomic-embed-text` model |
| 3.5 | Store embeddings in `file_analyses.embedding` (pgvector vector(768)) |
| 3.6 | Store full analysis JSON in `file_analyses` table |
| 3.7 | Background processing queue (System.Threading.Channels) — don't block scanner |
| 3.8 | CLI flag: `--skip-ai` for metadata-only scan; `--reanalyze` to force re-processing |

---

### Phase 4 — Search & Query

**Goal:** Make the index queryable via CLI.

| Task | Description |
|------|-------------|
| 4.1 | PostgreSQL full-text search: `tsvector` index on summary + tags |
| 4.2 | Semantic search: pgvector cosine similarity on embeddings |
| 4.3 | Hybrid search: combine FTS score + vector similarity |
| 4.4 | CLI command: `analyze search "<query>"` with ranked results |
| 4.5 | Filter flags: `--type pdf`, `--tag game`, `--category code`, `--since 2020` |
| 4.6 | CLI command: `analyze show <file-id>` — full analysis detail |
| 4.7 | CLI command: `analyze stats` — index summary (total files, by type, by category) |
| 4.8 | CLI command: `analyze similar <file-id>` — find semantically similar files |

---

### Phase 5 — MCP Server

**Goal:** Expose the index to Claude Desktop / Claude Code as a local tool.

| Task | Description |
|------|-------------|
| 5.1 | Create `FileAnalysis.MCP` project (ModelContextProtocol .NET SDK) |
| 5.2 | MCP tool: `search_files(query, limit, filters)` — hybrid search |
| 5.3 | MCP tool: `get_file_analysis(file_id)` — full analysis for one file |
| 5.4 | MCP tool: `find_similar(file_id, limit)` — vector similarity |
| 5.5 | MCP tool: `list_categories()` — all categories + counts |
| 5.6 | MCP tool: `get_stats()` — index overview |
| 5.7 | MCP tool: `list_tags(prefix?)` — tag browser |
| 5.8 | Transport: stdio (default for Claude Desktop) + optional HTTP/SSE for remote |
| 5.9 | Configuration: same connection string as CLI (can point to remote DB) |
| 5.10 | claude_desktop_config.json snippet for easy registration |

---

## PostgreSQL Schema (Draft)

```sql
-- Core file record
CREATE TABLE files (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  path        TEXT NOT NULL UNIQUE,
  name        TEXT NOT NULL,
  extension   TEXT,
  mime_type   TEXT,
  size_bytes  BIGINT,
  md5_hash    TEXT,
  created_at  TIMESTAMPTZ,
  modified_at TIMESTAMPTZ,
  indexed_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Extracted text content (chunked for large files)
CREATE TABLE file_contents (
  id        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  file_id   UUID NOT NULL REFERENCES files(id) ON DELETE CASCADE,
  chunk_idx INT NOT NULL DEFAULT 0,
  content   TEXT NOT NULL
);

-- AI analysis results
CREATE TABLE file_analyses (
  id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  file_id          UUID NOT NULL REFERENCES files(id) ON DELETE CASCADE,
  summary          TEXT,
  category         TEXT,
  language         TEXT,
  tags             TEXT[],
  topics           TEXT[],
  usefulness_score INT,   -- 1-10
  raw_response     JSONB,
  embedding        VECTOR(768),  -- nomic-embed-text dimensions
  analyzed_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  search_vector    TSVECTOR GENERATED ALWAYS AS (
    to_tsvector('english', coalesce(summary,'') || ' ' || coalesce(array_to_string(tags,' '),''))
  ) STORED
);

-- Indexes
CREATE INDEX idx_files_extension ON files(extension);
CREATE INDEX idx_analyses_category ON file_analyses(category);
CREATE INDEX idx_analyses_fts ON file_analyses USING GIN(search_vector);
CREATE INDEX idx_analyses_embedding ON file_analyses USING ivfflat(embedding vector_cosine_ops);
```

---

## Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=fileanalysis;Username=fa;Password=secret"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "AnalysisModel": "llama3.2:3b",
    "EmbeddingModel": "nomic-embed-text",
    "VisionModel": "llava:7b"
  },
  "Scanner": {
    "ExcludePatterns": ["node_modules", ".git", "bin", "obj", "*.tmp"],
    "MaxFileSizeBytes": 52428800,
    "MaxContentChunkSize": 4000
  }
}
```

---

## Agents & Skills Assigned

| Component | Agent |
|-----------|-------|
| Core domain + Infrastructure | `csharp-developer` |
| CLI commands | `csharp-developer` |
| MCP Server | `mcp-developer` |
| Unit + integration tests | `qa-expert` |

