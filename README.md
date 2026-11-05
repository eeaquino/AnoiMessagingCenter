# AnoiMessagingCenter

Allocation-conscious, weak-reference messaging center for .NET 10+. It is a drop-in replacement for the
original `IMessagingCenter` / `MessagingCenter` implementation: same members, same synchronous delivery,
no reflection on the send path, and subscriptions are released when their subscriber is collected.

- `Send` allocates **0 bytes** in steady state, including with struct payloads.
- Subscription lifetime is tied to the subscriber with an ephemeron (`ConditionalWeakTable`); dead
  subscriptions are pruned automatically.
- No runtime dependencies, no reflection, AOT/trim compatible.

## Install

```bash
dotnet add package AnoiMessagingCenter
```

## Quick start

```csharp
using AnoiMessagingCenter;

MessagingCenter.Subscribe<MainPage, string>(this, "status", (page, text) =>
{
    page.StatusLabel.Text = text;
});

MessagingCenter.Send(this, "status", "Ready");
```

Unsubscribe when a subscriber's lifetime ends:

```csharp
MessagingCenter.Unsubscribe<MainPage, string>(this, "status");
```

Subscriptions also stop being delivered when the subscriber is garbage collected, so an explicit
`Unsubscribe` is only needed to stop delivery while the subscriber is still alive.

Async callbacks are fire-and-forget, exactly like the original API:

```csharp
MessagingCenter.SubscribeAsync<MainPage, string>(this, "load", async (page, id) =>
{
    var data = await page.LoadAsync(id);
    page.Apply(data);
});
```

`Send` does not wait for the returned task. The callback starts on the sending thread and resumes on
the captured synchronization context.

## Error handling

Callback exceptions never escape `Send`. To observe them, subscribe to the additive hook:

```csharp
MessagingCenter.CallbackFailed += (exception, message) =>
    Serilog.Log.Error(exception, "Messaging callback failed for {Message}", message);
```

The handler receives the exception and the message key. Exceptions thrown by the handler are ignored.

## API

`IMessagingCenter` and `MessagingCenter` keep the original surface:

- `Send<TSender, TArgs>(sender, message, args)` and `Send<TSender>(sender, message)`
- `Subscribe<TSender, TArgs>(subscriber, message, callback, source = null)` and `Subscribe<TSender>`
- `SubscribeAsync<TSender, TArgs>` and `SubscribeAsync<TSender>` (fire-and-forget)
- `Unsubscribe<TSender, TArgs>` and `Unsubscribe<TSender>`
- `MessagingCenter.Instance` and the static methods of the same names
- `MessagingCenter.CallbackFailed` (new, additive)

Messages are keyed by `(message, typeof(TSender), typeof(TArgs))`. A non-null `source` limits delivery
to senders equal to that source (`sender.Equals(source)`). Duplicate subscriptions are allowed and all
run. Delivery is synchronous on the calling thread.

## What changed from the original

- Namespace is `AnoiMessagingCenter`; replace `using APIServiceShared.Shared.Helpers;`.
- Serilog is no longer a dependency; use `CallbackFailed` instead.
- Static-method subscriptions now fire (the original silently dropped them).
- Dead subscriptions are released instead of retained forever.
- `Send` allocates 0 bytes in steady state; reflection is gone.

No other source changes are required.

## Performance

Measured with BenchmarkDotNet on .NET 10 (Release); full run in
[`benchmarks/results.md`](benchmarks/results.md).

| Scenario (10,000 sends) | Original | AnoiMessagingCenter |
|---|---:|---:|
| 0 subscribers | 400,000 B | **0 B** |
| 1 subscriber | 1,840,000 B | **0 B** |
| 10 subscribers | 9,760,000 B | **0 B** |
| struct payload, 1 subscriber | 2,080,000 B | **0 B** |

Time for 10 subscribers: 14,443.8 us → 1,052.2 us; struct payload: 1,820.0 us → 299.1 us.
Subscribe/unsubscribe is approximately 25% slower per write but allocates about 17% less.

The hot path is one dictionary lookup, one lock/unlock to snapshot the subscription array, and an array
walk. Callbacks always run outside the lock, so they may send, subscribe, or unsubscribe re-entrantly.

## Building

```bash
dotnet build AnoiMessagingCenter.sln -c Release
dotnet test AnoiMessagingCenter.sln -c Release
dotnet pack src/AnoiMessagingCenter/AnoiMessagingCenter.csproj -c Release
```

## License

MIT.
