namespace AnoiMessagingCenter.Tests;

internal static class CompileCompatibility
{
    private sealed class Sender;

    private sealed class Args;

    private static void ExerciseStaticApi(object subscriber, Sender sender, Args args)
    {
        MessagingCenter.Send(sender, "message");
        MessagingCenter.Send(sender, "message", args);

        MessagingCenter.Subscribe<Sender>(subscriber, "message", static _ => { });
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { });
        MessagingCenter.Subscribe<Sender>(subscriber, "message", static _ => { }, sender);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { }, sender);

        MessagingCenter.SubscribeAsync<Sender>(subscriber, "message", static _ => Task.CompletedTask);
        MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, "message", static (_, _) => Task.CompletedTask);

        MessagingCenter.Unsubscribe<Sender>(subscriber, "message");
        MessagingCenter.Unsubscribe<Sender, Args>(subscriber, "message");

        MessagingCenter.Send<Sender, Args>(sender: sender, message: "message", args: args);
        MessagingCenter.Subscribe<Sender, Args>(
            subscriber: subscriber, message: "message", callback: static (_, _) => { }, source: sender);
        MessagingCenter.SubscribeAsync<Sender, Args>(
            subscriber: subscriber, message: "message", asyncCallback: static (_, _) => Task.CompletedTask, source: sender);
        MessagingCenter.Unsubscribe<Sender, Args>(subscriber: subscriber, message: "message");
    }

    private static void ExerciseInstanceApi(IMessagingCenter center, object subscriber, Sender sender, Args args)
    {
        center.Send(sender, "message");
        center.Send(sender, "message", args);

        center.Subscribe<Sender>(subscriber, "message", static _ => { });
        center.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { });
        center.SubscribeAsync<Sender>(subscriber, "message", static _ => Task.CompletedTask);
        center.SubscribeAsync<Sender, Args>(subscriber, "message", static (_, _) => Task.CompletedTask);

        center.Unsubscribe<Sender>(subscriber, "message");
        center.Unsubscribe<Sender, Args>(subscriber, "message");

        center.Send<Sender, Args>(sender: sender, message: "message", args: args);
        center.Subscribe<Sender, Args>(
            subscriber: subscriber, message: "message", callback: static (_, _) => { }, source: sender);
        center.SubscribeAsync<Sender, Args>(
            subscriber: subscriber, message: "message", callback: static (_, _) => Task.CompletedTask, source: sender);
        center.Unsubscribe<Sender, Args>(subscriber: subscriber, message: "message");
    }
}


