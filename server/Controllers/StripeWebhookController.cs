using HITSight.Server.Data;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/webhooks")]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly PlatformDbContext? _platformDb;
    private readonly TenantProvisioningService? _provisioning;
    private readonly PlatformEmailService _platformEmail;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IConfiguration config,
        PlatformEmailService platformEmail,
        ILogger<StripeWebhookController> logger,
        PlatformDbContext? platformDb = null,
        TenantProvisioningService? provisioning = null)
    {
        _config = config;
        _platformEmail = platformEmail;
        _logger = logger;
        _platformDb = platformDb;
        _provisioning = provisioning;
    }

    // POST /api/webhooks/stripe
    [HttpPost("stripe")]
    public async Task<IActionResult> HandleStripe()
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(webhookSecret))
            return StatusCode(503);

        // ── SIGNATURE VALIDATION FIRST — raw body, before any other processing ──
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                webhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook signature");
            return BadRequest(new { error = "Invalid signature." });
        }

        _logger.LogInformation("Stripe webhook: {Type}", stripeEvent.Type);

        try
        {
            await ProcessEventAsync(stripeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe event {Type} ({Id})", stripeEvent.Type, stripeEvent.Id);
            // Return 200 anyway so Stripe doesn't retry — we log for manual intervention
        }

        return Ok();
    }

    private async Task ProcessEventAsync(Event e)
    {
        switch (e.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompleted((Stripe.Checkout.Session)e.Data.Object);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdated((Subscription)e.Data.Object);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeleted((Subscription)e.Data.Object);
                break;

            case EventTypes.InvoicePaymentFailed:
                await HandlePaymentFailed((Invoice)e.Data.Object);
                break;

            default:
                _logger.LogDebug("Unhandled Stripe event: {Type}", e.Type);
                break;
        }
    }

    // ── checkout.session.completed ───────────────────────────────────────────

    private async Task HandleCheckoutCompleted(Stripe.Checkout.Session session)
    {
        if (_provisioning == null || _platformDb == null)
        {
            _logger.LogError("Platform not configured — cannot provision tenant from Stripe webhook");
            return;
        }

        var meta = session.Metadata ?? new Dictionary<string, string>();
        if (!meta.TryGetValue("companyName", out var companyName) ||
            !meta.TryGetValue("plan", out var plan) ||
            !meta.TryGetValue("email", out var email))
        {
            _logger.LogError("Checkout session {Id} missing required metadata", session.Id);
            return;
        }

        meta.TryGetValue("slug", out var preferredSlug);

        // Idempotency: skip if already provisioned for this Stripe subscription
        var existingBySubscription = await _platformDb.Tenants
            .AnyAsync(t => t.StripeSubscriptionId == session.SubscriptionId);
        if (existingBySubscription)
        {
            _logger.LogInformation("Tenant already provisioned for subscription {Sub}", session.SubscriptionId);
            return;
        }

        try
        {
            var result = await _provisioning.ProvisionAsync(
                companyName: companyName,
                adminEmail: email,
                plan: plan,
                stripeCustomerId: session.CustomerId,
                stripeSubscriptionId: session.SubscriptionId,
                subscriptionStatus: "trialing",
                trialDays: 14);

            _logger.LogInformation("Tenant provisioned via Stripe: {Slug}", result.Slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision tenant for checkout {Id}", session.Id);
        }
    }

    // ── customer.subscription.updated ───────────────────────────────────────

    private async Task HandleSubscriptionUpdated(Subscription subscription)
    {
        if (_platformDb == null) return;

        var tenant = await _platformDb.Tenants
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id);

        if (tenant == null)
        {
            _logger.LogWarning("No tenant found for subscription {Id}", subscription.Id);
            return;
        }

        var (plan, maxDevices) = GetPlanFromSubscription(subscription);

        tenant.SubscriptionStatus = subscription.Status;
        tenant.CurrentPeriodEndsAt = subscription.CurrentPeriodEnd;
        tenant.Plan = plan;
        tenant.MaxDevices = maxDevices;

        if (subscription.Status == "trialing" && subscription.TrialEnd.HasValue)
            tenant.TrialEndsAt = subscription.TrialEnd.Value;

        await _platformDb.SaveChangesAsync();
        _logger.LogInformation("Subscription updated for tenant {Slug}: status={Status}", tenant.Slug, subscription.Status);
    }

    // ── customer.subscription.deleted ───────────────────────────────────────

    private async Task HandleSubscriptionDeleted(Subscription subscription)
    {
        if (_platformDb == null) return;

        var tenant = await _platformDb.Tenants
            .FirstOrDefaultAsync(t => t.StripeSubscriptionId == subscription.Id);

        if (tenant == null) return;

        var now = DateTime.UtcNow;
        tenant.IsActive = false;
        tenant.SubscriptionStatus = "canceled";
        tenant.DeactivatedAt = now;
        tenant.ScheduledDeletionAt = now.AddDays(30);

        await _platformDb.SaveChangesAsync();

        _logger.LogInformation(
            "Tenant {Slug} deactivated — scheduled deletion at {Date}",
            tenant.Slug, tenant.ScheduledDeletionAt);

        // Notify tenant admin
        if (_platformEmail.IsConfigured && !string.IsNullOrEmpty(tenant.AdminEmail))
        {
            await _platformEmail.SendAsync(
                tenant.AdminEmail,
                "HITSight – Ihr Abonnement wurde beendet",
                AlertEmailService.BuildHtml(
                    "#dc2626", "Abonnement beendet",
                    "Ihr HITSight Abonnement wurde beendet",
                    $"""
                    <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
                      Ihr Zugang zu <strong>{tenant.Name}</strong> ist nun deaktiviert.
                    </p>
                    <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
                      Ihre Daten werden bis zum <strong>{tenant.ScheduledDeletionAt:dd.MM.yyyy}</strong> aufbewahrt
                      und danach automatisch gelöscht.
                    </p>
                    <p style="margin:0;font-size:14px;color:#3f3f46;line-height:1.6;">
                      Falls Sie Ihren Account reaktivieren möchten, wenden Sie sich bitte an unseren Support.
                    </p>
                    """));
        }
    }

    // ── invoice.payment_failed ───────────────────────────────────────────────

    private async Task HandlePaymentFailed(Invoice invoice)
    {
        if (_platformDb == null) return;

        var tenant = await _platformDb.Tenants
            .FirstOrDefaultAsync(t => t.StripeCustomerId == invoice.CustomerId);

        if (tenant == null) return;

        _logger.LogWarning("Payment failed for tenant {Slug}, invoice {Id}", tenant.Slug, invoice.Id);

        if (!_platformEmail.IsConfigured || string.IsNullOrEmpty(tenant.AdminEmail)) return;

        var nextRetry = invoice.NextPaymentAttempt.HasValue
            ? $"Der nächste Versuch erfolgt am {invoice.NextPaymentAttempt.Value:dd.MM.yyyy}."
            : "Bitte aktualisieren Sie Ihre Zahlungsmethode.";

        await _platformEmail.SendAsync(
            tenant.AdminEmail,
            "HITSight – Zahlung fehlgeschlagen",
            AlertEmailService.BuildHtml(
                "#ea580c", "Zahlungsfehler",
                "Eine Zahlung für Ihr HITSight Abonnement ist fehlgeschlagen",
                $"""
                <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
                  Die Abbuchung für <strong>{tenant.Name}</strong> konnte nicht durchgeführt werden.
                </p>
                <p style="margin:0 0 16px;font-size:14px;color:#3f3f46;line-height:1.6;">
                  {nextRetry}
                </p>
                <p style="margin:0;font-size:14px;color:#3f3f46;line-height:1.6;">
                  Bitte überprüfen Sie Ihre Zahlungsdaten in Ihrem Stripe-Kundenportal.
                </p>
                """));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private (string Plan, int MaxDevices) GetPlanFromSubscription(Subscription subscription)
    {
        var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id ?? "";

        if (priceId == _config["Stripe:ProMonthlyPriceId"] ||
            priceId == _config["Stripe:ProYearlyPriceId"])
            return ("pro", 100);

        if (priceId == _config["Stripe:EnterpriseMonthlyPriceId"] ||
            priceId == _config["Stripe:EnterpriseYearlyPriceId"])
            return ("enterprise", int.MaxValue);

        return ("starter", 25);
    }
}
