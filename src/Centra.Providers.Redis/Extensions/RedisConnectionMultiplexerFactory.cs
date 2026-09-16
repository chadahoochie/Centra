using Centra.Providers.Redis.Options;
using StackExchange.Redis;

namespace Centra.Providers.Redis.Extensions;

internal static class RedisConnectionMultiplexerFactory
{
    public static IConnectionMultiplexer Create(RedisProviderOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        if (opts.ConfigurationOptions is not null)
        {
            return ConnectionMultiplexer.Connect(opts.ConfigurationOptions);
        }

        var config = ConfigurationOptions.Parse(opts.ConnectionString);
        config.AbortOnConnectFail = false;
        config.CertificateValidation += (sender, certificate, chain, sslPolicyErrors) => true;
        return ConnectionMultiplexer.Connect(config);
    }
}
