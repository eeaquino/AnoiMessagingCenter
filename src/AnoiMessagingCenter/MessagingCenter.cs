using AnoiMessagingCenter.Internal;
using System.Runtime.CompilerServices;

namespace AnoiMessagingCenter;

/// <summary>Default messaging center. The static members operate on <see cref="Instance"/>.</summary>
public class MessagingCenter : IMessagingCenter
{
    private static readonly Lazy<MessagingCenter> _instance = new(() => new MessagingCenter());

    private readonly Lock _sync = new();
    private readonly Dictionary<MessageKey, WeakReference<Subscription>[]> _subscriptions = new();
    private readonly ConditionalWeakTable<object, List<Subscription>> _anchors = new();

    /// <summary>Gets the shared lazy singleton used by the static members.</summary>
    public static IMessagingCenter Instance => _instance.Value;

    /// <summary>
    /// Raised when a synchronous or asynchronous subscriber callback throws. Handler exceptions are ignored.
    /// </summary>
    public static event Action<Exception, string>? CallbackFailed;

    private static MessagingCenter Default => _instance.Value;

    /// <summary>Sends a message with arguments to subscribers registered for the exact sender and argument types.</summary>
    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
        => Default.SendCore(sender, message, args);

    /// <summary>Sends a message without arguments.</summary>
    public static void Send<TSender>(TSender sender, string message) where TSender : class
        => Default.SendCore(sender, message);

    /// <summary>Subscribes a callback that receives the sender and arguments.</summary>
    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class
        => Default.SubscribeCore(subscriber, message, callback, source);

    /// <summary>Subscribes a callback that receives only the sender.</summary>
    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class
        => Default.SubscribeCore(subscriber, message, callback, source);

    /// <summary>Subscribes an asynchronous callback that receives the sender and arguments.</summary>
    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => Default.SubscribeAsyncCore(subscriber, message, asyncCallback, source);

    /// <summary>Subscribes an asynchronous callback that receives only the sender.</summary>
    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => Default.SubscribeAsyncCore(subscriber, message, asyncCallback, source);

    /// <summary>Removes all subscriptions for the subscriber, message, and exact sender/argument types.</summary>
    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class
        => Default.UnsubscribeCore<TSender, TArgs>(subscriber, message);

    /// <summary>Removes all single-parameter subscriptions for the subscriber and message.</summary>
    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class
        => Default.UnsubscribeCore<TSender>(subscriber, message);

    void IMessagingCenter.Send<TSender, TArgs>(TSender sender, string message, TArgs args)
        => SendCore(sender, message, args);

    void IMessagingCenter.Send<TSender>(TSender sender, string message)
        => SendCore(sender, message);

#nullable disable
    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source)
        => SubscribeCore(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source)
        => SubscribeCore(subscriber, message, callback, source);

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source)
        => SubscribeAsyncCore(subscriber, message, asyncCallback, source);

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source)
        => SubscribeAsyncCore(subscriber, message, asyncCallback, source);
#nullable restore

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => UnsubscribeCore<TSender, TArgs>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => UnsubscribeCore<TSender>(subscriber, message);

    internal static void ClearSubscribers() => Default.ClearAll();

    internal static int GetSubscriptionCount(string message, Type senderType, Type? argType)
        => Default.CountSubscriptions(new MessageKey(message, senderType, argType));

    internal static void RaiseCallbackFailed(Exception exception, string message)
    {
        var handler = CallbackFailed;
        if (handler is null)
            return;

        try
        {
            handler(exception, message);
        }
        catch
        {
            // Never let diagnostics break message delivery.
        }
    }

    private void SendCore<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        WeakReference<Subscription>[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        var stale = false;

        foreach (var weakSubscription in snapshot)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
            {
                stale = true;
                continue;
            }

            if (subscription.IsRemoved)
            {
                stale = true;
                continue;
            }

            if (!subscription.IsSubscriberAlive)
            {
                stale = true;
                continue;
            }

            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender, TArgs> typed)
                typed.Invoke(sender, args);
        }

        if (stale)
            Prune(key, snapshot);
    }

    private void SendCore<TSender>(TSender sender, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        WeakReference<Subscription>[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        var stale = false;

        foreach (var weakSubscription in snapshot)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
            {
                stale = true;
                continue;
            }

            if (subscription.IsRemoved)
            {
                stale = true;
                continue;
            }

            if (!subscription.IsSubscriberAlive)
            {
                stale = true;
                continue;
            }

            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender> typed)
                typed.Invoke(sender);
        }

        if (stale)
            Prune(key, snapshot);
    }

    private void SubscribeCore<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        var subscription = new SyncSubscription<TSender, TArgs>(subscriber, message, source, callback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
            _anchors.GetOrCreateValue(subscriber).Add(subscription);
        }
    }

    private void SubscribeCore<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        var subscription = new SyncSubscription<TSender>(subscriber, message, source, callback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
            _anchors.GetOrCreateValue(subscriber).Add(subscription);
        }
    }

    private void UnsubscribeCore<TSender, TArgs>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            if (!TryRemoveSubscriptions(subscriptions, subscriber, out var kept))
                return;

            if (kept.Length == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept;
        }
    }

    private void UnsubscribeCore<TSender>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            if (!TryRemoveSubscriptions(subscriptions, subscriber, out var kept))
                return;

            if (kept.Length == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept;
        }
    }

    private bool TryRemoveSubscriptions(
        WeakReference<Subscription>[] subscriptions, object subscriber, out WeakReference<Subscription>[] kept)
    {
        var survivors = new List<WeakReference<Subscription>>(subscriptions.Length);
        List<Subscription>? removed = null;
        var changed = false;

        foreach (var weakSubscription in subscriptions)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
            {
                changed = true;
                continue;
            }

            if (subscription.IsRemoved)
            {
                changed = true;
                continue;
            }

            if (subscription.SubscriberIs(subscriber))
            {
                subscription.MarkRemoved();
                (removed ??= new List<Subscription>()).Add(subscription);
                changed = true;
                continue;
            }

            survivors.Add(weakSubscription);
        }

        if (removed is not null)
        {
            foreach (var subscription in removed)
                DetachAnchor(subscription);
        }

        kept = survivors.ToArray();
        return changed;
    }

    private void DetachAnchor(Subscription subscription)
    {
        if (!subscription.TryGetSubscriber(out var owner))
            return;

        if (!_anchors.TryGetValue(owner, out var anchored))
            return;

        anchored.Remove(subscription);

        if (anchored.Count == 0)
            _anchors.Remove(owner);
    }

    private void SubscribeAsyncCore<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(asyncCallback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        var subscription = new AsyncSubscription<TSender, TArgs>(subscriber, message, source, asyncCallback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
            _anchors.GetOrCreateValue(subscriber).Add(subscription);
        }
    }

    private void SubscribeAsyncCore<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(asyncCallback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        var subscription = new AsyncSubscription<TSender>(subscriber, message, source, asyncCallback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
            _anchors.GetOrCreateValue(subscriber).Add(subscription);
        }
    }

    private void ClearAll()
    {
        lock (_sync)
        {
            _subscriptions.Clear();
            _anchors.Clear();
        }
    }

    private int CountSubscriptions(MessageKey key)
    {
        lock (_sync)
        {
            return _subscriptions.TryGetValue(key, out var subscriptions) ? subscriptions.Length : 0;
        }
    }

    private void Prune(MessageKey key, WeakReference<Subscription>[] observed)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current) || !ReferenceEquals(current, observed))
                return;

            var survivors = new List<WeakReference<Subscription>>(current.Length);
            var changed = false;

            foreach (var weakSubscription in current)
            {
                if (!weakSubscription.TryGetTarget(out var subscription))
                {
                    changed = true;
                    continue;
                }

                if (subscription.IsRemoved)
                {
                    changed = true;
                    continue;
                }

                if (!subscription.IsSubscriberAlive)
                {
                    subscription.MarkRemoved();
                    changed = true;
                    continue;
                }

                survivors.Add(weakSubscription);
            }

            if (!changed)
                return;

            if (survivors.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = survivors.ToArray();
        }
    }

    private static WeakReference<Subscription>[] Append(
        WeakReference<Subscription>[]? existing, Subscription subscription)
    {
        var reference = new WeakReference<Subscription>(subscription);

        if (existing is null || existing.Length == 0)
            return new[] { reference };

        var result = new WeakReference<Subscription>[existing.Length + 1];
        Array.Copy(existing, result, existing.Length);
        result[^1] = reference;
        return result;
    }
}
