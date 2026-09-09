# Concurrency, State, and Virtual Actor Invariants

> An in-depth analysis of Centra's concurrency controls: turn-based virtual actor mailboxes, optimistic state commits (ETags), and distributed lease mutual exclusion.

---

## 🎭 1. Virtual Actor Turn-Based Concurrency

In a distributed multi-node system, stateful entity coordination typically suffers from multi-threaded race conditions, requiring complex lock nesting and deadlocks. Centra solves this via the **Virtual Actor Turn-Based Model**.

```
                           Concurrent Incoming Requests
                       [Turn 1]     [Turn 2]     [Turn 3]
                          │            │            │
                          ▼            ▼            ▼
                   ┌──────────────────────────────────────┐
                   │             ActorMailbox             │
                   │      (Channel-Based FIFO Queue)      │
                   └──────────────────┬───────────────────┘
                                      │ Sequential Turn Dispatch
                                      ▼
                   ┌──────────────────────────────────────┐
                   │            Actor Instance            │
                   │    (Strictly Single-Threaded Turn)   │
                   │                                      │
                   │  1. Invoke domain method             │
                   │  2. Mutate ActorStateManager cache   │
                   │  3. Auto-commit dirty keys via ETag  │
                   └──────────────────┬───────────────────┘
                                      │ ETag CAS Save
                                      ▼
                   ┌──────────────────────────────────────┐
                   │          IStateStore Driver          │
                   └──────────────────────────────────────┘
```

### Invariant: Single-Threaded Turn Execution
- Every active actor possesses its own dedicated `ActorMailbox` (backed by a bounded/unbounded async queue).
- Turns are dequeued and processed strictly one-at-a-time in FIFO order.
- A single actor instance **never** processes two turns concurrently.
- Developers write pure domain methods without `lock`, `SemaphoreSlim`, or monitor synchronization.

---

## 💾 2. Optimistic Concurrency & State Dirty Tracking

Rather than issuing immediate physical writes on every variable change, actor state operations are staged:

### `IActorStateManager` Lifecycle
1. **Read-Through Cache**: When `GetStateAsync<T>(key)` is called, the value is fetched from underlying `IStateStore` and cached in memory alongside its version `ETag`.
2. **Dirty Tracking**: Calling `SetStateAsync<T>(key, value)` marks the entry as `Dirty` without blocking on disk or network I/O. Calling `ClearStateAsync(key)` marks the entry as `Deleted`.
3. **Turn Completion Atomic Commit**: When the turn method returns, the mailbox invokes `ActorStateManager.SaveStateAsync()`:
   - All entries marked `Dirty` or `Deleted` are validated and saved.
   - Saves execute using **Optimistic Concurrency (ETags)**. If another replica modified the state concurrently, a Compare-And-Swap (CAS) conflict occurs and triggers conflict resolution.

---

## 🌐 3. Partition Placement: Consistent Hash Ring

To distribute virtual actors across physical cluster nodes without a centralized master node bottleneck, Centra employs a **Consistent Hash Ring** (`ConsistentHashRing`):

```
                       [Node-A (vnode 0..99)]
                             /        \
                            /          \
                           /            \
        [Node-C (vnode 0..99)]        [Node-B (vnode 0..99)]
```

- Each physical replica registers with the ring and creates **100 virtual nodes (vnodes)** uniformly distributed using FNV-1a / SHA hashing.
- Given an `ActorIdentity` (e.g. `AccountActor:acc-9872`), its hash is mapped clockwise to the nearest vnode.
- If the resolved node is the local instance, the turn enters the local `ActorMailbox`.
- If the resolved node is remote, the invocation is transparently forwarded via HTTP RPC to `/centra/actors/{type}/{id}/method/{methodName}` on that node.
- When nodes join or leave the cluster, only a fraction of actors rehash (`1 / N`), minimizing state churn.

---

## 🔒 4. Distributed Mutual Exclusion & Lease Renewal

Outside of actors, services often need cluster-wide mutual exclusion (e.g. distributed cron ticks, leader election):

```
Caller                   IDistributedLockProvider                 Storage (Redis/PG/SQL)
  │                                  │                                      │
  │── AcquireLockAsync(key, 30s) ───>│                                      │
  │                                  │── TryAcquireLease(key, token, 30s) ─>│
  │                                  │<── OK (Lease Granted) ───────────────│
  │                                  │                                      │
  │                                  │── [Start Background Heartbeat] ─────>│ (renews every 10s)
  │<── IDistributedLock (Success) ───│                                      │
  │                                  │                                      │
  │   ... Critical Section ...       │                                      │
  │                                  │                                      │
  │── DisposeAsync() ────────────────>│                                      │
  │                                  │── ReleaseLease(key, token) ─────────>│ (atomic delete/release)
```

### Invariants:
1. **Fencing Token / Owner ID**: Every lock acquisition generates a cryptographically unique `LockId` (`Guid.NewGuid():N`). Only the instance holding that token can renew or release the lock.
2. **Background Heartbeat Renewal**: If a lock is held longer than the lease duration, an internal timer automatically renews the lease at `period / 3` intervals until disposed.
3. **Crash Safety**: If a node crashes while holding a lock, the heartbeat ceases and the storage provider automatically expires the lease when the TTL lapses, preventing permanent deadlocks.
