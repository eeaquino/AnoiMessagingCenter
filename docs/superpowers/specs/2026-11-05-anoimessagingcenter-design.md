# AnoiMessagingCenter — Design Spec

- **Date:** 2026-11-05
- **Status:** Approved design; implementation not started
- **Package:** `AnoiMessagingCenter`
- **Framework:** `net10.0`

## 1. Summary

`AnoiMessagingCenter` is a standalone, dependency-free NuGet package that reproduces the existing
`IMessagingCenter` / `MessagingCenter` API with the same public surface, while removing reflection and
per-send allocations from the hot path and releasing subscriptions once their subscriber is collected.

The source for this work is the `APIServiceShared.Shared.Helpers` implementation supplied by the owner.
The public API is preserved; only the namespace changes (to `AnoiMessagingCenter`), plus one additive
event for error reporting. Existing call sites need only a new `using` and a package/project reference.

## 2. Goals

1. Keep every existing public member source-compatible (names, signatures, generic constraints,
   default arguments, return types, validation, and synchronous delivery).
2. Make `Send` allocation-free in steady state: no key tuple, no list copy, no `object[]`, no reflection.
3. Release subscription state when the subscriber becomes unreachable; never retain dead subscribers.
4. Ship as a well-formed NuGet package: README, XML docs, symbols, deterministic build, AOT/trim
   compatibility analyzers, tests, and benchmarks.
5. Prove the improvement with a BenchmarkDotNet comparison against the original implementation.

## 3. Non-goals

- No new messaging patterns (no request/response, no channels, no DI integration).
- No multi-targeting; `net10.0` consumers on later runtimes use the same asset.
- No public logger abstraction or `Microsoft.Extensions.Logging` dependency.
- No change to synchronous `Send` delivery semantics.

## 4. Public API contract

Namespace: `AnoiMessagingCenter`.

```csharp
public interface IMessagingCenter
{
    void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class;
    void Send<TSender>(TSender sender, string message) where TSender : class;
    void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class;
    void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class;
    void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> callback, TSender? source = null)
        where TSender : class;
    void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> callback, TSender? source = null)
        where TSender : class;
    void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class;
    void Unsubscribe<TSender>(object subscriber, string message) where TSender : class;
}

public class MessagingCenter : IMessagingCenter
{
    public static IMessagingCenter Instance { get; }
    public static event Action<Exception, string>? CallbackFailed;

    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class;
    public static void Send<TSender>(TSender sender, string message) where TSender : class;
    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class;
    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class;
    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> callback, TSender? source = null)
        where TSender : class;
    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> callback, TSender? source = null)
        where TSender : class;
    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class;
    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class;

    internal static void ClearSubscribers();
}
```

### 4.1 Preserved behavior

- The class is not sealed and keeps the implicit public parameterless constructor; a manually created
  instance has its own subscription table, exactly as today.
- `Instance` returns the lazily created singleton. Static methods operate on that singleton.
- `Send` validates `sender` then `message`; `Subscribe` validates `subscriber`, `callback`, then
  `message`; `Unsubscribe` validates `subscriber` then `message`. Exceptions are
  `ArgumentNullException` with the same parameter names as today.
- `source == null` matches all senders; otherwise a subscription only runs when
  `sender.Equals(source)`. Equality is virtual (`Equals`), not reference-only.
- Duplicate subscriptions are allowed and each runs; unsubscribing removes every entry that belongs to
  the exact subscriber instance for that message/type key.
- The key is `(message, typeof(TSender), typeof(TArgs))`; the single-parameter overloads use a null
  `TArgs` slot, so one- and two-parameter subscriptions never collide.
- Delivery is synchronous on the sending thread. `SubscribeAsync` callbacks are started on the sending
  thread and are not awaited by `Send` (fire-and-forget), matching today.

### 4.2 Additive member

`CallbackFailed` is new: it fires with `(Exception exception, string message)` when a synchronous or
asynchronous subscriber callback throws. The handler is copied once per failure (a null handler is a
no-op), it runs on the thread that faulted (the sender's thread for sync callbacks, the captured
context for async callbacks), and exceptions thrown by the handler are swallowed. The event is
additive and cannot break existing consumers. This replaces the direct `Serilog.Log.Error` calls.

### 4.3 Deliberate fixes (no signature changes)

- **Static callbacks work.** The original stores `callback.Target`; for a static method that is `null`,
  so the subscription silently never fired. The new design stores the delegate itself and invokes it.
- **Dead subscriptions are released.** The original keeps a subscription forever when a subscriber
  becomes unreachable; the new design ties subscription lifetime to the subscriber (section 5.4) and
  prunes dead entries during `Send`/`Unsubscribe`.
- **Serilog dependency removed.** No third-party runtime dependency.

## 5. Internal design

### 5.1 Message key

```csharp
internal readonly struct MessageKey : IEquatable<MessageKey>
{
    internal readonly string Message;
    internal readonly Type SenderType;
    internal readonly Type? ArgType;
    private readonly int _hash;
}
```

- The hash is computed once in the constructor with `HashCode.Combine`.
- Equality: ordinal string equality, reference equality for the two `Type` values (identical to
  `Tuple<string, Type, Type>` semantics).
- No heap allocation when used as a dictionary key.

### 5.2 Storage

```csharp
private readonly Lock _sync = new();
private readonly Dictionary<MessageKey, WeakReference<Subscription>[]> _subscriptions = new();
private readonly ConditionalWeakTable<object, List<Subscription>> _anchors = new();
```

- Arrays are copy-on-write. Subscribe/unsubscribe replace the array; send reads the current reference.
- `_subscriptions` is only touched under `_sync`; callbacks never run while the lock is held.
- `WeakReference<Subscription>` keeps the table from pinning subscriptions directly.

### 5.3 Subscription hierarchy

```csharp
internal abstract class Subscription
{
    private readonly WeakReference<object> _subscriber;
    private int _removed;

    internal string Message { get; }
    internal object? Source { get; }
    internal bool IsRemoved => Volatile.Read(ref _removed) != 0;
    internal bool IsSubscriberAlive => _subscriber.TryGetTarget(out _);
    internal void MarkRemoved() => Volatile.Write(ref _removed, 1);

    internal bool CanInvoke(object sender)
    {
        if (IsRemoved) return false;
        if (!_subscriber.TryGetTarget(out _)) return false;
        if (Source is not null && !sender.Equals(Source)) return false;
        return true;
    }
}

internal interface ISubscription<in TSender>
{
    void Invoke(TSender sender);
}

internal interface ISubscription<in TSender, in TArgs>
{
    void Invoke(TSender sender, TArgs args);
}

internal sealed class SyncSubscription<TSender> : Subscription, ISubscription<TSender>;
internal sealed class SyncSubscription<TSender, TArgs> : Subscription, ISubscription<TSender, TArgs>;
internal sealed class AsyncSubscription<TSender> : Subscription, ISubscription<TSender>;
internal sealed class AsyncSubscription<TSender, TArgs> : Subscription, ISubscription<TSender, TArgs>;
```

- Each concrete class stores the typed `Action`/`Func` directly. Invocation is one interface call plus
  a delegate call; there is no `MethodInfo`, no `object[]`, and no expression compilation.
- `Invoke` wraps the callback in `try/catch` and calls `MessagingCenter.RaiseCallbackFailed` on failure.
  The catch lives in the generic class so the message is available without a per-send allocation.
- Async classes implement `Invoke` as `_ = InvokeAsync(...)`, where `InvokeAsync` awaits without
  `ConfigureAwait(false)` (preserving the original synchronization-context capture) and catches into
  the failure hook.

### 5.4 Lifetime anchoring

The center must not keep subscribers alive, but the subscription must stay alive while the subscriber
does. A strong `center → subscription → delegate → subscriber` path would pin the subscriber, so the
center only stores weak references.

`ConditionalWeakTable<object, List<Subscription>>` provides the missing strong path with ephemeron
semantics: while the subscriber key is reachable, the table keeps its subscription list alive; once the
subscriber is unreachable (even if the list references it through a delegate), the entry and list become
collectible together. This gives:

- subscriber collected → its subscriptions are collected (fixes the original leak);
- subscriber alive → its callbacks keep their targets reachable, as today;
- callback target owned by an object other than the subscriber stays strongly referenced by the stored
  delegate while the subscriber lives, as today.

On unsubscribe, the removed subscription is also detached from its subscriber's anchor list; empty
anchor lists are removed from the table.

### 5.5 Send

1. Validate `sender` and `message`.
2. Build `MessageKey` on the stack.
3. `lock (_sync)`: read the current `WeakReference<Subscription>[]`; return if absent. Release the lock.
4. Outside the lock, for each weak reference: `TryGetTarget`; skip if dead or removed; call
   `subscription.CanInvoke(sender)`; cast to `ISubscription<TSender[, TArgs]>` and invoke.
5. If any weak reference was dead, best-effort prune that key: re-enter the lock, verify the array is
   still the same reference, rebuild it without dead/removed entries, and remove the key if empty.

Steady-state `Send` performs one dictionary lookup, one lock/unlock, one array walk, and no heap
allocation (including struct `TArgs`, which stays typed through the interface). No callback runs under
the lock, so a callback may subscribe, unsubscribe, or send recursively.

### 5.6 Subscribe

1. Validate `subscriber`, `callback`/`asyncCallback`, `message`.
2. Create the typed subscription (weak subscriber reference, strong source, stored delegate).
3. Under `_sync`: append a weak reference to a copy-on-write array for the key and add the subscription
   to the subscriber's anchor list.

`source` is stored strongly, matching the original closure's behavior.

### 5.7 Unsubscribe

1. Validate `subscriber` then `message`.
2. Under `_sync`: build a new array excluding entries whose subscriber is the exact argument, plus
   dead/removed entries; mark removed entries first (volatile) so an in-flight `Send` skips them; detach
   removed entries from their anchor lists; remove the key when the array becomes empty.

### 5.8 Pruning and `ClearSubscribers`

- Pruning is lazy and best-effort; it never blocks or reorders delivery.
- `internal static ClearSubscribers()` clears the default instance's table and anchor table (preserved
  for tests and internal callers).

## 6. Performance and memory

### 6.1 Hot-path comparison

| Operation | Original | AnoiMessagingCenter |
|---|---|---|
| `Send`, no subscribers | `Tuple` key allocation + dictionary lookup | stack key + dictionary lookup |
| `Send`, N subscribers | key tuple + `List.ToList()` + `object[]` per callback + `MethodInfo.Invoke` | no heap allocation; N typed delegate calls |
| Struct `TArgs` on `Send` | boxed into `object[]` per subscriber | passed typed; no boxing |
| `Subscribe` | tuple key + tuple subscription + `WeakReference` + `MaybeWeakReference` + filter closure (+ async wrapper closure) | stack key + one subscription object + `WeakReference<object>` + anchor-list slot + array copy |
| `Unsubscribe` | key tuple + list removal | array rebuild |

### 6.2 Complexity

- `Send`: O(S) where S = subscriptions for the key; one lock acquisition to snapshot.
- `Subscribe`: O(S) array copy + O(A) anchor append (A = subscriber's subscriptions).
- `Unsubscribe`: O(S) array rebuild + O(A) anchor removal.
- Dead-subscription cleanup amortized into `Send`/`Unsubscribe`.

### 6.3 AOT and trimming

No reflection, no `MakeGenericType`, no runtime code generation. The library enables
`IsAotCompatible` and the trim analyzers in its project file.

## 7. Repository layout and packaging

```
AnoiMessagingCenter/
├─ AnoiMessagingCenter.sln
├─ Directory.Build.props
├─ .gitignore
├─ README.md
├─ LICENSE
├─ docs/superpowers/specs/2026-11-05-anoimessagingcenter-design.md
├─ src/AnoiMessagingCenter/
│  ├─ AnoiMessagingCenter.csproj
│  ├─ IMessagingCenter.cs
│  ├─ MessagingCenter.cs
│  └─ Internal/{MessageKey,Subscription}.cs
├─ tests/AnoiMessagingCenter.Tests/
│  ├─ AnoiMessagingCenter.Tests.csproj
│  ├─ MessagingCenterTests.cs
│  ├─ AsyncTests.cs
│  ├─ LifetimeTests.cs
│  ├─ ApiShapeTests.cs
│  └─ ConcurrencyTests.cs
└─ benchmarks/AnoiMessagingCenter.Benchmarks/
   ├─ AnoiMessagingCenter.Benchmarks.csproj
   ├─ BaselineMessagingCenter.cs
   └─ MessagingBenchmarks.cs
```

### 7.1 Package metadata

- `PackageId`: `AnoiMessagingCenter`
- `Version`: `1.0.0`
- `Authors`: `Anoi`
- `Description`: "Allocation-conscious, weak-reference messaging center for .NET 10+."
- `PackageTags`: `messaging;event-aggregator;weak-reference;dotnet`
- `PackageLicenseExpression`: `MIT` (default decision for this first release; change before publishing
  if a different license is required)
- Embedded `README.md`, XML documentation file, `.snupkg` symbols, deterministic build.
- `IsAotCompatible=true`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`,
  `TargetFramework=net10.0`.
- No runtime dependencies.

## 8. Testing strategy

### 8.1 Unit behavior

- Every overload of `Send`, `Subscribe`, `SubscribeAsync`, `Unsubscribe` (static and interface paths).
- No-subscriber send; sender/args delivery; single-parameter overload.
- `source` filtering: null source matches all; non-null source matches only the equal sender; virtual
  `Equals` is honored.
- Type-key isolation: different `TSender`, different `TArgs`, one- vs two-parameter.
- Duplicate subscriptions all invoke; unsubscribe removes only the matching subscriber's entries.
- `ClearSubscribers` clears delivery.
- Static-method callback is invoked (regression test for the original bug).
- Callback exceptions do not escape `Send`; `CallbackFailed` receives the exception and message; a
  throwing hook is swallowed.

### 8.2 Async

- `SubscribeAsync` delivers sender/args; completion observable through `TaskCompletionSource`.
- Async exceptions reach the hook and never crash the process.
- Continuation resumes on the captured `SynchronizationContext` when one is present (custom
  single-threaded context test).
- `Send` does not block on the returned task.

### 8.3 Lifetime

- A subscriber that is dropped and collected no longer receives messages, and its subscription does not
  keep it alive (weak-reference assertion after forced GC, in a non-inlined helper).
- The subscription itself is collectible after subscriber death (weak reference to a subscription or
  delivery count stops increasing).
- A callback target distinct from the subscriber stays reachable while the subscriber lives.

### 8.4 Concurrency

- Parallel `Send` plus `Subscribe`/`Unsubscribe` for a bounded iteration count: no exceptions, no
  corruption, and post-quiesce delivery matches the final subscription set.
- Sending from inside a callback (re-entrancy) works.

### 8.5 API shape

- Reflection test asserts every original public member exists with the exact name, arity, parameter
  types, generic constraints, and defaults, and that the additive `CallbackFailed` event exists.
- A compile-time compatibility file exercises each member using original call patterns.

### 8.6 Benchmarks

- `benchmarks/` contains the original implementation copied as `BaselineMessagingCenter` with Serilog
  replaced by a no-op (baseline only; never shipped).
- BenchmarkDotNet with `[MemoryDiagnoser]`: send with 0/1/10 subscribers, struct args, filtered source,
  subscribe+unsubscribe.
- Acceptance evidence: new `Send` reports 0 B allocated; report before/after numbers in README.

## 9. Migration notes (README)

1. Replace the shared-project reference with the `AnoiMessagingCenter` package.
2. Replace `using APIServiceShared.Shared.Helpers;` with `using AnoiMessagingCenter;`.
3. If Serilog logging of callback failures is desired, subscribe to `MessagingCenter.CallbackFailed`.
4. Remove any workaround for static-method subscriptions; they now fire.
5. No other source changes are required.

## 10. Acceptance criteria

1. `dotnet build -c Release` succeeds with no warnings from the library project.
2. `dotnet test -c Release` passes, including GC and concurrency tests.
3. `dotnet pack -c Release` produces `AnoiMessagingCenter.1.0.0.nupkg` and `.snupkg`, containing the
   README and XML docs.
4. API-shape test passes against the frozen original surface (plus `CallbackFailed`).
5. Benchmark evidence shows exactly 0 B allocated for every measured `Send` scenario (no subscribers,
   one subscriber, ten subscribers, filtered source, struct args), with lower elapsed time than the
   baseline; subscribe/unsubscribe may allocate.
6. README documents installation, quick start, migration, and measured results.

## 11. Risks and mitigations

| Risk | Mitigation |
|---|---|
| GC tests are timing-sensitive | Loop `GC.Collect`/`WaitForPendingFinalizers` with a bounded retry helper; non-inlined factory; no other strong references in the test frame |
| ConditionalWeakTable ephemeron behavior misunderstood | Dedicated lifetime tests: subscriber alive → delivery; subscriber collected → no delivery and subscription released |
| Async context capture differs from original | Test with a custom single-threaded `SynchronizationContext`; do not use `ConfigureAwait(false)` |
| Benchmark noise | Release build, BenchmarkDotNet short job, report relative numbers and allocations |
| `System.Threading.Lock` availability | Target `net10.0`; `Lock` exists in .NET 9+ |
| Callback re-entrancy under lock | Callbacks always run outside `_sync` |

## 12. Decisions

- Namespace/package: `AnoiMessagingCenter` (owner choice).
- Error reporting: no Serilog; additive `CallbackFailed` event (owner choice).
- Lifetime: preserve current semantics, plus release dead subscriptions (owner choice).
- Scope: library + tests + benchmarks + README (owner choice).
- Internal strategy: copy-on-write snapshots + typed invokers (owner choice).
- License: MIT default for 1.0.0; owner may change it before publishing.
