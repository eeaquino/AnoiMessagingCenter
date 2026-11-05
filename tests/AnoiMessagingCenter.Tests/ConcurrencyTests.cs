using System.Collections.Concurrent;

namespace AnoiMessagingCenter.Tests;

public class ConcurrencyTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    private sealed class Subscriber
    {
        public void OnMessage(Sender sender, Args args)
        {
        }
    }

    [Fact]
    public void ConcurrentSubscribeSendUnsubscribe_DoesNotThrow()
    {
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 2_000, i =>
        {
            try
            {
                var subscriber = new Subscriber();
                var message = "concurrent-" + (i % 8);

                MessagingCenter.Subscribe<Sender, Args>(subscriber, message, subscriber.OnMessage);
                MessagingCenter.Send(new Sender(), message, new Args());
                MessagingCenter.Unsubscribe<Sender, Args>(subscriber, message);
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void CallbackCanSendAndSubscribe()
    {
        var nestedCalls = 0;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "outer", (_, _) =>
        {
            MessagingCenter.Send(new Sender(), "inner", new Args());
        });
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "inner", (_, _) => nestedCalls++);

        MessagingCenter.Send(new Sender(), "outer", new Args());

        Assert.Equal(1, nestedCalls);
        GC.KeepAlive(subscriber);
    }
}
