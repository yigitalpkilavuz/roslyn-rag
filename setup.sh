#!/usr/bin/env bash
set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()  { echo -e "${BLUE}[INFO]${NC} $1"; }
ok()    { echo -e "${GREEN}[OK]${NC} $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
fail()  { echo -e "${RED}[FAIL]${NC} $1"; exit 1; }

OS="$(uname -s)"

echo ""
echo "=== Roslyn RAG — Setup ==="
echo ""

# 1. Check .NET SDK
info "Checking .NET SDK..."
if command -v dotnet &>/dev/null; then
    ok "dotnet $(dotnet --version)"
else
    fail "dotnet not found. Install .NET 10 SDK: https://dot.net/download"
fi

# 2. Check Docker & start Qdrant
info "Checking Docker..."
if command -v docker &>/dev/null; then
    if docker info &>/dev/null; then
        ok "Docker is running"
        info "Starting Qdrant..."
        docker compose up -d
        ok "Qdrant started on ports 6333 (REST) and 6334 (gRPC)"
    else
        fail "Docker is installed but not running. Start Docker Desktop first."
    fi
else
    fail "Docker not found. Install Docker Desktop: https://docker.com/products/docker-desktop"
fi

# 3. Check Ollama
info "Checking Ollama..."
if command -v ollama &>/dev/null; then
    ok "Ollama found"
else
    case "$OS" in
        Darwin) fail "Ollama not found. Install with: brew install ollama" ;;
        Linux)  fail "Ollama not found. Install with: curl -fsSL https://ollama.com/install.sh | sh" ;;
        *)      fail "Ollama not found. Install from: https://ollama.com/download" ;;
    esac
fi

# 4. Check if Ollama is serving
info "Checking if Ollama is serving..."
if curl -sf http://localhost:11434/api/tags &>/dev/null; then
    ok "Ollama is serving"
else
    info "Starting Ollama..."
    if [[ "$OS" == "Linux" ]] && command -v systemctl &>/dev/null; then
        systemctl start ollama 2>/dev/null || ollama serve &>/dev/null &
    else
        ollama serve &>/dev/null &
    fi
    sleep 3
    if curl -sf http://localhost:11434/api/tags &>/dev/null; then
        ok "Ollama started"
    else
        warn "Could not start Ollama automatically. Run 'ollama serve' in another terminal."
    fi
fi

# 5. Pull models
info "Pulling embedding model (nomic-embed-text)..."
ollama pull nomic-embed-text

info "Pulling LLM model (llama3:8b)..."
ollama pull llama3:8b

ok "All models ready"

# 6. Build project
info "Building project..."
dotnet build --configuration Release --verbosity quiet
ok "Build succeeded"

# 7. Done
echo ""
echo -e "${GREEN}=== Setup complete! ===${NC}"
echo ""
echo "Usage:"
echo "  dotnet run --project src/RoslynRag.Cli -- index <path-to.sln>"
echo "  dotnet run --project src/RoslynRag.Cli -- query \"How does X work?\""
echo "  dotnet run --project src/RoslynRag.Cli -- status"
echo ""
echo "Or install as a global tool:"
echo "  dotnet pack src/RoslynRag.Cli -o nupkg"
echo "  dotnet tool install --global --add-source nupkg RoslynRag"
echo "  roslyn-rag index <path-to.sln>"
echo ""
