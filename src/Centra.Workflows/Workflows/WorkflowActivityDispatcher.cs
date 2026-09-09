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
    private readonly IWorkflowInputConverter _inputConverter;
    private readonly IWorkflowActivityMethodInvoker _invoker;

    public WorkflowActivityDispatcher(
        IServiceProvider serviceProvider,
        IWorkflowRegistry registry,
        ICentraSerializer serializer,
        IResiliencePipelineProvider? resilienceProvider = null,
        ILogger<WorkflowActivityDispatcher>? logger = null,
        IWorkflowInputConverter? inputConverter = null,
        IWorkflowActivityMethodInvoker? invoker = null)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _serializer = serializer;
        _resilienceProvider = resilienceProvider;
        _logger = logger;
        _inputConverter = inputConverter ?? WorkflowInputConverter.Instance;
        _invoker = invoker ?? WorkflowActivityMethodInvoker.Instance;
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
        object? typedInput = _inputConverter.ConvertInput(input, actDef.InputType, _serializer);

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
                    async (token) => await _invoker.InvokeActivityMethodAsync<TOutput>(activityInstance, actContext, typedInput, token).ConfigureAwait(false),
                    effectiveToken).ConfigureAwait(false);
            }

            return await _invoker.InvokeActivityMethodAsync<TOutput>(activityInstance, actContext, typedInput, effectiveToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not WorkflowActivityExecutionException)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
            _logger?.LogError(inner, "Activity '{ActivityName}' failed for workflow instance '{InstanceId}'", activityName, instanceId.Value);
            throw new WorkflowActivityExecutionException(activityName, $"Execution of activity '{activityName}' threw an exception: {inner.Message}", inner);
        }
    }
}
