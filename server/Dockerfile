# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 5000

# ── Build server ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/HITGuard.Server.csproj server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet restore server/HITGuard.Server.csproj
COPY server/ server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet publish server/HITGuard.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Build agent (win-x64) ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS agent-build
WORKDIR /src
COPY agent/HITGuard.Agent.csproj agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet restore agent/HITGuard.Agent.csproj -r win-x64
COPY agent/ agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet publish agent/HITGuard.Agent.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -o /agent-publish
RUN grep -oP '(?<=<Version>)[^<]+' agent/HITGuard.Agent.csproj \
    | head -1 > /agent-publish/agent-version.txt \
    || echo "1.0.0" > /agent-publish/agent-version.txt

# ── Build installer (embeds agent, win-x64) ───────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS installer-build
WORKDIR /src
COPY installer/Installer.csproj installer/
RUN --mount=type=cache,id=nuget-installer,target=/root/.nuget/packages \
    dotnet restore installer/Installer.csproj -r win-x64
COPY installer/ installer/
COPY --from=agent-build /agent-publish/HITGuard.Agent.exe installer/Resources/HITGuard.Agent.exe
RUN --mount=type=cache,id=nuget-installer,target=/root/.nuget/packages \
    dotnet publish installer/Installer.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -o /installer-publish

# ── Build MSI installer (wixl from msitools, Linux-native) ───────────────
FROM fedora:38 AS msi-build
RUN --mount=type=cache,id=dnf-cache,target=/var/cache/dnf \
    dnf install -y msitools
WORKDIR /msi
COPY installer-msi/Package.wxs .
COPY installer-msi/files/appsettings.json ./files/appsettings.json
COPY --from=agent-build /agent-publish/HITGuard.Agent.exe ./files/HITGuard.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt ./agent-version.txt
RUN VERSION=$(cat agent-version.txt) && \
    sed -i "s/Version=\"[0-9][0-9.]*\"/Version=\"$VERSION\"/" Package.wxs && \
    mkdir -p /msi-publish && wixl -a x64 -o /msi-publish/HITGuard-Setup.msi Package.wxs

# ── Final image ───────────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=installer-build /installer-publish/HITGuard-Setup.exe /app/installer/HITGuard-Setup.exe
COPY --from=msi-build /msi-publish/HITGuard-Setup.msi /app/installer/HITGuard-Setup.msi
COPY --from=agent-build /agent-publish/HITGuard.Agent.exe /app/agent/HITGuard.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt /app/agent/agent-version.txt
ENTRYPOINT ["dotnet", "HITGuard.Server.dll"]
