using System.Runtime.CompilerServices;

namespace AnoiMessagingCenter.Tests;

public class LifetimeTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    private sealed class Counter
    {
        public int Value;
    }

    private sealed class LifetimeSubscriber
    {
        private readonly Counter _counter;

        public LifetimeSubscriber(Counter counter) => _counter = counter;

        public void OnMessage(Sender sender, Args args) => _counter.Value++;
    }

    private sealed class CallbackTarget
    {
        private readonly Counter _counter;

        public CallbackTarget(Counter counter) => _counter = counter;

        public void OnMessage(Sender sender, Args args) => _counter.Value++;
    }

    [Fact]
    public void Subscriber_IsNotKeptAliveBySubscription()
    {
        var subscriber = CreateSubscriber(new Counter());

        AssertCollected(subscriber);
    }

    [Fact]
    public void CollectedSubscriber_NoLongerReceivesMessages()
    {
        var counter = new Counter();
        var subscriber = CreateSubscriber(counter);

        AssertCollected(subscriber);

        MessagingCenter.Send(new Sender(), "lifetime", new Args());

        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public void ExternalCallbackTarget_IsReleasedAfterSubscriberIsCollected()
    {
        var (subscriber, target) = CreateSubscriberWithExternalTarget(new Counter());

        AssertCollected(subscriber);
        AssertCollected(target);
    }

    [Fact]
    public void ExternalCallbackTarget_StaysAliveWhileSubscriberLives()
    {
        var subscriber = new LifetimeSubscriber(new Counter());
        var target = CreateExternalTarget(subscriber, new Counter());

        ForceGc();

        Assert.True(target.IsAlive);
        GC.KeepAlive(subscriber);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSubscriber(Counter counter)
    {
        var subscriber = new LifetimeSubscriber(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime", subscriber.OnMessage);
        return new WeakReference(subscriber);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Subscriber, WeakReference Target) CreateSubscriberWithExternalTarget(Counter counter)
    {
        var subscriber = new LifetimeSubscriber(counter);
        var target = new CallbackTarget(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime", target.OnMessage);
        return (new WeakReference(subscriber), new WeakReference(target));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateExternalTarget(LifetimeSubscriber subscriber, Counter counter)
    {
        var target = new CallbackTarget(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime-target", target.OnMessage);
        return new WeakReference(target);
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
            ForceGc();

        Assert.False(reference.IsAlive);
    }

    private static void ForceGc()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
