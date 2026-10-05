using Centra;
using Centra.Hosting.Inbox;
using Centra.Hosting.Outbox;
using Centra.PubSub.Inbox;
using Centra.PubSub.Outbox;
using Centra.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraStateStoreOutboxInboxExtensionsTests
{
    [Fact]
    public void AddCentraStateStoreOutbox_Registers_StateStoreOutboxStore_With_Default_StoreName()
    {
        var services = new ServiceCollection();
        var mockStateStore = Substitute.For<IStateStore>();
        services.AddSingleton(mockStateStore);
        services.Configure<CentraOptions>(opts => opts.DefaultStateStore = "custom-default-store");

        services.AddCentraStateStoreOutbox();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetService<IOutboxStore>();

        outboxStore.ShouldNotBeNull();
        outboxStore.ShouldBeOfType<StateStoreOutboxStore>();
    }

    [Fact]
    public void AddCentraStateStoreOutbox_Registers_StateStoreOutboxStore_With_Explicit_StoreName()
    {
        var services = new ServiceCollection();
        var mockStateStore = Substitute.For<IStateStore>();
        services.AddSingleton(mockStateStore);
        services.Configure<CentraOptions>(opts => opts.DefaultStateStore = "default-store");

        var configured = false;
        services.AddCentraStateStoreOutbox(stateStoreName: "explicit-store", configure: _ =>
        {
            configured = true;
        });

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetService<IOutboxStore>();
        _ = sp.GetRequiredService<IOptions<OutboxOptions>>().Value;

        outboxStore.ShouldNotBeNull();
        outboxStore.ShouldBeOfType<StateStoreOutboxStore>();
        configured.ShouldBeTrue();
    }

    [Fact]
    public void AddCentraStateStoreInbox_Registers_StateStoreInboxStore_With_Default_StoreName()
    {
        var services = new ServiceCollection();
        var mockStateStore = Substitute.For<IStateStore>();
        services.AddSingleton(mockStateStore);
        services.Configure<CentraOptions>(opts => opts.DefaultStateStore = "custom-default-store");

        services.AddCentraStateStoreInbox();

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetService<IInboxStore>();

        inboxStore.ShouldNotBeNull();
        inboxStore.ShouldBeOfType<StateStoreInboxStore>();
    }

    [Fact]
    public void AddCentraStateStoreInbox_Registers_StateStoreInboxStore_With_Explicit_StoreName()
    {
        var services = new ServiceCollection();
        var mockStateStore = Substitute.For<IStateStore>();
        services.AddSingleton(mockStateStore);
        services.Configure<CentraOptions>(opts => opts.DefaultStateStore = "default-store");

        var configured = false;
        services.AddCentraStateStoreInbox(stateStoreName: "explicit-inbox-store", configure: _ =>
        {
            configured = true;
        });

        var sp = services.BuildServiceProvider();
        var inboxStore = sp.GetService<IInboxStore>();
        _ = sp.GetRequiredService<IOptions<InboxOptions>>().Value;

        inboxStore.ShouldNotBeNull();
        inboxStore.ShouldBeOfType<StateStoreInboxStore>();
        configured.ShouldBeTrue();
    }
}
