using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Centra.Generators;

internal static class ServiceClientSyntaxParser
{
    private const string ServiceClientAttributeShortName = "ServiceClient";
    private const string ServiceClientAttributeFullName = "Centra.Invocation.ServiceClientAttribute";
    private const string ServiceMethodAttributeShortName = "ServiceMethod";

    public static ServiceClientModel? Parse(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not InterfaceDeclarationSyntax interfaceDeclaration)
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration, cancellationToken) as INamedTypeSymbol;
        if (symbol is null)
            return null;

        var clientAttr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == ServiceClientAttributeShortName ||
            a.AttributeClass?.Name == ServiceClientAttributeShortName + "Attribute" ||
            a.AttributeClass?.ToDisplayString() == ServiceClientAttributeFullName);

        if (clientAttr is null)
            return null;

        var appId = clientAttr.ConstructorArguments.Length > 0
            ? clientAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty
            : string.Empty;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString();
        var interfaceName = symbol.Name;
        var proxyName = interfaceName.StartsWith("I") && interfaceName.Length > 1
            ? interfaceName.Substring(1) + "Proxy"
            : interfaceName + "Proxy";

        var methods = new List<ServiceMethodModel>();
        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary) continue;

            var methodAttr = member.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name == ServiceMethodAttributeShortName ||
                a.AttributeClass?.Name == ServiceMethodAttributeShortName + "Attribute");

            var targetMethodName = methodAttr?.ConstructorArguments.Length > 0
                ? methodAttr.ConstructorArguments[0].Value?.ToString() ?? member.Name
                : member.Name;

            var httpVerb = methodAttr?.ConstructorArguments.Length > 1
                ? methodAttr.ConstructorArguments[1].Value?.ToString() ?? "POST"
                : "POST";

            var returnTypeStr = member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var isValueTask = returnTypeStr.Contains("ValueTask");

            string? genericReturnType = null;
            if (member.ReturnType is INamedTypeSymbol namedReturn && namedReturn.TypeArguments.Length == 1)
            {
                genericReturnType = namedReturn.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            var parameters = new List<ServiceMethodParameterModel>();
            ServiceMethodParameterModel? bodyParam = null;
            ServiceMethodParameterModel? ctParam = null;

            foreach (var p in member.Parameters)
            {
                var pTypeStr = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isCt = pTypeStr.Contains("CancellationToken");
                var paramModel = new ServiceMethodParameterModel(p.Name, pTypeStr, isCt);
                parameters.Add(paramModel);

                if (isCt)
                {
                    ctParam = paramModel;
                }
                else if (bodyParam is null)
                {
                    bodyParam = paramModel;
                }
            }

            methods.Add(new ServiceMethodModel(
                member.Name,
                targetMethodName,
                httpVerb,
                returnTypeStr,
                genericReturnType,
                isValueTask,
                new EquatableArray<ServiceMethodParameterModel>(parameters.ToArray()),
                bodyParam,
                ctParam));
        }

        return new ServiceClientModel(
            ns,
            interfaceName,
            proxyName,
            appId,
            new EquatableArray<ServiceMethodModel>(methods.ToArray()));
    }
}
