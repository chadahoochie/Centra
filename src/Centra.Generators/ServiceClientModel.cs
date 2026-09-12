using System;

namespace Centra.Generators;

internal sealed record ServiceClientModel(
    string Namespace,
    string InterfaceName,
    string ProxyClassName,
    string AppId,
    EquatableArray<ServiceMethodModel> Methods);
