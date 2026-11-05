using System.Reflection;

namespace AnoiMessagingCenter.Tests;

public class ApiShapeTests
{
    [Fact]
    public void Interface_Exposes_Exactly_The_Original_Members()
    {
        var signatures = typeof(IMessagingCenter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Send/1/2",
                "Send/2/3",
                "Subscribe/1/4",
                "Subscribe/2/4",
                "SubscribeAsync/1/4",
                "SubscribeAsync/2/4",
                "Unsubscribe/1/2",
                "Unsubscribe/2/2",
            },
            signatures);
    }

    [Fact]
    public void Class_Keeps_Static_Members_And_Additive_Event()
    {
        var type = typeof(MessagingCenter);

        Assert.True(type.IsPublic);
        Assert.False(type.IsSealed);
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.NotNull(type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetEvent("CallbackFailed", BindingFlags.Public | BindingFlags.Static));

        var signatures = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Send/1/2",
                "Send/2/3",
                "Subscribe/1/4",
                "Subscribe/2/4",
                "SubscribeAsync/1/4",
                "SubscribeAsync/2/4",
                "Unsubscribe/1/2",
                "Unsubscribe/2/2",
            },
            signatures);
    }

    private static string Describe(MethodInfo method)
        => $"{method.Name}/{method.GetGenericArguments().Length}/{method.GetParameters().Length}";
}

