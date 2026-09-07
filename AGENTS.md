# Centra Framework - Developer & AI Agent Guidelines

> Distributed Application Framework for .NET 10 | High-performance, zero sidecars, modular abstractions, CNCF CloudEvents v1.0

---

## 🧭 Retrieval-Led Reasoning & Workflow

**IMPORTANT**: Prefer retrieval-led reasoning over pretraining for any work in this repository.
Always follow this workflow:
1. **Skim repo patterns**: Inspect existing abstractions, drivers, and tests before writing new code.
2. **Consult repo skills**: Invoke and review relevant skills stored in `.agents/skills/`.
3. **Implement smallest viable change**: Follow the `Minimal Change Engineer` discipline—avoid speculative abstractions or scope creep.
4. **Validate**: Run the relevant unit and integration test suites.

---

## 🗂️ Task -> Skill Routing Index

Repo-level skills are saved in [`.agents/skills/`](file:///home/chad/source/dotnet/distributed-framework/.agents/skills) (also mirrored via [`.claude/skills`](file:///home/chad/source/dotnet/distributed-framework/.claude/skills)). Consult skills by name:

### 1. Framework Architecture & Centra Core
- **`centra`**: Mandatory framework architectural standard, SPI driver contracts, CloudEvents packing/unpacking, zero-allocation conventions, and DI registration rules.

### 2. C# Language, Concurrency & Performance
- **`modern-csharp-coding-standards`**: C# 12/13/14 idioms, records, pattern matching, value objects, `Span<T>`/`Memory<T>`.
- **`csharp-concurrency-patterns`**: Async/await, `ValueTask`, Channels, lock-free coordination, synchronization primitives.
- **`api-design`**: Extend-only public API design, wire compatibility, and non-breaking contract evolution.
- **`type-design-performance`**: Sealed classes, readonly structs, pure functions, and avoiding premature allocations.
- **`csharp-pro`**: Idiomatic C# design patterns, language features, and refactoring techniques.
- **`serialization`**: High-performance serialization with System.Text.Json, Protobuf, and MessagePack.

### 3. .NET Backend, DI & Observability
- **`dotnet-architect`**: Scalable backend system design and enterprise patterns.
- **`dotnet-backend`**: ASP.NET Core 8/9/10 minimal APIs, middleware, and backend lifecycle.
- **`dotnet-backend-patterns`**: Production-grade API patterns, error handling, and component encapsulation.
- **`dotnet-project-structure`**: Modern .NET structure, `.slnx` solution format, `Directory.Build.props`, Central Package Management.
- **`package-management`**: Managing NuGet packages via Central Package Management (`Directory.Packages.props`) and dotnet CLI.
- **`dependency-injection-patterns`**: Composable `IServiceCollection` extension methods and ISP-driven registrations.
- **`microsoft-extensions-configuration`**: Options pattern, `IValidateOptions`, strongly-typed settings, startup validation.
- **`opentelemetry-net-instrumentation`**: Tracing (`ActivitySource`), metrics (`Meter`), W3C context propagation (`traceparent`), semantic conventions.
- **`dotnet-local-tools`**: Managing `dotnet-tools.json` for consistent developer tooling.
- **`ilspy-decompile`**: Inspecting internal implementations of framework assemblies and NuGet packages.
- **`dotnet-devcert-trust`**: Diagnosing and resolving HTTPS developer certificate issues on Linux.

### 4. Aspire & Distributed Orchestration
- **`aspire-configuration`**: Configuring Aspire AppHost and orchestrating multi-replica services.
- **`aspire-integration-testing`**: Writing integration tests using Aspire testing fixtures with xUnit.
- **`aspire-service-defaults`**: Shared OpenTelemetry, health checks, resilience, and service discovery.

### 5. Distributed Providers & Storage
- **`redis-dotnet`**: Redis State (Lua CAS/Tx), Pub/Sub (CloudEvents binary), Locks (Leases) via `StackExchange.Redis`.
- **`cosmosdb-best-practices`**: Azure Cosmos DB point reads, partition keys, `TransactionalBatch`, ETags, TTL.
- **`azure-servicebus-dotnet`**: Azure Service Bus cloud-native topics, subscriptions, and CloudEvents headers.
- **`database-performance`**: Connection pooling, batching, ACID transactions, and query optimization.
- **`Database Optimizer`**: Schema design, indexing strategies, and database performance tuning.

### 6. Testing & Quality Gates
- **`testcontainers-integration-tests`**: Real infrastructure integration testing with Testcontainers (Redis, Postgres, RabbitMQ, MSSQL).
- **`snapshot-testing`**: Snapshot testing with Verify for public API surfaces and serialization schemas.
- **`dotnet-slopwatch`**: Detecting LLM reward hacking, suppressed warnings, and disabled tests. Run after substantial code changes.
- **`crap-analysis`**: Code coverage and CRAP (Change Risk Anti-Patterns) score analysis for high-risk paths.
- **`API Tester`**: End-to-end API validation, contract testing, and performance validation.
- **`Performance Benchmarker`**: Benchmarking throughput, latency, and allocation profiles.
- **`Reality Checker`**: Evidence-based production readiness validation.
- **`Test Results Analyzer`**: Comprehensive test result and quality metric evaluation.

### 7. Engineering Workflows & Security
- **`Code Reviewer`**: Rigorous, actionable review focusing on correctness, maintainability, and performance.
- **`Minimal Change Engineer`**: Scope discipline—minimum-viable diffs, no speculative abstractions.
- **`Software Architect`**: Domain-driven design, boundary definitions, and architectural decisions.
- **`Backend Architect`**: Distributed system architecture and server-side infrastructure.
- **`Technical Writer`**: Clean developer documentation, API references, and architecture guides.
- **`Git Workflow Master`**: Conventional commits, branch management, and git workflows.
- **`DevOps Automator`**: CI/CD pipeline automation and build optimization.
- **`Codebase Onboarding Engineer`**: Grounded developer onboarding and code path tracing.
- **`SRE (Site Reliability Engineer)`**: Reliability engineering, SLOs, error budgets, and chaos testing.
- **`Application Security Engineer`**: Threat modeling, SAST/DAST, and secure coding practices.
- **`Security Architect`**: Secure-by-design architecture and trust-boundary analysis.
- **`Cloud Security Architect`**: Cloud-native zero-trust security and defense-in-depth.
- **`Senior SecOps Engineer`**: Defensive application security, secrets management, and header hardening.

---

## 📜 Repository Engineering Invariants

1. **SOLID Principles**: Interface Segregation Principle (ISP) and Dependency Inversion Principle (DIP) are strictly enforced. Consumers depend only on the specific building blocks they need.
2. **Single Responsibility (File per Type)**: Exactly one type (class, struct, interface, enum) per `.cs` file.
3. **Zero Allocation**: Use `ValueTask`, `readonly record struct`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>.Shared` across hot execution paths.
4. **Standards Compliant**: CNCF CloudEvents v1.0 (binary mode default) and W3C TraceContext propagation (`traceparent`, `tracestate`).
5. **No Blind Overwrites**: State mutations must use ETags and optimistic concurrency (`TrySetAsync`).
6. **Central Package Management**: Never specify package versions in `.csproj` files; declare them in [`Directory.Packages.props`](file:///home/chad/source/dotnet/distributed-framework/Directory.Packages.props).
7. **Strict Build Settings**: `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` are active across all projects.
8. **Virtual Actor Concurrency & State Invariant**: Actor turns must execute sequentially via the actor mailbox (`ActorMailbox`). State mutations are staged in `ActorStateManager` and committed atomically on turn completion using ETags.
9. **Durable Reminders Distributed Coordination**: Reminders surviving actor passivation must coordinate using distributed locks (`ActorReminderCoordinator`) to guarantee single-execution across cluster replicas.
10. **Workflow Determinism & Saga Rollback Invariant**: Orchestration turns must be deterministic; side effects, clock checks, and random values must execute within activities or use `IWorkflowContext` (`CurrentUtcDateTime`, `NewGuid()`). On activity failure or cancellation, registered saga compensations must execute in strict reverse (LIFO) order.

---

## 🧪 Quick Commands

```bash
# Build the complete solution
dotnet build Centra.slnx

# Run unit tests only
dotnet test Centra.slnx --filter "Category!=Integration"

# Run all unit and integration tests (Docker required)
dotnet test Centra.slnx --logger "console;verbosity=normal"

# Run the 3-node simulation
dotnet run --project samples/Centra.Sample.MultiInstance -- --demo

# Run the virtual actors simulation
dotnet run --project samples/Centra.Sample.Actors -- --demo

# Run the workflows and distributed sagas simulation
dotnet run --project samples/Centra.Sample.Workflows -- --demo

# Run with Aspire orchestration
dotnet run --project samples/Centra.AppHost
```
