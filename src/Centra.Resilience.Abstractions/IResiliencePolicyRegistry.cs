namespace Centra.Resilience;

/// <summary>
/// Manages registration and retrieval of resilience policy definitions.
/// </summary>
public interface IResiliencePolicyRegistry
{
    /// <summary>
    /// Registers or updates a resilience policy definition.
    /// </summary>
    void RegisterPolicy(CentraResiliencePolicyDefinition definition);

    /// <summary>
    /// Removes a resilience policy definition by name.
    /// </summary>
    bool RemovePolicy(string policyName);

    /// <summary>
    /// Gets a resilience policy definition by name if registered.
    /// </summary>
    CentraResiliencePolicyDefinition? GetPolicy(string policyName);

    /// <summary>
    /// Gets all registered resilience policy definitions.
    /// </summary>
    IReadOnlyCollection<CentraResiliencePolicyDefinition> GetAllPolicies();
}
