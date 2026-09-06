namespace Centra.PubSub;

public enum EventHandlingResult
{
    Success,
    Retry,
    DeadLetter,
    Drop
}
