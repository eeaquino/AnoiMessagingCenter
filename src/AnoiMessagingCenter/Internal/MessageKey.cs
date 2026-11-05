namespace AnoiMessagingCenter.Internal;

internal readonly struct MessageKey : IEquatable<MessageKey>
{
    internal readonly string Message;
    internal readonly Type SenderType;
    internal readonly Type? ArgType;
    private readonly int _hash;

    internal MessageKey(string message, Type senderType, Type? argType)
    {
        Message = message;
        SenderType = senderType;
        ArgType = argType;
        _hash = HashCode.Combine(message, senderType, argType);
    }

    public bool Equals(MessageKey other)
        => ReferenceEquals(SenderType, other.SenderType)
        && ReferenceEquals(ArgType, other.ArgType)
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is MessageKey other && Equals(other);

    public override int GetHashCode() => _hash;
}
