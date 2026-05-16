# Entwicklungsnotizen

## Aktuelles Problem: Download gibt HTML zurück

**Symptom:** `GET /install/{token}` liefert immer ein HTML-Dokument statt der .exe.

**Mögliche Ursachen (von wahrscheinlichst zu unwahrscheinlichst):**

1. **Frontend-Container nicht neu deployed** — Die nginx.conf-Änderung (location /install/) ist noch nicht aktiv. Lösung: Frontend-Container in Portainer neu builden + deployen.

2. **Token bereits verbraucht** — Token wurde beim ersten (fehlgeschlagenen) Download als `Used=true` markiert. Das Backend gibt dann eine styled HTML-Fehlerseite zurück ("Link bereits verwendet"). Lösung: Neuen Token erstellen.

3. **InstallerService.IsAvailable = false** — Binary nicht gefunden unter `AppContext.BaseDirectory/installer/HITSight-Setup.exe`. Backend gibt 503 zurück.

4. **`${SERVER_PORT}` nicht ersetzt** — Wenn envsubst nicht greift, schlägt proxy_pass fehl. Testen: In den Frontend-Container exec und `/etc/nginx/conf.d/default.conf` prüfen.

**Diagnose mit curl:**
```bash
# Direkt gegen den Server testen (bypasses nginx):
curl -I http://localhost:5000/install/{token}

# Gegen Frontend-nginx testen:
curl -I http://localhost:8030/install/{token}

# Gegen Outpost testen:
curl -I http://localhost:8031/install/{token}
```

## Gelöste Probleme

### UTF-16LE Encoding (behoben)
.NET-Single-File-Binaries speichern Stringkonstanten als UTF-16LE (2 Bytes/Char), nicht ASCII.
Alle `Encoding.ASCII` durch `Encoding.Unicode` ersetzt.

### totalChars falsch (behoben)
Kommentar in installer/Program.cs behauptete 448, tatsächlich sind es 433 Zeichen für HACKIT_SERVER_URL.
Gemessen: 415 Gleichheitszeichen + 18 Prefix-Zeichen = 433.

### OutpostPublicUrl leer (behoben)
`?? ""` erkennt leere Strings nicht als null. `string.IsNullOrEmpty()` verwenden.
Fallback: `$"{Request.Scheme}://{Request.Host}"` — aber das ist die interne Adresse!
OUTPOST_PUBLIC_URL muss im Portainer-Stack korrekt gesetzt sein.

### nginx buffering truncated Download (behoben)
nginx pufferte 138-MB-Response in Temp-Datei → Download abgebrochen.
Fix: `proxy_buffering off` in frontend/nginx.conf und outpost.conf.

### Port 8031 hardcoded in Frontend (behoben)
`getDownloadUrl` nutzte `http://${hostname}:8031` → jetzt `${window.location.origin}`.

### 138 MB in-memory laden (behoben)
`File.ReadAllBytesAsync` lud die gesamte .exe in RAM.
Ersetzt durch `InstallerService` + `PatchedFileStream` (streamt on-the-fly).

## Offene Punkte

- **SSL:** Domain `hitsight.server-netz.de` braucht DNS-A-Record auf die Server-IP, dann certbot ausführen.
- **Download gibt HTML zurück:** Siehe oben — vermutlich Frontend-Container nicht neu deployed.

## Wichtige Dateipfade

| Datei | Zweck |
|-------|-------|
| `server/Controllers/InstallController.cs` | Download-Endpoint + Token-Verwaltung |
| `server/Services/InstallerService.cs` | Singleton, scannt Binary-Offsets beim Start |
| `server/Services/PatchedFileStream.cs` | On-the-fly Streaming-Patch |
| `server/Program.cs` | DI-Registrierung, DB-Migrations |
| `frontend/nginx.conf` | nginx-Template für Frontend-Container |
| `outpost.conf` | nginx-Config für Outpost-Container |
| `installer/Program.cs` | Definiert Platzhalter-Strings (HITSIGHT_SERVER_URL: etc.) |
| `.env.example` | Dokumentation aller Env-Variablen |

## Portainer Stack Env (benötigte Werte)

```
POSTGRES_PASSWORD=...
JWT_KEY=...                         (min. 32 Zeichen)
ENCRYPTION_KEY=...                  (genau 32 Zeichen)
OUTPOST_PUBLIC_URL=http://IP:8031   (extern erreichbar!)
EMAIL_HOST=...                      (optional)
EMAIL_PORT=587
EMAIL_USERNAME=...
EMAIL_PASSWORD=...
EMAIL_FROM=...
EMAIL_TO=...
```
