#!/usr/bin/env bash
# Seeds a running instance of the API with a handful of nodes and tenants so you
# can immediately explore /api/nodes, /api/tenants, /api/health, and /api/metrics.
#
# Usage: ./scripts/seed-demo-data.sh [base_url]
# Default base_url: http://localhost:5080

set -euo pipefail

BASE_URL="${1:-http://localhost:5080}"

echo "Registering nodes against ${BASE_URL} ..."

curl -s -X POST "${BASE_URL}/api/nodes" \
  -H "Content-Type: application/json" \
  -d '{"region": "eastus", "totalCapacityUnits": 100}' | python3 -m json.tool

curl -s -X POST "${BASE_URL}/api/nodes" \
  -H "Content-Type: application/json" \
  -d '{"region": "eastus", "totalCapacityUnits": 200}' | python3 -m json.tool

curl -s -X POST "${BASE_URL}/api/nodes" \
  -H "Content-Type: application/json" \
  -d '{"region": "westus", "totalCapacityUnits": 150}' | python3 -m json.tool

echo "Allocating tenants ..."

curl -s -X POST "${BASE_URL}/api/tenants/allocate" \
  -H "Content-Type: application/json" \
  -d '{"tenantName": "contoso-orders-db", "requiredCapacityUnits": 40, "preferredRegion": "eastus", "priority": "Standard"}' | python3 -m json.tool

curl -s -X POST "${BASE_URL}/api/tenants/allocate" \
  -H "Content-Type: application/json" \
  -d '{"tenantName": "fabrikam-payments-db", "requiredCapacityUnits": 60, "preferredRegion": "eastus", "priority": "BusinessCritical"}' | python3 -m json.tool

echo "Current fleet state:"
curl -s "${BASE_URL}/api/nodes" | python3 -m json.tool

echo "SLO report:"
curl -s "${BASE_URL}/api/health" | python3 -m json.tool
