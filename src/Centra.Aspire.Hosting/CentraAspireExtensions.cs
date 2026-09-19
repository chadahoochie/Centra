using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Centra.Components;

namespace Centra.Aspire.Hosting;

public static class CentraAspireExtensions
{
    /// <summary>
    /// Adds the Centra Control Plane to the application model as a launchable container that
    /// pulls the published control-plane image.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Aspire resource name.</param>
    /// <param name="port">Optional fixed host port; omit to let Aspire allocate one.</param>
    /// <remarks>
    /// The resource pulls <c>ghcr.io/chadahoochie/centra-controlplane:1.0.0</c> by default; the
    /// tag tracks <c>VersionPrefix</c> in <c>Directory.Build.props</c>, so the tag the resource
    /// pulls is the tag the publish workflow pushes. Override the coordinates with Aspire's own
    /// container builder extensions — <c>WithImage</c>, <c>WithImageTag</c>,
    /// <c>WithImageRegistry</c>, <c>WithImagePullPolicy</c>.
    /// Aspire injects OTLP exporter configuration into project resources automatically but not
    /// into containers, so the resource opts in explicitly and its telemetry reaches the
    /// dashboard on both routes.
    /// Teams that already carry Centra's source in their solution can instead orchestrate the
    /// project directly with <c>AddProject&lt;Projects.Centra_ControlPlane&gt;</c> and wire
    /// services to it through the <see cref="IResourceWithEndpoints"/> overload of
    /// <c>WithCentra</c>.
    /// </remarks>
    public static IResourceBuilder<CentraControlPlaneResource> AddCentraControlPlane(
        this IDistributedApplicationBuilder builder,
        string name = "centra-controlplane",
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var resource = new CentraControlPlaneResource(name);

        return builder.AddResource(resource)
            .WithImage("chadahoochie/centra-controlplane", "1.0.0")
            .WithImageRegistry("ghcr.io")
            .WithOtlpExporter()
            .WithHttpEndpoint(
                port: port,
                targetPort: 8080,
                name: CentraControlPlaneResource.HttpEndpointName);
    }

    /// <summary>
    /// Points a resource at a Centra Control Plane container added by
    /// <see cref="AddCentraControlPlane"/>.
    /// </summary>
    /// <param name="builder">The resource to configure.</param>
    /// <param name="controlPlane">The Control Plane resource.</param>
    public static IResourceBuilder<T> WithCentra<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<CentraControlPlaneResource> controlPlane)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(controlPlane);

        return builder.WithCentra(controlPlane, CentraControlPlaneResource.HttpEndpointName);
    }

    /// <summary>
    /// Points a resource at any endpoint-bearing Control Plane resource — for example a
    /// <c>ProjectResource</c> added with <c>AddProject&lt;Projects.Centra_ControlPlane&gt;</c>
    /// when Centra's source is part of the solution.
    /// </summary>
    /// <param name="builder">The resource to configure.</param>
    /// <param name="controlPlane">The Control Plane resource.</param>
    /// <param name="endpointName">Name of the Control Plane's HTTP endpoint.</param>
    public static IResourceBuilder<T> WithCentra<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<IResourceWithEndpoints> controlPlane,
        string endpointName = CentraControlPlaneResource.HttpEndpointName)
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
            .WithReference(rabbitmq, "rabbitmq")
            .WithEnvironment("Centra__RabbitMQ__ConnectionString", rabbitmq.Resource.ConnectionStringExpression);
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
