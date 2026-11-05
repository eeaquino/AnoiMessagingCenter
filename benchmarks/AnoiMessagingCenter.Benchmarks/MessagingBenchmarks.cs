using AnoiMessagingCenter.Benchmarks.Baseline;
using BenchmarkDotNet.Attributes;

namespace AnoiMessagingCenter.Benchmarks;

[MemoryDiagnoser]
public class SendBenchmarks
{
    private const string Message = "bench-message";
    private static readonly Sender SharedSender = new();
    private static readonly Payload SharedPayload = new();
    private Subscriber[] _subscribers = Array.Empty<Subscriber>();

    [Params(0, 1, 10)]
    public int SubscriberCount { get; set; }

    private string _baselineMessage = null!;
    private string _newMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baselineMessage = Message + "-baseline-" + SubscriberCount;
        _newMessage = Message + "-new-" + SubscriberCount;
        _subscribers = Enumerable.Range(0, SubscriberCount).Select(_ => new Subscriber()).ToArray();

        foreach (var subscriber in _subscribers)
        {
            BaselineMessagingCenter.Subscribe<Sender, Payload>(subscriber, _baselineMessage, subscriber.OnMessage, null!);
            MessagingCenter.Subscribe<Sender, Payload>(subscriber, _newMessage, subscriber.OnMessage, null!);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var subscriber in _subscribers)
        {
            BaselineMessagingCenter.Unsubscribe<Sender, Payload>(subscriber, _baselineMessage);
            MessagingCenter.Unsubscribe<Sender, Payload>(subscriber, _newMessage);
        }
    }

    [Benchmark(Baseline = true)]
    public void Baseline_Send()
    {
        for (var i = 0; i < 10_000; i++)
            BaselineMessagingCenter.Send(SharedSender, _baselineMessage, SharedPayload);
    }

    [Benchmark]
    public void New_Send()
    {
        for (var i = 0; i < 10_000; i++)
            MessagingCenter.Send(SharedSender, _newMessage, SharedPayload);
    }

    public sealed class Sender;

    public sealed class Payload;

    private sealed class Subscriber
    {
        public void OnMessage(Sender sender, Payload payload)
        {
        }
    }
}

[MemoryDiagnoser]
public class StructSendBenchmarks
{
    private const string Message = "bench-struct";
    private static readonly StructPayload SharedPayload = new() { A = 1, B = 2 };
    private static readonly Sender SharedSender = new();
    private Subscriber _subscriber = null!;
    private string _baselineMessage = null!;
    private string _newMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baselineMessage = Message + "-baseline";
        _newMessage = Message + "-new";
        _subscriber = new Subscriber();
        BaselineMessagingCenter.Subscribe<Sender, StructPayload>(_subscriber, _baselineMessage, _subscriber.OnMessage, null!);
        MessagingCenter.Subscribe<Sender, StructPayload>(_subscriber, _newMessage, _subscriber.OnMessage, null!);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BaselineMessagingCenter.Unsubscribe<Sender, StructPayload>(_subscriber, _baselineMessage);
        MessagingCenter.Unsubscribe<Sender, StructPayload>(_subscriber, _newMessage);
    }

    [Benchmark(Baseline = true)]
    public void Baseline_SendStruct()
    {
        for (var i = 0; i < 10_000; i++)
            BaselineMessagingCenter.Send(SharedSender, _baselineMessage, SharedPayload);
    }

    [Benchmark]
    public void New_SendStruct()
    {
        for (var i = 0; i < 10_000; i++)
            MessagingCenter.Send(SharedSender, _newMessage, SharedPayload);
    }

    public sealed class Sender;

    public struct StructPayload
    {
        public int A;
        public int B;
    }

    private sealed class Subscriber
    {
        public void OnMessage(Sender sender, StructPayload payload)
        {
        }
    }
}

[MemoryDiagnoser]
public class SubscribeBenchmarks
{
    private const string Message = "bench-subscribe";

    [Benchmark(Baseline = true)]
    public void Baseline_SubscribeUnsubscribe()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var subscriber = new Subscriber();
            BaselineMessagingCenter.Subscribe<Sender, Payload>(subscriber, Message, subscriber.OnMessage, null!);
            BaselineMessagingCenter.Unsubscribe<Sender, Payload>(subscriber, Message);
        }
    }

    [Benchmark]
    public void New_SubscribeUnsubscribe()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var subscriber = new Subscriber();
            MessagingCenter.Subscribe<Sender, Payload>(subscriber, Message, subscriber.OnMessage, null!);
            MessagingCenter.Unsubscribe<Sender, Payload>(subscriber, Message);
        }
    }

    public sealed class Sender;

    public sealed class Payload;

    private sealed class Subscriber
    {
        public void OnMessage(Sender sender, Payload payload)
        {
        }
    }
}


