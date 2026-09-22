using Centra.Bindings;

namespace Centra.Bindings.Routing;

public sealed record CentraCronJobRegistration(
    string JobName,
    string CronExpression,
    Type JobType,
    CronScheduleOptions? Options = null);
