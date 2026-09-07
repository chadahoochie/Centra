using System.Net;
using System.Text;
using System.Text.Json;
using Centra.Actors;
using Centra.Hosting.Extensions;
using Centra.Providers.InMemory.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class CentraActorEndpointsTests
{
    [Fact]
    public async Task Should_Route_Http_Post_To_Actor_Method_And_Return_Result()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddCentra(options => options.AppId = "actor-test-app");
        builder.Services.AddCentraInMemory();
        builder.Services.AddCentraActors();
        builder.Services.AddCentraActor<GreetingActor, IGreetingActor>();

        var app = builder.Build();
        app.MapCentraEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();

        // Act: POST /centra/actors/GreetingActor/guest-1/method/GreetAsync with JSON body "World"
        var requestBody = JsonSerializer.Serialize("World");
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/centra/actors/GreetingActor/guest-1/method/GreetAsync", content);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var responseString = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<string>(responseString);
        result.ShouldBe("Hello, World!");

        await app.StopAsync();
    }

    [Fact]
    public async Task Should_Return_404_When_Actor_Method_Does_Not_Exist()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddCentra(options => options.AppId = "actor-test-app");
        builder.Services.AddCentraInMemory();
        builder.Services.AddCentraActors();
        builder.Services.AddCentraActor<GreetingActor, IGreetingActor>();

        var app = builder.Build();
        app.MapCentraEndpoints();
        await app.StartAsync();

        var client = app.GetTestClient();

        // Act: POST nonexistent method
        var response = await client.PostAsync("/centra/actors/GreetingActor/guest-1/method/NoSuchMethod", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await app.StopAsync();
    }

    public interface IGreetingActor : IActor
    {
        Task<string> GreetAsync(string name);
    }

    public sealed class GreetingActor : Actor, IGreetingActor
    {
        public Task<string> GreetAsync(string name)
        {
            return Task.FromResult($"Hello, {name}!");
        }
    }
}
