#!/usr/bin/env bash
# =============================================================================
# ShopSphere Pre-Flight Infrastructure Port Checker (Bash)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="${SCRIPT_DIR}/.env"

# Associative array of default ports
declare -A PORT_DEFAULTS=(
  ["SQLSERVER_PORT"]="11433"
  ["POSTGRES_PORT"]="15432"
  ["MONGO_PORT"]="27018"
  ["REDIS_PORT"]="16379"
  ["RABBITMQ_PORT"]="15672"
  ["RABBITMQ_MGMT_PORT"]="25672"
  ["SEQ_PORT"]="15341"
  ["SEQ_INGESTION_PORT"]="15342"
  ["JAEGER_PORT"]="16686"
  ["JAEGER_OTLP_PORT"]="4317"
  ["JAEGER_OTLP_HTTP_PORT"]="4318"
)

declare -A SERVICE_NAMES=(
  ["SQLSERVER_PORT"]="Microsoft SQL Server 2022"
  ["POSTGRES_PORT"]="PostgreSQL 16"
  ["MONGO_PORT"]="MongoDB 7.0"
  ["REDIS_PORT"]="Redis 7.2"
  ["RABBITMQ_PORT"]="RabbitMQ AMQP"
  ["RABBITMQ_MGMT_PORT"]="RabbitMQ Management UI"
  ["SEQ_PORT"]="Datalust Seq UI"
  ["SEQ_INGESTION_PORT"]="Datalust Seq Ingestion"
  ["JAEGER_PORT"]="Jaeger UI"
  ["JAEGER_OTLP_PORT"]="Jaeger OTLP gRPC"
  ["JAEGER_OTLP_HTTP_PORT"]="Jaeger OTLP HTTP"
)

# Load .env if present
if [ -f "$ENV_FILE" ]; then
  # Read variables from .env
  while IFS='=' read -r key val || [ -n "$key" ]; do
    key=$(echo "$key" | tr -d '[:space:]')
    val=$(echo "$val" | tr -d '[:space:]')
    if [[ ! "$key" =~ ^# ]] && [ -n "$key" ]; then
      export "$key=$val"
    fi
  done < "$ENV_FILE"
fi

check_port() {
  local port=$1
  if command -v nc >/dev/null 2>&1; then
    nc -z 127.0.0.1 "$port" >/dev/null 2>&1
    return $?
  elif command -v lsof >/dev/null 2>&1; then
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
    return $?
  elif command -v ss >/dev/null 2>&1; then
    ss -tlpn | grep -q ":$port "
    return $?
  else
    # Fallback to /dev/tcp
    (echo > /dev/tcp/127.0.0.1/"$port") >/dev/null 2>&1
    return $?
  fi
}

find_alt_port() {
  local start=$1
  local alt=$((start + 1))
  while [ "$alt" -lt 65535 ]; do
    if ! check_port "$alt"; then
      echo "$alt"
      return
    fi
    alt=$((alt + 1))
  done
  echo $((start + 1000))
}

echo "================================================================="
echo "  ShopSphere Pre-Flight Infrastructure Port Checker"
echo "================================================================="

has_conflicts=0

for var in "SQLSERVER_PORT" "POSTGRES_PORT" "MONGO_PORT" "REDIS_PORT" "RABBITMQ_PORT" "RABBITMQ_MGMT_PORT" "SEQ_PORT" "SEQ_INGESTION_PORT" "JAEGER_PORT" "JAEGER_OTLP_PORT" "JAEGER_OTLP_HTTP_PORT"; do
  val="${!var:-${PORT_DEFAULTS[$var]}}"
  service="${SERVICE_NAMES[$var]}"

  if check_port "$val"; then
    has_conflicts=1
    alt=$(find_alt_port "$val")
    echo " [CONFLICT] $service"
    echo "            Variable: $var"
    echo "            Port:     $val is ALREADY IN USE by another process."
    echo "            Action:   Update $var=$alt in docker/.env"
    echo ""
  else
    printf " [OK]       %-28s Port: %s is available\n" "$service" "$val"
  fi
done

echo "================================================================="

if [ "$has_conflicts" -eq 1 ]; then
  echo "WARNING: One or more ports are already bound!"
  echo "Please adjust conflicting port values in docker/.env before starting the containers."
  exit 1
else
  echo "SUCCESS: All infrastructure host ports are free!"
  echo "You can safely start the containers with:"
  echo "  docker compose -f docker/docker-compose.yml --env-file docker/.env up -d"
  exit 0
fi
