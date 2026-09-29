# Dependency Injection Agent Prompts

These prompts are intentionally separate. Agents must inspect the current implementation and tests first, preserve Native AOT/source-generated activation, avoid reflection-based activation, and run the relevant TUnit tests. Do not edit unrelated worktree changes.

## 1. Synchronous Disposal

Implement `IDisposable` on `ServiceProvider` and `OwnedServiceScope` without changing existing `IAsyncDisposable` semantics. Define first-mode-wins behavior for concurrent sync/async disposal. Sync disposal must wait for admitted work, dispose children and owned resources in reverse order, call `IDisposable` for dual-interface objects, report async-only resources clearly, aggregate failures, and remain idempotent. Add lifecycle, ordering, failure, and concurrency tests. Do not use sync-over-async disposal for user resources.

## 2. Non-Generic Scope APIs

Add `CreateScope(Type, params ServiceScopeInput[])` to `ServiceProvider` and `OwnedServiceScope`; make generic overloads delegate to it. Preserve parent validation, repeated-ancestor rejection, input validation, shutdown races, and AOT compatibility. Add tests for custom scopes, invalid types, parent relationships, inputs, and equivalence with generic APIs. Do not add `CreateScopeAsync`; creation has no asynchronous work.

## 3. Multiple Registrations and `IEnumerable<T>`

Add explicit append/multiple-registration APIs while preserving existing last-registration-wins APIs. Implement ordered `IEnumerable<T>` resolution, including empty collections, lifetime validation, scoped caching where appropriate, disposal ownership, rollback, concurrent activation, generated constructor dependencies, and deterministic ordering. Define whether direct `T` resolution remains replacement-based. Add container and source-generator tests and document keyed-entry behavior separately.

## 4. Open Generic Registrations

Design open generic support without reflection activation, `Activator.CreateInstance`, expression compilation, or weakened Native AOT guarantees. Prefer generated closed registrations or an explicit finite closure contract. If unrestricted runtime closure cannot satisfy AOT and lifetime validation, do not implement it: add clear diagnostics/documentation and tests proving unsupported registrations fail. Cover constraints, precedence, cache isolation, lifetimes, cycles, disposal, and trimming.

## 5. Keyed/Named Services

Add keyed registration and resolution with composite identity `(service type, key type, key value)`, preserving all unkeyed behavior. Define null/equality semantics, replacement rules, lifetime/cache isolation, diagnostics, generated constant-key dependencies, factory access, and `GetKeyedService`. Add tests for keys, scopes, concurrency, failures, disposal, cycles, and interaction with collections. Do not infer keys from `IServiceProvider.GetService(Type)`.

## 6. Explicit Decorators

Implement registration-time typed decorators, not runtime proxies/interceptors. Preserve the decorated service lifetime, activation graph, cycle checks, rollback, ownership, and reverse disposal. The decorator must receive the inner instance without recursively resolving the public service key. Initially reject or scope out open generic, keyed, instance-owned, and asynchronous decorator cases unless explicitly designed. Add ordering, lifetime, failure, disposal, concurrency, and generator tests.

## 7. Diagnostics and Resolution Tracing

Add disabled-by-default structured diagnostics for resolution paths, cache hit/wait/construct events, scopes, lifetimes, activation source, failures, cleanup, timing, and optional allocation measurement. Preserve existing exception messages and inner exceptions. Use immutable event snapshots and a low-overhead opt-in sink/configuration. Never add hot-path allocations when disabled. Add tests for event correctness, failure paths, scope ownership, disabled behavior, and AOT compatibility.

## 8. Lazy Scope Allocation

Make `_children`, scoped cache, and owned-resource storage lazy where safe; keep input validation/tracking correct. Synchronize initialization under the existing provider gate and preserve concurrent first resolution. Add empty-scope and non-disposable-scope fast paths without changing cleanup ordering, task identity, active-operation waiting, or ownership release. Add allocation-focused benchmarks and lifecycle/concurrency tests.

## 9. Lifecycle and Concurrency Tests

Add focused TUnit tests for empty scopes, active resolution during shutdown, child creation races, sibling independence, concurrent disposal identity, async-only and dual-interface disposal, rollback, cleanup failures, scope inputs, and repeated operations. Use deterministic task barriers, no sleeps, awaited assertions, and default parallel execution. Do not rely on brittle allocation byte counts for correctness.

## 10. Scope Allocation and Disposal Optimization

Benchmark and optimize empty scope creation, empty create/dispose, resolve/create/dispose, scoped cache creation, and disposable-resource cleanup separately. Remove unnecessary task, collection, and state-machine allocations while preserving all disposal and concurrency invariants. Validate with Release benchmarks on repeated launches and the full test suite; report before/after allocations and timings.

## 11. Collection Resolution Gate

Before implementing collection resolution, identify a real engine consumer requiring multiple implementations. If none exists, document the deliberate deferral and add no speculative API. If a consumer exists, implement only the required ordered collection semantics, registration model, lifetimes, disposal, generated activation, and tests; preserve ordinary replacement registration behavior.

## 12. Compatibility API Gate

Before adding Microsoft DI compatibility APIs, identify an external consumer requiring them. If none exists, do not add `IServiceScope`, `IServiceScopeFactory`, `CreateAsyncScope`, or aliases solely for familiarity. If a consumer exists, add the smallest adapter surface, document semantic differences, and test disposal, scope ownership, async behavior, and unsupported features.

## Orchestration Rules

Run independent research/design agents in parallel first. Then implement only non-overlapping tracks in parallel: diagnostics, scope performance/tests, and one feature API at a time. Collection, keyed, decorator, and open-generic work share registration/resolution internals and must be serialized or isolated in worktrees. Reconcile public API and generated-code changes before running the full suite.
