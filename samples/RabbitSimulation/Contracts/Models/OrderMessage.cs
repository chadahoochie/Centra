using Centra.Events;

namespace Centra.Sample.RabbitSimulation.Contracts.Models;

//EventContract allows fThis allows consumers and event routers to inspect the schema version before deserialization, enabling   
//side-by-side migration between contract versions (e.g., v1 and v2) over the same topic.
[EventContract("orders.new", Version = "1")]
public sealed record OrderMessage(
    string OrderId,
    string CustomerName,
    string CustomerEmail,
    string ItemDescription,
    string Category,
    int Quantity,
    decimal UnitPrice,
    string ShippingAddress,
    DateTimeOffset CreatedAt);
