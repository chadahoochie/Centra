using Centra.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Distributed Infrastructure Containers
var redis = builder.AddRedis("redis");

var rabbitUsername = builder.AddParameter("rabbitmq-username", "guest");
var rabbitPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);
var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: rabbitUsername, password: rabbitPassword)
    .WithManagementPlugin();

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("centra-db");

var sqlserver = builder.AddSqlServer("sqlserver")
    .AddDatabase("centra-sql-db");

// 2. Centra Control Plane
var controlPlane = builder.AddProject<Projects.Centra_ControlPlane>("control-plane")
    .WithHttpEndpoint(port: 8080, name: "http");

// 3. Application Services
var orders = builder.AddProject<Projects.Centra_Sample_OrdersService>("orders-service")
    .WithCentra(controlPlane)
    .WithCentraRedis(redis)
    .WithCentraRabbitMQ(rabbitmq)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(controlPlane);

var multiInstance = builder.AddProject<Projects.Centra_Sample_MultiInstance>("multi-instance-service")
    .WithCentra(controlPlane)
    .WithCentraRedis(redis)
    .WithCentraRabbitMQ(rabbitmq)
    .WithReplicas(3)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(controlPlane);

var actors = builder.AddProject<Projects.Centra_Sample_Actors>("actors-service")
    .WithCentra(controlPlane)
    .WithCentraRedis(redis)
    .WaitFor(redis)
    .WaitFor(controlPlane);

var workflows = builder.AddProject<Projects.Centra_Sample_Workflows>("workflows-service")
    .WithCentra(controlPlane)
    .WithCentraRedis(redis)
    .WaitFor(redis)
    .WaitFor(controlPlane);

var bindings = builder.AddProject<Projects.Centra_Sample_Bindings>("bindings-service")
    .WithCentra(controlPlane)
    .WithCentraRedis(redis)
    .WaitFor(redis)
    .WaitFor(controlPlane);

var resilience = builder.AddProject<Projects.Centra_Sample_Resilience>("resilience-service")
    .WithCentra(controlPlane)
    .WaitFor(controlPlane);

builder.Build().Run();
