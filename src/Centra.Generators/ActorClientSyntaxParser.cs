using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Centra.Generators;

internal static class ActorClientSyntaxParser
{
    private const string IActorInterfaceShortName = "IActor";
    private const string IActorInterfaceFullName = "Centra.Actors.IActor";

    public static ActorClientModel? Parse(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not InterfaceDeclarationSyntax interfaceDeclaration)
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration, cancellationToken) as INamedTypeSymbol;
        if (symbol is null)
            return null;

        if (symbol.Name == IActorInterfaceShortName || symbol.Name == "IActorProxy")
            return null;

        var implementsActor = symbol.AllInterfaces.Any(i =>
            i.Name == IActorInterfaceShortName ||
            i.ToDisplayString() == IActorInterfaceFullName);

        if (!implementsActor)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString();
        var interfaceName = symbol.Name;
        var cleanName = interfaceName.StartsWith("I") && interfaceName.Length > 1
            ? interfaceName.Substring(1)
            : interfaceName;
        var proxyName = cleanName + "Proxy";
        var actorTypeName = cleanName.EndsWith("Actor") ? cleanName : cleanName + "Actor";

        var methods = new List<ActorMethodModel>();
        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary) continue;

            var returnTypeStr = member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isValueTask = returnTypeStr.Contains("ValueTask");

            string? genericReturnType = null;
            if (member.ReturnType is INamedTypeSymbol namedReturn && namedReturn.TypeArguments.Length == 1)
            {
                genericReturnType = namedReturn.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var parameters = new List<ActorMethodParameterModel>();
            ActorMethodParameterModel? ctParam = null;

            foreach (var p in member.Parameters)
            {
                var pTypeStr = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isCt = pTypeStr.Contains("CancellationToken");
                var paramModel = new ActorMethodParameterModel(p.Name, pTypeStr, isCt);
                parameters.Add(paramModel);

                if (isCt)
                {
                    ctParam = paramModel;
                }
            }

            methods.Add(new ActorMethodModel(
                member.Name,
                returnTypeStr,
                genericReturnType,
                isValueTask,
                new EquatableArray<ActorMethodParameterModel>(parameters.ToArray()),
                ctParam));
        }

        return new ActorClientModel(
            ns,
            interfaceName,
            proxyName,
            actorTypeName,
            new EquatableArray<ActorMethodModel>(methods.ToArray()));
    }
}
