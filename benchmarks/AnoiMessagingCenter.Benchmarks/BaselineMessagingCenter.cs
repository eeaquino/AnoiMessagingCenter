#nullable disable
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AnoiMessagingCenter.Benchmarks.Baseline;

public sealed class BaselineMessagingCenter : IMessagingCenter
{
    private static readonly Lazy<IMessagingCenter> _instance = new(() => new BaselineMessagingCenter());
    public static IMessagingCenter Instance => _instance.Value;

    private readonly Lock _sync = new();
    private readonly Dictionary<Sender, List<Subscription>> _subscriptions = new();

    private sealed class Sender : Tuple<string, Type, Type>
    {
        public Sender(string message, Type senderType, Type argType)
            : base(message, senderType, argType) { }
    }

    private delegate bool Filter(object sender);

    private sealed class MaybeWeakReference
    {
        private readonly WeakReference _delegateWeakReference;
        private readonly object _delegateStrongReference;
        private readonly bool _isStrongReference;

        public MaybeWeakReference(object subscriber, object delegateSource)
        {
            if (subscriber.Equals(delegateSource))
            {
                _delegateWeakReference = new WeakReference(delegateSource);
                _isStrongReference = false;
            }
            else
            {
                _delegateStrongReference = delegateSource;
                _isStrongReference = true;
            }
        }

        public object Target => _isStrongReference ? _delegateStrongReference : _delegateWeakReference.Target;
        public bool IsAlive => _isStrongReference || _delegateWeakReference.IsAlive;
    }

    private sealed class Subscription : Tuple<WeakReference, MaybeWeakReference, MethodInfo, Filter>
    {
        public Subscription(object subscriber, object delegateSource, MethodInfo methodInfo, Filter filter)
            : base(new WeakReference(subscriber), new MaybeWeakReference(subscriber, delegateSource), methodInfo, filter) { }

        public WeakReference Subscriber => Item1;
        private MaybeWeakReference DelegateSource => Item2;
        private MethodInfo MethodInfo => Item3;
        private Filter Filter => Item4;

        public void InvokeCallback(object sender, object args)
        {
            if (!Filter(sender))
                return;

            var target = DelegateSource.Target;
            if (target == null)
                return;

            try
            {
                MethodInfo.Invoke(
                    MethodInfo.IsStatic ? null : target,
                    MethodInfo.GetParameters().Length == 1 ? new[] { sender } : new[] { sender, args });
            }
            catch
            {
                // Serilog removed for benchmark isolation.
            }
        }

        public bool CanBeRemoved() => !Subscriber.IsAlive || !DelegateSource.IsAlive;
    }

    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args)
        where TSender : class
        => Instance.Send(sender, message, args);

    void IMessagingCenter.Send<TSender, TArgs>(TSender sender, string message, TArgs args)
    {
        if (sender == null)
            throw new ArgumentNullException(nameof(sender));
        InnerSend(message, typeof(TSender), typeof(TArgs), sender, args);
    }

    public static void Send<TSender>(TSender sender, string message) where TSender : class
        => Instance.Send(sender, message);

    void IMessagingCenter.Send<TSender>(TSender sender, string message)
    {
        if (sender == null)
            throw new ArgumentNullException(nameof(sender));
        InnerSend(message, typeof(TSender), null, sender, null);
    }

    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source = null)
        where TSender : class
        => Instance.Subscribe(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        var target = callback.Target;
        Filter filter = sender => source == null || sender.Equals(source);
        InnerSubscribe(subscriber, message, typeof(TSender), typeof(TArgs), target, callback.GetMethodInfo(), filter);
    }

    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source = null)
        where TSender : class
        => Instance.Subscribe(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        var target = callback.Target;
        Filter filter = sender => source == null || sender.Equals(source);
        InnerSubscribe(subscriber, message, typeof(TSender), null, target, callback.GetMethodInfo(), filter);
    }

    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source = null)
        where TSender : class
        => Instance.SubscribeAsync(subscriber, message, asyncCallback, source);

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (asyncCallback == null)
            throw new ArgumentNullException(nameof(asyncCallback));

        Action<TSender, TArgs> callback = async (s, a) =>
        {
            try
            {
                await asyncCallback(s, a);
            }
            catch
            {
            }
        };

        ((IMessagingCenter)this).Subscribe(subscriber, message, callback, source);
    }

    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source = null)
        where TSender : class
        => Instance.SubscribeAsync(subscriber, message, asyncCallback, source);

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (asyncCallback == null)
            throw new ArgumentNullException(nameof(asyncCallback));

        Action<TSender> callback = async s =>
        {
            try
            {
                await asyncCallback(s);
            }
            catch
            {
            }
        };

        ((IMessagingCenter)this).Subscribe(subscriber, message, callback, source);
    }

    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class
        => Instance.Unsubscribe<TSender, TArgs>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => InnerUnsubscribe(message, typeof(TSender), typeof(TArgs), subscriber);

    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class
        => Instance.Unsubscribe<TSender>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => InnerUnsubscribe(message, typeof(TSender), null, subscriber);

    private void InnerSend(string message, Type senderType, Type argType, object sender, object args)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        List<Subscription> subscriptionsCopy;
        var key = new Sender(message, senderType, argType);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subList) || subList == null || subList.Count == 0)
                return;
            subscriptionsCopy = subList.ToList();
        }

        foreach (var subscription in subscriptionsCopy)
        {
            bool isValidSubscription;
            lock (_sync)
            {
                if (!_subscriptions.ContainsKey(key))
                    continue;

                isValidSubscription = subscription.Subscriber.Target != null && _subscriptions[key].Contains(subscription);
            }

            if (isValidSubscription)
                subscription.InvokeCallback(sender, args);
        }
    }

    private void InnerSubscribe(
        object subscriber, string message, Type senderType, Type argType, object target, MethodInfo methodInfo, Filter filter)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var key = new Sender(message, senderType, argType);
        var value = new Subscription(subscriber, target, methodInfo, filter);

        lock (_sync)
        {
            if (_subscriptions.TryGetValue(key, out var existing))
                existing.Add(value);
            else
                _subscriptions[key] = new List<Subscription> { value };
        }
    }

    private void InnerUnsubscribe(string message, Type senderType, Type argType, object subscriber)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var key = new Sender(message, senderType, argType);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var existing))
                return;

            existing.RemoveAll(s => s.CanBeRemoved() || s.Subscriber.Target == subscriber);
            if (existing.Count == 0)
                _subscriptions.Remove(key);
        }
    }
}
