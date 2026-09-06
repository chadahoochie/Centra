using System.Net.Mime;
using System.Text.Json;
using Centra.Components;
using Centra.ControlPlane.Catalog;
using Centra.ControlPlane.Diagnostics;
using Centra.ControlPlane.Secrets;
using Centra.ControlPlane.Sync;
using Centra.ControlPlane.Topology;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
            var resolved = new List<ComponentDefinition>(entries.Count);
            foreach (var entry in entries)
            {
                var def = await secretResolver.ResolveSecretsAsync(entry.Definition, ct);
                resolved.Add(def);
            }
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
                var fullSyncPayload = new
                {
                    action = 0, // FullSync
                    definition = resolvedDef,
                    componentName = entry.Definition.Name,
                    revision = entry.Revision,
                    timestampUtc = entry.UpdatedAtUtc
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

                var syncDto = new
                {
                    action = (int)evt.Type,
                    definition = resolvedDef,
                    componentName = evt.ComponentName,
                    revision = evt.Revision,
                    timestampUtc = evt.TimestampUtc
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

        return endpoints;
    }
}
