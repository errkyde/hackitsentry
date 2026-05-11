# HackIT Sentry — Multi-Tenant SaaS: Implementierungsauftrag

Du bist ein Orchestrating Senior Software Architect und leitest ein spezialisiertes Multi-Agent-Team.
Deine Aufgabe ist die vollständige, produktionsreife Implementierung eines Multi-Tenant-SaaS-Systems auf Basis des bestehenden HackIT Sentry Projekts.

**Fang sofort an. Stelle keine Fragen. Triff bei Unklarheiten vernünftige Annahmen und dokumentiere sie.**

---

## Schritt 0 — Orientierung (sofort ausführen)

Bevor du irgendetwas implementierst, lese diese Dateien vollständig:

```
/home/phil/sentry/server/Program.cs
/home/phil/sentry/server/Data/AppDbContext.cs
/home/phil/sentry/server/Models/Device.cs
/home/phil/sentry/server/Models/User.cs
/home/phil/sentry/server/Models/DeployKey.cs
/home/phil/sentry/server/Services/JwtService.cs
/home/phil/sentry/server/Services/RuntimeSettings.cs
/home/phil/sentry/server/Services/DeviceOfflineAlertService.cs
/home/phil/sentry/server/Services/AlertEmailService.cs
/home/phil/sentry/server/Services/LicenseEncryptionService.cs
/home/phil/sentry/server/Controllers/AgentController.cs
/home/phil/sentry/server/Controllers/InstallController.cs
/home/phil/sentry/server/appsettings.json
/home/phil/sentry/docker-compose.yml
/home/phil/sentry/nginx-proxy.conf
```

Danach liste kurz auf was du verstanden hast (Architektur, DB-Migrations-Ansatz, Auth-Flows) und fange mit Phase 1 an.

---

## Was dieses Projekt ist

HackIT Sentry ist eine Device-Management-Plattform für IT-Dienstleister. Ein Windows-Agent läuft auf Kunden-PCs und meldet sich regelmäßig beim Server. Admins sehen alle Geräte, können Software-Inventar einsehen, Befehle senden, Windows/Office-Lizenzschlüssel abrufen und Alerts konfigurieren.

**Aktueller Zustand:** Single-Tenant. Eine Installation = eine Firma. Keine Mandantentrennung.

**Ziel:** Multi-Tenant SaaS. Jede Firma bekommt eine vollständig isolierte Instanz, erreichbar über eine eigene Subdomain. Kunden kaufen über Stripe, bekommen innerhalb von Sekunden Zugang, alles läuft automatisch.

---

## Dein Agent-Team

Weise Aufgaben explizit zu und hole vor dem Weiterschalten das Sign-off ein:

- **Backend Developer** — ASP.NET Core 8, C#, PostgreSQL, EF Core
- **Frontend Developer** — React 18, TypeScript, Vite, shadcn/ui, Tailwind CSS
- **DevOps Engineer** — Docker, nginx, PostgreSQL-Administration
- **Stripe Integration Engineer** — Stripe API, Webhooks, Checkout
- **DSGVO-Beauftragter** — Datenisolation, Lösch-Workflows, DSGVO Art. 17
- **Security Auditor** — Auth, Webhook-Signatur, Tenant-Isolation
- **QA Engineer** — Integrationstests, Edge Cases, Regressionsprüfung
- **Code Reviewer** — Reviewt jeden abgeschlossenen Task vor Sign-off

---

## Kritische Rahmenbedingungen (nicht verhandelbar)

**Migrations-Ansatz:** Das Projekt verwendet KEINE EF Core Migrations. Alle Schemaänderungen laufen über `CREATE TABLE IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS` Raw-SQL-Blöcke in `server/Program.cs`. Dieses Muster muss beibehalten und erweitert werden. Es ist bewusst idempotent.

**Nichts brechen:** Agent-Check-in, Geräteverwaltung, LDAP, Deploy-Keys, Software-Deployment, Script-Templates — alles muss nach der Umstellung exakt so funktionieren wie vorher.

**Kein TenantId auf Models:** Die Isolation läuft über separate Datenbanken, nicht über Row-Level-Security. Kein einziges bestehendes Model bekommt eine `TenantId`-Spalte.

---

## Architektur-Entscheidung: Database per Tenant

Jeder Tenant bekommt eine eigene PostgreSQL-Datenbank auf demselben Server:

```
PostgreSQL-Server
├── sentry_platform          ← nur Routing-Metadaten (Tenant-Registry)
├── sentry_muster_gmbh       ← alle Daten von Tenant "muster-gmbh"
├── sentry_mueller_it        ← alle Daten von Tenant "mueller-it"
└── sentry_beispiel_ag       ← alle Daten von Tenant "beispiel-ag"
```

Eine neue `PlatformDbContext` enthält nur die `Tenants`-Tabelle. Der bestehende `AppDbContext` bleibt strukturell unverändert, wird aber als `Scoped` mit dynamischer Connection-String registriert.

**Warum:** Vollständige DSGVO-Konformität. Man kann einem Kunden sagen: "Ihre Daten liegen in einer eigenen Datenbank, technisch unmöglich dass ein anderer Kunde darauf zugreift." Datenlöschung = `DROP DATABASE`. Kein Risiko durch vergessene Query-Filter.

---

## Produkt-Entscheidungen

**Pakete:**
- Starter: max 25 Geräte
- Pro: max 100 Geräte
- Enterprise: unbegrenzt
- Preise: konfigurierbar über Admin-Panel (kein Hardcoding — aus Platform-DB geladen)
- Abrechnung: monatlich und jährlich (jährlich = Faktor 10/12, entspricht 2 Monate gratis)

**Trial:** 14 Tage kostenlos, Kreditkarte wird sofort bei Stripe hinterlegt, Abbuchung erst nach Trial-Ende

**Subdomain-Generierung:**
- Automatisch aus Firmenname beim Checkout (z.B. "Muster GmbH" → "muster-gmbh")
- Kunden sehen nie welche Subdomains bereits existieren (keine Enumeration möglich)
- Admin kann Subdomain nachträglich über Admin-Panel umbenennen
- Format: `{slug}.{PLATFORM_DOMAIN}` (Domain per Env-Variable konfigurierbar)

**Gerätelimit:** Harter Block — neue Agent-Registrierungen werden abgelehnt wenn Limit erreicht. Klare Fehlermeldung mit Upgrade-Hinweis im UI.

**Nach Abo-Ende:**
- Account bleibt aktiv bis Ende der bezahlten Periode
- Danach: Login und Agent-Check-ins geblockt
- Daten werden 30 Tage aufbewahrt, dann automatisch gelöscht (Background-Job droppt die DB)

**Stripe Tax:** Aktiviert — Stripe berechnet MwSt automatisch (19% DE)

**E-Mail:** Bestehende SMTP-Infrastruktur (Muster von `AlertEmailService` übernehmen)

**Admin-Panel:** Eigene Subdomain `admin.{PLATFORM_DOMAIN}`, abgesichert mit TOTP 2FA, separater JWT-Signing-Key

**Sprache:** Deutsch durchgehend (Landing Page, E-Mails, UI-Texte)

**Benutzer pro Tenant:** Unbegrenzt (unverändert zum heutigen Verhalten)

**Support:** E-Mail-Adresse + osTicket-Link (beide über Admin-Panel konfigurierbar)

---

## Phase 1 — Foundation

*Assigned to: Backend Developer. Review by: Code Reviewer + DSGVO-Beauftragter.*

### 1.1 Platform Database

Erstelle `server/Models/Tenant.cs`:

```csharp
public class Tenant {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = "";              // URL-sicheres Subdomain-Segment
    public string Name { get; set; } = "";              // Anzeigename, z.B. "Muster GmbH"
    public string DbName { get; set; } = "";            // PostgreSQL-Datenbankname
    public string Plan { get; set; } = "starter";       // "starter" | "pro" | "enterprise"
    public int MaxDevices { get; set; } = 25;
    public bool IsActive { get; set; } = true;
    public string AdminEmail { get; set; } = "";        // für Welcome-Mail + Support
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }     // "trialing"|"active"|"past_due"|"canceled"
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime? ScheduledDeletionAt { get; set; } // = DeactivatedAt + 30 Tage
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Erstelle `server/Data/PlatformDbContext.cs` — separater DbContext nur für die Platform-DB. Registriere ihn in `Program.cs` mit eigenem Connection-String (`Platform:ConnectionString`). Schema-Erstellung läuft ebenfalls über Raw-SQL im bestehenden Startup-Block.

**DSGVO-Beauftragter prüft:** Platform-DB enthält ausschließlich Routing-Metadaten. Keine Gerätedaten, keine Passwörter, keine Lizenzschlüssel, keine personenbezogenen Daten außer der Admin-E-Mail für die Abonnementkommunikation.

### 1.2 ITenantContext + Middleware

Erstelle `server/Services/TenantContext.cs`:

```csharp
public interface ITenantContext {
    Guid TenantId { get; }
    string Slug { get; }
    string ConnectionString { get; }
    string Plan { get; }
    int MaxDevices { get; }
    bool IsActive { get; }
}
```

Erstelle `server/Middleware/TenantResolutionMiddleware.cs`:
- Liest `Host`-Header, extrahiert Subdomain
- Überspringt Auflösung für die `admin.`-Subdomain
- Lookup in `PlatformDbContext` (mit `IMemoryCache`, 60s TTL, um DB-Hit pro Request zu vermeiden)
- Tenant nicht gefunden oder nicht aktiv: gibt `404` zurück — identische Response für beide Fälle (verhindert Timing-basierte Enumeration)
- Setzt `ITenantContext` als Scoped-Service für den Request
- Platzierung in der Pipeline: **vor** `UseAuthentication`

### 1.3 AppDbContext — Scoped mit dynamischer Connection

Ersetze in `Program.cs` den `AddDbContext<AppDbContext>`-Aufruf:

```csharp
services.AddScoped<AppDbContext>(sp => {
    var tenant = sp.GetRequiredService<ITenantContext>();
    var opts = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(tenant.ConnectionString)
        .Options;
    return new AppDbContext(opts);
});
```

### 1.4 RuntimeSettings — Scoped statt Singleton

`RuntimeSettings` lädt aktuell beim Start als Singleton aus Config + DB. Mit mehreren Tenants (jeder mit eigener `AppSettings`-Tabelle in eigener DB) muss es `Scoped` werden.

- Registration von `AddSingleton` auf `AddScoped` ändern
- Settings werden beim ersten Zugriff innerhalb des Request-Scopes aus der Tenant-DB geladen
- Werte innerhalb des Scopes gecacht (nicht global)
- `DeviceOfflineAlertService` (Background Service) erstellt pro Tenant-Iteration einen eigenen Scope

### 1.5 DbMigrator — DDL aus Program.cs extrahieren

Extrahiere alle `db.Database.ExecuteSqlRaw(...)` Blöcke aus `Program.cs` in eine statische Klasse `server/Services/DbMigrator.cs` mit einer einzigen Methode `public static Task RunAsync(AppDbContext db)`.

Die Methode ist idempotent (alle Statements nutzen bereits `IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`).

Startup-Ablauf in `Program.cs`:
1. Platform-DB initialisieren (Tabellen anlegen)
2. Alle aktiven Tenants aus Platform-DB laden
3. Für jeden Tenant: `AppDbContext` mit dessen Connection-String öffnen, `DbMigrator.RunAsync(tenantDb)` aufrufen

### 1.6 TenantProvisioningService

Erstelle `server/Services/TenantProvisioningService.cs`. Dieser Service ist für das vollautomatische Anlegen eines neuen Tenants zuständig:

1. Firmennamen slugifizieren (Kleinbuchstaben, Leerzeichen/Sonderzeichen → Bindestriche)
2. Kollisionsprüfung gegen Platform-DB, ggf. `-2`, `-3` anhängen
3. PostgreSQL-Datenbank anlegen via Admin-Connection: `CREATE DATABASE "{dbName}"`
4. `AppDbContext` mit neuer DB-Connection instanziieren
5. `DbMigrator.RunAsync(newTenantDb)` aufrufen
6. Admin-User mit zufälligem 16-Zeichen-Passwort anlegen (bcrypt, in Tenant-DB gespeichert)
7. Standard-Software-Pakete seeden (gleiche Liste wie heute in `Program.cs`)
8. Deploy-Key für MSI-Installer generieren (in Tenant-DB)
9. `Tenant`-Record in Platform-DB speichern
10. Welcome-E-Mail auslösen (Phase 3)
11. Rückgabe: Slug, Login-URL, Admin-Zugangsdaten, MSI-Download-URL

### 1.7 Gerätelimit-Enforcement

In `server/Controllers/AgentController.cs`, im Registrierungs-Endpunkt:
- `ITenantContext` injizieren
- Vor dem Approven eines Pending-Device: aktive Geräteanzahl des Tenants zählen
- Wenn `Anzahl >= tenant.MaxDevices`: `429` zurückgeben mit Body `{ "error": "device_limit_reached", "limit": N }`

In `frontend/src/pages/Pending.tsx`: diesen Fehler erkennen und Upgrade-CTA anzeigen statt generischer Fehlermeldung.

### 1.8 DeviceOfflineAlertService — Multi-Tenant

Refactore `server/Services/DeviceOfflineAlertService.cs`:
- Pro Timer-Tick: alle aktiven Tenants aus Platform-DB laden
- Pro Tenant: eigenen DI-Scope erstellen, `AppDbContext` für diesen Tenant auflösen, Offline-Check durchführen
- Fehler in einem Tenant dürfen andere Tenants nicht beeinflussen (jeder Tenant in eigenem `try/catch`)

---

## Phase 2 — Stripe-Integration

*Assigned to: Stripe Integration Engineer. Review by: Security Auditor + Code Reviewer.*

### 2.1 Konfiguration

Neue Env-Variablen (in `appsettings.json` als Platzhalter, Werte via Umgebungsvariablen):

```
Stripe:SecretKey
Stripe:PublishableKey
Stripe:WebhookSecret
Stripe:StarterMonthlyPriceId
Stripe:StarterYearlyPriceId
Stripe:ProMonthlyPriceId
Stripe:ProYearlyPriceId
Stripe:EnterpriseMonthlyPriceId
Stripe:EnterpriseYearlyPriceId
```

Preise werden einmalig im Stripe-Dashboard angelegt und per ID referenziert.

### 2.2 Checkout-Session-Endpunkt

Erstelle `server/Controllers/CheckoutController.cs` (kein `[Authorize]` — öffentlicher Endpunkt):

`POST /api/checkout/session`
```json
{
  "plan": "starter",
  "billingInterval": "monthly",
  "companyName": "Muster GmbH",
  "email": "admin@muster.de"
}
```

- Inputs validieren
- Slug aus `companyName` generieren, Eindeutigkeit prüfen
- Stripe Checkout Session erstellen:
  - Mode: `subscription`, Trial: 14 Tage, Payment: card, Tax: automatic
  - Metadata: `companyName`, `plan`, `slug`, `email`
  - Success-URL: `https://{slug}.{PLATFORM_DOMAIN}/login?welcome=1`
  - Cancel-URL: `https://{PLATFORM_DOMAIN}/#pricing`
- Rückgabe: `{ sessionId, publishableKey }`

Endpunkt mit Rate-Limiter absichern (max. 10 Requests/IP/Minute).

### 2.3 Stripe Webhook Handler

Erstelle `server/Controllers/StripeWebhookController.cs`:

`POST /api/webhooks/stripe` — dieser Endpunkt liegt **außerhalb** der Tenant-Middleware.

**Signaturvalidierung ZUERST** — Raw Request Body verwenden (nicht geparste JSON), bevor irgendetwas anderes passiert:

```csharp
var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
var stripeEvent = EventUtility.ConstructEvent(json, 
    Request.Headers["Stripe-Signature"], _config["Stripe:WebhookSecret"]);
```

Zu behandelnde Events:

**`checkout.session.completed`**
- Metadata extrahieren: `companyName`, `plan`, `slug`, `email`, Stripe-Customer/Subscription-IDs
- `TenantProvisioningService.ProvisionAsync(...)` aufrufen
- `StripeCustomerId`, `StripeSubscriptionId`, `SubscriptionStatus = "trialing"` in Platform-DB speichern

**`customer.subscription.updated`**
- `SubscriptionStatus`, `CurrentPeriodEndsAt`, `Plan`, `MaxDevices` in Platform-DB aktualisieren

**`customer.subscription.deleted`**
- `IsActive = false`, `DeactivatedAt = now`, `ScheduledDeletionAt = now + 30 Tage` setzen
- Tenant verliert sofort Zugang (Middleware gibt 404 zurück)

**`invoice.payment_failed`**
- Warn-E-Mail an Admin-E-Mail des Tenants senden
- Tenant NICHT deaktivieren — Stripe retryt und schickt ggf. `customer.subscription.deleted`

**Security Auditor prüft:**
- Signaturvalidierung passiert vor jeglicher Verarbeitung
- Raw Body wird für Signaturcheck verwendet
- Endpunkt liegt nicht hinter Tenant-Middleware

### 2.4 Tenant-Cleanup Background Service

Erstelle `server/Services/TenantCleanupService.cs` (IHostedService, läuft täglich):
- Platform-DB nach Tenants abfragen wo `ScheduledDeletionAt <= now`
- Pro Tenant: `DROP DATABASE "{dbName}"` via Admin-Connection
- Tenant-Record aus Platform-DB entfernen
- Löschung in Plattform-Audit-Log protokollieren

**DSGVO-Beauftragter bestätigt:** Das Droppen der Datenbank entfernt alle Daten inklusive verschlüsselter Lizenzschlüssel, Audit-Logs und personenbezogener Daten. Dies ist die technische Umsetzung von DSGVO Art. 17 (Recht auf Löschung).

---

## Phase 3 — E-Mail-Automatisierung

*Assigned to: Backend Developer. Review by: Code Reviewer.*

Alle E-Mails auf Deutsch. Muster von `AlertEmailService` übernehmen oder `TenantEmailService` erstellen.

### 3.1 Welcome-E-Mail (nach Provisionierung)

Betreff: `Willkommen bei HackIT Sentry — Ihre Zugangsdaten`

Inhalt:
- Login-URL: `https://{slug}.{PLATFORM_DOMAIN}/login`
- Benutzername: `admin`
- Passwort: `{generatedPassword}` (nur in dieser E-Mail — nicht im Klartext gespeichert)
- MSI-Download-Link: `https://{slug}.{PLATFORM_DOMAIN}/install/{deployKeyToken}`
- Trial-Info: endet am `{TrialEndsAt:dd.MM.yyyy}`
- Support-E-Mail + osTicket-Link (aus Platform-Konfiguration)

### 3.2 Trial-Ablauf-Warnung (3 Tage vorher)

Background-Job scannt täglich `TrialEndsAt - 3 Tage`. E-Mail mit Hinweis auf bevorstehende Abbuchung.

### 3.3 Zahlung fehlgeschlagen

Gesendet durch Webhook-Handler bei `invoice.payment_failed`.

### 3.4 Account deaktiviert

Gesendet bei `customer.subscription.deleted`. Informiert über 30-tägige Datenhaltung.

---

## Phase 4 — Admin-Panel

*Assigned to: Frontend Developer + Backend Developer. Review by: Security Auditor + Code Reviewer.*

Erreichbar unter `admin.{PLATFORM_DOMAIN}`. Super-Admin-Accounts liegen in der Platform-DB (eigene `SuperAdminUsers`-Tabelle — nicht in Tenant-DBs).

### 4.1 Super Admin Auth

`POST /platform/auth/login`
- Username + Passwort → gibt temporären Token zurück (nur gültig für TOTP-Verifikation)

`POST /platform/auth/totp`
- TOTP-Code prüfen (Google-Authenticator-kompatibel)
- Gibt JWT aus mit Claim `role: SuperAdmin`, Ablauf 4h
- **Separater Signing-Key** (`Platform:JwtKey`) — nicht der gleiche wie Tenant-JWTs

TOTP-Setup: First-Login-Flow mit QR-Code, Bestätigung vor Aktivierung.

**Security Auditor prüft:**
- SuperAdmin-JWT kann nicht gegen Tenant-API-Endpunkte verwendet werden
- Tenant-Middleware lehnt Tokens ohne validen Tenant-Claim ab
- SuperAdmin-Tokens funktionieren nur auf `/platform/`-Routen

### 4.2 Admin-Panel UI

Seiten:

**Dashboard**
- Gesamt-Tenants (aktiv / Trial / deaktiviert)
- Gesamt-Geräte über alle Tenants
- Neuanmeldungen (letzte 7 Tage)

**Tenant-Liste**
- Name, Slug, Paket, Geräte vs. Limit, Status, Subscription-Status, Erstellt-am, Aktionen
- Filter nach Paket, Status; Suche nach Name/Slug

**Tenant-Detail**
- Alle Metadaten
- Bearbeiten: Name, Slug (mit Kollisionsprüfung), Paket, MaxDevices
- Trial verlängern (setzt neues `TrialEndsAt`)
- Manuell deaktivieren / reaktivieren
- Manuelle Löschung (mit Bestätigungs-Dialog)
- Stripe-Dashboard-Deeplink: `https://dashboard.stripe.com/customers/{StripeCustomerId}`

**Platform-Einstellungen**
- SMTP-Konfiguration
- Platform-Domain
- Support-E-Mail + osTicket-URL
- Paketpreise (monatlich/jährlich je Paket) — werden als JSON in Platform-DB `AppSettings` gespeichert

**Tenant manuell anlegen**
- Für Kunden die nicht über Stripe-Checkout gehen (Rechnungskunden, interne Tests)
- Formular: Firmenname, E-Mail, Paket, Trial-Tage
- Ruft `TenantProvisioningService` direkt auf

---

## Phase 5 — Landing Page

*Assigned to: Frontend Developer. Review by: Code Reviewer.*

Deutsche Landing Page auf der Root-Domain `{PLATFORM_DOMAIN}`.

**Sections:**

**Hero:** Produktname HackIT Sentry, Fokus auf IT-Dienstleister und Mittelstand, CTA "Jetzt kostenlos testen"

**Features:** Gerätemonitoring, Software-Inventar, Fernbefehle, Patch-Management, Lizenzverwaltung, Alerts

**Preise:**
- Drei Cards: Starter / Pro / Enterprise
- Preise dynamisch von `/api/platform/pricing` (aus Platform-DB, kein Hardcoding)
- Toggle: Monatlich / Jährlich (jährlich zeigt "2 Monate gratis"-Badge)
- "Jetzt starten"-Button öffnet Checkout-Flow

**Checkout-Flow (inline):**
- Firmenname-Eingabe mit Live-Slug-Vorschau: "Ihr Zugang wird: muster-gmbh.hackit-sentry.de"
- E-Mail-Eingabe
- Submit ruft `POST /api/checkout/session` auf → Weiterleitung zu Stripe Checkout
- Nach Stripe: Weiterleitung zu `https://{slug}.{PLATFORM_DOMAIN}/login?welcome=1`

**DSGVO-Vertrauens-Section:**
- "Ihre Daten in einer eigenen Datenbank"
- "DSGVO-konform, Server in Deutschland"
- "Datenlöschung auf Knopfdruck"

**Support/Kontakt:** E-Mail + osTicket-Link (aus Platform-API geladen)

---

## Phase 6 — Infrastruktur

*Assigned to: DevOps Engineer. Review by: Code Reviewer.*

### 6.1 nginx — Wildcard-Subdomain-Routing

`nginx-proxy.conf` aktualisieren:
- `admin.{domain}` → Admin-Panel-App
- `*.{domain}` → Haupt-App (Tenant-Routing in ASP.NET Core Middleware)
- Root `{domain}` → Landing Page

### 6.2 Docker Compose

`docker-compose.yml` aktualisieren:
- `Platform:ConnectionString` für Platform-DB
- Alle Stripe-Env-Variablen
- PostgreSQL-Container: App-User erhält `CREATEDB`-Recht
- Neue Variablen: `PLATFORM_DOMAIN`, `PLATFORM_JWT_KEY`

### 6.3 Wildcard-SSL

In `README.md` dokumentieren:
```bash
certbot certonly --dns-{provider} -d "{domain}" -d "*.{domain}"
```
Wildcard-Zertifikat deckt alle Tenant-Subdomains automatisch ab.

---

## Phase 7 — Tenant-UI-Ergänzungen

*Assigned to: Frontend Developer. Review by: Code Reviewer.*

**Login-Seite (`Login.tsx`):**
- Bei `?welcome=1`: Banner "Willkommen! Ihre Instanz ist bereit."

**Einstellungen (`Settings.tsx`):**
- Neuer Abschnitt "Abonnement": aktuelles Paket, Geräte vs. Limit, nächstes Abrechnungsdatum, Link zum Stripe Customer Portal

**Gerätelimit erreicht:**
- In `Pending.tsx`: Banner mit Paketname, aktuelle Zahl, Limit, "Paket upgraden"-Button

---

## Umgebungsvariablen (vollständige Liste)

```
# Bestehend
POSTGRES_PASSWORD
JWT_KEY
ENCRYPTION_KEY
OUTPOST_PUBLIC_URL
EMAIL_HOST / EMAIL_PORT / EMAIL_USERNAME / EMAIL_PASSWORD / EMAIL_FROM / EMAIL_TO

# Neu — Platform
PLATFORM_CONNECTION_STRING     ← Connection-String für Platform-DB (sentry_platform)
PLATFORM_DOMAIN                ← z.B. hackit-sentry.de
PLATFORM_JWT_KEY               ← separater Signing-Key für SuperAdmin-JWTs (min. 32 Zeichen)
ADMIN_SUBDOMAIN                ← z.B. admin

# Neu — Stripe
STRIPE_SECRET_KEY
STRIPE_PUBLISHABLE_KEY
STRIPE_WEBHOOK_SECRET
STRIPE_STARTER_MONTHLY_PRICE_ID
STRIPE_STARTER_YEARLY_PRICE_ID
STRIPE_PRO_MONTHLY_PRICE_ID
STRIPE_PRO_YEARLY_PRICE_ID
STRIPE_ENTERPRISE_MONTHLY_PRICE_ID
STRIPE_ENTERPRISE_YEARLY_PRICE_ID
```

---

## Quality Gates

Vor dem Abschluss jeder Phase prüft der QA Engineer:

**Phase 1:**
- Neuer Tenant provisionierbar: DB angelegt, Tabellen vorhanden, Admin-Login funktioniert
- Tenant-Isolation: Cross-Tenant-Query liefert 0 Ergebnisse
- `DbMigrator.RunAsync` ist idempotent — zweifaches Ausführen ohne Fehler und ohne Datenverlust
- Agent-Check-in funktioniert für bestehende Geräte unverändert
- Gerätelimit-Hard-Block greift exakt am konfigurierten Limit

**Phase 2:**
- Stripe Checkout für alle 6 Preis-Kombinationen (3 Pakete × 2 Intervalle) korrekt
- Webhook `checkout.session.completed` provisioniert Tenant vollständig end-to-end
- Webhook `customer.subscription.deleted` sperrt Login und Agent-Check-ins sofort
- Manipulierte Webhook-Payloads werden mit 400 abgelehnt
- Cleanup-Job löscht DB exakt nach 30 Tagen

**Phase 3:**
- Welcome-E-Mail innerhalb 60 Sekunden nach Stripe-Checkout empfangen
- MSI-Download-Link in Welcome-E-Mail liefert korrekt gepatchten Installer
- Trial-Warnung exakt 3 Tage vor `TrialEndsAt`

**Phase 4:**
- SuperAdmin-TOTP-Login mit Google Authenticator funktioniert
- SuperAdmin-JWT von Tenant-API-Endpunkten abgelehnt (401/403)
- Subdomain-Umbenennung wirkt sofort (Cache invalidiert)
- Manuelle Tenant-Anlage ohne Stripe funktioniert

**Phase 5:**
- Preise werden dynamisch geladen (Änderung in Platform-Config aktualisiert die Seite)
- Slug-Vorschau aktualisiert sich live während der Eingabe
- Stripe-Checkout-Weiterleitung für alle Pakete funktioniert
- Welcome-Banner bei `?welcome=1` korrekt

**Phase 6:**
- `*.{domain}` Subdomains werden nach nginx-Neustart korrekt aufgelöst
- SSL-Zertifikat deckt Wildcard ab (Prüfung mit `curl -I https://test.{domain}`)
- Neue Tenant-DB wird mit korrekten PostgreSQL-Benutzerrechten angelegt

---

## DSGVO-Checkliste

- [ ] Jede Tenant-DB ist physisch isoliert — verifiziert durch Schema-Inspektion
- [ ] Platform-DB enthält ausschließlich Routing-Metadaten
- [ ] Lizenzschlüssel AES-256-verschlüsselt in Tenant-DB, Schlüssel liegt in Env-Config (nicht in DB)
- [ ] Tenant-DB-Drop entfernt alle personenbezogenen Daten (Art. 17-Umsetzung)
- [ ] Audit-Logs pro Tenant nur in der jeweiligen Tenant-DB
- [ ] Admin-E-Mail in Platform-DB nur für Abrechnungskommunikation
- [ ] Stripe als Sub-Auftragsverarbeiter dokumentiert
- [ ] 30-Tage-Retention wird automatisch ohne manuelle Aktion durchgesetzt

---

## Security-Checkliste

- [ ] Stripe-Webhook-Signatur vor jeder Verarbeitung validiert (Raw Body, nicht geparste JSON)
- [ ] Tenant-Middleware kann nicht durch manipulierte `Host`-Header umgangen werden
- [ ] SuperAdmin-JWT mit eigenem Signing-Key — nicht für Tenant-Endpunkte nutzbar
- [ ] TOTP-Secret sicher gespeichert (verschlüsselt in Platform-DB, niemals in Logs)
- [ ] Admin-Panel nicht über Tenant-Subdomains erreichbar
- [ ] Subdomain-Enumeration verhindert: 404 für unbekannte = 404 für inaktive (kein Timing-Unterschied)
- [ ] Stripe Customer Portal Session nur für `StripeCustomerId` des anfragenden Tenants
- [ ] PostgreSQL-Admin-User (für CREATE DATABASE) hat keinen Zugang zu App-Endpunkten
- [ ] Rate-Limiter auf `/api/checkout/session` und `/platform/auth/login`

---

## Ausgabe-Format (für jede Phase)

1. Welcher Agent führt welchen Task aus
2. Jede erstellte oder geänderte Datei vollständig (kein Diff, vollständiger Inhalt)
3. Code Reviewer gibt explizit Sign-off oder listet erforderliche Änderungen
4. DSGVO-Beauftragter und Security Auditor signieren relevante Komponenten ab
5. QA Engineer führt Checkliste für die Phase durch und meldet Pass/Fail pro Punkt
6. Erst nach allen Sign-offs: Phase als abgeschlossen markieren und mit der nächsten beginnen

**Beginne jetzt mit Phase 1.1 — Platform Database.**
