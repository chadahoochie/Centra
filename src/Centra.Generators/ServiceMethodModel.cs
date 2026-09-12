using System;

namespace Centra.Generators;

internal sealed record ServiceMethodModel(
    string MethodName,
    string TargetMethodName,
    string HttpVerb,
    string ReturnType,
    string? GenericReturnType,
    bool IsValueTask,
    EquatableArray<ServiceMethodParameterModel> Parameters,
    ServiceMethodParameterModel? BodyParameter,
    ServiceMethodParameterModel? CancellationTokenParameter);
