# AnoiMessagingCenter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a dependency-free `AnoiMessagingCenter` NuGet package for .NET 10 that preserves the original `IMessagingCenter`/`MessagingCenter` API while removing reflection and per-send allocations and releasing dead subscriptions.

**Architecture:** Copy-on-write subscription arrays keyed by a struct `MessageKey`, typed generic subscription classes instead of `MethodInfo.Invoke`, and `ConditionalWeakTable` anchoring so subscriptions live exactly as long as their subscriber. Callbacks always run outside the dictionary lock.

**Tech Stack:** C# 14, .NET 10, `System.Threading.Lock`, `ConditionalWeakTable`, xUnit, BenchmarkDotNet, NuGet packaging.

**Spec:** `docs/superpowers/specs/2026-11-05-anoimessagingcenter-design.md`

## Global Constraints

- Target framework: `net10.0` only. SDK: 10.0.403 or later.
- Package ID and namespace: `AnoiMessagingCenter`. Class name: `MessagingCenter`.
- The original public API must not change: 8 interface methods, 8 static methods, `Instance`, `internal static ClearSubscribers()`, generic constraints, defaults, validation order, and exception parameter names.
- One additive public member only: `public static event Action<Exception, string>? CallbackFailed;`.
- No runtime NuGet dependencies. No Serilog.
- Library project: nullable enabled, XML docs on every public member, warnings as errors, `IsAotCompatible=true`, deterministic build.
- `Send` must allocate 0 bytes in steady state, including with struct `TArgs`.
- Tests must pass in Release (`dotnet test -c Release`). Static event tests require disabled test parallelization.
- Use `System.Threading.Lock` and `lock` statements; never invoke callbacks while holding `_sync`.
- Explicit interface implementations of `Subscribe`/`SubscribeAsync` cannot restate `where TSender : class` (CS0460). Wrap those four explicit implementations in `#nullable disable`/`#nullable restore` and declare their `source` parameter as `TSender source`; static methods keep `TSender?`.
- Commit after every task with a conventional message.

---

### Task 1: Scaffolding, API skeleton, and API-shape tests

**Files:**
- Create: `.gitignore`
- Create: `Directory.Build.props`
- Create: `README.md`
- Create: `LICENSE`
- Create: `src/AnoiMessagingCenter/AnoiMessagingCenter.csproj`
- Create: `src/AnoiMessagingCenter/IMessagingCenter.cs`
- Create: `src/AnoiMessagingCenter/MessagingCenter.cs`
- Create: `tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj`
- Create: `tests/AnoiMessagingCenter.Tests/AssemblyInfo.cs`
- Create: `tests/AnoiMessagingCenter.Tests/TestBase.cs`
- Create: `tests/AnoiMessagingCenter.Tests/ApiShapeTests.cs`
- Create: `tests/AnoiMessagingCenter.Tests/CompileCompatibility.cs`
- Create: `AnoiMessagingCenter.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: the exact public API skeleton, the test harness, and the internal `ClearSubscribers()`/`RaiseCallbackFailed()` hooks later tasks fill in.

- [ ] **Step 1: Create the solution and projects**

Run:

```bash
dotnet new sln -n AnoiMessagingCenter --format sln
dotnet new classlib -n AnoiMessagingCenter -o src/AnoiMessagingCenter -f net10.0
dotnet new xunit -n AnoiMessagingCenter.Tests -o tests/AnoiMessagingCenter.Tests -f net10.0
dotnet sln add src/AnoiMessagingCenter/AnoiMessagingCenter.csproj tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj
dotnet add tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj reference src/AnoiMessagingCenter/AnoiMessagingCenter.csproj
```

Delete the template files `src/AnoiMessagingCenter/Class1.cs` and `tests/AnoiMessagingCenter.Tests/UnitTest1.cs`.

- [ ] **Step 2: Add repository-wide build settings**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Deterministic>true</Deterministic>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

`.gitignore`:

```text
bin/
obj/
artifacts/
.vs/
*.user
TestResults/
BenchmarkDotNet.Artifacts/
```

`README.md` (stub; expanded in Task 8):

```markdown
# AnoiMessagingCenter

Allocation-conscious, weak-reference messaging center for .NET 10+. Full documentation is added with the first release.
```

`LICENSE`:

```text
MIT License

Copyright (c) 2026 Anoi

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 3: Configure the library project**

Replace `src/AnoiMessagingCenter/AnoiMessagingCenter.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <PackageId>AnoiMessagingCenter</PackageId>
    <Version>1.0.0</Version>
    <Authors>Anoi</Authors>
    <Description>Allocation-conscious, weak-reference messaging center for .NET 10+.</Description>
    <PackageTags>messaging;event-aggregator;weak-reference;dotnet</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <RepositoryType>git</RepositoryType>
    <PackageOutputPath>$(MSBuildThisFileDirectory)..\..\artifacts\</PackageOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\" />
    <None Include="..\..\LICENSE" Pack="true" PackagePath="\" />
    <InternalsVisibleTo Include="AnoiMessagingCenter.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add the interface exactly as specified**

`src/AnoiMessagingCenter/IMessagingCenter.cs`:

```csharp
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
```

- [ ] **Step 5: Add the public skeleton**

`src/AnoiMessagingCenter/MessagingCenter.cs`:

```csharp
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

    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source)
        => throw new NotImplementedException();

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source)
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source)
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source)
        => throw new NotImplementedException();

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
```

- [ ] **Step 6: Add test infrastructure**

`tests/AnoiMessagingCenter.Tests/AssemblyInfo.cs`:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

`tests/AnoiMessagingCenter.Tests/TestBase.cs`:

```csharp
namespace AnoiMessagingCenter.Tests;

public abstract class TestBase : IDisposable
{
    protected TestBase() => MessagingCenter.ClearSubscribers();

    public void Dispose() => MessagingCenter.ClearSubscribers();
}
```

- [ ] **Step 7: Write the failing-or-passing shape test**

`tests/AnoiMessagingCenter.Tests/ApiShapeTests.cs`:

```csharp
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
```

- [ ] **Step 8: Add compile-time compatibility checks**

`tests/AnoiMessagingCenter.Tests/CompileCompatibility.cs`:

```csharp
namespace AnoiMessagingCenter.Tests;

internal static class CompileCompatibility
{
    private sealed class Sender;

    private sealed class Args;

    public static void ExerciseStaticApi(object subscriber, Sender sender, Args args)
    {
        MessagingCenter.Send(sender, "message");
        MessagingCenter.Send(sender, "message", args);

        MessagingCenter.Subscribe<Sender>(subscriber, "message", static _ => { });
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { });
        MessagingCenter.Subscribe<Sender>(subscriber, "message", static _ => { }, sender);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { }, sender);

        MessagingCenter.SubscribeAsync<Sender>(subscriber, "message", static s => Task.CompletedTask);
        MessagingCenter.SubscribeAsync<Sender, Args>(subscriber, "message", static (s, a) => Task.CompletedTask);

        MessagingCenter.Unsubscribe<Sender>(subscriber, "message");
        MessagingCenter.Unsubscribe<Sender, Args>(subscriber, "message");

        MessagingCenter.Send<Sender, Args>(sender: sender, message: "message", args: args);
        MessagingCenter.Subscribe<Sender, Args>(
            subscriber: subscriber, message: "message", callback: static (_, _) => { }, source: sender);
        MessagingCenter.SubscribeAsync<Sender, Args>(
            subscriber: subscriber, message: "message", asyncCallback: static (_, _) => Task.CompletedTask, source: sender);
        MessagingCenter.Unsubscribe<Sender, Args>(subscriber: subscriber, message: "message");
    }

    public static void ExerciseInstanceApi(IMessagingCenter center, object subscriber, Sender sender, Args args)
    {
        center.Send(sender, "message");
        center.Send(sender, "message", args);

        center.Subscribe<Sender>(subscriber, "message", static _ => { });
        center.Subscribe<Sender, Args>(subscriber, "message", static (_, _) => { });
        center.SubscribeAsync<Sender>(subscriber, "message", static s => Task.CompletedTask);
        center.SubscribeAsync<Sender, Args>(subscriber, "message", static (s, a) => Task.CompletedTask);

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
```

- [ ] **Step 9: Build and test the skeleton**

Run:

```bash
dotnet build AnoiMessagingCenter.sln -c Release
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --no-build
```

Expected: build succeeds, 2 API-shape tests pass. The skeleton throws only when exercised; no behavioral tests exist yet.

- [ ] **Step 10: Commit**

```bash
git add .gitignore Directory.Build.props README.md LICENSE AnoiMessagingCenter.sln src tests
git commit -m "chore: scaffold AnoiMessagingCenter solution and public API"
```

---

### Task 2: Synchronous messaging core with struct keys and copy-on-write arrays

**Files:**
- Create: `src/AnoiMessagingCenter/Internal/MessageKey.cs`
- Create: `src/AnoiMessagingCenter/Internal/Subscription.cs`
- Modify: `src/AnoiMessagingCenter/MessagingCenter.cs`
- Test: `tests/AnoiMessagingCenter.Tests/MessagingCenterTests.cs`

**Interfaces:**
- Consumes: `IMessagingCenter`, `TestBase`, `ClearSubscribers()`.
- Produces: `MessageKey`, `Subscription`, `ISubscription<TSender>`, `ISubscription<TSender,TArgs>`, `SyncSubscription<*>`; working 2-parameter and 1-parameter `Send`/`Subscribe`/`Unsubscribe` including `source` filtering; async members still throw.

- [ ] **Step 1: Write the failing sync tests**

`tests/AnoiMessagingCenter.Tests/MessagingCenterTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~MessagingCenterTests"
```

Expected: FAIL with `System.NotImplementedException` from the skeleton.

- [ ] **Step 3: Add the struct message key**

`src/AnoiMessagingCenter/Internal/MessageKey.cs`:

```csharp
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
```

- [ ] **Step 4: Add the subscription types**

`src/AnoiMessagingCenter/Internal/Subscription.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace AnoiMessagingCenter.Internal;

internal interface ISubscription<in TSender>
{
    void Invoke(TSender sender);
}

internal interface ISubscription<in TSender, in TArgs>
{
    void Invoke(TSender sender, TArgs args);
}

internal abstract class Subscription
{
    private readonly WeakReference<object> _subscriber;
    private int _removed;

    protected Subscription(object subscriber, string message, object? source)
    {
        _subscriber = new WeakReference<object>(subscriber);
        Message = message;
        Source = source;
    }

    internal string Message { get; }

    internal object? Source { get; }

    internal bool IsRemoved => Volatile.Read(ref _removed) != 0;

    internal bool IsSubscriberAlive => _subscriber.TryGetTarget(out _);

    internal bool SubscriberIs(object subscriber)
        => _subscriber.TryGetTarget(out var target) && ReferenceEquals(target, subscriber);

    internal bool TryGetSubscriber([NotNullWhen(true)] out object? subscriber)
        => _subscriber.TryGetTarget(out subscriber);

    internal void MarkRemoved() => Volatile.Write(ref _removed, 1);

    internal bool CanInvoke(object sender)
    {
        if (IsRemoved)
            return false;

        if (!_subscriber.TryGetTarget(out _))
            return false;

        if (Source is not null && !sender.Equals(Source))
            return false;

        return true;
    }
}

internal sealed class SyncSubscription<TSender, TArgs> : Subscription, ISubscription<TSender, TArgs>
{
    private readonly Action<TSender, TArgs> _callback;

    internal SyncSubscription(object subscriber, string message, object? source, Action<TSender, TArgs> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender, TArgs args) => _callback(sender, args);
}

internal sealed class SyncSubscription<TSender> : Subscription, ISubscription<TSender>
{
    private readonly Action<TSender> _callback;

    internal SyncSubscription(object subscriber, string message, object? source, Action<TSender> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender) => _callback(sender);
}
```

- [ ] **Step 5: Implement the synchronous core**

Replace `src/AnoiMessagingCenter/MessagingCenter.cs` with:

```csharp
using AnoiMessagingCenter.Internal;

namespace AnoiMessagingCenter;

/// <summary>Default messaging center. The static members operate on <see cref="Instance"/>.</summary>
public class MessagingCenter : IMessagingCenter
{
    private static readonly Lazy<MessagingCenter> _instance = new(() => new MessagingCenter());

    private readonly Lock _sync = new();
    private readonly Dictionary<MessageKey, Subscription[]> _subscriptions = new();

    /// <summary>Gets the shared lazy singleton used by the static members.</summary>
    public static IMessagingCenter Instance => _instance.Value;

    /// <summary>
    /// Raised when a synchronous or asynchronous subscriber callback throws. Handler exceptions are ignored.
    /// </summary>
    public static event Action<Exception, string>? CallbackFailed;

    private static MessagingCenter Default => _instance.Value;

    /// <summary>Sends a message with arguments to subscribers registered for the exact sender and argument types.</summary>
    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
        => Default.SendCore(sender, message, args);

    /// <summary>Sends a message without arguments.</summary>
    public static void Send<TSender>(TSender sender, string message) where TSender : class
        => Default.SendCore(sender, message);

    /// <summary>Subscribes a callback that receives the sender and arguments.</summary>
    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source = null)
        where TSender : class
        => Default.SubscribeCore(subscriber, message, callback, source);

    /// <summary>Subscribes a callback that receives only the sender.</summary>
    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source = null)
        where TSender : class
        => Default.SubscribeCore(subscriber, message, callback, source);

    /// <summary>Subscribes an asynchronous callback that receives the sender and arguments.</summary>
    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <summary>Subscribes an asynchronous callback that receives only the sender.</summary>
    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source = null)
        where TSender : class
        => throw new NotImplementedException();

    /// <summary>Removes all subscriptions for the subscriber, message, and exact sender/argument types.</summary>
    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class
        => Default.UnsubscribeCore<TSender, TArgs>(subscriber, message);

    /// <summary>Removes all single-parameter subscriptions for the subscriber and message.</summary>
    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class
        => Default.UnsubscribeCore<TSender>(subscriber, message);

    void IMessagingCenter.Send<TSender, TArgs>(TSender sender, string message, TArgs args)
        => SendCore(sender, message, args);

    void IMessagingCenter.Send<TSender>(TSender sender, string message)
        => SendCore(sender, message);

    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source)
        => SubscribeCore(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source)
        => SubscribeCore(subscriber, message, callback, source);

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source)
        => throw new NotImplementedException();

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source)
        => throw new NotImplementedException();

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => UnsubscribeCore<TSender, TArgs>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => UnsubscribeCore<TSender>(subscriber, message);

    internal static void ClearSubscribers() => Default.ClearAll();

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

    private void SendCore<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        Subscription[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var subscription in snapshot)
        {
            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender, TArgs> typed)
                typed.Invoke(sender, args);
        }
    }

    private void SendCore<TSender>(TSender sender, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        Subscription[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var subscription in snapshot)
        {
            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender> typed)
                typed.Invoke(sender);
        }
    }

    private void SubscribeCore<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        var subscription = new SyncSubscription<TSender, TArgs>(subscriber, message, source, callback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
        }
    }

    private void SubscribeCore<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        var subscription = new SyncSubscription<TSender>(subscriber, message, source, callback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
        }
    }

    private void UnsubscribeCore<TSender, TArgs>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            var kept = new List<Subscription>(subscriptions.Length);

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsRemoved)
                    continue;

                if (subscription.SubscriberIs(subscriber))
                {
                    subscription.MarkRemoved();
                    continue;
                }

                kept.Add(subscription);
            }

            if (kept.Count == subscriptions.Length)
                return;

            if (kept.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept.ToArray();
        }
    }

    private void UnsubscribeCore<TSender>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            var kept = new List<Subscription>(subscriptions.Length);

            foreach (var subscription in subscriptions)
            {
                if (subscription.IsRemoved)
                    continue;

                if (subscription.SubscriberIs(subscriber))
                {
                    subscription.MarkRemoved();
                    continue;
                }

                kept.Add(subscription);
            }

            if (kept.Count == subscriptions.Length)
                return;

            if (kept.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept.ToArray();
        }
    }

    private void ClearAll()
    {
        lock (_sync)
        {
            _subscriptions.Clear();
        }
    }

    private static Subscription[] Append(Subscription[]? existing, Subscription subscription)
    {
        if (existing is null || existing.Length == 0)
            return new[] { subscription };

        var result = new Subscription[existing.Length + 1];
        Array.Copy(existing, result, existing.Length);
        result[^1] = subscription;
        return result;
    }
}
```

- [ ] **Step 6: Run the sync tests**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~MessagingCenterTests"
```

Expected: PASS (12 tests).

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat: add synchronous messaging core with struct keys and COW storage"
```

---

### Task 3: Async subscriptions

**Files:**
- Modify: `src/AnoiMessagingCenter/Internal/Subscription.cs`
- Modify: `src/AnoiMessagingCenter/MessagingCenter.cs`
- Test: `tests/AnoiMessagingCenter.Tests/AsyncTests.cs`

**Interfaces:**
- Consumes: `Subscription`, `ISubscription<*>`.
- Produces: `AsyncSubscription<TSender>`, `AsyncSubscription<TSender,TArgs>`, `SubscribeAsyncCore<*>`; fire-and-forget delivery that captures the caller's `SynchronizationContext`.

- [ ] **Step 1: Write the failing async tests**

`tests/AnoiMessagingCenter.Tests/AsyncTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~AsyncTests"
```

Expected: FAIL with `System.NotImplementedException`.

- [ ] **Step 3: Add async subscription types**

Append to `src/AnoiMessagingCenter/Internal/Subscription.cs`:

```csharp
internal sealed class AsyncSubscription<TSender, TArgs> : Subscription, ISubscription<TSender, TArgs>
{
    private readonly Func<TSender, TArgs, Task> _callback;

    internal AsyncSubscription(object subscriber, string message, object? source, Func<TSender, TArgs, Task> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender, TArgs args) => _ = InvokeAsync(sender, args);

    private async Task InvokeAsync(TSender sender, TArgs args)
    {
        await _callback(sender, args);
    }
}

internal sealed class AsyncSubscription<TSender> : Subscription, ISubscription<TSender>
{
    private readonly Func<TSender, Task> _callback;

    internal AsyncSubscription(object subscriber, string message, object? source, Func<TSender, Task> callback)
        : base(subscriber, message, source)
        => _callback = callback;

    public void Invoke(TSender sender) => _ = InvokeAsync(sender);

    private async Task InvokeAsync(TSender sender)
    {
        await _callback(sender);
    }
}
```

Do not use `ConfigureAwait(false)`; preserving the captured context is part of the contract.

- [ ] **Step 4: Wire `SubscribeAsync` in the center**

In `src/AnoiMessagingCenter/MessagingCenter.cs`:

Replace the two static `SubscribeAsync` bodies (currently `throw new NotImplementedException();`) with:

```csharp
        => Default.SubscribeAsyncCore(subscriber, message, asyncCallback, source);
```

Replace the two explicit interface `SubscribeAsync` bodies with:

```csharp
        => SubscribeAsyncCore(subscriber, message, asyncCallback, source);
```

Add these private methods before `ClearAll()`:

```csharp
    private void SubscribeAsyncCore<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(asyncCallback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        var subscription = new AsyncSubscription<TSender, TArgs>(subscriber, message, source, asyncCallback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
        }
    }

    private void SubscribeAsyncCore<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender? source)
        where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(asyncCallback);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        var subscription = new AsyncSubscription<TSender>(subscriber, message, source, asyncCallback);

        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
        }
    }
```

- [ ] **Step 5: Run the async tests**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~AsyncTests"
```

Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add fire-and-forget async subscriptions"
```

---

### Task 4: Error containment and `CallbackFailed`

**Files:**
- Modify: `src/AnoiMessagingCenter/Internal/Subscription.cs`
- Test: `tests/AnoiMessagingCenter.Tests/ErrorHandlingTests.cs`

**Interfaces:**
- Consumes: `MessagingCenter.RaiseCallbackFailed`, sync and async subscriptions.
- Produces: guaranteed exception containment; `CallbackFailed` raised with the callback exception and message.

- [ ] **Step 1: Write the failing tests**

`tests/AnoiMessagingCenter.Tests/ErrorHandlingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~ErrorHandlingTests"
```

Expected: FAIL — `SyncCallbackException...` and `OneThrowingCallback...` fail because the callback exception escapes `Send`; async fails because no hook is raised.

- [ ] **Step 3: Catch and report in every subscription type**

In `src/AnoiMessagingCenter/Internal/Subscription.cs`, change `SyncSubscription<TSender,TArgs>.Invoke` to:

```csharp
    public void Invoke(TSender sender, TArgs args)
    {
        try
        {
            _callback(sender, args);
        }
        catch (Exception exception)
        {
            MessagingCenter.RaiseCallbackFailed(exception, Message);
        }
    }
```

Change `SyncSubscription<TSender>.Invoke` to:

```csharp
    public void Invoke(TSender sender)
    {
        try
        {
            _callback(sender);
        }
        catch (Exception exception)
        {
            MessagingCenter.RaiseCallbackFailed(exception, Message);
        }
    }
```

Change both async `InvokeAsync` methods to catch:

```csharp
    private async Task InvokeAsync(TSender sender, TArgs args)
    {
        try
        {
            await _callback(sender, args);
        }
        catch (Exception exception)
        {
            MessagingCenter.RaiseCallbackFailed(exception, Message);
        }
    }
```

```csharp
    private async Task InvokeAsync(TSender sender)
    {
        try
        {
            await _callback(sender);
        }
        catch (Exception exception)
        {
            MessagingCenter.RaiseCallbackFailed(exception, Message);
        }
    }
```

- [ ] **Step 4: Run the error tests**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~ErrorHandlingTests"
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: contain callback exceptions and add CallbackFailed hook"
```

---

### Task 5: Weak subscription lifetime with `ConditionalWeakTable` anchoring

**Files:**
- Modify: `src/AnoiMessagingCenter/MessagingCenter.cs`
- Test: `tests/AnoiMessagingCenter.Tests/LifetimeTests.cs`

**Interfaces:**
- Consumes: sync/async subscription types, `Subscription.SubscriberIs`, `TryGetSubscriber`.
- Produces: center storage `Dictionary<MessageKey, WeakReference<Subscription>[]>`, anchor table, `DetachAnchor`, and release of subscriptions when subscribers die.

- [ ] **Step 1: Write the failing lifetime tests**

`tests/AnoiMessagingCenter.Tests/LifetimeTests.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace AnoiMessagingCenter.Tests;

public class LifetimeTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    private sealed class Counter
    {
        public int Value;
    }

    private sealed class LifetimeSubscriber
    {
        private readonly Counter _counter;

        public LifetimeSubscriber(Counter counter) => _counter = counter;

        public void OnMessage(Sender sender, Args args) => _counter.Value++;
    }

    private sealed class CallbackTarget
    {
        private readonly Counter _counter;

        public CallbackTarget(Counter counter) => _counter = counter;

        public void OnMessage(Sender sender, Args args) => _counter.Value++;
    }

    [Fact]
    public void Subscriber_IsNotKeptAliveBySubscription()
    {
        var subscriber = CreateSubscriber(new Counter());

        AssertCollected(subscriber);
    }

    [Fact]
    public void CollectedSubscriber_NoLongerReceivesMessages()
    {
        var counter = new Counter();
        var subscriber = CreateSubscriber(counter);

        AssertCollected(subscriber);

        MessagingCenter.Send(new Sender(), "lifetime", new Args());

        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public void ExternalCallbackTarget_IsReleasedAfterSubscriberIsCollected()
    {
        var (subscriber, target) = CreateSubscriberWithExternalTarget(new Counter());

        AssertCollected(subscriber);
        AssertCollected(target);
    }

    [Fact]
    public void ExternalCallbackTarget_StaysAliveWhileSubscriberLives()
    {
        var subscriber = new LifetimeSubscriber(new Counter());
        var target = CreateExternalTarget(subscriber, new Counter());

        ForceGc();

        Assert.True(target.IsAlive);
        GC.KeepAlive(subscriber);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateSubscriber(Counter counter)
    {
        var subscriber = new LifetimeSubscriber(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime", subscriber.OnMessage);
        return new WeakReference(subscriber);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Subscriber, WeakReference Target) CreateSubscriberWithExternalTarget(Counter counter)
    {
        var subscriber = new LifetimeSubscriber(counter);
        var target = new CallbackTarget(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime", target.OnMessage);
        return (new WeakReference(subscriber), new WeakReference(target));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateExternalTarget(LifetimeSubscriber subscriber, Counter counter)
    {
        var target = new CallbackTarget(counter);
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "lifetime-target", target.OnMessage);
        return new WeakReference(target);
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
            ForceGc();

        Assert.False(reference.IsAlive);
    }

    private static void ForceGc()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~LifetimeTests"
```

Expected: FAIL. The strong `Subscription[]` keeps the delegate (and therefore the subscriber) alive, so `Subscriber_IsNotKeptAliveBySubscription` and `ExternalCallbackTarget_IsReleasedAfterSubscriberIsCollected` fail.

- [ ] **Step 3: Switch storage to weak references and add anchors**

Add `using System.Runtime.CompilerServices;` at the top of `src/AnoiMessagingCenter/MessagingCenter.cs` (needed by `ConditionalWeakTable`).

Replace the fields in `src/AnoiMessagingCenter/MessagingCenter.cs`:

```csharp
    private readonly Lock _sync = new();
    private readonly Dictionary<MessageKey, WeakReference<Subscription>[]> _subscriptions = new();
    private readonly ConditionalWeakTable<object, List<Subscription>> _anchors = new();
```

Replace `SendCore<TSender, TArgs>` and `SendCore<TSender>` with:

```csharp
    private void SendCore<TSender, TArgs>(TSender sender, string message, TArgs args) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));
        WeakReference<Subscription>[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var weakSubscription in snapshot)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
                continue;

            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender, TArgs> typed)
                typed.Invoke(sender, args);
        }
    }

    private void SendCore<TSender>(TSender sender, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);
        WeakReference<Subscription>[] snapshot;

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current))
                return;

            snapshot = current;
        }

        foreach (var weakSubscription in snapshot)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
                continue;

            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender> typed)
                typed.Invoke(sender);
        }
    }
```

In both `SubscribeCore` methods and both `SubscribeAsyncCore` methods, replace the lock body:

```csharp
        lock (_sync)
        {
            _subscriptions[key] = Append(_subscriptions.GetValueOrDefault(key), subscription);
            _anchors.GetOrCreateValue(subscriber).Add(subscription);
        }
```

Replace both `UnsubscribeCore` bodies with:

```csharp
    private void UnsubscribeCore<TSender, TArgs>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), typeof(TArgs));

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            if (!TryRemoveSubscriptions(subscriptions, subscriber, out var kept))
                return;

            if (kept.Length == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept;
        }
    }

    private void UnsubscribeCore<TSender>(object subscriber, string message) where TSender : class
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(message);

        var key = new MessageKey(message, typeof(TSender), null);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subscriptions))
                return;

            if (!TryRemoveSubscriptions(subscriptions, subscriber, out var kept))
                return;

            if (kept.Length == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = kept;
        }
    }

    private bool TryRemoveSubscriptions(
        WeakReference<Subscription>[] subscriptions, object subscriber, out WeakReference<Subscription>[] kept)
    {
        var survivors = new List<WeakReference<Subscription>>(subscriptions.Length);
        List<Subscription>? removed = null;
        var changed = false;

        foreach (var weakSubscription in subscriptions)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
            {
                changed = true;
                continue;
            }

            if (subscription.IsRemoved)
            {
                changed = true;
                continue;
            }

            if (subscription.SubscriberIs(subscriber))
            {
                subscription.MarkRemoved();
                (removed ??= new List<Subscription>()).Add(subscription);
                changed = true;
                continue;
            }

            survivors.Add(weakSubscription);
        }

        if (removed is not null)
        {
            foreach (var subscription in removed)
                DetachAnchor(subscription);
        }

        kept = survivors.ToArray();
        return changed;
    }

    private void DetachAnchor(Subscription subscription)
    {
        if (!subscription.TryGetSubscriber(out var owner))
            return;

        if (!_anchors.TryGetValue(owner, out var anchored))
            return;

        anchored.Remove(subscription);

        if (anchored.Count == 0)
            _anchors.Remove(owner);
    }
```

Replace `ClearAll` and `Append` with:

```csharp
    private void ClearAll()
    {
        lock (_sync)
        {
            _subscriptions.Clear();
            _anchors.Clear();
        }
    }

    private static WeakReference<Subscription>[] Append(
        WeakReference<Subscription>[]? existing, Subscription subscription)
    {
        var reference = new WeakReference<Subscription>(subscription);

        if (existing is null || existing.Length == 0)
            return new[] { reference };

        var result = new WeakReference<Subscription>[existing.Length + 1];
        Array.Copy(existing, result, existing.Length);
        result[^1] = reference;
        return result;
    }
```

- [ ] **Step 4: Run lifetime and full tests**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~LifetimeTests"
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release
```

Expected: lifetime tests PASS; all earlier tests still PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: anchor weak subscriptions to subscriber lifetime"
```

---

### Task 6: Dead-entry pruning, `ClearSubscribers`, and concurrency

**Files:**
- Modify: `src/AnoiMessagingCenter/MessagingCenter.cs`
- Test: `tests/AnoiMessagingCenter.Tests/ConcurrencyTests.cs`
- Test: `tests/AnoiMessagingCenter.Tests/MessagingCenterTests.cs` (add pruning/clear tests)

**Interfaces:**
- Consumes: weak array storage and anchors from Task 5.
- Produces: `Prune`, internal `GetSubscriptionCount` test hook, `ClearSubscribers` behavior, re-entrancy safety.

- [ ] **Step 1: Write the failing tests**

Append to `tests/AnoiMessagingCenter.Tests/MessagingCenterTests.cs` (inside the class):

```csharp
    [Fact]
    public void ClearSubscribers_StopsDelivery()
    {
        var calls = 0;
        var subscriber = new object();
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "clear", (_, _) => calls++);

        MessagingCenter.ClearSubscribers();
        MessagingCenter.Send(new Sender(), "clear", new Args());

        Assert.Equal(0, calls);
        GC.KeepAlive(subscriber);
    }

    [Fact]
    public void DeadSubscriptions_ArePrunedDuringSend()
    {
        var subscriber = CreateDeadSubscriber("prune");
        AssertCollected(subscriber);

        MessagingCenter.Send(new Sender(), "prune", new Args());

        Assert.Equal(0, MessagingCenter.GetSubscriptionCount("prune", typeof(Sender), typeof(Args)));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDeadSubscriber(string message)
    {
        var subscriber = new object();
        MessagingCenter.Subscribe<Sender, Args>(subscriber, message, static (_, _) => { });
        return new WeakReference(subscriber);
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (var attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(reference.IsAlive);
    }
```

`tests/AnoiMessagingCenter.Tests/ConcurrencyTests.cs`:

```csharp
using System.Collections.Concurrent;

namespace AnoiMessagingCenter.Tests;

public class ConcurrencyTests : TestBase
{
    private sealed class Sender;

    private sealed class Args;

    private sealed class Subscriber
    {
        public void OnMessage(Sender sender, Args args)
        {
        }
    }

    [Fact]
    public void ConcurrentSubscribeSendUnsubscribe_DoesNotThrow()
    {
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 2_000, i =>
        {
            try
            {
                var subscriber = new Subscriber();
                var message = "concurrent-" + (i % 8);

                MessagingCenter.Subscribe<Sender, Args>(subscriber, message, subscriber.OnMessage);
                MessagingCenter.Send(new Sender(), message, new Args());
                MessagingCenter.Unsubscribe<Sender, Args>(subscriber, message);
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void CallbackCanSendAndSubscribe()
    {
        var nestedCalls = 0;
        var subscriber = new object();

        MessagingCenter.Subscribe<Sender, Args>(subscriber, "outer", (_, _) =>
        {
            MessagingCenter.Send(new Sender(), "inner", new Args());
        });
        MessagingCenter.Subscribe<Sender, Args>(subscriber, "inner", (_, _) => nestedCalls++);

        MessagingCenter.Send(new Sender(), "outer", new Args());

        Assert.Equal(1, nestedCalls);
        GC.KeepAlive(subscriber);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~ConcurrencyTests|FullyQualifiedName~DeadSubscriptions_ArePrunedDuringSend"
```

Expected: FAIL to compile — `GetSubscriptionCount` does not exist.

- [ ] **Step 3: Add the test hook and pruning**

Add to `src/AnoiMessagingCenter/MessagingCenter.cs` near `ClearSubscribers`:

```csharp
    internal static int GetSubscriptionCount(string message, Type senderType, Type? argType)
        => Default.CountSubscriptions(new MessageKey(message, senderType, argType));
```

Add near `ClearAll`:

```csharp
    private int CountSubscriptions(MessageKey key)
    {
        lock (_sync)
        {
            return _subscriptions.TryGetValue(key, out var subscriptions) ? subscriptions.Length : 0;
        }
    }

    private void Prune(MessageKey key, WeakReference<Subscription>[] observed)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var current) || !ReferenceEquals(current, observed))
                return;

            var survivors = new List<WeakReference<Subscription>>(current.Length);
            var changed = false;

            foreach (var weakSubscription in current)
            {
                if (!weakSubscription.TryGetTarget(out var subscription))
                {
                    changed = true;
                    continue;
                }

                if (subscription.IsRemoved)
                {
                    changed = true;
                    continue;
                }

                if (!subscription.IsSubscriberAlive)
                {
                    subscription.MarkRemoved();
                    changed = true;
                    continue;
                }

                survivors.Add(weakSubscription);
            }

            if (!changed)
                return;

            if (survivors.Count == 0)
                _subscriptions.Remove(key);
            else
                _subscriptions[key] = survivors.ToArray();
        }
    }
```

In both `SendCore` methods, add a `stale` flag. Replace the loop and exit with:

```csharp
        var stale = false;

        foreach (var weakSubscription in snapshot)
        {
            if (!weakSubscription.TryGetTarget(out var subscription))
            {
                stale = true;
                continue;
            }

            if (subscription.IsRemoved)
            {
                stale = true;
                continue;
            }

            if (!subscription.IsSubscriberAlive)
            {
                stale = true;
                continue;
            }

            if (!subscription.CanInvoke(sender))
                continue;

            if (subscription is ISubscription<TSender, TArgs> typed)
                typed.Invoke(sender, args);
        }

        if (stale)
            Prune(key, snapshot);
```

For the single-parameter overload the cast line is `ISubscription<TSender> typed` / `typed.Invoke(sender)`.

- [ ] **Step 4: Run the pruning and concurrency tests**

Run:

```bash
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release --filter "FullyQualifiedName~ConcurrencyTests|FullyQualifiedName~DeadSubscriptions_ArePrunedDuringSend|FullyQualifiedName~ClearSubscribers_StopsDelivery"
dotnet test tests/AnoiMessagingCenter.Tests/AnoiMessagingCenter.Tests.csproj -c Release
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: prune dead subscriptions and verify concurrent access"
```

---

### Task 7: Benchmarks and measured evidence

**Files:**
- Create: `benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj`
- Create: `benchmarks/AnoiMessagingCenter.Benchmarks/Program.cs`
- Create: `benchmarks/AnoiMessagingCenter.Benchmarks/BaselineMessagingCenter.cs`
- Create: `benchmarks/AnoiMessagingCenter.Benchmarks/MessagingBenchmarks.cs`
- Create: `benchmarks/results.md` (generated output)
- Modify: `AnoiMessagingCenter.sln`

**Interfaces:**
- Consumes: the finished library and the original implementation as a baseline.
- Produces: allocation and timing evidence for `Send`, struct args, and subscribe/unsubscribe.

- [ ] **Step 1: Create the benchmark project**

Run:

```bash
dotnet new console -n AnoiMessagingCenter.Benchmarks -o benchmarks/AnoiMessagingCenter.Benchmarks -f net10.0
dotnet sln add benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj
dotnet add benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj reference src/AnoiMessagingCenter/AnoiMessagingCenter.csproj
```

`benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AnoiMessagingCenter\AnoiMessagingCenter.csproj" />
  </ItemGroup>
</Project>
```

Then add BenchmarkDotNet; this writes a pinned version into the project file:

```bash
dotnet add benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj package BenchmarkDotNet
```

- [ ] **Step 2: Add the baseline implementation**

`benchmarks/AnoiMessagingCenter.Benchmarks/BaselineMessagingCenter.cs` — a faithful copy of the original, with Serilog replaced by an empty catch, under a benchmark-only namespace:

```csharp
#nullable disable
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AnoiMessagingCenter.Benchmarks.Baseline;

public sealed class BaselineMessagingCenter : IMessagingCenter
{
    private static readonly Lazy<IMessagingCenter> _instance = new(() => new BaselineMessagingCenter());
    public static IMessagingCenter Instance => _instance.Value;

    private readonly Lock _sync = new();
    private readonly Dictionary<Sender, List<Subscription>> _subscriptions = new();

    private sealed class Sender : Tuple<string, Type, Type>
    {
        public Sender(string message, Type senderType, Type argType)
            : base(message, senderType, argType) { }
    }

    private delegate bool Filter(object sender);

    private sealed class MaybeWeakReference
    {
        private readonly WeakReference _delegateWeakReference;
        private readonly object _delegateStrongReference;
        private readonly bool _isStrongReference;

        public MaybeWeakReference(object subscriber, object delegateSource)
        {
            if (subscriber.Equals(delegateSource))
            {
                _delegateWeakReference = new WeakReference(delegateSource);
                _isStrongReference = false;
            }
            else
            {
                _delegateStrongReference = delegateSource;
                _isStrongReference = true;
            }
        }

        public object Target => _isStrongReference ? _delegateStrongReference : _delegateWeakReference.Target;
        public bool IsAlive => _isStrongReference || _delegateWeakReference.IsAlive;
    }

    private sealed class Subscription : Tuple<WeakReference, MaybeWeakReference, MethodInfo, Filter>
    {
        public Subscription(object subscriber, object delegateSource, MethodInfo methodInfo, Filter filter)
            : base(new WeakReference(subscriber), new MaybeWeakReference(subscriber, delegateSource), methodInfo, filter) { }

        public WeakReference Subscriber => Item1;
        private MaybeWeakReference DelegateSource => Item2;
        private MethodInfo MethodInfo => Item3;
        private Filter Filter => Item4;

        public void InvokeCallback(object sender, object args)
        {
            if (!Filter(sender))
                return;

            var target = DelegateSource.Target;
            if (target == null)
                return;

            try
            {
                MethodInfo.Invoke(
                    MethodInfo.IsStatic ? null : target,
                    MethodInfo.GetParameters().Length == 1 ? new[] { sender } : new[] { sender, args });
            }
            catch
            {
                // Serilog removed for benchmark isolation.
            }
        }

        public bool CanBeRemoved() => !Subscriber.IsAlive || !DelegateSource.IsAlive;
    }

    public static void Send<TSender, TArgs>(TSender sender, string message, TArgs args)
        where TSender : class
        => Instance.Send(sender, message, args);

    void IMessagingCenter.Send<TSender, TArgs>(TSender sender, string message, TArgs args)
    {
        if (sender == null)
            throw new ArgumentNullException(nameof(sender));
        InnerSend(message, typeof(TSender), typeof(TArgs), sender, args);
    }

    public static void Send<TSender>(TSender sender, string message) where TSender : class
        => Instance.Send(sender, message);

    void IMessagingCenter.Send<TSender>(TSender sender, string message)
    {
        if (sender == null)
            throw new ArgumentNullException(nameof(sender));
        InnerSend(message, typeof(TSender), null, sender, null);
    }

    public static void Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source = null)
        where TSender : class
        => Instance.Subscribe(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender, TArgs>(
        object subscriber, string message, Action<TSender, TArgs> callback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        var target = callback.Target;
        Filter filter = sender => source == null || sender.Equals(source);
        InnerSubscribe(subscriber, message, typeof(TSender), typeof(TArgs), target, callback.GetMethodInfo(), filter);
    }

    public static void Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source = null)
        where TSender : class
        => Instance.Subscribe(subscriber, message, callback, source);

    void IMessagingCenter.Subscribe<TSender>(
        object subscriber, string message, Action<TSender> callback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        var target = callback.Target;
        Filter filter = sender => source == null || sender.Equals(source);
        InnerSubscribe(subscriber, message, typeof(TSender), null, target, callback.GetMethodInfo(), filter);
    }

    public static void SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source = null)
        where TSender : class
        => Instance.SubscribeAsync(subscriber, message, asyncCallback, source);

    void IMessagingCenter.SubscribeAsync<TSender, TArgs>(
        object subscriber, string message, Func<TSender, TArgs, Task> asyncCallback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (asyncCallback == null)
            throw new ArgumentNullException(nameof(asyncCallback));

        Action<TSender, TArgs> callback = async (s, a) =>
        {
            try
            {
                await asyncCallback(s, a);
            }
            catch
            {
            }
        };

        ((IMessagingCenter)this).Subscribe(subscriber, message, callback, source);
    }

    public static void SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source = null)
        where TSender : class
        => Instance.SubscribeAsync(subscriber, message, asyncCallback, source);

    void IMessagingCenter.SubscribeAsync<TSender>(
        object subscriber, string message, Func<TSender, Task> asyncCallback, TSender source)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (asyncCallback == null)
            throw new ArgumentNullException(nameof(asyncCallback));

        Action<TSender> callback = async s =>
        {
            try
            {
                await asyncCallback(s);
            }
            catch
            {
            }
        };

        ((IMessagingCenter)this).Subscribe(subscriber, message, callback, source);
    }

    public static void Unsubscribe<TSender, TArgs>(object subscriber, string message) where TSender : class
        => Instance.Unsubscribe<TSender, TArgs>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender, TArgs>(object subscriber, string message)
        => InnerUnsubscribe(message, typeof(TSender), typeof(TArgs), subscriber);

    public static void Unsubscribe<TSender>(object subscriber, string message) where TSender : class
        => Instance.Unsubscribe<TSender>(subscriber, message);

    void IMessagingCenter.Unsubscribe<TSender>(object subscriber, string message)
        => InnerUnsubscribe(message, typeof(TSender), null, subscriber);

    private void InnerSend(string message, Type senderType, Type argType, object sender, object args)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        List<Subscription> subscriptionsCopy;
        var key = new Sender(message, senderType, argType);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var subList) || subList == null || subList.Count == 0)
                return;
            subscriptionsCopy = subList.ToList();
        }

        foreach (var subscription in subscriptionsCopy)
        {
            bool isValidSubscription;
            lock (_sync)
            {
                if (!_subscriptions.ContainsKey(key))
                    continue;

                isValidSubscription = subscription.Subscriber.Target != null && _subscriptions[key].Contains(subscription);
            }

            if (isValidSubscription)
                subscription.InvokeCallback(sender, args);
        }
    }

    private void InnerSubscribe(
        object subscriber, string message, Type senderType, Type argType, object target, MethodInfo methodInfo, Filter filter)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var key = new Sender(message, senderType, argType);
        var value = new Subscription(subscriber, target, methodInfo, filter);

        lock (_sync)
        {
            if (_subscriptions.TryGetValue(key, out var existing))
                existing.Add(value);
            else
                _subscriptions[key] = new List<Subscription> { value };
        }
    }

    private void InnerUnsubscribe(string message, Type senderType, Type argType, object subscriber)
    {
        if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var key = new Sender(message, senderType, argType);

        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(key, out var existing))
                return;

            existing.RemoveAll(s => s.CanBeRemoved() || s.Subscriber.Target == subscriber);
            if (existing.Count == 0)
                _subscriptions.Remove(key);
        }
    }
}
```

- [ ] **Step 3: Add the benchmark scenarios**

`benchmarks/AnoiMessagingCenter.Benchmarks/MessagingBenchmarks.cs`:

```csharp
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
            BaselineMessagingCenter.Subscribe<Sender, Payload>(subscriber, _baselineMessage, subscriber.OnMessage, null);
            MessagingCenter.Subscribe<Sender, Payload>(subscriber, _newMessage, subscriber.OnMessage, null);
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
        for (var i = 0; i < 1_000; i++)
            BaselineMessagingCenter.Send(SharedSender, _baselineMessage, SharedPayload);
    }

    [Benchmark]
    public void New_Send()
    {
        for (var i = 0; i < 1_000; i++)
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
        BaselineMessagingCenter.Subscribe<Sender, StructPayload>(_subscriber, _baselineMessage, _subscriber.OnMessage, null);
        MessagingCenter.Subscribe<Sender, StructPayload>(_subscriber, _newMessage, _subscriber.OnMessage, null);
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
        for (var i = 0; i < 1_000; i++)
            BaselineMessagingCenter.Send(SharedSender, _baselineMessage, SharedPayload);
    }

    [Benchmark]
    public void New_SendStruct()
    {
        for (var i = 0; i < 1_000; i++)
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
        for (var i = 0; i < 100; i++)
        {
            var subscriber = new Subscriber();
            BaselineMessagingCenter.Subscribe<Sender, Payload>(subscriber, Message, subscriber.OnMessage, null);
            BaselineMessagingCenter.Unsubscribe<Sender, Payload>(subscriber, Message);
        }
    }

    [Benchmark]
    public void New_SubscribeUnsubscribe()
    {
        for (var i = 0; i < 100; i++)
        {
            var subscriber = new Subscriber();
            MessagingCenter.Subscribe<Sender, Payload>(subscriber, Message, subscriber.OnMessage, null);
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
```

- [ ] **Step 4: Add the entry point**

`benchmarks/AnoiMessagingCenter.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```



- [ ] **Step 5: Build and run a smoke benchmark**

Run:

```bash
dotnet build benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj -c Release
dotnet run --project benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj -c Release -- --filter "*" --job dry
```

Expected: build succeeds; dry run completes without errors.

- [ ] **Step 6: Run the short benchmark and save results**

Run:

```bash
dotnet run --project benchmarks/AnoiMessagingCenter.Benchmarks/AnoiMessagingCenter.Benchmarks.csproj -c Release -- --filter "*" --job short
```

Expected: all benchmark methods report; `New_Send` and `New_SendStruct` show `0 B` allocated and lower mean time than the baseline. Copy the Markdown summary from the console into `benchmarks/results.md` with a heading and date.

- [ ] **Step 7: Commit**

```bash
git add benchmarks AnoiMessagingCenter.sln
git commit -m "perf: add BenchmarkDotNet baseline and record measured results"
```

---

### Task 8: README, packaging, and final verification

**Files:**
- Modify: `README.md`
- Modify: `src/AnoiMessagingCenter/AnoiMessagingCenter.csproj` (only if pack validation requires)
- Test: full suite and pack output

**Interfaces:**
- Consumes: everything.
- Produces: a publishable package and user documentation, verified end to end.

- [ ] **Step 1: Write the README**

Replace `README.md` with:

```markdown
# AnoiMessagingCenter

Allocation-conscious, weak-reference messaging center for .NET 10+. It is a drop-in replacement for the
original `IMessagingCenter`/`MessagingCenter` implementation: same members, same synchronous delivery,
no reflection on the send path, and subscriptions are released when their subscriber is collected.

## Install

```bash
dotnet add package AnoiMessagingCenter
```

## Quick start

```csharp
using AnoiMessagingCenter;

MessagingCenter.Subscribe<MainPage, string>(this, "status", (page, text) =>
{
    page.StatusLabel.Text = text;
});

MessagingCenter.Send(this, "status", "Ready");
```

Unsubscribe when a subscriber's lifetime ends:

```csharp
MessagingCenter.Unsubscribe<MainPage, string>(this, "status");
```

Async callbacks are fire-and-forget, exactly like the original API:

```csharp
MessagingCenter.SubscribeAsync<MainPage, string>(this, "load", async (page, id) =>
{
    var data = await page.LoadAsync(id);
    page.Apply(data);
});
```

## Error handling

Callback exceptions never escape `Send`. To observe them, subscribe to the additive hook:

```csharp
MessagingCenter.CallbackFailed += (exception, message) =>
    Serilog.Log.Error(exception, "Messaging callback failed for {Message}", message);
```

## What changed from the original

- Namespace is `AnoiMessagingCenter`; replace `using APIServiceShared.Shared.Helpers;`.
- Serilog is no longer a dependency; use `CallbackFailed` instead.
- Static-method subscriptions now fire (the original silently dropped them).
- Dead subscriptions are released instead of retained forever.
- `Send` allocates 0 bytes in steady state; reflection is gone.

No other source changes are required.

## Performance

Measured with BenchmarkDotNet on .NET 10 (Release). See [`benchmarks/results.md`](benchmarks/results.md)
for the full run. The hot path performs one dictionary lookup and an array walk; subscribe/unsubscribe
copy a small array. Struct argument payloads stay typed and are not boxed.

## License

MIT.
```

- [ ] **Step 2: Verify the license file**

`LICENSE` was created in Task 1 with the MIT text and is packed by the project. Confirm it exists and is unchanged:

```bash
Get-Content LICENSE | Select-Object -First 3
```

Expected:

```text
MIT License

Copyright (c) 2026 Anoi
```

- [ ] **Step 3: Run the full test suite in Release**

```bash
dotnet test AnoiMessagingCenter.sln -c Release
```

Expected: all tests pass.

- [ ] **Step 4: Pack and inspect the package**

```bash
dotnet pack src/AnoiMessagingCenter/AnoiMessagingCenter.csproj -c Release
```

Expected: `artifacts/AnoiMessagingCenter.1.0.0.nupkg` and `artifacts/AnoiMessagingCenter.1.0.0.snupkg`.

Inspect the package contents:

```bash
tar -tf artifacts/AnoiMessagingCenter.1.0.0.nupkg
```

Expected entries include `lib/net10.0/AnoiMessagingCenter.dll`, `lib/net10.0/AnoiMessagingCenter.xml`, `README.md`, and `LICENSE`.

- [ ] **Step 5: Verify the package installs into a clean consumer**

```bash
dotnet new console -n PackageSmoke -o artifacts/PackageSmoke -f net10.0
dotnet add artifacts/PackageSmoke/PackageSmoke.csproj package AnoiMessagingCenter --source artifacts --version 1.0.0
dotnet build artifacts/PackageSmoke/PackageSmoke.csproj -c Release
```

Expected: restore and build succeed using the local package.

- [ ] **Step 6: Commit**

```bash
git add README.md LICENSE src tests benchmarks
git commit -m "docs: add README, MIT license, and package verification"
```

Do not commit `artifacts/PackageSmoke` or the `.nupkg` files; `.gitignore` excludes `artifacts/`.

---

## Self-Review Checklist (run after the last task)

- [ ] Spec section 4 (public API) → Tasks 1–3 and `ApiShapeTests`.
- [ ] Spec section 4.1 (validation/order/source equality) → Task 2 tests.
- [ ] Spec section 4.2 (`CallbackFailed`) → Task 4 tests.
- [ ] Spec section 4.3 (static callbacks, dead-subscription release) → Task 2 compatibility code, Task 5 lifetime tests.
- [ ] Spec section 5.1–5.2 (MessageKey, COW, lock) → Task 2/5 implementation.
- [ ] Spec section 5.3 (typed subscription hierarchy) → Tasks 2–4.
- [ ] Spec section 5.4 (`ConditionalWeakTable` anchoring) → Task 5.
- [ ] Spec section 5.5 (send outside lock, zero allocation) → Task 2/6 and Task 7 benchmarks.
- [ ] Spec section 5.6–5.8 (subscribe/unsubscribe/prune/clear) → Tasks 2, 5, 6.
- [ ] Spec section 7 (layout/metadata) → Tasks 1, 7, 8.
- [ ] Spec section 8 (tests/benchmarks) → Tasks 1–7.
- [ ] Spec section 10 (acceptance criteria) → Task 8 verification.

No placeholders remain. Public method types and names are consistent across tasks: `SendCore`, `SubscribeCore`,
`SubscribeAsyncCore`, `UnsubscribeCore`, `Append`, `DetachAnchor`, `Prune`, `ClearAll`,
`GetSubscriptionCount`, `RaiseCallbackFailed`.

