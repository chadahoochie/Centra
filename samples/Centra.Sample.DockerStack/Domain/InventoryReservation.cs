namespace Centra.Sample.DockerStack.Domain;

public sealed record InventoryReservation(
    string ReservationId,
    string ProductId,
    int Quantity);
