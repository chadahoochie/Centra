using System;
using System.Threading;
using System.Threading.Tasks;
using Centra.Core.Workflows;
using Centra.Serialization;
using Centra.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Workflows;

public sealed class TestCalculatorActivity
{
    public ValueTask<int> RunAsync(WorkflowActivityContext context, int input)
    {
        return ValueTask.FromResult(input * 2);
    }
}

public sealed class TestFailingActivity
{
    public ValueTask<string> RunAsync(WorkflowActivityContext context, string input)
    {
        throw new InvalidOperationException("Activity failed!");
    }
}

public sealed class TestVoidActivity
{
    public ValueTask RunAsync(WorkflowActivityContext context, object? input)
    {
        return ValueTask.CompletedTask;
    }
}

public sealed class WorkflowActivityDispatcherTests
{
    private readonly WorkflowRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICentraSerializer _serializer;
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
        
        _sut = new WorkflowActivityDispatcher(
            _serviceProvider,
            _registry,
            _serializer,
            null,
            null);
            
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
    public async Task Should_Wrap_Activity_Exception_In_WorkflowActivityExecutionException()
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
        // Activator.CreateInstance(typeof(int)) == 0. 0 * 2 == 0
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
}
