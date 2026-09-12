using System;

namespace Centra.Generators;

internal sealed record ActorMethodParameterModel(
    string Name,
    string Type,
    bool IsCancellationToken);
