using Centra.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var controlPlane = builder.AddCentraControlPlane("control-plane");

var orders = builder.AddProject<Projects.Centra_Sample_OrdersService>("orders-service")
    .WithCentra(controlPlane);

var multiInstance = builder.AddProject<Projects.Centra_Sample_MultiInstance>("multi-instance-service")
    .WithCentra(controlPlane)
    .WithReplicas(3);

builder.Build().Run();
