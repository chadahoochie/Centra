namespace Centra.Sample.Workflows.Domain;

/// <summary>
/// Domain model representing reserved inventory units.
/// </summary>
public sealed record InventoryReservation(
    string ReservationId,
    string ProductId,
    int Quantity);
