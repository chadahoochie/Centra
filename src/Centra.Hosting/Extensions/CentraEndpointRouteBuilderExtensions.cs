using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Events;
using Centra.Hosting.Routing;
using Centra.PubSub;
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

        return endpoints;
    }
}
