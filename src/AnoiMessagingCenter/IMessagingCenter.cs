namespace AnoiMessagingCenter;

/// <summary>Publishes and subscribes to string-keyed messages identified by sender and argument types.</summary>
public interface IMessagingCenter
{
    /// <summary>Sends a message with arguments to subscribers registered for the exact sender and argument types.</summary>
    void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class;

    /// <summary>Sends a message without arguments.</summary>
    void Send<TSender>(TSender sender, string message) where TSender : class;

    /// <summary>Subscribes a callback that receives the sender and arguments.</summary>
    void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class;

    /// <summary>Subscribes a callback that receives only the sender.</summary>
    void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class;

    /// <summary>Subscribes an asynchronous callback that receives the sender and arguments.</summary>
    void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> callback, TSender? source = null)
        where TSender : class;

    /// <summary>Subscribes an asynchronous callback that receives only the sender.</summary>
    void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> callback, TSender? source = null)
        where TSender : class;

    /// <summary>Removes all subscriptions for the subscriber, message, and exact sender/argument types.</summary>
    void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class;

    /// <summary>Removes all single-parameter subscriptions for the subscriber and message.</summary>
    void Unsubscribe<TSender>(object subscriber, string message) where TSender : class;
}
