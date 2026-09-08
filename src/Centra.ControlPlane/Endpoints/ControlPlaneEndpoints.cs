using System.Net.Mime;
using System.Text.Json;
using Centra.Components;
using Centra.Actors;
using Centra.Core.Actors;
using Centra.Workflows;
using Centra.Core.Workflows;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Diagnostics;
using Centra.ControlPlane.Secrets;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Centra.Sync;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.ControlPlane.Endpoints;

public static class ControlPlaneEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCentraControlPlaneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");

        // Component Catalog CRUD
        group.MapGet("/components", async (IComponentCatalog catalog, IControlPlaneSecretResolver secretResolver, CancellationToken ct) =>
        {
            var entries = await catalog.GetAllComponentsAsync(ct);
            var resolved = await ResolveComponentDefinitionsAsync(entries, secretResolver, ct).ConfigureAwait(false);
            return Results.Ok(resolved);
        });

        group.MapGet("/components/{name}", async (string name, IComponentCatalog catalog, IControlPlaneSecretResolver secretResolver, CancellationToken ct) =>
        {
            var entry = await catalog.GetComponentAsync(name, ct);
            if (entry is null)
            {
                return Results.NotFound();
            }

            var resolved = await secretResolver.ResolveSecretsAsync(entry.Definition, ct);
            return Results.Ok(resolved);
        });

        group.MapGet("/bindings", async (IComponentCatalog catalog, IControlPlaneSecretResolver secretResolver, CancellationToken ct) =>
        {
            var entries = await catalog.GetAllComponentsAsync(ct);
            var bindingEntries = entries.Where(e => e.Definition.Type == ComponentType.Binding);
            var resolved = await ResolveComponentDefinitionsAsync(bindingEntries, secretResolver, ct).ConfigureAwait(false);
            return Results.Ok(resolved);
        });

        group.MapPost("/components", async (
            ComponentDefinition definition,
            IComponentCatalog catalog,
            IComponentSyncDispatcher dispatcher,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            using var activity = ControlPlaneDiagnostics.StartCatalogActivity("UpsertComponent", definition.Name);
            var logger = loggerFactory.CreateLogger("Centra.ControlPlane.Endpoints");

            var existing = await catalog.GetComponentAsync(definition.Name, ct);
            var entry = await catalog.UpsertComponentAsync(definition, ct);

            var eventType = existing is null ? ComponentSyncEventType.Added : ComponentSyncEventType.Updated;
            var syncEvent = new ComponentSyncEvent(eventType, definition, definition.Name, entry.Revision, entry.UpdatedAtUtc);

            ControlPlaneMeters.RecordComponentRegistered(definition.Name, definition.Type.ToString());
            ControlPlaneLogMessages.ComponentRegistered(logger, definition.Name, definition.Type.ToString(), entry.Revision);

            await dispatcher.PublishEventAsync(syncEvent, ct);

            return Results.Created($"/api/v1/components/{definition.Name}", entry);
        });

        group.MapDelete("/components/{name}", async (
            string name,
            IComponentCatalog catalog,
            IComponentSyncDispatcher dispatcher,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            using var activity = ControlPlaneDiagnostics.StartCatalogActivity("DeleteComponent", name);
            var logger = loggerFactory.CreateLogger("Centra.ControlPlane.Endpoints");

            var deleted = await catalog.DeleteComponentAsync(name, ct);
            if (!deleted)
            {
                return Results.NotFound();
            }

            var revision = await catalog.GetCurrentRevisionAsync(ct);
            var syncEvent = new ComponentSyncEvent(ComponentSyncEventType.Removed, null, name, revision, timeProvider.GetUtcNow());

            ControlPlaneLogMessages.ComponentRemoved(logger, name);
            await dispatcher.PublishEventAsync(syncEvent, ct);

            return Results.NoContent();
        });

        // Resilience Policy CRUD
        group.MapGet("/resilience", async (IResiliencePolicyCatalog catalog, CancellationToken ct) =>
        {
            var entries = await catalog.GetAllPoliciesAsync(ct);
            var list = entries.Select(e => e.Policy).ToList();
            return Results.Ok(list);
        });

        group.MapGet("/resilience/{name}", async (string name, IResiliencePolicyCatalog catalog, CancellationToken ct) =>
        {
            var entry = await catalog.GetPolicyAsync(name, ct);
            if (entry is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(entry.Policy);
        });

        group.MapPost("/resilience", async (
            ResiliencePolicyDto policy,
            IResiliencePolicyCatalog catalog,
            IComponentSyncDispatcher dispatcher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var entry = await catalog.UpsertPolicyAsync(policy, ct);
            var syncEvent = new ResilienceSyncEventDto
            {
                Action = "Upserted",
                PolicyName = policy.PolicyName,
                Policy = policy,
                Revision = entry.Revision,
                TimestampUtc = entry.UpdatedAtUtc
            };

            await dispatcher.BroadcastResilienceUpdateAsync(syncEvent, ct);

            return Results.Created($"/api/v1/resilience/{policy.PolicyName}", entry.Policy);
        });

        group.MapDelete("/resilience/{name}", async (
            string name,
            IResiliencePolicyCatalog catalog,
            IComponentSyncDispatcher dispatcher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var deleted = await catalog.DeletePolicyAsync(name, ct);
            if (!deleted)
            {
                return Results.NotFound();
            }

            var revision = await catalog.GetCurrentRevisionAsync(ct);
            var syncEvent = new ResilienceSyncEventDto
            {
                Action = "Deleted",
                PolicyName = name,
                Policy = null,
                Revision = revision,
                TimestampUtc = timeProvider.GetUtcNow()
            };

            await dispatcher.BroadcastResilienceUpdateAsync(syncEvent, ct);

            return Results.NoContent();
        });

        group.MapGet("/resilience/stream", async (
            HttpContext httpContext,
            string? appId,
            string? instanceId,
            IResiliencePolicyCatalog catalog,
            IComponentSyncDispatcher dispatcher,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            var effectiveAppId = string.IsNullOrWhiteSpace(appId) ? "anonymous" : appId;
            var effectiveInstanceId = string.IsNullOrWhiteSpace(instanceId) ? Guid.NewGuid().ToString("N") : instanceId;

            // 1. Initial snapshot
            var entries = await catalog.GetAllPoliciesAsync(ct);
            foreach (var entry in entries)
            {
                var fullSyncPayload = new ResilienceSyncEventDto
                {
                    Action = "Upserted",
                    PolicyName = entry.Policy.PolicyName,
                    Policy = entry.Policy,
                    Revision = entry.Revision,
                    TimestampUtc = entry.UpdatedAtUtc
                };
                var json = JsonSerializer.Serialize(fullSyncPayload, JsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            }
            await httpContext.Response.Body.FlushAsync(ct);

            // 2. Stream live mutations
            await foreach (var evt in dispatcher.SubscribeResilienceAsync(effectiveAppId, effectiveInstanceId, ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        });

        // Real-Time Streaming SSE Endpoint
        group.MapGet("/sync/stream", async (
            HttpContext httpContext,
            string? appId,
            string? instanceId,
            IComponentCatalog catalog,
            IControlPlaneSecretResolver secretResolver,
            IComponentSyncDispatcher dispatcher,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            var effectiveAppId = string.IsNullOrWhiteSpace(appId) ? "anonymous" : appId;
            var effectiveInstanceId = string.IsNullOrWhiteSpace(instanceId) ? Guid.NewGuid().ToString("N") : instanceId;

            // 1. Initial snapshot: send FullSync event for each registered component
            var entries = await catalog.GetAllComponentsAsync(ct);
            var revision = await catalog.GetCurrentRevisionAsync(ct);

            foreach (var entry in entries)
            {
                var resolvedDef = await secretResolver.ResolveSecretsAsync(entry.Definition, ct);
                var fullSyncPayload = new ComponentSyncEventDto
                {
                    Action = ComponentSyncAction.FullSync,
                    Definition = resolvedDef,
                    ComponentName = entry.Definition.Name,
                    Revision = entry.Revision,
                    TimestampUtc = entry.UpdatedAtUtc
                };
                var json = JsonSerializer.Serialize(fullSyncPayload, JsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            }
            await httpContext.Response.Body.FlushAsync(ct);

            // 2. Stream subsequent live mutations
            await foreach (var evt in dispatcher.SubscribeAsync(effectiveAppId, effectiveInstanceId, ct))
            {
                ComponentDefinition? resolvedDef = null;
                if (evt.Definition is not null)
                {
                    resolvedDef = await secretResolver.ResolveSecretsAsync(evt.Definition, ct);
                }

                var syncDto = new ComponentSyncEventDto
                {
                    Action = (ComponentSyncAction)(int)evt.Type,
                    Definition = resolvedDef,
                    ComponentName = evt.ComponentName,
                    Revision = evt.Revision,
                    TimestampUtc = evt.TimestampUtc
                };

                var json = JsonSerializer.Serialize(syncDto, JsonOptions);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        });

        // Heartbeat & Topology
        group.MapPost("/heartbeat", async (
            HeartbeatRequest request,
            ITopologyTracker topology,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Centra.ControlPlane.Endpoints");
            var response = await topology.RecordHeartbeatAsync(request, ct);

            ControlPlaneMeters.RecordHeartbeatReceived(request.AppId, request.Status);
            ControlPlaneLogMessages.HeartbeatReceived(logger, request.AppId, request.InstanceId, request.Status);

            return Results.Ok(response);
        });

        group.MapGet("/topology", async (ITopologyTracker topology, CancellationToken ct) =>
        {
            var nodes = await topology.GetActiveNodesAsync(ct);
            return Results.Ok(nodes);
        });

        group.MapGet("/health", (TimeProvider timeProvider) =>
        {
            return Results.Ok(new
            {
                status = "Healthy",
                service = "Centra.ControlPlane",
                version = "1.0.0",
                timestampUtc = timeProvider.GetUtcNow()
            });
        });

        // Actor Runtime Inspection & Lifecycle
        group.MapGet("/actors/types", (IServiceProvider serviceProvider) =>
        {
            var registrations = serviceProvider.GetServices<ActorRegistration>();
            var types = registrations.Select(r => r.ActorType.Name).Distinct().ToList();
            return Results.Ok(types);
        });

        group.MapGet("/actors/activations", (IServiceProvider serviceProvider) =>
        {
            var actorManager = serviceProvider.GetService<ActorManager>();
            var count = actorManager?.ActiveCount ?? 0;
            return Results.Ok(new { activeCount = count });
        });

        group.MapPost("/actors/{actorType}/{actorId}/passivate", async (
            string actorType,
            string actorId,
            IServiceProvider serviceProvider,
            CancellationToken ct) =>
        {
            var actorManager = serviceProvider.GetService<ActorManager>();
            var passivated = false;
            if (actorManager is not null)
            {
                passivated = await actorManager.PassivateActorAsync(
                    new ActorIdentity(new ActorType(actorType), new ActorId(actorId)),
                    ct);
            }

            return Results.Ok(new { passivated });
        });

        // Workflow Runtime Inspection
        group.MapGet("/workflows/definitions", (IServiceProvider serviceProvider) =>
        {
            var registry = serviceProvider.GetService<IWorkflowRegistry>();
            var defs = registry?.GetWorkflows().Select(w => new
            {
                name = w.Name,
                workflowType = w.WorkflowType.Name,
                inputType = w.InputType.Name,
                outputType = w.OutputType.Name
            }) ?? [];
            return Results.Ok(defs);
        });

        group.MapGet("/workflows/activities", (IServiceProvider serviceProvider) =>
        {
            var registry = serviceProvider.GetService<IWorkflowRegistry>();
            var activities = registry?.GetActivities().Select(a => new
            {
                name = a.Name,
                activityType = a.ActivityType.Name,
                inputType = a.InputType.Name,
                outputType = a.OutputType.Name
            }) ?? [];
            return Results.Ok(activities);
        });

        group.MapGet("/workflows/instances/{instanceId}", async (
            string instanceId,
            IServiceProvider serviceProvider,
            CancellationToken ct) =>
        {
            var engine = serviceProvider.GetService<IWorkflowEngine>();
            if (engine is null) return Results.NotFound();

            var state = await engine.GetWorkflowStateAsync(new WorkflowInstanceId(instanceId), ct);
            if (state is null) return Results.NotFound();

            return Results.Ok(new
            {
                instanceId = state.Value.InstanceId.Value,
                workflowName = state.Value.WorkflowName,
                status = state.Value.Status.ToString(),
                customStatus = state.Value.CustomStatus,
                createdAt = state.Value.CreatedAt,
                lastUpdatedAt = state.Value.LastUpdatedAt,
                failureDetails = state.Value.FailureDetails
            });
        });

        group.MapGet("/workflows/instances/{instanceId}/history", async (
            string instanceId,
            IServiceProvider serviceProvider,
            CancellationToken ct) =>
        {
            var engine = serviceProvider.GetService<IWorkflowEngine>();
            if (engine is null) return Results.NotFound();

            var history = await engine.GetWorkflowHistoryAsync(new WorkflowInstanceId(instanceId), ct);
            return Results.Ok(history);
        });

        return endpoints;
    }

    private static async ValueTask<List<ComponentDefinition>> ResolveComponentDefinitionsAsync(
        IEnumerable<ComponentCatalogEntry> entries,
        IControlPlaneSecretResolver secretResolver,
        CancellationToken ct)
    {
        var resolved = new List<ComponentDefinition>();
        foreach (var entry in entries)
        {
            var def = await secretResolver.ResolveSecretsAsync(entry.Definition, ct).ConfigureAwait(false);
            resolved.Add(def);
        }
        return resolved;
    }
}
