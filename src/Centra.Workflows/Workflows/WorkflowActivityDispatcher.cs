using System.Diagnostics;
using System.Reflection;
using Centra.Diagnostics;
using Centra.Resilience;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Centra.Core.Workflows;

/// <summary>
/// Dispatches activity execution with parameter adaptation, Polly v8 resilience, and OpenTelemetry instrumentation.
/// </summary>
public sealed class WorkflowActivityDispatcher : IWorkflowActivityDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkflowRegistry _registry;
    private readonly ICentraSerializer _serializer;
    private readonly IResiliencePipelineProvider? _resilienceProvider;
    private readonly ILogger<WorkflowActivityDispatcher>? _logger;

    public WorkflowActivityDispatcher(
        IServiceProvider serviceProvider,
        IWorkflowRegistry registry,
        ICentraSerializer serializer,
        IResiliencePipelineProvider? resilienceProvider = null,
        ILogger<WorkflowActivityDispatcher>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _serializer = serializer;
        _resilienceProvider = resilienceProvider;
        _logger = logger;
    }

    public async ValueTask<TOutput> DispatchActivityAsync<TOutput>(
        WorkflowInstanceId instanceId,
        string activityName,
        object? input,
        ActivityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);

        if (!_registry.TryGetActivity(activityName, out var actDef))
        {
            throw new WorkflowActivityExecutionException(activityName, $"Activity '{activityName}' is not registered in the workflow registry.");
        }

        object? activityInstance;
        try
        {
            activityInstance = _serviceProvider.GetService(actDef.ActivityType)
                ?? ActivatorUtilities.CreateInstance(_serviceProvider, actDef.ActivityType);
        }
        catch (Exception ex)
        {
            throw new WorkflowActivityExecutionException(activityName, $"Failed to instantiate activity '{activityName}' ({actDef.ActivityType.FullName}).", ex);
        }

        // Convert input if necessary
        object? typedInput = ConvertInput(input, actDef.InputType);

        using var cts = options?.Timeout.HasValue == true
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (cts is not null && options?.Timeout.HasValue == true)
        {
            cts.CancelAfter(options.Timeout.Value);
        }

        var effectiveToken = cts?.Token ?? cancellationToken;
        var actContext = new WorkflowActivityContext(instanceId, activityName, effectiveToken);

        // Tracing
        using var activity = CentraDiagnostics.Source.StartActivity("Centra.Workflow.Activity", ActivityKind.Internal);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Activity {activityName}";
            activity.SetTag("centra.component", "workflow");
            activity.SetTag("centra.workflow.instance_id", instanceId.Value);
            activity.SetTag("centra.workflow.activity_name", activityName);
        }

        var pipelineName = options?.ResiliencePolicyName ?? $"workflow:activity:{activityName}";
        var pipeline = _resilienceProvider?.GetPipeline(pipelineName);

        try
        {
            if (pipeline is not null)
            {
                return await pipeline.ExecuteAsync(
                    async (token) => await InvokeActivityMethodAsync<TOutput>(activityInstance, actContext, typedInput, token).ConfigureAwait(false),
                    effectiveToken).ConfigureAwait(false);
            }

            return await InvokeActivityMethodAsync<TOutput>(activityInstance, actContext, typedInput, effectiveToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not WorkflowActivityExecutionException)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
            _logger?.LogError(inner, "Activity '{ActivityName}' failed for workflow instance '{InstanceId}'", activityName, instanceId.Value);
            throw new WorkflowActivityExecutionException(activityName, $"Execution of activity '{activityName}' threw an exception: {inner.Message}", inner);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, WorkflowActivityContext, object?, ValueTask<object?>>> ActivityInvokers = new();

    private async ValueTask<TOutput> InvokeActivityMethodAsync<TOutput>(
        object activityInstance,
        WorkflowActivityContext context,
        object? typedInput,
        CancellationToken cancellationToken)
    {
        var invoker = ActivityInvokers.GetOrAdd(activityInstance.GetType(), static type => CreateActivityInvoker(type));
        var result = await invoker(activityInstance, context, typedInput).ConfigureAwait(false);
        return result is null ? default! : (TOutput)result;
    }

    private static Func<object, WorkflowActivityContext, object?, ValueTask<object?>> CreateActivityInvoker(Type activityType)
    {
        var runMethod = activityType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Type '{activityType.FullName}' does not have a public RunAsync method.");

        var returnType = runMethod.ReturnType;

        if (returnType == typeof(ValueTask))
        {
            return async (instance, ctx, input) =>
            {
                try
                {
                    var taskObj = runMethod.Invoke(instance, [ctx, input]);
                    if (taskObj is ValueTask vt)
                    {
                        await vt.ConfigureAwait(false);
                    }
                    return null;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            };
        }

        if (returnType == typeof(Task))
        {
            return async (instance, ctx, input) =>
            {
                try
                {
                    var taskObj = runMethod.Invoke(instance, [ctx, input]);
                    if (taskObj is Task t)
                    {
                        await t.ConfigureAwait(false);
                    }
                    return null;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            };
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTaskMethod = returnType.GetMethod("AsTask")!;

            return async (instance, ctx, input) =>
            {
                try
                {
                    var taskObj = runMethod.Invoke(instance, [ctx, input]);
                    if (taskObj is null) return null;
                    var task = (Task)asTaskMethod.Invoke(taskObj, null)!;
                    await task.ConfigureAwait(false);
                    return task.GetType().GetProperty("Result")?.GetValue(task);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            };
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return async (instance, ctx, input) =>
            {
                try
                {
                    var taskObj = runMethod.Invoke(instance, [ctx, input]);
                    if (taskObj is Task task)
                    {
                        await task.ConfigureAwait(false);
                        return task.GetType().GetProperty("Result")?.GetValue(task);
                    }
                    return null;
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    throw tie.InnerException;
                }
            };
        }

        return (instance, ctx, input) =>
        {
            try
            {
                var raw = runMethod.Invoke(instance, [ctx, input]);
                return ValueTask.FromResult(raw);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw tie.InnerException;
            }
        };
    }

    private object? ConvertInput(object? input, Type targetType)
    {
        if (input is null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        if (targetType.IsInstanceOfType(input))
        {
            return input;
        }

        if (input is byte[] bytes)
        {
            return _serializer.Deserialize(bytes, targetType);
        }

        if (input is ReadOnlyMemory<byte> memory)
        {
            return _serializer.Deserialize(memory, targetType);
        }

        // Serialize and deserialize to convert objects
        var serialized = _serializer.Serialize(input);
        return _serializer.Deserialize(serialized, targetType);
    }
}
