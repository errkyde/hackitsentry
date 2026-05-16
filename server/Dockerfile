# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 5000

# ── Build server ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/HITSight.Server.csproj server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet restore server/HITSight.Server.csproj
COPY server/ server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet publish server/HITSight.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Build agent (win-x64) ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS agent-build
WORKDIR /src
COPY agent/HITSight.Agent.csproj agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet restore agent/HITSight.Agent.csproj -r win-x64
COPY agent/ agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet publish agent/HITSight.Agent.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -o /agent-publish
RUN grep -oP '(?<=<Version>)[^<]+' agent/HITSight.Agent.csproj \
    | head -1 > /agent-publish/agent-version.txt \
    || echo "1.0.0" > /agent-publish/agent-version.txt

# ── Build installer (embeds agent, win-x64) ───────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS installer-build
WORKDIR /src
COPY installer/Installer.csproj installer/
RUN --mount=type=cache,id=nuget-installer,target=/root/.nuget/packages \
    dotnet restore installer/Installer.csproj -r win-x64
COPY installer/ installer/
COPY --from=agent-build /agent-publish/HITSight.Agent.exe installer/Resources/HITSight.Agent.exe
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
COPY --from=agent-build /agent-publish/HITSight.Agent.exe ./files/HITSight.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt ./agent-version.txt
RUN VERSION=$(cat agent-version.txt) && \
    sed -i "s/Version=\"[0-9][0-9.]*\"/Version=\"$VERSION\"/" Package.wxs && \
    mkdir -p /msi-publish && wixl -a x64 -o /msi-publish/HITSight-Setup.msi Package.wxs

# ── Final image ───────────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=installer-build /installer-publish/HITSight-Setup.exe /app/installer/HITSight-Setup.exe
COPY --from=msi-build /msi-publish/HITSight-Setup.msi /app/installer/HITSight-Setup.msi
COPY --from=agent-build /agent-publish/HITSight.Agent.exe /app/agent/HITSight.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt /app/agent/agent-version.txt
ENTRYPOINT ["dotnet", "HITSight.Server.dll"]
