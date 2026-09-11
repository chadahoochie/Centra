using System.Diagnostics;
using System.Reflection;
using Centra.Core.Workflows;
using Centra.Diagnostics;
using Centra.Resilience;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class WorkflowActivityDispatcherTests
{
    private readonly WorkflowRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICentraSerializer _serializer;
    private readonly ILogger<WorkflowActivityDispatcher> _logger;
    private readonly WorkflowActivityDispatcher _sut;
    private readonly WorkflowInstanceId _instanceId;

    public WorkflowActivityDispatcherTests()
    {
        _registry = new WorkflowRegistry();

        var services = new ServiceCollection();
        services.AddTransient<TestCalculatorActivity>();
        services.AddTransient<TestFailingActivity>();
        services.AddTransient<TestVoidActivity>();
        _serviceProvider = services.BuildServiceProvider();

        _serializer = Substitute.For<ICentraSerializer>();
        _logger = Substitute.For<ILogger<WorkflowActivityDispatcher>>();

        _sut = new WorkflowActivityDispatcher(
            _serviceProvider,
            _registry,
            _serializer,
            null,
            _logger);

        _instanceId = WorkflowInstanceId.New();
    }

    [Fact]
    public async Task Should_Dispatch_Activity_And_Return_Result()
    {
        // Arrange
        var activityName = "CalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        // Act
        var result = await _sut.DispatchActivityAsync<int>(_instanceId, activityName, 5);

        // Assert
        result.ShouldBe(10);
    }

    [Fact]
    public async Task Should_Record_Activity_Tracing_When_Listener_Active()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var activityName = "CalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        // Act
        var result = await _sut.DispatchActivityAsync<int>(_instanceId, activityName, 7);

        // Assert
        result.ShouldBe(14);
    }

    [Fact]
    public async Task Should_Throw_WorkflowActivityExecutionException_When_Activity_Not_Registered()
    {
        // Arrange
        var activityName = "UnknownActivity";

        // Act
        var exception = await Should.ThrowAsync<WorkflowActivityExecutionException>(async () =>
            await _sut.DispatchActivityAsync<int>(_instanceId, activityName, null));

        // Assert
        exception.ActivityName.ShouldBe(activityName);
        exception.Message.ShouldContain("is not registered");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_Throw_When_ActivityName_Is_NullOrWhiteSpace(string? activityName)
    {
        // Act
        var exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await _sut.DispatchActivityAsync<int>(_instanceId, activityName!, null));

        // Assert
        exception.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Throw_WorkflowActivityExecutionException_When_Activity_Instantiation_Fails()
    {
        // Arrange
        var activityName = "UnresolvableActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(UnconstructibleActivity), typeof(object), typeof(object)));

        // Act
        var exception = await Should.ThrowAsync<WorkflowActivityExecutionException>(async () =>
            await _sut.DispatchActivityAsync<object>(_instanceId, activityName, null));

        // Assert
        exception.ActivityName.ShouldBe(activityName);
        exception.Message.ShouldContain("Failed to instantiate activity");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Wrap_Activity_Exception_In_WorkflowActivityExecutionException_And_Log()
    {
        // Arrange
        var activityName = "FailingActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestFailingActivity), typeof(string), typeof(string)));

        // Act
        var exception = await Should.ThrowAsync<WorkflowActivityExecutionException>(async () =>
            await _sut.DispatchActivityAsync<string>(_instanceId, activityName, "input"));

        // Assert
        exception.ActivityName.ShouldBe(activityName);
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.InnerException.Message.ShouldBe("Activity failed!");

        _logger.ReceivedWithAnyArgs().Log(
            LogLevel.Error,
            default,
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Should_Unwrap_TargetInvocationException_And_Log()
    {
        // Arrange
        var activityName = "TargetInvocationFailingActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestTargetInvocationActivity), typeof(string), typeof(string)));

        // Act
        var exception = await Should.ThrowAsync<WorkflowActivityExecutionException>(async () =>
            await _sut.DispatchActivityAsync<string>(_instanceId, activityName, "input"));

        // Assert
        exception.ActivityName.ShouldBe(activityName);
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.InnerException.Message.ShouldBe("Direct target invocation failure");
    }

    [Fact]
    public async Task Should_Dispatch_Through_ResiliencePipeline_When_Configured()
    {
        // Arrange
        var activityName = "ResilientCalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        var pipeline = Substitute.For<IResiliencePipeline>();
        pipeline.ExecuteAsync(Arg.Any<Func<CancellationToken, ValueTask<int>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var callback = callInfo.Arg<Func<CancellationToken, ValueTask<int>>>();
                var token = callInfo.Arg<CancellationToken>();
                async ValueTask<int> Execute()
                {
                    var result = await callback(token);
                    return result + 100; // intercepted by resilience
                }
                return Execute();
            });

        var resilienceProvider = Substitute.For<IResiliencePipelineProvider>();
        resilienceProvider.GetPipeline("custom-policy").Returns(pipeline);

        var options = new ActivityOptions
        {
            ResiliencePolicyName = "custom-policy",
            Timeout = TimeSpan.FromSeconds(5)
        };

        var sut = new WorkflowActivityDispatcher(
            _serviceProvider,
            _registry,
            _serializer,
            resilienceProvider,
            _logger);

        // Act
        var finalResult = await sut.DispatchActivityAsync<int>(_instanceId, activityName, 3, options);

        // Assert
        finalResult.ShouldBe(106); // 3 * 2 + 100
        resilienceProvider.Received(1).GetPipeline("custom-policy");
    }

    [Fact]
    public async Task Should_Convert_Null_Input_For_Value_Type()
    {
        // Arrange
        var activityName = "CalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        // Act
        var result = await _sut.DispatchActivityAsync<int>(_instanceId, activityName, null);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Convert_Null_Input_For_Reference_Type()
    {
        // Arrange
        var activityName = "VoidActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestVoidActivity), typeof(object), typeof(void)));

        // Act
        await Should.NotThrowAsync(async () =>
            await _sut.DispatchActivityAsync<object>(_instanceId, activityName, null));
    }

    [Fact]
    public async Task Should_Pass_Through_Input_When_Type_Matches()
    {
        // Arrange
        var activityName = "CalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        // Act
        var result = await _sut.DispatchActivityAsync<int>(_instanceId, activityName, 42);

        // Assert
        result.ShouldBe(84);
        _serializer.DidNotReceiveWithAnyArgs().Deserialize(default(byte[]), default!);
    }

    [Fact]
    public async Task Should_Convert_Byte_Array_Input_Via_Serializer()
    {
        // Arrange
        var activityName = "CalcActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestCalculatorActivity), typeof(int), typeof(int)));

        var bytes = new byte[] { 1, 2, 3 };
        _serializer.Deserialize(bytes, typeof(int)).Returns(50);

        // Act
        var result = await _sut.DispatchActivityAsync<int>(_instanceId, activityName, bytes);

        // Assert
        result.ShouldBe(100);
        _serializer.Received(1).Deserialize(bytes, typeof(int));
    }

    [Fact]
    public async Task Should_Dispatch_Void_Activity_Successfully()
    {
        // Arrange
        var activityName = "VoidActivity";
        _registry.RegisterActivity(new WorkflowActivityDefinition(activityName, typeof(TestVoidActivity), typeof(object), typeof(void)));

        // Act
        await Should.NotThrowAsync(async () =>
            await _sut.DispatchActivityAsync<object>(_instanceId, activityName, new object()));
    }

    private sealed class TestCalculatorActivity
    {
        public ValueTask<int> RunAsync(WorkflowActivityContext context, int input)
        {
            return ValueTask.FromResult(input * 2);
        }
    }

    private sealed class TestFailingActivity
    {
        public ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
        {
            throw new InvalidOperationException("Activity failed!");
        }
    }

    private sealed class TestTargetInvocationActivity
    {
        public ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
        {
            throw new TargetInvocationException("wrapper", new InvalidOperationException("Direct target invocation failure"));
        }
    }

    private sealed class TestVoidActivity
    {
        public ValueTask RunAsync(WorkflowActivityContext context, object? input)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class UnconstructibleActivity
    {
        public UnconstructibleActivity(int nonResolvablePrimitive)
        {
        }

        public ValueTask<object> RunAsync(WorkflowActivityContext context, object? input)
        {
            return ValueTask.FromResult(new object());
        }
    }
}
