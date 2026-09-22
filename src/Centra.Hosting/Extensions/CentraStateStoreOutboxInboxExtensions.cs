using Centra.Hosting.Inbox;
using Centra.Hosting.Outbox;
using Centra.PubSub.Inbox;
using Centra.PubSub.Outbox;
using Centra.State;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class CentraStateStoreOutboxInboxExtensions
{
    public static IServiceCollection AddCentraStateStoreOutbox(
        this IServiceCollection services,
        string? stateStoreName = null,
        Action<OutboxOptions>? configure = null)
    {
        services.AddCentraOutbox(configure);

        services.Replace(ServiceDescriptor.Singleton<IOutboxStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var options = sp.GetRequiredService<IOptions<Centra.CentraOptions>>().Value;
            var resolvedStore = stateStoreName ?? options.DefaultStateStore;
            return new StateStoreOutboxStore(stateStore, resolvedStore);
        }));

        return services;
    }

    public static IServiceCollection AddCentraStateStoreInbox(
        this IServiceCollection services,
        string? stateStoreName = null,
        Action<InboxOptions>? configure = null)
    {
        services.AddCentraInbox(configure);

        services.Replace(ServiceDescriptor.Singleton<IInboxStore>(sp =>
        {
            var stateStore = sp.GetRequiredService<IStateStore>();
            var options = sp.GetRequiredService<IOptions<Centra.CentraOptions>>().Value;
            var resolvedStore = stateStoreName ?? options.DefaultStateStore;
            return new StateStoreInboxStore(stateStore, resolvedStore);
        }));

        return services;
    }
}
