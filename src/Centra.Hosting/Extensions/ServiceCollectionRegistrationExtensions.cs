using Microsoft.Extensions.DependencyInjection;

namespace Centra.Hosting.Extensions;

/// <summary>
/// Lets registration helpers stay idempotent when a component is registered both by attribute
/// scanning and by an explicit call - whichever registration for a given type happens last wins,
/// replacing any earlier one, rather than producing two entries for the same component. This
/// matches the common call pattern of AddCentra(...) (which scans) followed by explicit
/// AddCentraX&lt;T&gt;() overrides for specific types further down in Program.cs.
/// </summary>
internal static class ServiceCollectionRegistrationExtensions
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
