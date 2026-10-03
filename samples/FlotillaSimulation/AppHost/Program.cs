using Centra.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Standalone Containerized Flotilla Consensus Server (TCP: 9100, UDP: 9200, gRPC: 9300, HTTP: 9301)
var server = builder.AddCentraFlotilla("flotilla-server", tcpPort: 9100, udpPort: 9200, grpcPort: 9300, httpPort: 9301)
    .WithImageTag("latest");

// 2. API Service (Centra Service Invocation target)
var api = builder.AddProject<Projects.Centra_Sample_FlotillaSimulation_Api>("flotilla-api")
    .WithHttpEndpoint();

// 3. Consumer Services showcasing multi-protocol subscription
var consumerTcp = builder.AddProject<Projects.Centra_Sample_FlotillaSimulation_Consumer>("flotilla-consumer-tcp")
    .WithHttpEndpoint()
    .WithReference(server)
    .WithReference(api)
    .WaitFor(server)
    .WaitFor(api)
    .WithEnvironment("Centra__Flotilla__Transport", "tcp")
    .WithCentraFlotilla(server, CentraFlotillaResource.TcpEndpointName)
    .WithEnvironment("Centra__Services__flotilla-api__Address", api.GetEndpoint("http"));

var consumerGrpc = builder.AddProject<Projects.Centra_Sample_FlotillaSimulation_Consumer>("flotilla-consumer-grpc")
    .WithHttpEndpoint()
    .WithReference(server)
    .WithReference(api)
    .WaitFor(server)
    .WaitFor(api)
    .WithEnvironment("Centra__Flotilla__Transport", "grpc")
    .WithCentraFlotilla(server, CentraFlotillaResource.GrpcEndpointName)
    .WithEnvironment("Centra__Services__flotilla-api__Address", api.GetEndpoint("http"));

// 4. Producer Service (Emits 1 telemetry message/sec to Flotilla Raft consensus cluster)
var producer = builder.AddProject<Projects.Centra_Sample_FlotillaSimulation_Producer>("flotilla-producer")
    .WithHttpEndpoint()
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("Centra__Flotilla__Transport", "tcp")
    .WithCentraFlotilla(server, CentraFlotillaResource.TcpEndpointName);

builder.Build().Run();

