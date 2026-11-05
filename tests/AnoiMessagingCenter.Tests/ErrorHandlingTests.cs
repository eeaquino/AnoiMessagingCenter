namespace AnoiMessagingCenter.Tests;

public class ErrorHandlingTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    [Fact]
    public void SyncCallbackException_DoesNotEscapeSend_AndRaisesEvent()
    {
        Exception? captured = null;
        string? capturedMessage = null;
        var subscriber = new object();

        Action<Exception, string> handler = (exception, message) =>
        {
            captured = exception;
            capturedMessage = message;
        };

        MessagingCenter.CallbackFailed += handler;
        try
        {
            MessagingCenter.Subscribe<Sender, Args>(subscriber, "boom", static (_, _) => throw new InvalidOperationException("sync"));

            var exception = Record.Exception(() => MessagingCenter.Send(new Sender(), "boom", new Args()));

            Assert.Null(exception);
            Assert.IsType<InvalidOperationException>(captured);
            Assert.Equal("boom", capturedMessage);
            GC.KeepAlive(subscriber);
        }
        finally
        {
            MessagingCenter.CallbackFailed -= handler;
        }
    }

    [Fact]
    public void OneThrowingCallback_DoesNotStopOtherSubscribers()
    {
        var calls = 0;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "mixed", static (_, _) => throw new InvalidOperationException());
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "mixed", (_, _) => calls++);

        MessagingCenter.Send(new Sender(), "mixed", new Args());

        Assert.Equal(1, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task AsyncCallbackException_RaisesEvent()
    {
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new object();

        Action<Exception, string> handler = (exception, _) => captured.TrySetResult(exception);
        MessagingCenter.CallbackFailed += handler;

        try
        {
            MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, "async-boom", static async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("async");
            });

            MessagingCenter.Send(new Sender(), "async-boom", new Args());

            var exception = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<InvalidOperationException>(exception);
            GC.KeepAlive(subscriber);
        }
        finally
        {
            MessagingCenter.CallbackFailed -= handler;
        }
    }

    [Fact]
    public void ThrowingHandler_DoesNotEscapeSend()
    {
        Action<Exception, string> handler = static (_, _) => throw new InvalidOperationException("handler");
        MessagingCenter.CallbackFailed += handler;

        try
        {
            var subscriber = new object();
            MessagingCenter.Subscribe<Sender>(subscriber, "handler-throws", static _ => throw new InvalidOperationException("callback"));

            var exception = Record.Exception(() => MessagingCenter.Send(new Sender(), "handler-throws"));

            Assert.Null(exception);
            GC.KeepAlive(subscriber);
        }
        finally
        {
            MessagingCenter.CallbackFailed -= handler;
        }
    }
}
