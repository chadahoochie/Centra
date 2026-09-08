FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ src/
COPY samples/Centra.Sample.DockerStack/ samples/Centra.Sample.DockerStack/

RUN dotnet publish samples/Centra.Sample.DockerStack/Centra.Sample.DockerStack.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Centra.Sample.DockerStack.dll"]
