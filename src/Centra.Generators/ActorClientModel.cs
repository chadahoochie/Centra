using System;

namespace Centra.Generators;

internal sealed record ActorClientModel(
    string Namespace,
    string InterfaceName,
    string ProxyClassName,
    string ActorTypeName,
    EquatableArray<ActorMethodModel> Methods);
