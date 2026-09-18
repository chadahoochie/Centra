using System.Collections.Concurrent;
using System.Linq.Expressions;
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

        var parameters = runMethod.GetParameters();
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var contextParam = Expression.Parameter(typeof(WorkflowActivityContext), "context");
        var inputParam = Expression.Parameter(typeof(object), "input");

        var typedInstance = Expression.Convert(instanceParam, activityType);
        var typedContext = Expression.Convert(contextParam, parameters.Length > 0 ? parameters[0].ParameterType : typeof(WorkflowActivityContext));

        Expression call;
        if (parameters.Length == 0)
        {
            call = Expression.Call(typedInstance, runMethod);
        }
        else if (parameters.Length == 1)
        {
            call = Expression.Call(typedInstance, runMethod, typedContext);
        }
        else
        {
            var inputType = parameters[1].ParameterType;
            Expression typedInput = inputType.IsValueType
                ? Expression.Condition(
                    Expression.Equal(inputParam, Expression.Constant(null, typeof(object))),
                    Expression.Default(inputType),
                    Expression.Convert(inputParam, inputType))
                : Expression.Convert(inputParam, inputType);

            call = Expression.Call(typedInstance, runMethod, typedContext, typedInput);
        }

        var returnType = runMethod.ReturnType;

        if (returnType == typeof(void))
        {
            var voidBody = Expression.Block(call, Expression.Constant(null, typeof(object)));
            var compiledVoid = Expression.Lambda<Func<object, WorkflowActivityContext, object?, object?>>(
                voidBody, instanceParam, contextParam, inputParam).Compile();

            return (instance, ctx, input) =>
            {
                compiledVoid(instance, ctx, input);
                return ValueTask.FromResult<object?>(null);
            };
        }

        var returnBody = Expression.Convert(call, typeof(object));
        var compiledInvoke = Expression.Lambda<Func<object, WorkflowActivityContext, object?, object?>>(
            returnBody, instanceParam, contextParam, inputParam).Compile();

        if (returnType == typeof(ValueTask))
        {
            return async (instance, ctx, input) =>
            {
                var taskObj = compiledInvoke(instance, ctx, input);
                if (taskObj is ValueTask vt)
                {
                    await vt.ConfigureAwait(false);
                }
                return null;
            };
        }

        if (returnType == typeof(Task))
        {
            return async (instance, ctx, input) =>
            {
                var taskObj = compiledInvoke(instance, ctx, input);
                if (taskObj is Task t)
                {
                    await t.ConfigureAwait(false);
                }
                return null;
            };
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var resultType = returnType.GenericTypeArguments[0];
            var asTaskMethod = returnType.GetMethod("AsTask")!;
            var vtParam = Expression.Parameter(typeof(object), "vt");
            var asTaskCall = Expression.Call(Expression.Convert(vtParam, returnType), asTaskMethod);
            var compiledAsTask = Expression.Lambda<Func<object, Task>>(asTaskCall, vtParam).Compile();

            var taskType = typeof(Task<>).MakeGenericType(resultType);
            var taskParam = Expression.Parameter(typeof(Task), "t");
            var resultProp = taskType.GetProperty("Result")!;
            var resultExpr = Expression.Convert(Expression.Property(Expression.Convert(taskParam, taskType), resultProp), typeof(object));
            var compiledGetResult = Expression.Lambda<Func<Task, object?>>(resultExpr, taskParam).Compile();

            return async (instance, ctx, input) =>
            {
                var taskObj = compiledInvoke(instance, ctx, input);
                if (taskObj is null) return null;
                var task = compiledAsTask(taskObj);
                await task.ConfigureAwait(false);
                return compiledGetResult(task);
            };
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GenericTypeArguments[0];
            var taskType = typeof(Task<>).MakeGenericType(resultType);
            var taskParam = Expression.Parameter(typeof(Task), "t");
            var resultProp = taskType.GetProperty("Result")!;
            var resultExpr = Expression.Convert(Expression.Property(Expression.Convert(taskParam, taskType), resultProp), typeof(object));
            var compiledGetResult = Expression.Lambda<Func<Task, object?>>(resultExpr, taskParam).Compile();

            return async (instance, ctx, input) =>
            {
                var taskObj = compiledInvoke(instance, ctx, input);
                if (taskObj is Task task)
                {
                    await task.ConfigureAwait(false);
                    return compiledGetResult(task);
                }
                return null;
            };
        }

        return (instance, ctx, input) =>
        {
            var raw = compiledInvoke(instance, ctx, input);
            return ValueTask.FromResult(raw);
        };
    }
}
