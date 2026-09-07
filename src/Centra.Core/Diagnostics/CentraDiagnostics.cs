using System.Diagnostics;

namespace Centra.Diagnostics;

public static class CentraDiagnostics
{
    public const string SourceName = "Centra";
    public const string Version = "1.0.0";

    public static readonly ActivitySource Source = new(SourceName, Version);

    public static Activity? StartPublishActivity(string pubSubName, string topic)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity("Centra.PubSub.Publish", ActivityKind.Producer);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Publish {topic}";
            activity.SetTag("centra.component", "pubsub");
            activity.SetTag("centra.pubsub.name", pubSubName);
            activity.SetTag("messaging.destination", topic);
        }

        return activity;
    }

    public static Activity? StartProcessActivity(string pubSubName, string topic, ActivityContext parentContext = default)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = parentContext != default
            ? Source.StartActivity("Centra.PubSub.Process", ActivityKind.Consumer, parentContext)
            : Source.StartActivity("Centra.PubSub.Process", ActivityKind.Consumer);

        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Process {topic}";
            activity.SetTag("centra.component", "pubsub");
            activity.SetTag("centra.pubsub.name", pubSubName);
            activity.SetTag("messaging.source", topic);
            activity.SetTag("messaging.operation", "process");
        }

        return activity;
    }

    public static Activity? StartInvokeClientActivity(string targetAppId, string method)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity($"Centra.Invoke.{method}", ActivityKind.Client);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Invoke {targetAppId}/{method}";
            activity.SetTag("centra.component", "invoke");
            activity.SetTag("peer.service", targetAppId);
            activity.SetTag("rpc.system", "centra");
            activity.SetTag("rpc.method", method);
        }

        return activity;
    }

    public static Activity? StartInvokeServerActivity(string method, ActivityContext parentContext = default)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = parentContext != default
            ? Source.StartActivity($"Centra.Handle.{method}", ActivityKind.Server, parentContext)
            : Source.StartActivity($"Centra.Handle.{method}", ActivityKind.Server);

        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Handle {method}";
            activity.SetTag("centra.component", "invoke");
            activity.SetTag("rpc.system", "centra");
            activity.SetTag("rpc.method", method);
        }

        return activity;
    }

    public static Activity? StartStateActivity(string operation, string storeName, string key)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity($"Centra.State.{operation}", ActivityKind.Internal);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"State {operation} {storeName}/{key}";
            activity.SetTag("centra.component", "state");
            activity.SetTag("centra.store.name", storeName);
            activity.SetTag("centra.key", key);
            activity.SetTag("centra.operation", operation);
        }

        return activity;
    }

    public static Activity? StartLockActivity(string operation, string lockStore, string resourceId)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity($"Centra.Lock.{operation}", ActivityKind.Internal);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Lock {operation} {lockStore}/{resourceId}";
            activity.SetTag("centra.component", "lock");
            activity.SetTag("centra.lock_store.name", lockStore);
            activity.SetTag("centra.resource", resourceId);
        }

        return activity;
    }

    public static Activity? StartBindingOutputActivity(string bindingName, string? operation)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity("Centra.Binding.Invoke", ActivityKind.Producer);
        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Binding Invoke {bindingName}";
            activity.SetTag("centra.component", "binding");
            activity.SetTag("centra.binding.name", bindingName);
            if (!string.IsNullOrEmpty(operation))
            {
                activity.SetTag("centra.binding.operation", operation);
            }
        }

        return activity;
    }

    public static Activity? StartBindingInputActivity(string bindingName, ActivityContext parentContext = default)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = parentContext != default
            ? Source.StartActivity("Centra.Binding.Trigger", ActivityKind.Consumer, parentContext)
            : Source.StartActivity("Centra.Binding.Trigger", ActivityKind.Consumer);

        if (activity is not null && activity.IsAllDataRequested)
        {
            activity.DisplayName = $"Binding Trigger {bindingName}";
            activity.SetTag("centra.component", "binding");
            activity.SetTag("centra.binding.name", bindingName);
        }

        return activity;
    }
}
