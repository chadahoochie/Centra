using System.Diagnostics;
using Centra.Bindings;
using Centra.Diagnostics;
using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
using Centra.Workflows;
using Centra.Core.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.Hosting.Extensions;

public static class CentraEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCentraEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registrations = endpoints.ServiceProvider.GetServices<CentraTopicRegistration>();

        foreach (var reg in registrations)
        {
            var routePattern = $"/centra/events/{reg.PubSubName}/{reg.Topic.TrimStart('/')}";

            endpoints.MapPost(routePattern, async (HttpContext context) =>
            {
                // Read payload bytes
                using var ms = new MemoryStream();
                await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
                var payload = ms.ToArray();

                // Build header dictionary
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in context.Request.Headers)
                {
                    headers[header.Key] = header.Value.ToString();
                }

                var parentContext = CentraTracePropagator.Extract(headers);
                var startTime = Stopwatch.GetTimestamp();
                using var activity = CentraDiagnostics.StartProcessActivity(reg.PubSubName, reg.Topic, parentContext);

                try
                {
                    var handler = context.RequestServices.GetService(reg.HandlerType);
                    if (handler is null)
                    {
                        return Results.NotFound();
                    }

                    var unpackMethod = typeof(CloudEventUnpacker)
                        .GetMethod(nameof(CloudEventUnpacker.Unpack))!
                        .MakeGenericMethod(reg.EventType);

                    var unpacked = unpackMethod.Invoke(null, [new ReadOnlyMemory<byte>(payload), headers]);
                    var dataProp = unpacked!.GetType().GetProperty(nameof(UnpackedCloudEvent<object>.Data))!;
                    var contextProp = unpacked.GetType().GetProperty(nameof(UnpackedCloudEvent<object>.Context))!;

                    var eventData = dataProp.GetValue(unpacked);
                    var eventContext = (EventContext)contextProp.GetValue(unpacked)!;

                    var handleMethod = reg.HandlerType.GetMethod(nameof(IEventHandler<object>.HandleAsync))!;
                    var resultTask = (Task<EventHandlingResult>)handleMethod.Invoke(handler, [eventData, eventContext, context.RequestAborted])!;
                    var result = await resultTask.ConfigureAwait(false);

                    var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                    CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, result.ToString(), durationMs);

                    return result switch
                    {
                        EventHandlingResult.Success => Results.Ok(),
                        EventHandlingResult.Drop => Results.Ok(),
                        EventHandlingResult.Retry => Results.StatusCode(StatusCodes.Status500InternalServerError),
                        EventHandlingResult.DeadLetter => Results.Ok(),
                        _ => Results.Ok()
                    };
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    var durationMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                    CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, "error", durationMs);
                    return Results.StatusCode(StatusCodes.Status500InternalServerError);
                }
            });
        }

        // Map Input Binding Triggers
        endpoints.MapPost("/centra/bindings/{bindingName}", async (string bindingName, HttpContext context) =>
        {
            var dispatcher = context.RequestServices.GetService<CentraInputBindingDispatcher>();
            if (dispatcher is null || !dispatcher.HasHandler(bindingName))
            {
                return Results.NotFound();
            }

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            var payload = ms.ToArray();

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in context.Request.Headers)
            {
                metadata[header.Key] = header.Value.ToString();
            }

            try
            {
                var bindingData = new BindingData(payload, metadata, context.Request.ContentType);
                var response = await dispatcher.DispatchAsync(bindingName, bindingData, context.RequestAborted).ConfigureAwait(false);

                if (response.Metadata is not null)
                {
                    foreach (var kvp in response.Metadata)
                    {
                        if (kvp.Key.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.Headers.TryAdd(kvp.Key["header:".Length..], kvp.Value);
                        }
                    }
                }

                if (!response.Data.IsEmpty)
                {
                    var contentType = "application/octet-stream";
                    if (response.Metadata is not null && response.Metadata.TryGetValue("content-type", out var ct))
                    {
                        contentType = ct;
                    }
                    return Results.Bytes(response.Data.ToArray(), contentType);
                }

                return Results.Ok();
            }
            catch (Exception)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        });

        endpoints.MapCentraActorEndpoints();
        endpoints.MapCentraWorkflowEndpoints();

        return endpoints;
    }

    public static IEndpointRouteBuilder MapCentraActorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Map Virtual Actor Invocations
        endpoints.MapPost("/centra/actors/{actorType}/{actorId}/method/{methodName}", async (
            string actorType,
            string actorId,
            string methodName,
            HttpContext context) =>
        {
            var actorManager = context.RequestServices.GetService<Centra.Core.Actors.ActorManager>();
            if (actorManager is null)
            {
                return Results.NotFound();
            }

            var identity = new Centra.Actors.ActorIdentity(actorType, actorId);

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            var bodyBytes = ms.ToArray();

            try
            {
                var response = await actorManager.DispatchAsync(identity, async actor =>
                {
                    var actorClassType = actor.GetType();
                    var method = actorClassType.GetMethods()
                        .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

                    if (method == null)
                    {
                        throw new MissingMethodException(actorClassType.Name, methodName);
                    }

                    var parameters = method.GetParameters();
                    object?[] args;

                    if (parameters.Length == 0)
                    {
                        args = Array.Empty<object?>();
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                    {
                        args = [context.RequestAborted];
                    }
                    else
                    {
                        var paramType = parameters[0].ParameterType;
                        var paramValue = bodyBytes.Length > 0
                            ? System.Text.Json.JsonSerializer.Deserialize(bodyBytes, paramType)
                            : null;

                        if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
                        {
                            args = [paramValue, context.RequestAborted];
                        }
                        else
                        {
                            args = [paramValue];
                        }
                    }

                    var rawResult = method.Invoke(actor, args);
                    if (rawResult is Task task)
                    {
                        await task.ConfigureAwait(false);
                        var taskType = task.GetType();
                        if (taskType.IsGenericType)
                        {
                            var prop = taskType.GetProperty("Result");
                            return prop?.GetValue(task);
                        }
                        return null;
                    }

                    if (rawResult != null)
                    {
                        var rawType = rawResult.GetType();
                        if (rawType.IsGenericType && rawType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                        {
                            var asTaskMethod = rawType.GetMethod("AsTask")!;
                            var asTask = (Task)asTaskMethod.Invoke(rawResult, null)!;
                            await asTask.ConfigureAwait(false);
                            var prop = asTask.GetType().GetProperty("Result");
                            return prop?.GetValue(asTask);
                        }

                        if (rawResult is ValueTask vt)
                        {
                            await vt.ConfigureAwait(false);
                            return null;
                        }
                    }

                    return rawResult;
                }, context.RequestAborted);

                if (response is null)
                {
                    return Results.Ok();
                }

                return Results.Ok(response);
            }
            catch (MissingMethodException)
            {
                return Results.NotFound();
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return endpoints;
    }

    public static IEndpointRouteBuilder MapCentraWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Workflow Endpoints
        endpoints.MapPost("/centra/workflows/{workflowName}/start", async (
            string workflowName,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            var payload = ms.Length > 0 ? ms.ToArray() : null;

            var instanceId = await workflowClient.StartWorkflowAsync(
                workflowName,
                payload,
                null,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Accepted($"/centra/workflows/{instanceId.Value}", new { instanceId = instanceId.Value });
        });

        endpoints.MapPost("/centra/workflows/{workflowName}/{instanceId}/start", async (
            string workflowName,
            string instanceId,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            var payload = ms.Length > 0 ? ms.ToArray() : null;

            var actualId = await workflowClient.StartWorkflowAsync(
                workflowName,
                payload,
                instanceId,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Accepted($"/centra/workflows/{actualId.Value}", new { instanceId = actualId.Value });
        });

        endpoints.MapGet("/centra/workflows/{instanceId}", async (
            string instanceId,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            var state = await workflowClient.GetWorkflowStateAsync(new WorkflowInstanceId(instanceId), context.RequestAborted).ConfigureAwait(false);
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

        endpoints.MapGet("/centra/workflows/{instanceId}/history", async (
            string instanceId,
            HttpContext context) =>
        {
            var workflowEngine = context.RequestServices.GetService<IWorkflowEngine>();
            if (workflowEngine is null) return Results.NotFound();

            var history = await workflowEngine.GetWorkflowHistoryAsync(new WorkflowInstanceId(instanceId), context.RequestAborted).ConfigureAwait(false);
            return Results.Ok(history);
        });

        endpoints.MapPost("/centra/workflows/{instanceId}/raise-event/{eventName}", async (
            string instanceId,
            string eventName,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms).ConfigureAwait(false);
            var payload = ms.Length > 0 ? ms.ToArray() : null;

            await workflowClient.RaiseEventAsync(
                new WorkflowInstanceId(instanceId),
                eventName,
                payload,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Accepted();
        });

        endpoints.MapPost("/centra/workflows/{instanceId}/terminate", async (
            string instanceId,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            using var reader = new StreamReader(context.Request.Body);
            var reason = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reason)) reason = "Terminated via API";

            await workflowClient.TerminateWorkflowAsync(
                new WorkflowInstanceId(instanceId),
                reason,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Ok();
        });

        endpoints.MapDelete("/centra/workflows/{instanceId}", async (
            string instanceId,
            HttpContext context) =>
        {
            var workflowClient = context.RequestServices.GetService<IWorkflowClient>();
            if (workflowClient is null) return Results.NotFound();

            await workflowClient.PurgeWorkflowAsync(new WorkflowInstanceId(instanceId), context.RequestAborted).ConfigureAwait(false);
            return Results.NoContent();
        });

        return endpoints;
    }
}
