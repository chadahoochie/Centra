namespace Centra.Resilience;

/// <summary>
/// Resolves compiled resilience pipelines for specific targets and components.
/// </summary>
public interface IResiliencePipelineProvider
{
    /// <summary>
    /// Gets a resilience pipeline by name.
    /// </summary>
    IResiliencePipeline GetPipeline(string pipelineName);

    /// <summary>
    /// Gets the resilience pipeline configured for a specific service invocation AppId.
    /// </summary>
    IResiliencePipeline GetServiceInvocationPipeline(string serviceAppId);

    /// <summary>
    /// Gets the resilience pipeline configured for a specific state store.
    /// </summary>
    IResiliencePipeline GetStateStorePipeline(string storeName);

    /// <summary>
    /// Gets the resilience pipeline configured for a specific pub/sub component.
    /// </summary>
    IResiliencePipeline GetPubSubPipeline(string pubSubName);
}
