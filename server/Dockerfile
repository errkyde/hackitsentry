# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 5000

# ── Build server ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/HackITSentry.Server.csproj server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet restore server/HackITSentry.Server.csproj
COPY server/ server/
RUN --mount=type=cache,id=nuget-server,target=/root/.nuget/packages \
    dotnet publish server/HackITSentry.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Build agent (win-x64) ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS agent-build
WORKDIR /src
COPY agent/HackITSentry.Agent.csproj agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet restore agent/HackITSentry.Agent.csproj -r win-x64
COPY agent/ agent/
RUN --mount=type=cache,id=nuget-agent,target=/root/.nuget/packages \
    dotnet publish agent/HackITSentry.Agent.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -o /agent-publish
RUN grep -oP '(?<=<Version>)[^<]+' agent/HackITSentry.Agent.csproj \
    | head -1 > /agent-publish/agent-version.txt \
    || echo "1.0.0" > /agent-publish/agent-version.txt

# ── Build installer (embeds agent, win-x64) ───────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS installer-build
WORKDIR /src
COPY installer/Installer.csproj installer/
RUN --mount=type=cache,id=nuget-installer,target=/root/.nuget/packages \
    dotnet restore installer/Installer.csproj -r win-x64
COPY installer/ installer/
COPY --from=agent-build /agent-publish/HackITSentry.Agent.exe installer/Resources/HackITSentry.Agent.exe
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
COPY --from=agent-build /agent-publish/HackITSentry.Agent.exe ./files/HackITSentry.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt ./agent-version.txt
RUN VERSION=$(cat agent-version.txt) && \
    sed -i "s/Version=\"[0-9][0-9.]*\"/Version=\"$VERSION\"/" Package.wxs && \
    mkdir -p /msi-publish && wixl -a x64 -o /msi-publish/HackITSentry-Setup.msi Package.wxs

# ── Final image ───────────────────────────────────────────────────────────
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=installer-build /installer-publish/HackITSentry-Setup.exe /app/installer/HackITSentry-Setup.exe
COPY --from=msi-build /msi-publish/HackITSentry-Setup.msi /app/installer/HackITSentry-Setup.msi
COPY --from=agent-build /agent-publish/HackITSentry.Agent.exe /app/agent/HackITSentry.Agent.exe
COPY --from=agent-build /agent-publish/agent-version.txt /app/agent/agent-version.txt
ENTRYPOINT ["dotnet", "HackITSentry.Server.dll"]
