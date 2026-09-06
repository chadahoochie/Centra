namespace Centra.Locks;

public readonly record struct LockAcquisitionResult(bool Succeeded, IDistributedLock? Lock);
