using Bogus;
using Centra.Sample.RabbitSimulation.Contracts.Models;

namespace Centra.Sample.RabbitSimulation.Producer.Services;

public sealed class BogusOrderGenerator
{
    private readonly Faker<OrderMessage> _faker;

    public BogusOrderGenerator()
    {
        _faker = new Faker<OrderMessage>()
            .CustomInstantiator(f => new OrderMessage(
                OrderId: $"ord-{f.Random.AlphaNumeric(8).ToLowerInvariant()}",
                CustomerName: f.Name.FullName(),
                CustomerEmail: f.Internet.Email(),
                ItemDescription: f.Commerce.ProductName(),
                Category: f.Commerce.Categories(1)[0],
                Quantity: f.Random.Int(1, 10),
                UnitPrice: Math.Round(f.Random.Decimal(12.50m, 350.00m), 2),
                ShippingAddress: $"{f.Address.StreetAddress()}, {f.Address.City()}, {f.Address.StateAbbr()} {f.Address.ZipCode()}",
                CreatedAt: DateTimeOffset.UtcNow));
    }

    public OrderMessage Generate() => _faker.Generate();
}
