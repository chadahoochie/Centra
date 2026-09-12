using System;

namespace Centra.Generators;

internal sealed record ActorMethodModel(
    string MethodName,
    string ReturnType,
    string? GenericReturnType,
    bool IsValueTask,
    EquatableArray<ActorMethodParameterModel> Parameters,
    ActorMethodParameterModel? CancellationTokenParameter);
