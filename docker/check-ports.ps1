<#
.SYNOPSIS
    Pre-flight port availability checker for ShopSphere Docker backing infrastructure.
.DESCRIPTION
    Inspects host ports specified in docker/.env to prevent port collisions before running docker compose up.
#>

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvFile = Join-Path $ScriptDir ".env"

$portConfigs = @(
    @{ Service = "API Gateway (YARP)";        EnvVar = "API_GATEWAY_PORT";     Default = 5001 }
    @{ Service = "Microsoft SQL Server 2022"; EnvVar = "SQLSERVER_PORT";       Default = 11433 }
    @{ Service = "PostgreSQL 16";             EnvVar = "POSTGRES_PORT";        Default = 15432 }
    @{ Service = "MongoDB 7.0";               EnvVar = "MONGO_PORT";           Default = 27018 }
    @{ Service = "Redis 7.2";                 EnvVar = "REDIS_PORT";           Default = 16379 }
    @{ Service = "RabbitMQ AMQP";             EnvVar = "RABBITMQ_PORT";        Default = 15672 }
    @{ Service = "RabbitMQ Management UI";    EnvVar = "RABBITMQ_MGMT_PORT";   Default = 25672 }
    @{ Service = "Datalust Seq UI";           EnvVar = "SEQ_PORT";             Default = 15341 }
    @{ Service = "Datalust Seq Ingestion";    EnvVar = "SEQ_INGESTION_PORT";   Default = 15342 }
    @{ Service = "Jaeger UI";                 EnvVar = "JAEGER_PORT";          Default = 16686 }
    @{ Service = "Jaeger OTLP gRPC";          EnvVar = "JAEGER_OTLP_PORT";     Default = 4317 }
    @{ Service = "Jaeger OTLP HTTP";          EnvVar = "JAEGER_OTLP_HTTP_PORT";Default = 4318 }
)

# Parse .env if available
$envValues = @{}
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | ForEach-Object {
        $line = $_.Trim()
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.StartsWith("#")) {
            $parts = $line.Split('=', 2)
            if ($parts.Count -eq 2) {
                $envValues[$parts[0].Trim()] = $parts[1].Trim()
            }
        }
    }
}

function Test-PortInUse([int]$Port) {
    try {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        $listener.Stop()
        return $false
    }
    catch {
        return $true
    }
}

function Find-AlternativePort([int]$StartPort) {
    $alt = $StartPort + 1
    while ($alt -lt 65535) {
        if (-not (Test-PortInUse $alt)) {
            return $alt
        }
        $alt++
    }
    return $StartPort + 1000
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  ShopSphere Pre-Flight Infrastructure Port Checker" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "Checking configured host ports against active network bindings..." -ForegroundColor Gray
Write-Host ""

$hasConflicts = $false

foreach ($cfg in $portConfigs) {
    $port = $cfg.Default
    if ($envValues.ContainsKey($cfg.EnvVar)) {
        [int]::TryParse($envValues[$cfg.EnvVar], [ref]$port) | Out-Null
    }

    $inUse = Test-PortInUse -Port $port

    if ($inUse) {
        $hasConflicts = $true
        $suggested = Find-AlternativePort -StartPort $port
        Write-Host " [CONFLICT] $($cfg.Service)" -ForegroundColor Red
        Write-Host "            Variable:  $($cfg.EnvVar)" -ForegroundColor Yellow
        Write-Host "            Port:      $port is ALREADY IN USE by another process." -ForegroundColor Red
        Write-Host "            Action:    Update $($cfg.EnvVar)=$suggested in docker/.env" -ForegroundColor Yellow
        Write-Host ""
    }
    else {
        Write-Host " [OK]       $($cfg.Service.PadRight(28)) Port: $port is available" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan

if ($hasConflicts) {
    Write-Host "WARNING: One or more ports are already bound!" -ForegroundColor Red
    Write-Host "Please adjust conflicting port values in docker/.env before starting the containers." -ForegroundColor Yellow
    exit 1
}
else {
    Write-Host "SUCCESS: All infrastructure host ports are free!" -ForegroundColor Green
    Write-Host "You can safely start the containers with:" -ForegroundColor White
    Write-Host "  docker compose -f docker/docker-compose.yml --env-file docker/.env up -d" -ForegroundColor Green
    exit 0
}
