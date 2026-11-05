using AnoiMessagingCenter.Internal;

namespace AnoiMessagingCenter;

/// <summary>Default messaging center. The static members operate on <see cref="Instance"/>.</summary>
public class MessagingCenter : IMessagingCenter
{
    private static readonly Lazy<MessagingCenter> _instance = new(() => new MessagingCenter());

    private readonly Lock _sync = new();
    private readonly Dictionary<MessageKey, Subscription[]> _subscriptions = new();

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
        => throw new NotImplementedException();

    /// <summary>Subscribes an asynchronous callback that receives only the sender.</summary>
    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

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
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source)
        => throw new NotImplementedException();
#nullable restore

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => UnsubscribeCore<TSender, TArgs>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => UnsubscribeCore<TSender>(subscriber, message);

    internal static void ClearSubscribers() => Default.ClearAll();

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
        Subscription[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var subscription in snapshot)
        {
            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender, TArgs> typed)
                typed.Invoke(sender, args);
        }
    }

    private void SendCore<TSender>(TSender sender, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        Subscription[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var subscription in snapshot)
        {
            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender> typed)
                typed.Invoke(sender);
        }
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

            var kept = new List<Subscription>(subscriptions.Length);

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsRemoved)
                    continue;

                if (subscription.SubscriberIs(subscriber))
                {
                    subscription.MarkRemoved();
                    continue;
                }

                kept.Add(subscription);
            }

            if (kept.Count == subscriptions.Length)
                return;

            if (kept.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept.ToArray();
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

            var kept = new List<Subscription>(subscriptions.Length);

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsRemoved)
                    continue;

                if (subscription.SubscriberIs(subscriber))
                {
                    subscription.MarkRemoved();
                    continue;
                }

                kept.Add(subscription);
            }

            if (kept.Count == subscriptions.Length)
                return;

            if (kept.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept.ToArray();
        }
    }

    private void ClearAll()
    {
        lock (_sync)
        {
            _subscriptions.Clear();
        }
    }

    private static Subscription[] Append(Subscription[]? existing, Subscription subscription)
    {
        if (existing is null || existing.Length == 0)
            return new[] { subscription };

        var result = new Subscription[existing.Length + 1];
        Array.Copy(existing, result, existing.Length);
        result[^1] = subscription;
        return result;
    }
}
