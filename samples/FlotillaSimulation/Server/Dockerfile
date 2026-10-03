FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ src/
COPY samples/FlotillaSimulation/Server/ samples/FlotillaSimulation/Server/

RUN dotnet publish samples/FlotillaSimulation/Server/Centra.Sample.FlotillaSimulation.Server.csproj \
    -c Release \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 9100 9200/udp 9300 9301
ENTRYPOINT ["dotnet", "Centra.Sample.FlotillaSimulation.Server.dll"]
