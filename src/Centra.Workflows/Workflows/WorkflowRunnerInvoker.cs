using System.Collections.Concurrent;
using System.Reflection;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Default implementation of <see cref="IWorkflowRunnerInvoker"/> compiling and caching workflow execution delegates.
/// </summary>
public sealed class WorkflowRunnerInvoker : IWorkflowRunnerInvoker
{
    /// <summary>
    /// Singleton default instance of <see cref="WorkflowRunnerInvoker"/>.
    /// </summary>
    public static readonly WorkflowRunnerInvoker Instance = new();

    private readonly ConcurrentDictionary<Type, Func<object, IWorkflowContext, object?, ValueTask<object?>>> _runners = new();

    public async ValueTask<object?> InvokeWorkflowRunAsync(
        object workflowInstance,
        IWorkflowContext context,
        object? typedInput)
    {
        ArgumentNullException.ThrowIfNull(workflowInstance);

        var runner = _runners.GetOrAdd(workflowInstance.GetType(), CreateWorkflowRunner);
        return await runner(workflowInstance, context, typedInput).ConfigureAwait(false);
    }

    /// <summary>
    /// Compiles an execution delegate for the public RunAsync method on the workflow type.
    /// </summary>
    public Func<object, IWorkflowContext, object?, ValueTask<object?>> CreateWorkflowRunner(Type workflowType)
    {
        ArgumentNullException.ThrowIfNull(workflowType);

        var runMethod = workflowType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Workflow '{workflowType.FullName}' does not have a public RunAsync method.");

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
}
