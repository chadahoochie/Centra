using Centra.Sample.Bindings.Simulation;

Console.WriteLine("Centra Distributed Application Framework - Schedulers & Bindings");
if (!args.Contains("--demo"))
{
    Console.WriteLine("Executing interactive multi-node simulation (pass --demo for automated mode)...");
}

await BindingsDemoRunner.RunAsync();
