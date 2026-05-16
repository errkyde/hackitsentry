# HITSight — Deployment Guide

## Single-Tenant (default)

No extra configuration needed. Run:

```bash
docker compose up -d
```

Access the app at `http://your-server:8030`. The `proxy` service is not required in this mode.

---

## SaaS / Multi-Tenant

### Prerequisites

- A domain (e.g. `hitsight.example.com`)
- A **wildcard DNS record**: `*.hitsight.example.com → your-server-IP`
- Root domain record: `hitsight.example.com → your-server-IP`
- Ports **80** and **443** open on your server

### 1 — Configure `.env`

```dotenv
PLATFORM_DOMAIN=hitsight.example.com
PLATFORM_CONNECTION_STRING=Host=db;Database=hitsight_platform;Username=postgres;Password=changeme
PLATFORM_JWT_KEY=<random-32-chars>
ADMIN_SUBDOMAIN=admin

# Stripe (optional — needed for paid plans)
STRIPE_SECRET_KEY=sk_live_...
STRIPE_PUBLISHABLE_KEY=pk_live_...
STRIPE_WEBHOOK_SECRET=whsec_...
STRIPE_STARTER_MONTHLY_PRICE_ID=price_...
STRIPE_STARTER_YEARLY_PRICE_ID=price_...
STRIPE_PRO_MONTHLY_PRICE_ID=price_...
STRIPE_PRO_YEARLY_PRICE_ID=price_...
STRIPE_ENTERPRISE_MONTHLY_PRICE_ID=price_...
STRIPE_ENTERPRISE_YEARLY_PRICE_ID=price_...

# Email (optional)
EMAIL_HOST=smtp.example.com
EMAIL_PORT=587
EMAIL_USERNAME=noreply@example.com
EMAIL_PASSWORD=...
EMAIL_FROM=noreply@example.com
EMAIL_USE_SSL=false
```

### 2 — Start services

```bash
docker compose up -d
```

This starts the `proxy` service on ports 80/443 and routes all `*.hitsight.example.com` traffic to the frontend.

Verify the proxy is running:

```bash
docker compose logs proxy
```

### 3 — SSL / HTTPS

#### Option A — Cloudflare (recommended, zero-config SSL)

1. Add your domain to Cloudflare
2. Set DNS records to **Proxied** (orange cloud)
3. In Cloudflare SSL settings: set mode to **Full (strict)**
4. Done — Cloudflare provides wildcard HTTPS automatically

No changes needed to `proxy.conf`.

#### Option B — Let's Encrypt wildcard certificate (certbot)

Wildcard certificates require **DNS-01 challenge**. Example with Cloudflare DNS:

```bash
# Install certbot + Cloudflare plugin
apt install certbot python3-certbot-dns-cloudflare

# Create credentials file
cat > /etc/cloudflare.ini << EOF
dns_cloudflare_api_token = YOUR_CF_API_TOKEN
EOF
chmod 600 /etc/cloudflare.ini

# Obtain wildcard certificate
certbot certonly \
  --dns-cloudflare \
  --dns-cloudflare-credentials /etc/cloudflare.ini \
  -d "hitsight.example.com" \
  -d "*.hitsight.example.com"
```

Then edit `docker-compose.yml` to mount the certs into the proxy:

```yaml
proxy:
  volumes:
    - ./proxy.conf:/etc/nginx/templates/default.conf.template:ro
    - /etc/letsencrypt/live/hitsight.example.com/fullchain.pem:/etc/ssl/certs/fullchain.pem:ro
    - /etc/letsencrypt/live/hitsight.example.com/privkey.pem:/etc/ssl/private/privkey.pem:ro
```

And in `proxy.conf`, uncomment the HTTPS server block and the HTTP→HTTPS redirect.

Auto-renewal (add to crontab):

```bash
0 3 * * * certbot renew --quiet && docker compose restart proxy
```

#### Option C — nginx Proxy Manager (GUI)

If you already use [nginx Proxy Manager](https://nginxproxymanager.com/):

1. Add a proxy host for `hitsight.example.com` → `localhost:8030`
2. Enable wildcard SSL via Let's Encrypt in the NPM UI
3. **Do not** start the `proxy` service (comment it out in docker-compose.yml):
   ```yaml
   # proxy:
   #   ...
   ```
4. Make sure the frontend is accessible on port 8030 from the NPM container.

---

## Admin Panel

The platform admin panel is accessible at:

```
https://hitsight.example.com/adminpage
```

Default credentials: `superadmin` / `changeme`

**Change the password immediately after first login.**

The first login requires TOTP setup. Use Google Authenticator, Authy, or any TOTP-compatible app.

---

## Agent outpost

Agents communicate via the outpost on port **8031** (separate from the main proxy).

The `OutpostPublicUrl` should point to the outpost:

```dotenv
OUTPOST_PUBLIC_URL=https://hitsight.example.com:8031
```

If you use Cloudflare, note that non-standard ports need Cloudflare Spectrum (paid). In that case, either use port 443 for the outpost (change `OUTPOST_PORT=443`) or put it on a subdomain that bypasses the orange cloud.

---

## Stripe Webhook

In your Stripe dashboard, add a webhook endpoint:

```
https://hitsight.example.com/api/webhooks/stripe
```

Events to subscribe to:
- `checkout.session.completed`
- `customer.subscription.updated`
- `customer.subscription.deleted`
- `invoice.payment_failed`

Copy the webhook signing secret into `STRIPE_WEBHOOK_SECRET` in your `.env`.
