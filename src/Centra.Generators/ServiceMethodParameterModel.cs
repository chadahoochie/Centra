using System;

namespace Centra.Generators;

internal sealed record ServiceMethodParameterModel(
    string Name,
    string Type,
    bool IsCancellationToken);
