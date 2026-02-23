# roslyn-rag

A local RAG system for .NET codebases. It parses your solution with Roslyn, builds a searchable index (vector + keyword), and lets you ask questions about your code using a local LLM. Everything runs on your machine — no API keys, no cloud.

## What it does

You point it at a `.sln` or `.slnx` file. It opens the solution with Roslyn's compiler APIs, walks the syntax trees, and extracts chunks: class headers, methods, constructors — each with its namespace, dependencies, base types, and attributes attached. Those chunks get embedded and stored in Qdrant for vector search, and indexed in Lucene.NET for BM25 keyword search.

When you ask a question, it runs both searches in parallel, merges the results with Reciprocal Rank Fusion (RRF), and feeds the top matches as context to a local LLM. The answer comes back grounded in your actual code, with file paths and line numbers.

It also tracks the git commit SHA after each indexing run. Next time you index, it diffs against the last commit and only re-processes changed files.

## How it works

```
Index:  .sln → Roslyn parse → chunk extraction → embed (Ollama) → Qdrant + Lucene
                                     ↓
                          syntax-aware splitting for large chunks

Query:  question → embed → vector search (Qdrant)  ─┐
                         → keyword search (Lucene) ──┤→ RRF fusion → LLM → answer
```

Parsing uses a `CSharpSyntaxWalker` to visit every class, method, and constructor. For each chunk, it builds an embedding text that includes file path, namespace, class hierarchy, constructor dependencies, and the source code. Chunks larger than 4000 characters get split at syntax boundaries — blank lines, closing braces, statement ends — so nothing cuts off mid-expression.

Search is hybrid. Vector similarity catches semantic matches ("how does authentication work" finds your auth code even if the word "authentication" never appears). BM25 catches exact matches (searching for `ParseSolutionAsync` finds exactly that). RRF merges both ranked lists with `score = 1/(k + rank + 1)`, so a result that ranks high in both lists gets boosted.

Incremental indexing calls `git diff --name-status` between the stored commit and HEAD, deletes stale chunks for modified/deleted files, then parses and embeds only what changed.

## Tech stack

| Component | What | Why |
|---|---|---|
| Roslyn (`Microsoft.CodeAnalysis`) | C# parsing with full compiler context | Accurate AST, resolves types, understands the whole solution |
| Qdrant | Vector database (cosine similarity, gRPC) | Runs in Docker, no config needed |
| Lucene.NET | BM25 keyword index | In-process, no external service |
| Ollama | Local model inference | Runs embedding and LLM models locally |
| `nomic-embed-text` | Embedding model (768 dim) | Good general-purpose embeddings, runs on CPU |
| `llama3:8b` | LLM for answer generation | Handles code questions at 8B, swap for a larger model if you need more |
| RRF | Result fusion | Simple, parameter-light way to combine two ranked lists |

## Prerequisites

- [.NET 10 SDK](https://dot.net/download) (preview)
- [Docker](https://docker.com/products/docker-desktop) (for Qdrant)
- [Ollama](https://ollama.com/download)

## Quick start

There's a setup script that handles everything — checks dependencies, starts Qdrant, starts Ollama, pulls models, builds the project:

```bash
# macOS / Linux
./setup.sh

# Windows (PowerShell)
.\setup.ps1
```

### Manual setup

```bash
# Start Qdrant
docker compose up -d

# Start Ollama (keep running in background)
ollama serve &

# Pull models
ollama pull nomic-embed-text
ollama pull llama3:8b

# Build
dotnet build
```

## Usage

```bash
# Index a solution
dotnet run --project src/RoslynRag.Cli -- index path/to/YourSolution.sln

# Force full re-index (skip incremental)
dotnet run --project src/RoslynRag.Cli -- index path/to/YourSolution.sln --full

# Ask a question
dotnet run --project src/RoslynRag.Cli -- query "How does incremental indexing work?"

# Get raw search results without LLM
dotnet run --project src/RoslynRag.Cli -- query "QueryPipeline" --no-llm

# Return more results
dotnet run --project src/RoslynRag.Cli -- query "error handling" --top-k 20

# Check index status
dotnet run --project src/RoslynRag.Cli -- status

# Delete everything and start fresh
dotnet run --project src/RoslynRag.Cli -- reset
```

If Qdrant or Ollama isn't running when you run `index` or `query`, you'll get a clear error message telling you what to start and how.

### Install as a global tool

If you don't want to type `dotnet run --project ...` every time:

```bash
dotnet pack src/RoslynRag.Cli -o nupkg
dotnet tool install --global --add-source nupkg RoslynRag
```

Then:

```bash
roslyn-rag index path/to/YourSolution.sln
roslyn-rag query "What does the Fuse method do?"
roslyn-rag status
```

## Project structure

```
src/
  RoslynRag.Core/        Interfaces and models (no dependencies)
  RoslynRag.Parsing/     Roslyn-based code parsing + chunk splitting
  RoslynRag.Indexing/    Indexing pipeline, embedding service, git diff
  RoslynRag.Storage/     Qdrant, Lucene, RRF fusion, state persistence
  RoslynRag.Query/       Query pipeline, LLM service, prompt construction
  RoslynRag.Cli/         CLI commands and entry point
tests/
  One test project per library
```

## Configuration

Settings are constants in `src/RoslynRag.Cli/Program.cs`. There's no config file yet — change the values directly if you need different defaults.

| Setting | Default | Notes |
|---|---|---|
| Qdrant | `localhost:6334` | gRPC port |
| Ollama | `http://localhost:11434` | |
| Embedding model | `nomic-embed-text` | 768 dimensions |
| LLM | `llama3:8b` | Used for answer generation |
| Batch size | `32` | Embedding batch size per Ollama request |
| Max chunk size | `4000` chars | Chunks larger than this get split |

## Limitations

- Only parses C# files (`.cs`). No F#, VB, or other languages.
- Needs the solution to build. Roslyn opens it as a full workspace, so broken project references will cause warnings or missing files.
- LLM answer quality depends on the model. `llama3:8b` works for straightforward questions but struggles with complex architectural queries. You can swap in a larger model through `Program.cs`.
- Chunk splitting is heuristic-based (line boundaries). A tree-sitter integration that splits at AST node boundaries is planned but not implemented yet.
