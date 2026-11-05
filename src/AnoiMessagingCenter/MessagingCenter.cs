namespace AnoiMessagingCenter;

/// <summary>Default messaging center. The static members operate on <see cref="Instance"/>.</summary>
public class MessagingCenter : IMessagingCenter
{
    private static readonly Lazy<MessagingCenter> _instance = new(() => new MessagingCenter());

    /// <summary>Gets the shared lazy singleton used by the static members.</summary>
    public static IMessagingCenter Instance => _instance.Value;

    /// <summary>
    /// Raised when a synchronous or asynchronous subscriber callback throws. Handler exceptions are ignored.
    /// </summary>
    public static event Action<Exception, string>? CallbackFailed;

    /// <inheritdoc />
    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void Send<TSender>(TSender sender, string message) where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class
        => throw new NotImplementedException();

    /// <inheritdoc />
    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class
        => throw new NotImplementedException();

    void IMessagingCenter.Send<TSender, TArgs>(TSender sender, string message, TArgs args)
        => throw new NotImplementedException();

    void IMessagingCenter.Send<TSender>(TSender sender, string message)
        => throw new NotImplementedException();

#nullable disable
    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source)
        => throw new NotImplementedException();

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source)
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source)
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source)
        => throw new NotImplementedException();
#nullable restore

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => throw new NotImplementedException();

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => throw new NotImplementedException();

    internal static void ClearSubscribers()
    {
    }

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
}
