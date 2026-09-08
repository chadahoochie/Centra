FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY samples/Centra.Sample.DockerStack.Simulator/ samples/Centra.Sample.DockerStack.Simulator/

RUN dotnet publish samples/Centra.Sample.DockerStack.Simulator/Centra.Sample.DockerStack.Simulator.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "Centra.Sample.DockerStack.Simulator.dll"]
