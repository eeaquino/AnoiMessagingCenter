namespace AnoiMessagingCenter.Tests;

public abstract class TestBase : IDisposable
{
    protected TestBase() => MessagingCenter.ClearSubscribers();

    public void Dispose() => MessagingCenter.ClearSubscribers();
}
