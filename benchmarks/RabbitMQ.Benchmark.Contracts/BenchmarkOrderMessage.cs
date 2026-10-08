namespace RabbitMQ.Benchmark.Contracts;

public sealed class BenchmarkOrderMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public long SentTimestampTicks { get; set; }

    public byte[] Payload { get; set; } = Array.Empty<byte>();
}
