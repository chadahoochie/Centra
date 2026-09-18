#!/usr/bin/env bash
set -e

NODE1="http://localhost:8081"
NODE2="http://localhost:8082"
NODE3="http://localhost:8083"
SIGNOZ="http://localhost:3301"

echo "================================================================="
echo "  Centra Distributed Application Framework - SigNoz Simulation   "
echo "================================================================="
echo "Verifying cluster connectivity..."

for port in 8081 8082 8083; do
  if ! curl -s -f "http://localhost:$port/" > /dev/null; then
    echo "ERROR: node on port $port is not responding. Ensure 'docker compose -f samples/SigNozStack/docker-compose.yml up' is running."
    exit 1
  fi
done

echo "Cluster nodes healthy on ports 8081, 8082, 8083."
echo ""

echo "[1/9] Testing Service Invocation (Round-robin RPC via Control Plane topology)..."
for i in 1 2 3; do
  curl -s "$NODE1/peers/info" | grep -o '"instanceId":"[^"]*"' || true
done
echo ""

echo "[2/9] Testing Virtual Actors (Consistent hash ring sharding & proxying)..."
for i in $(seq 1 10); do
  curl -s -X POST "$NODE1/actors/actor-$i/increment" | grep -o '"handledByInstanceId":"[^"]*"' || true
done
curl -s "$NODE2/actors/actor-1" | grep -o '"value":[0-9]*' || true
curl -s "$NODE3/actors/actor-2" | grep -o '"value":[0-9]*' || true
echo ""

echo "[3/9] Testing Workflows & Distributed Sagas (Success & Compensation)..."
# Success saga
SAGA_RESP=$(curl -s -X POST "$NODE1/centra/workflows/OrderProcessingWorkflow/start" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-demo-ok","customerId":"cust-1","productId":"sku-100","quantity":2,"totalAmount":250.00}')
echo "Success saga started: $SAGA_RESP"

# Failing saga (exceeds $1000 threshold, triggers ProcessPayment failure and ReleaseInventory compensation)
FAIL_RESP=$(curl -s -X POST "$NODE2/centra/workflows/OrderProcessingWorkflow/start" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ord-demo-fail","customerId":"cust-2","productId":"sku-200","quantity":5,"totalAmount":1500.00}')
echo "Compensation saga started: $FAIL_RESP"
echo ""

echo "[4/9] Testing CNCF CloudEvents Pub/Sub (RabbitMQ topics & subscribers)..."
curl -s -X POST "$NODE1/tasks/dispatch" \
  -H "Content-Type: application/json" \
  -d '{"taskType":"sigNozDemo","payload":"Telemetry showcase payload"}' | grep -o '"taskId":"[^"]*"' || true
sleep 1
curl -s "$NODE2/tasks" | grep -o '"taskId":"[^"]*"' | head -n 3 || true
echo ""

echo "[5/9] Testing Redis State Store (ETag CAS optimistic concurrency retry)..."
curl -s -X POST "$NODE1/state/counter/increment" \
  -H "Content-Type: application/json" \
  -d '{"incrementBy":1,"simulateConflict":false}' | grep -o '"retryAttempts":[0-9]*' || true
curl -s -X POST "$NODE3/state/counter/increment" \
  -H "Content-Type: application/json" \
  -d '{"incrementBy":5,"simulateConflict":true}' | grep -o '"retryAttempts":[0-9]*' || true
echo ""

echo "[6/9] Testing Distributed Mutual Exclusion Locks (Leader election)..."
curl -s -X POST "$NODE1/leader/acquire?leaseSeconds=5" | grep -o '"isLeader":[^,]*' || true
# Node 2 attempts lock while held (should report 409 Conflict / not leader)
curl -s -X POST "$NODE2/leader/acquire" | grep -o '"isLeader":[^,]*' || true
curl -s -X POST "$NODE1/leader/release" | grep -o '"released":[^}]*' || true
curl -s -X POST "$NODE2/leader/acquire?leaseSeconds=5" | grep -o '"isLeader":[^,]*' || true
curl -s -X POST "$NODE2/leader/release" | grep -o '"released":[^}]*' || true
echo ""

echo "[7/9] Testing Distributed Single-Execution Cron Bindings..."
curl -s "$NODE1/cron/last-run" | grep -o '"handledByInstanceId":"[^"]*"' || true
curl -s "$NODE2/cron/last-run" | grep -o '"handledByInstanceId":"[^"]*"' || true
echo ""

echo "[8/9] Testing Relational Persistence (PostgreSQL State & Locks)..."
curl -s -X POST "$NODE1/db/postgres/order-signoz-1" \
  -H "Content-Type: application/json" \
  -d '{"value":"{\"status\":\"PendingPG\",\"amount\":450}"}' | grep -o '"database":"[^"]*"' || true
curl -s "$NODE2/db/postgres/order-signoz-1" | grep -o '"value":"[^"]*"' || true

curl -s -X POST "$NODE2/db/postgres/order-signoz-1" \
  -H "Content-Type: application/json" \
  -d '{"value":"{\"status\":\"CompletedPG\",\"amount\":450}"}' | grep -o '"database":"[^"]*"' || true
curl -s "$NODE3/db/postgres/order-signoz-1" | grep -o '"eTag":"[^"]*"' || true
echo ""

echo "[9/9] Testing Polly v8 Resilience Pipeline (Retries, Timeout, Telemetry)..."
curl -s -X POST "$NODE1/resilience/simulate?induceFailure=true" | grep -o '"attempts":[0-9]*' || true
curl -s -X POST "$NODE2/resilience/simulate?induceFailure=false" | grep -o '"attempts":[0-9]*' || true
echo ""

echo "================================================================="
echo "  Simulation Completed Successfully!                             "
echo "================================================================="
echo "Explore live telemetry in SigNoz:"
echo "  - Dashboard: $SIGNOZ/dashboard"
echo "  - Traces:    $SIGNOZ/traces?service=dockerstack-node"
echo "  - Services:  $SIGNOZ/services"
echo "================================================================="
