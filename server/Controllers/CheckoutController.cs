using HITSight.Server.Data;
using HITSight.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using System.Text.RegularExpressions;

namespace HITSight.Server.Controllers;

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<CheckoutController> _logger;
    private readonly PlatformDbContext? _platformDb;

    public CheckoutController(IConfiguration config, ILogger<CheckoutController> logger, PlatformDbContext? platformDb = null)
    {
        _config = config;
        _logger = logger;
        _platformDb = platformDb;
    }

    // POST /api/checkout/session
    [HttpPost("session")]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> CreateSession([FromBody] CheckoutRequest request)
    {
        if (_platformDb == null)
            return StatusCode(503, new { error = "Platform not configured." });

        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.CompanyName) || request.CompanyName.Length > 100)
            return UnprocessableEntity(new { error = "Firmenname fehlt oder zu lang." });

        if (string.IsNullOrWhiteSpace(request.Email) || !IsValidEmail(request.Email))
            return UnprocessableEntity(new { error = "Ungültige E-Mail-Adresse." });

        var validPlans = new[] { "starter", "pro", "enterprise" };
        if (!validPlans.Contains(request.Plan))
            return UnprocessableEntity(new { error = "Ungültiges Paket." });

        if (request.BillingInterval != "monthly" && request.BillingInterval != "yearly")
            return UnprocessableEntity(new { error = "Ungültiges Abrechnungsintervall." });

        var priceId = GetPriceId(request.Plan, request.BillingInterval);
        if (string.IsNullOrEmpty(priceId))
            return StatusCode(503, new { error = "Preise nicht konfiguriert." });

        var publishableKey = _config["Stripe:PublishableKey"];
        if (string.IsNullOrEmpty(publishableKey))
            return StatusCode(503, new { error = "Stripe nicht konfiguriert." });

        // Generate preview slug (provisioning finalises uniqueness at webhook time)
        var slug = TenantProvisioningService.SlugifyName(request.CompanyName);
        if (string.IsNullOrEmpty(slug))
            return UnprocessableEntity(new { error = "Firmenname konnte nicht als Subdomain verarbeitet werden." });

        // If slug is taken, show user what suffix they'll get
        var candidate = slug;
        var suffix = 2;
        while (await _platformDb.Tenants.AnyAsync(t => t.Slug == candidate))
            candidate = $"{slug}-{suffix++}";
        slug = candidate;

        var platformDomain = _config["Platform:Domain"] ?? "localhost";

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            PaymentMethodTypes = ["card"],
            LineItems =
            [
                new SessionLineItemOptions { Price = priceId, Quantity = 1 }
            ],
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                TrialPeriodDays = 14,
            },
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            CustomerEmail = request.Email,
            Metadata = new Dictionary<string, string>
            {
                ["companyName"] = request.CompanyName,
                ["plan"] = request.Plan,
                ["slug"] = slug,
                ["email"] = request.Email,
            },
            SuccessUrl = $"https://{slug}.{platformDomain}/login?welcome=1",
            CancelUrl = $"https://{platformDomain}/#pricing",
        };

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return Ok(new { sessionId = session.Id, publishableKey });
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe session creation failed");
            return StatusCode(502, new { error = "Stripe-Fehler. Bitte versuche es erneut." });
        }
    }

    // GET /api/checkout/pricing  — public pricing data for the landing page
    [HttpGet("pricing")]
    public IActionResult GetPricing()
    {
        // Prices are fetched from Stripe dashboard and configured via env vars
        // This endpoint returns plan metadata (limits, features) — not prices
        // Actual prices are loaded from Stripe via the publishable key on the frontend
        return Ok(new
        {
            plans = new[]
            {
                new { id = "starter",    name = "Starter",    maxDevices = (int?)25,  features = new[] { "Bis zu 25 Geräte",        "Alle Kern-Features", "E-Mail-Support"         } },
                new { id = "pro",        name = "Pro",        maxDevices = (int?)100, features = new[] { "Bis zu 100 Geräte",       "Alle Kern-Features", "Prioritäts-Support"     } },
                new { id = "enterprise", name = "Enterprise", maxDevices = (int?)null, features = new[] { "Unbegrenzte Geräte",    "Alle Features",      "Dedizierter Support"    } },
            },
            publishableKey = _config["Stripe:PublishableKey"],
            monthlyPriceIds = new
            {
                starter = _config["Stripe:StarterMonthlyPriceId"],
                pro     = _config["Stripe:ProMonthlyPriceId"],
                enterprise = _config["Stripe:EnterpriseMonthlyPriceId"],
            },
            yearlyPriceIds = new
            {
                starter = _config["Stripe:StarterYearlyPriceId"],
                pro     = _config["Stripe:ProYearlyPriceId"],
                enterprise = _config["Stripe:EnterpriseYearlyPriceId"],
            },
        });
    }

    private string? GetPriceId(string plan, string interval) => (plan, interval) switch
    {
        ("starter",    "monthly") => _config["Stripe:StarterMonthlyPriceId"],
        ("starter",    "yearly")  => _config["Stripe:StarterYearlyPriceId"],
        ("pro",        "monthly") => _config["Stripe:ProMonthlyPriceId"],
        ("pro",        "yearly")  => _config["Stripe:ProYearlyPriceId"],
        ("enterprise", "monthly") => _config["Stripe:EnterpriseMonthlyPriceId"],
        ("enterprise", "yearly")  => _config["Stripe:EnterpriseYearlyPriceId"],
        _ => null
    };

    private static bool IsValidEmail(string email) =>
        Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}

public record CheckoutRequest(string CompanyName, string Email, string Plan, string BillingInterval);
