using System.Diagnostics;

namespace Centra.ControlPlane.Diagnostics;

public static class ControlPlaneDiagnostics
{
    public const string ActivitySourceName = "Centra.ControlPlane";
    public const string Version = "1.0.0";

    public static readonly ActivitySource Source = new(ActivitySourceName, Version);

    public static Activity? StartCatalogActivity(string operation, string componentName)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity($"Centra.ControlPlane.{operation}", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("centra.component.name", componentName);
            activity.SetTag("centra.operation", operation);
        }
        return activity;
    }

    public static Activity? StartSyncActivity(string eventType, string? componentName)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity("Centra.ControlPlane.Sync", ActivityKind.Producer);
        if (activity is not null)
        {
            activity.SetTag("centra.sync.type", eventType);
            if (componentName is not null)
            {
                activity.SetTag("centra.component.name", componentName);
            }
        }
        return activity;
    }
}
