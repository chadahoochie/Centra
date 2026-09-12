using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Components;

namespace Centra.Aspire.Hosting;

public static class CentraAspireExtensions
{
    public static IResourceBuilder<CentraControlPlaneResource> AddCentraControlPlane(
        this IDistributedApplicationBuilder builder,
        string name = "centra-controlplane",
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var resource = new CentraControlPlaneResource(name);

        return builder.AddResource(resource)
            .WithHttpEndpoint(port: port, name: CentraControlPlaneResource.HttpEndpointName);
    }

    public static IResourceBuilder<T> WithCentra<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<CentraControlPlaneResource> controlPlane)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(controlPlane);

        return builder
            .WithEnvironment("Centra__ControlPlaneEndpoint", controlPlane.Resource.HttpEndpoint)
            .WithEnvironment("Centra__AppId", builder.Resource.Name);
    }

    public static IResourceBuilder<T> WithCentra<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithEndpoints> controlPlane,
        string endpointName = "http")
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(controlPlane);

        return builder
            .WithEnvironment("Centra__ControlPlaneEndpoint", controlPlane.GetEndpoint(endpointName))
            .WithEnvironment("Centra__AppId", builder.Resource.Name);
    }

    public static IResourceBuilder<T> WithCentraRedis<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> redis)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(redis);

        return builder
            .WithReference(redis, "redis")
            .WithEnvironment("Centra__Redis__ConnectionString", redis.Resource.ConnectionStringExpression);
    }

    public static IResourceBuilder<T> WithCentraRabbitMQ<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> rabbitmq)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(rabbitmq);

        return builder
            .WithReference(rabbitmq, "rabbitmq");
    }

    public static IResourceBuilder<T> WithCentraPostgreSql<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> postgres)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(postgres);

        return builder
            .WithReference(postgres, "postgresql")
            .WithEnvironment("Centra__PostgreSql__ConnectionString", postgres.Resource.ConnectionStringExpression);
    }

    public static IResourceBuilder<T> WithCentraSqlServer<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithConnectionString> sqlServer)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sqlServer);

        return builder
            .WithReference(sqlServer, "sqlserver")
            .WithEnvironment("Centra__SqlServer__ConnectionString", sqlServer.Resource.ConnectionStringExpression);
    }
}
