# HackIT Sentry

Zentrales IT-Monitoring-System. Agenten auf Windows-Geräten melden sich regelmäßig beim Server.

## Architektur

```
[Browser] ──► [frontend :8030]  ──► /api/*        ──► [server :5000]
                                 ──► /install/*    ──► [server :5000]

[Agent]   ──► [outpost  :8031]  ──► /install/*    ──► [server :5000]
                                 ──► /api/agent/*  ──► [server :5000]
                                 ──► /             ──► 403

[server] ──► [db :5432 postgres]
```

**Container:**
| Name | Image | Port | Aufgabe |
|------|-------|------|---------|
| `db` | postgres:16-alpine | intern | Datenbank |
| `server` | ghcr.io/errkyde/hackitsentry-server | intern :5000 | ASP.NET Core API |
| `frontend` | ghcr.io/errkyde/hackitsentry-frontend | 8030 | React SPA + nginx-Proxy |
| `outpost` | nginx:alpine | 8031 | Externer Zugang für Agenten |

## Deployment

### Portainer / Dockhand (empfohlen)

Docker Images werden automatisch bei jedem Push auf `main` gebaut und auf `ghcr.io` veröffentlicht. Für eine neue Installation reicht es, den Stack auf den Git-Repo zu zeigen.

**1. Stack anlegen**

In Portainer: *Stacks → Add stack → Repository*

| Feld | Wert |
|------|------|
| Repository URL | `https://github.com/errkyde/hackitsentry` |
| Compose path | `docker-compose.yml` |

**2. Env-Variablen setzen**

| Variable | Beschreibung |
|----------|--------------|
| `POSTGRES_PASSWORD` | Beliebiges Passwort für die Datenbank |
| `JWT_KEY` | Zufälliger String, mind. 32 Zeichen |
| `ENCRYPTION_KEY` | Zufälliger String, **genau** 32 Zeichen |
| `OUTPOST_PUBLIC_URL` | Extern erreichbare URL des Servers, z.B. `https://sentry.example.com` |

Alle weiteren optionalen Variablen: siehe `.env.example`.

**3. Deploy**

Stack deployen — fertig. Die Datenbank wird beim ersten Start automatisch eingerichtet.

Bei jedem Push auf `main` bauen die GitHub Actions neue Images. Portainer/Dockhand kann so konfiguriert werden, dass es automatisch auf neue Images prüft und die Container neu startet.

---

### Manuell (ohne Portainer)

```bash
git clone https://github.com/errkyde/hackitsentry && cd hackitsentry
./setup.sh          # erzeugt .env mit zufälligen Secrets
# OUTPOST_PUBLIC_URL in .env eintragen
docker compose up -d
```

## Wichtige Env-Variablen

| Variable | Pflicht | Beschreibung |
|----------|---------|--------------|
| `POSTGRES_PASSWORD` | ja | DB-Passwort |
| `JWT_KEY` | ja | Min. 32 Zeichen |
| `ENCRYPTION_KEY` | ja | Genau 32 Zeichen (AES-256) |
| `OUTPOST_PUBLIC_URL` | ja | Extern erreichbare URL des Outpost-Containers, z.B. `http://192.168.1.100:8031` |
| `CHECKIN_INTERVAL_MINUTES` | nein | Standard: 30 |
| `EMAIL_HOST` | nein | SMTP-Host (leer = deaktiviert) |
| `OUTPOST_PORT` | nein | Standard: 8031 |
| `FRONTEND_PORT` | nein | Standard: 8030 |

## Installer-Download-Flow

1. Admin erstellt Installationslink im Frontend (→ `POST /api/install-tokens`)
2. Link wird per E-Mail oder manuell geteilt
3. Nutzer öffnet `{OUTPOST_PUBLIC_URL}/install/{token}`
4. Outpost-nginx proxied zu `server:5000/install/{token}`
5. Server validiert Token, patcht die .exe on-the-fly, streamt sie
6. Token wird als `Used=true` markiert (einmalige Nutzung)

**Wichtig:** `OUTPOST_PUBLIC_URL` muss korrekt gesetzt sein — sonst zeigt der E-Mail-Link auf die interne Hostname-URL.

## Binary-Patching (InstallerService)

Die `.exe` enthält UTF-16LE-Platzhalter:
- `HACKIT_SERVER_URL:====...` — 433 Zeichen gesamt (18 Prefix + 415 Wert)
- `HACKIT_INSTALL_TOK:===...` — 128 Zeichen gesamt (19 Prefix + 109 Wert)

`InstallerService` scannt beim Start einmalig die Offsets (erste 15 MB).
`PatchedFileStream` streamt die Datei und überschreibt die Slots on-the-fly — kein 138-MB-RAM-Load.

## nginx-Konfiguration

### frontend/nginx.conf
- `location /api/` → proxy zu `server:${SERVER_PORT}`
- `location /install/` → proxy zu `server:${SERVER_PORT}` + `proxy_buffering off` (wichtig für 138-MB-Download)
- `location /` → SPA-Fallback (`try_files`)

`${SERVER_PORT}` wird vom nginx:alpine-Basis-Image via `envsubst` ersetzt (Template liegt in `/etc/nginx/templates/`).

### outpost.conf
- `location /install/` → proxy zu `server:5000` + `proxy_buffering off`
- `location /api/agent/` → proxy zu `server:5000`
- `location /` → 403

## SSL (optional)

Siehe `docker-compose.https.yml` — nutzt `./certs/` für Zertifikate.
Domain muss per DNS-A-Record auf den Server zeigen, dann certbot ausführen.

## Projekt-Struktur

```
sentry/
├── server/                    ASP.NET Core 8 API
│   ├── Controllers/
│   │   ├── InstallController.cs    Download + Token-Verwaltung
│   │   └── ...
│   ├── Services/
│   │   ├── InstallerService.cs     Singleton, scannt Binary-Offsets
│   │   ├── PatchedFileStream.cs    Streaming-Patch-Stream
│   │   └── ...
│   └── Dockerfile
├── frontend/                  React 19 + Vite + TypeScript
│   ├── src/
│   └── Dockerfile             nginx:alpine, Template via envsubst
├── agent/                     Windows-Agent (C#, win-x64)
├── installer/                 NSIS-Installer (baut HackITSentry-Setup.exe)
├── outpost.conf               nginx-Config für Outpost-Container
├── docker-compose.yml
├── docker-compose.https.yml
└── .env.example
```
