using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCentraControlPlane();

var app = builder.Build();

app.MapCentraControlPlaneEndpoints();

app.Run();

public partial class Program { }
