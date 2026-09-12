using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Centra.Generators;

[Generator]
public sealed class CentraProxyGenerator : IIncrementalGenerator
{
    private const string ServiceClientAttributeShortName = "ServiceClient";
    private const string ServiceClientAttributeFullName = "Centra.Invocation.ServiceClientAttribute";
    private const string ServiceMethodAttributeShortName = "ServiceMethod";
    private const string IActorInterfaceShortName = "IActor";
    private const string IActorInterfaceFullName = "Centra.Actors.IActor";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var serviceClients = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids && ids.AttributeLists.Count > 0,
                transform: static (ctx, ct) => ServiceClientSyntaxParser.Parse(ctx, ct))
            .Where(static m => m is not null);

        var actorClients = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InterfaceDeclarationSyntax ids && ids.BaseList is not null,
                transform: static (ctx, ct) => ActorClientSyntaxParser.Parse(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(serviceClients, static (spc, model) =>
        {
            if (model is null) return;
            var code = ServiceClientSourceEmitter.Emit(model);
            spc.AddSource($"{model.ProxyClassName}.g.cs", SourceText.From(code, Encoding.UTF8));
        });

        context.RegisterSourceOutput(actorClients, static (spc, model) =>
        {
            if (model is null) return;
            var code = ActorClientSourceEmitter.Emit(model);
            spc.AddSource($"{model.ProxyClassName}.g.cs", SourceText.From(code, Encoding.UTF8));
        });
    }
}
