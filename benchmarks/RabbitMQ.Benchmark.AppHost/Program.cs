var builder = DistributedApplication.CreateBuilder(args);

// 1. RabbitMQ Container with Management Plugin
var username = builder.AddParameter("rabbitmq-username", "guest");
var password = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: username, password: password)
    .WithManagementPlugin();

// 2. Centra Benchmark Harness
var centraBenchmark = builder.AddProject<Projects.Centra_Benchmarks_RabbitMQ>("centra-benchmark")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("RABBITMQ_URI", rabbitmq.GetEndpoint("tcp"))
    .WithArgs("--scenario", "endtoend", "--messages", "20000", "--concurrency", "4");

// 3. Package.Queueing Benchmark Harness
var pkgBenchmark = builder.AddProject<Projects.PackageQueueing_Benchmarks_RabbitMQ>("package-queueing-benchmark")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithEnvironment("RABBITMQ_URI", rabbitmq.GetEndpoint("tcp"))
    .WithArgs("--scenario", "endtoend", "--messages", "20000", "--concurrency", "4");

builder.Build().Run();
