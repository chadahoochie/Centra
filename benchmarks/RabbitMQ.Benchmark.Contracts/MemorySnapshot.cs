namespace RabbitMQ.Benchmark.Contracts;

public readonly record struct MemorySnapshot(
    long AllocatedBytes,
    int Gen0,
    int Gen1,
    int Gen2)
{
    public static MemorySnapshot Capture()
    {
        return new MemorySnapshot(
            GC.GetTotalAllocatedBytes(precise: true),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    public static MemoryDelta ComputeDelta(MemorySnapshot before, MemorySnapshot after)
    {
        return new MemoryDelta(
            after.AllocatedBytes - before.AllocatedBytes,
            after.Gen0 - before.Gen0,
            after.Gen1 - before.Gen1,
            after.Gen2 - before.Gen2);
    }
}
