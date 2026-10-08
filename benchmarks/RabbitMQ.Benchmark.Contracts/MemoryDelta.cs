namespace RabbitMQ.Benchmark.Contracts;

public readonly record struct MemoryDelta(
    long AllocatedBytes,
    int Gen0,
    int Gen1,
    int Gen2);
