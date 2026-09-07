using Centra.Bindings;
using Centra.Hosting.Extensions;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Routing;
using Centra.Locks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraBindingsHostingTests
{
    [Fact]
    public void AddCentraBindings_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings();

        var sp = services.BuildServiceProvider();

        sp.GetService<IOutputBinding>().ShouldNotBeNull();
        sp.GetService<CentraInputBindingDispatcher>().ShouldNotBeNull();
        sp.GetService<IScheduler>().ShouldNotBeNull();
    }

    [Fact]
    public void AddCentraCronJob_RegistersJobConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings();
        services.AddCentraCronJob<SampleJob>("sample-job", "0 * * * *");

        var sp = services.BuildServiceProvider();

        var registrations = sp.GetServices<CentraCronJobRegistration>().ToList();
        registrations.Count.ShouldBe(1);
        registrations[0].JobName.ShouldBe("sample-job");
        registrations[0].CronExpression.ShouldBe("0 * * * *");
        registrations[0].JobType.ShouldBe(typeof(SampleJob));
    }

    [Fact]
    public void AddCentraInputBindingHandler_RegistersBindingRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings();
        services.AddCentraInputBindingHandler<SampleTriggerHandler>("orders-in");

        var sp = services.BuildServiceProvider();

        var registrations = sp.GetServices<CentraInputBindingRegistration>().ToList();
        registrations.Count.ShouldBe(1);
        registrations[0].BindingName.ShouldBe("orders-in");
        registrations[0].HandlerType.ShouldBe(typeof(SampleTriggerHandler));
    }

    [Fact]
    public async Task CentraBindingsHostedService_StartAndStop_ActivatesAndDisposesSchedulers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings();
        services.AddCentraCronJob<SampleJob>("active-job", "*/5 * * * *");
        services.AddCentraInputBindingHandler<SampleTriggerHandler>("webhooks-in");

        var lockProvider = Substitute.For<IDistributedLockProvider>();
        services.AddSingleton(lockProvider);

        var sp = services.BuildServiceProvider();

        var hostedService = sp.GetServices<IHostedService>()
            .OfType<CentraBindingsHostedService>()
            .FirstOrDefault();

        hostedService.ShouldNotBeNull();

        await hostedService.StartAsync(CancellationToken.None);

        var dispatcher = sp.GetRequiredService<CentraInputBindingDispatcher>();
        dispatcher.HasHandler("webhooks-in").ShouldBeTrue();

        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddCentraHttpWebhookBinding_RegistersDriverAndComponentRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings(opts => opts.AppId = "custom-app");
        services.AddCentraHttpWebhookBinding("orders-out");

        var sp = services.BuildServiceProvider();

        var driver = sp.GetRequiredService<Centra.Drivers.IBindingDriver>();
        driver.ShouldNotBeNull();
        driver.ShouldBeOfType<HttpWebhookBindingDriver>();

        var registry = sp.GetRequiredService<Centra.Registry.ComponentRegistry>();
        registry.GetBindingDriver("orders-out").ShouldBe(driver);
    }

    [Fact]
    public void AddCentraHttpWebhookBinding_WithExplicitHttpClient_RegistersDriver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCentraBindings();

        using var explicitClient = new HttpClient();
        services.AddCentraHttpWebhookBinding("custom-http", explicitClient);

        var sp = services.BuildServiceProvider();

        var driver = sp.GetRequiredService<Centra.Drivers.IBindingDriver>();
        driver.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddCentraCronJob_InvalidJobName_ThrowsArgumentException(string invalid)
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentException>(() =>
            services.AddCentraCronJob<SampleJob>(invalid, "* * * * *"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddCentraCronJob_InvalidCron_ThrowsArgumentException(string invalid)
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentException>(() =>
            services.AddCentraCronJob<SampleJob>("job", invalid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddCentraInputBindingHandler_InvalidName_ThrowsArgumentException(string invalid)
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentException>(() =>
            services.AddCentraInputBindingHandler<SampleTriggerHandler>(invalid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddCentraHttpWebhookBinding_InvalidName_ThrowsArgumentException(string invalid)
    {
        var services = new ServiceCollection();
        Should.Throw<ArgumentException>(() =>
            services.AddCentraHttpWebhookBinding(invalid));
    }

    private sealed class SampleJob : IJobHandler
    {
        public ValueTask ExecuteAsync(ScheduledJobContext context) => ValueTask.CompletedTask;
    }

    private sealed class SampleTriggerHandler : IBindingTriggerHandler
    {
        public ValueTask<BindingResponse> HandleTriggerAsync(BindingData data, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new BindingResponse(data.Data));
        }
    }
}
