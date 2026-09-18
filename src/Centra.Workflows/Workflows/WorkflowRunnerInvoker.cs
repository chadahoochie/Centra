using System.Collections.Concurrent;
using System.Linq.Expressions;
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

        var parameters = runMethod.GetParameters();
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var contextParam = Expression.Parameter(typeof(IWorkflowContext), "context");
        var inputParam = Expression.Parameter(typeof(object), "input");

        var typedInstance = Expression.Convert(instanceParam, workflowType);
        var typedContext = Expression.Convert(contextParam, parameters.Length > 0 ? parameters[0].ParameterType : typeof(IWorkflowContext));

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
            var compiledVoid = Expression.Lambda<Func<object, IWorkflowContext, object?, object?>>(
                voidBody, instanceParam, contextParam, inputParam).Compile();

            return (instance, ctx, input) =>
            {
                compiledVoid(instance, ctx, input);
                return ValueTask.FromResult<object?>(null);
            };
        }

        var returnBody = Expression.Convert(call, typeof(object));
        var compiledInvoke = Expression.Lambda<Func<object, IWorkflowContext, object?, object?>>(
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
