$ErrorActionPreference = "Stop"

function Info($msg)  { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Ok($msg)    { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn($msg)  { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Fail($msg)  { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "=== Roslyn RAG - Setup ===" -ForegroundColor White
Write-Host ""

# 1. Check .NET SDK
Info "Checking .NET SDK..."
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $version = dotnet --version
    Ok "dotnet $version"
} else {
    Fail "dotnet not found. Install .NET 10 SDK: https://dot.net/download"
}

# 2. Check Docker & start Qdrant
Info "Checking Docker..."
if (Get-Command docker -ErrorAction SilentlyContinue) {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -eq 0) {
        Ok "Docker is running"
        Info "Starting Qdrant..."
        docker compose up -d
        if ($LASTEXITCODE -eq 0) {
            Ok "Qdrant started on ports 6333 (REST) and 6334 (gRPC)"
        } else {
            Fail "Failed to start Qdrant. Check docker-compose.yml."
        }
    } else {
        Fail "Docker is installed but not running. Start Docker Desktop first."
    }
} else {
    Fail "Docker not found. Install Docker Desktop: https://docker.com/products/docker-desktop"
}

# 3. Check Ollama
Info "Checking Ollama..."
if (Get-Command ollama -ErrorAction SilentlyContinue) {
    Ok "Ollama found"
} else {
    Fail "Ollama not found. Download from: https://ollama.com/download/windows"
}

# 4. Check if Ollama is serving
Info "Checking if Ollama is serving..."
try {
    $null = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5
    Ok "Ollama is serving"
} catch {
    Info "Starting Ollama..."
    Start-Process ollama -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 3
    try {
        $null = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5
        Ok "Ollama started"
    } catch {
        Warn "Could not start Ollama automatically. Run 'ollama serve' in another terminal."
    }
}

# 5. Pull models
Info "Pulling embedding model (nomic-embed-text)..."
ollama pull nomic-embed-text
if ($LASTEXITCODE -ne 0) { Fail "Failed to pull nomic-embed-text" }

Info "Pulling LLM model (llama3:8b)..."
ollama pull llama3:8b
if ($LASTEXITCODE -ne 0) { Fail "Failed to pull llama3:8b" }

Ok "All models ready"

# 6. Build project
Info "Building project..."
dotnet build --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { Fail "Build failed" }
Ok "Build succeeded"

# 7. Done
Write-Host ""
Write-Host "=== Setup complete! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Usage:"
Write-Host "  dotnet run --project src/RoslynRag.Cli -- index <path-to.sln>"
Write-Host "  dotnet run --project src/RoslynRag.Cli -- query `"How does X work?`""
Write-Host "  dotnet run --project src/RoslynRag.Cli -- status"
Write-Host ""
Write-Host "Or install as a global tool:"
Write-Host "  dotnet pack src/RoslynRag.Cli -o nupkg"
Write-Host "  dotnet tool install --global --add-source nupkg RoslynRag"
Write-Host "  roslyn-rag index <path-to.sln>"
Write-Host ""
