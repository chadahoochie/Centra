using Centra.Resilience;

namespace Centra.Hosting.Options;

/// <summary>
/// Options for configuring Centra resilience policies, default pipelines, and telemetry.
/// </summary>
public sealed class CentraResilienceOptions
{
    private readonly List<CentraResiliencePolicyDefinition> _policies = new();

    /// <summary>
    /// Gets the list of resilience policy definitions configured upfront.
    /// </summary>
    public IReadOnlyList<CentraResiliencePolicyDefinition> Policies => _policies;

    /// <summary>
    /// Adds a resilience policy definition to the options.
    /// </summary>
    public CentraResilienceOptions AddPolicy(CentraResiliencePolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _policies.Add(definition);
        return this;
    }
}
