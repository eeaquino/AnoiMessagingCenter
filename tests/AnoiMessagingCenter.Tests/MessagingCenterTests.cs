namespace AnoiMessagingCenter.Tests;

public class MessagingCenterTests : TestBase
{
    private sealed class Sender;

    private sealed class OtherSender;

    private sealed class Args
    {
        public int Value { get; init; }
    }

    private sealed class EquatableSender : IEquatable<EquatableSender>
    {
        public int Id { get; init; }

        public bool Equals(EquatableSender? other) => other is not null && other.Id == Id;

        public override bool Equals(object? obj) => obj is EquatableSender other && Equals(other);

        public override int GetHashCode() => Id;
    }

    [Fact]
    public void Send_WithNoSubscribers_DoesNotThrow()
    {
        MessagingCenter.Send(new Sender(), "no-subscribers", new Args());
    }

    [Fact]
    public void Subscribe_ThenSend_InvokesCallbackWithSenderAndArgs()
    {
        Sender? receivedSender = null;
        Args? receivedArgs = null;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "deliver", (sender, args) =>
        {
            receivedSender = sender;
            receivedArgs = args;
        });

        var sentSender = new Sender();
        var sentArgs = new Args { Value = 42 };
        MessagingCenter.Send(sentSender, "deliver", sentArgs);

        Assert.Same(sentSender, receivedSender);
        Assert.Same(sentArgs, receivedArgs);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void Send_WithDifferentArgsType_DoesNotInvoke()
    {
        var calls = 0;
        var subscriber = new object();
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "types", (_, _) => calls++);

        MessagingCenter.Send(new Sender(), "types", "different-args");

        Assert.Equal(0, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void Send_WithDifferentSenderType_DoesNotInvoke()
    {
        var calls = 0;
        var subscriber = new object();
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "types", (_, _) => calls++);

        MessagingCenter.Send(new OtherSender(), "types", new Args());

        Assert.Equal(0, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void DuplicateSubscriptions_AllInvoke()
    {
        var calls = 0;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "duplicate", (_, _) => calls++);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "duplicate", (_, _) => calls++);

        MessagingCenter.Send(new Sender(), "duplicate", new Args());

        Assert.Equal(2, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void Unsubscribe_StopsDeliveryForThatSubscriberOnly()
    {
        var firstCalls = 0;
        var secondCalls = 0;
        var first = new object();
        var second = new object();

        MessagingCenter.Subscribe<Sender, Args>(first, "unsubscribe", (_, _) => firstCalls++);
        MessagingCenter.Subscribe<Sender, Args>(second, "unsubscribe", (_, _) => secondCalls++);

        MessagingCenter.Unsubscribe<Sender, Args>(first, "unsubscribe");
        MessagingCenter.Send(new Sender(), "unsubscribe", new Args());

        Assert.Equal(0, firstCalls);
        Assert.Equal(1, secondCalls);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [Fact]
    public void OneParameterOverload_InvokesAndIsIndependentFromTwoParameter()
    {
        var oneParameterCalls = 0;
        var twoParameterCalls = 0;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender>(subscriber, "single", _ => oneParameterCalls++);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "single", (_, _) => twoParameterCalls++);

        MessagingCenter.Send(new Sender(), "single");

        Assert.Equal(1, oneParameterCalls);
        Assert.Equal(0, twoParameterCalls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void SourceFilter_OnlyInvokesMatchingSenderInstance()
    {
        var calls = 0;
        var matching = new Sender();
        var other = new Sender();
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "source", (_, _) => calls++, matching);

        MessagingCenter.Send(other, "source", new Args());
        Assert.Equal(0, calls);

        MessagingCenter.Send(matching, "source", new Args());
        Assert.Equal(1, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void SourceFilter_UsesVirtualEquals()
    {
        var calls = 0;
        var source = new EquatableSender { Id = 1 };
        var subscriber = new object();

        MessagingCenter.Subscribe<EquatableSender, Args>(subscriber, "source-equals", (_, _) => calls++, source);

        MessagingCenter.Send(new EquatableSender { Id = 1 }, "source-equals", new Args());
        Assert.Equal(1, calls);

        MessagingCenter.Send(new EquatableSender { Id = 2 }, "source-equals", new Args());
        Assert.Equal(1, calls);
        GC.KeepAlive(subscriber);
    }

    private static int s_staticCalls;

    private static void StaticHandler(Sender sender, Args args) => s_staticCalls++;

    [Fact]
    public void StaticMethodCallback_IsInvoked()
    {
        s_staticCalls = 0;
        var subscriber = new object();
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "static-callback", StaticHandler);

        MessagingCenter.Send(new Sender(), "static-callback", new Args());

        Assert.Equal(1, s_staticCalls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void NullArguments_ThrowWithOriginalParameterNames()
    {
        var subscriber = new object();
        Action<Sender, Args> callback = static (_, _) => { };

        Assert.Equal("sender", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Send<Sender, Args>(null!, "message", new Args())).ParamName);
        Assert.Equal("message", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Send(new Sender(), null!, new Args())).ParamName);
        Assert.Equal("subscriber", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Subscribe<Sender, Args>(null!, "message", callback)).ParamName);
        Assert.Equal("callback", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Subscribe<Sender, Args>(subscriber, "message", null!)).ParamName);
        Assert.Equal("message", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Subscribe<Sender, Args>(subscriber, null!, callback)).ParamName);
        Assert.Equal("subscriber", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Unsubscribe<Sender, Args>(null!, "message")).ParamName);
        Assert.Equal("message", Assert.Throws<ArgumentNullException>(
            () => MessagingCenter.Unsubscribe<Sender, Args>(subscriber, null!)).ParamName);
    }

    [Fact]
    public void InterfacePath_And_StaticPath_ShareState()
    {
        var calls = 0;
        var subscriber = new object();
        IMessagingCenter center = MessagingCenter.Instance;

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "shared", (_, _) => calls++);
        center.Send(new Sender(), "shared", new Args());

        Assert.Equal(1, calls);
        GC.KeepAlive(subscriber);
    }
}
