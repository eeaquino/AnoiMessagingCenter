using System.Diagnostics.CodeAnalysis;

namespace AnoiMessagingCenter.Internal;

internal interface ISubscription<in TSender>
{
    void Invoke(TSender sender);
}

internal interface ISubscription<in TSender, in TArgs>
{
    void Invoke(TSender sender, TArgs args);
}

internal abstract class Subscription
{
    private readonly WeakReference<object> _subscriber;
    private int _removed;

    protected Subscription(object subscriber, string message, object? source)
    {
        _subscriber = new WeakReference<object>(subscriber);
        Message = message;
        Source = source;
    }

    internal string Message { get; }

    internal object? Source { get; }

    internal bool IsRemoved => Volatile.Read(ref _removed) != 0;

    internal bool IsSubscriberAlive => _subscriber.TryGetTarget(out _);

    internal bool SubscriberIs(object subscriber)
        => _subscriber.TryGetTarget(out var target) && ReferenceEquals(target, subscriber);

    internal bool TryGetSubscriber([NotNullWhen(true)] out object? subscriber)
        => _subscriber.TryGetTarget(out subscriber);

    internal void MarkRemoved() => Volatile.Write(ref _removed, 1);

    internal bool CanInvoke(object sender)
    {
        if (IsRemoved)
            return false;

        if (!_subscriber.TryGetTarget(out _))
            return false;

        if (Source is not null && !sender.Equals(Source))
            return false;

        return true;
    }
}

internal sealed class SyncSubscription<TSender, TArgs> : Subscription, ISubscription<TSender, TArgs>
{
    private readonly Action<TSender, TArgs> _callback;

    internal SyncSubscription(object subscriber, string message, object? source, Action<TSender, TArgs> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender, TArgs args) => _callback(sender, args);
}

internal sealed class SyncSubscription<TSender> : Subscription, ISubscription<TSender>
{
    private readonly Action<TSender> _callback;

    internal SyncSubscription(object subscriber, string message, object? source, Action<TSender> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender) => _callback(sender);
}
