using System.Collections.Concurrent;
using System.Reflection;
using Centra.Workflows;

namespace Centra.Core.Workflows;

/// <summary>
/// Default implementation of <see cref="IWorkflowActivityMethodInvoker"/> compiling and caching dynamic invocation delegates.
/// </summary>
public sealed class WorkflowActivityMethodInvoker : IWorkflowActivityMethodInvoker
{
    /// <summary>
    /// Singleton default instance of <see cref="WorkflowActivityMethodInvoker"/>.
    /// </summary>
    public static readonly WorkflowActivityMethodInvoker Instance = new();

    private readonly ConcurrentDictionary<Type, Func<object, WorkflowActivityContext, object?, ValueTask<object?>>> _invokers = new();

    public async ValueTask<TOutput> InvokeActivityMethodAsync<TOutput>(
        object activityInstance,
        WorkflowActivityContext context,
        object? typedInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityInstance);

        var invoker = _invokers.GetOrAdd(activityInstance.GetType(), CreateInvoker);
        var result = await invoker(activityInstance, context, typedInput).ConfigureAwait(false);
        return result is null ? default! : (TOutput)result;
    }

    /// <summary>
    /// Compiles an invocation delegate for the public RunAsync method on the activity type.
    /// </summary>
    public Func<object, WorkflowActivityContext, object?, ValueTask<object?>> CreateInvoker(Type activityType)
    {
        ArgumentNullException.ThrowIfNull(activityType);

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
}
