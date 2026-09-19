using Microsoft.Extensions.Options;

namespace Centra.Providers.RabbitMQ.Options;

/// <summary>
/// Rejects provider options that would silently degrade the shutdown drain rather than fail loudly.
/// </summary>
public sealed class RabbitMQProviderOptionsValidator : IValidateOptions<RabbitMQProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMQProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.TotalShutdownDrainTimeout != Timeout.InfiniteTimeSpan
            && options.TotalShutdownDrainTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RabbitMQProviderOptions)}.{nameof(RabbitMQProviderOptions.TotalShutdownDrainTimeout)} must be a positive duration, or Timeout.InfiniteTimeSpan to await in-flight handlers without a limit; got {options.TotalShutdownDrainTimeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}
