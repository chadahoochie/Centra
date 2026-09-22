namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Lets registration helpers stay idempotent when a component is registered both by attribute
/// scanning and by an explicit call - whichever registration for a given type happens last wins,
/// replacing any earlier one, rather than producing two entries for the same component.
/// </summary>
public static class ServiceCollectionRegistrationExtensions
{
    public static void ReplaceRegistrationFor<TRegistration>(
        this IServiceCollection services,
        Func<TRegistration, bool> predicate,
        TRegistration registration)
        where TRegistration : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TRegistration) &&
                services[i].ImplementationInstance is TRegistration existing &&
                predicate(existing))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton(registration);
    }
}
