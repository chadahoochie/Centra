FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install clang and zlib for native AOT compilation
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ src/

RUN dotnet publish src/Centra.ControlPlane/Centra.ControlPlane.csproj \
    -c Release \
    -r linux-x64 \
    -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app/Centra.ControlPlane ./

USER app
EXPOSE 8080
ENTRYPOINT ["./Centra.ControlPlane"]
