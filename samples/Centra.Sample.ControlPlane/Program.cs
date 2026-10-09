using System;
using System.Linq;
using Centra.ControlPlane.Dashboard;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.Sample.ControlPlane.Services;
using Centra.Sample.ControlPlane.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
{
    var simResult = await ControlPlaneDemoRunner.RunAsync(args);
    return simResult.AllStepsSucceeded ? 0 : 1;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddCentraControlPlane();
builder.Services.AddCentraControlPlaneSecurity(options =>
{
    options.Enabled = false; // Permissive for local interactive browsing
});

builder.Services.AddHostedService<SimulatedNodeFleetHostedService>();

var app = builder.Build();

app.MapCentraControlPlaneEndpoints();
app.MapCentraDashboard(new ControlPlaneDashboardOptions
{
    Enabled = true,
    Path = "/dashboard",
    Title = "Centra Control Plane Dashboard"
});

app.MapGet("/", () => Results.Redirect("/dashboard"));

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine();
Console.WriteLine("================================================================================");
Console.WriteLine(" CENTRA DISTRIBUTED APPLICATION FRAMEWORK - CONTROL PLANE & DASHBOARD SERVER     ");
Console.WriteLine("================================================================================");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine(" Control Plane Dashboard: http://localhost:5050/dashboard");
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine(" Simulated Fleets Active: cluster-alpha (4 nodes), cluster-beta (3 nodes), cluster-gamma (4 nodes)");
Console.ResetColor();
Console.WriteLine();

app.Run();
return 0;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class Program { }
