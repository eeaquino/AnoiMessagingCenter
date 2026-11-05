namespace AnoiMessagingCenter.Tests;

public class AsyncTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    [Fact]
    public async Task SubscribeAsync_TwoParameters_InvokesAndCompletes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new object();
        Sender? receivedSender = null;

        MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, "async", (sender, _) =>
        {
            receivedSender = sender;
            completion.TrySetResult();
            return Task.CompletedTask;
        });

        var sent = new Sender();
        MessagingCenter.Send(sent, "async", new Args());

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(sent, receivedSender);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task SubscribeAsync_OneParameter_InvokesAndCompletes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new object();

        MessagingCenter.SubscribeAsync<Sender>(subscriber, "async-single", _ =>
        {
            completion.TrySetResult();
            return Task.CompletedTask;
        });

        MessagingCenter.Send(new Sender(), "async-single");

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(completion.Task.IsCompletedSuccessfully);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task Send_DoesNotWaitForAsyncCallback()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = new object();

        MessagingCenter.SubscribeAsync<Sender>(subscriber, "slow", async _ =>
        {
            started.TrySetResult();
            await release.Task;
        });

        MessagingCenter.Send(new Sender(), "slow");

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(release.Task.IsCompleted);

        release.TrySetResult();
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public async Task AsyncCallback_ResumesOnCapturedSynchronizationContext()
    {
        var previous = SynchronizationContext.Current;
        var context = new RecordingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscriber = new object();

            MessagingCenter.SubscribeAsync<Sender>(subscriber, "async-context", async _ =>
            {
                await Task.Yield();
                completion.TrySetResult();
            });

            MessagingCenter.Send(new Sender(), "async-context");

            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.PostCount > 0);
            GC.KeepAlive(subscriber);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void NullArguments_ThrowWithOriginalParameterNames()
    {
        var subscriber = new object();
        Func<Sender, Args, Task> two = static (_, _) => Task.CompletedTask;
        Func<Sender, Task> one = static _ => Task.CompletedTask;

        Assert.Equal("subscriber", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.SubscribeAsync<Sender, Args>(null!, "m", two)).ParamName);
        Assert.Equal("asyncCallback", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, "m", null!)).ParamName);
        Assert.Equal("message", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, null!, two)).ParamName);
        Assert.Equal("subscriber", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.SubscribeAsync<Sender>(null!, "m", one)).ParamName);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(
                static postState =>
                {
                    var (callback, callbackState) = ((SendOrPostCallback, object?))postState!;
                    callback(callbackState);
                },
                (d, state));
        }
    }
}
