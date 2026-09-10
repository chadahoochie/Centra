var builder = DistributedApplication.CreateBuilder(args);

// 1. RabbitMQ Container with Management Plugin
var username = builder.AddParameter("rabbitmq-username", "admin");
var password = builder.AddParameter("rabbitmq-password", "admin", secret: true);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: username, password: password)
    .WithManagementPlugin();

// 2. API Service (Centra Service Invocation target)
var api = builder.AddProject<Projects.Centra_Sample_RabbitSimulation_Api>("rabbit-api")
    .WithHttpEndpoint();

// 3. Consumer Service (Subscribes to RabbitMQ topic and invokes rabbit-api)
var consumer = builder.AddProject<Projects.Centra_Sample_RabbitSimulation_Consumer>("rabbit-consumer")
    .WithHttpEndpoint()
    .WithReference(rabbitmq)
    .WithReference(api)
    .WaitFor(rabbitmq)
    .WaitFor(api)
    .WithEnvironment("Centra__Services__rabbit-api__Address", api.GetEndpoint("http"));

// 4. Producer Service (Emits 1 bogus message/sec to RabbitMQ)
var producer = builder.AddProject<Projects.Centra_Sample_RabbitSimulation_Producer>("rabbit-producer")
    .WithHttpEndpoint()
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

builder.Build().Run();
