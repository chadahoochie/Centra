using Centra.Sample.Bindings.Simulation;

if (args.Contains("--demo"))
{
    await BindingsDemoRunner.RunAsync();
    return;
}

Console.WriteLine("Running Centra.Sample.Bindings in interactive mode. Pass --demo to execute automated simulation.");
await BindingsDemoRunner.RunAsync();
