import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { loadStripe } from "@stripe/stripe-js";
import { checkout, type PricingInfo } from "@/lib/api";

// Mirror of server-side Slugify
function slugify(input: string): string {
  let s = input.toLowerCase();
  s = s.replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss");
  s = s.replace(/[^a-z0-9]+/g, "-");
  s = s.replace(/^-+|-+$/g, "");
  s = s.replace(/-{2,}/g, "-");
  return s || "";
}

const PLATFORM_DOMAIN =
  (import.meta.env.VITE_PLATFORM_DOMAIN as string) || window.location.hostname;

// ── Checkout modal ────────────────────────────────────────────────────────────

interface CheckoutModalProps {
  initialPlan: string;
  pricingInfo: PricingInfo | null;
  onClose: () => void;
}

function CheckoutModal({ initialPlan, pricingInfo, onClose }: CheckoutModalProps) {
  const [companyName, setCompanyName] = useState("");
  const [email, setEmail] = useState("");
  const [plan, setPlan] = useState(initialPlan);
  const [billing, setBilling] = useState<"monthly" | "yearly">("monthly");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const slug = slugify(companyName);
  const slugPreview = slug ? `${slug}.${PLATFORM_DOMAIN}` : `ihr-unternehmen.${PLATFORM_DOMAIN}`;

  const stripeReady = !!pricingInfo?.publishableKey;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!stripeReady) { setError("Stripe ist nicht konfiguriert."); return; }
    setError("");
    setLoading(true);
    try {
      const res = await checkout.createSession({
        companyName,
        email,
        plan,
        billingInterval: billing,
      });
      const stripe = await loadStripe(res.publishableKey);
      await stripe?.redirectToCheckout({ sessionId: res.sessionId });
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler beim Start des Checkouts.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 p-4">
      <div className="bg-zinc-900 border border-zinc-800 rounded-2xl w-full max-w-md p-6 space-y-5">
        <div className="flex items-start justify-between">
          <h3 className="text-base font-semibold text-zinc-100">14 Tage kostenlos testen</h3>
          <button onClick={onClose} className="text-zinc-500 hover:text-zinc-300 text-xl leading-none">×</button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="text-xs text-zinc-400">Firmenname</label>
            <input
              className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2.5 text-sm text-zinc-100 outline-none focus:border-zinc-500"
              value={companyName}
              onChange={e => setCompanyName(e.target.value)}
              placeholder="Muster GmbH"
              autoFocus
              required
            />
            <p className="mt-1.5 text-xs text-zinc-500 font-mono">
              {slug
                ? <><span className="text-zinc-300">{slug}</span>.{PLATFORM_DOMAIN}</>
                : <span className="opacity-50">ihr-unternehmen.{PLATFORM_DOMAIN}</span>
              }
            </p>
          </div>

          <div>
            <label className="text-xs text-zinc-400">E-Mail-Adresse</label>
            <input
              type="email"
              className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2.5 text-sm text-zinc-100 outline-none focus:border-zinc-500"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="admin@muster-gmbh.de"
              required
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="text-xs text-zinc-400">Paket</label>
              <select
                className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2.5 text-sm text-zinc-100 outline-none"
                value={plan}
                onChange={e => setPlan(e.target.value)}
              >
                <option value="starter">Starter (25 Geräte)</option>
                <option value="pro">Pro (100 Geräte)</option>
                <option value="enterprise">Enterprise (∞)</option>
              </select>
            </div>
            <div>
              <label className="text-xs text-zinc-400">Abrechnung</label>
              <select
                className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2.5 text-sm text-zinc-100 outline-none"
                value={billing}
                onChange={e => setBilling(e.target.value as "monthly" | "yearly")}
              >
                <option value="monthly">Monatlich</option>
                <option value="yearly">Jährlich (2 Monate gratis)</option>
              </select>
            </div>
          </div>

          {!stripeReady && (
            <p className="text-xs text-amber-400 bg-amber-900/20 border border-amber-800/50 rounded-lg px-3 py-2">
              Stripe ist nicht konfiguriert — Checkout nicht verfügbar.
            </p>
          )}

          {error && <p className="text-xs text-red-400">{error}</p>}

          <button
            type="submit"
            disabled={loading || !stripeReady}
            className="w-full bg-zinc-100 hover:bg-white text-zinc-900 font-semibold rounded-lg py-2.5 text-sm disabled:opacity-40"
          >
            {loading ? "Weiterleitung zu Stripe..." : "Weiter zu Stripe →"}
          </button>

          <p className="text-center text-xs text-zinc-500">
            14 Tage kostenlos · Keine Einrichtungsgebühr · Jederzeit kündbar
          </p>
        </form>
      </div>
    </div>
  );
}

// ── Plan card ─────────────────────────────────────────────────────────────────

const PLAN_STYLES: Record<string, { accent: string; badge?: string }> = {
  starter:    { accent: "border-zinc-700" },
  pro:        { accent: "border-violet-600", badge: "Beliebt" },
  enterprise: { accent: "border-zinc-700" },
};

function PlanCard({
  plan, features, onSelect,
}: {
  plan: PricingInfo["plans"][0];
  features: string[];
  onSelect: (planId: string) => void;
}) {
  const style = PLAN_STYLES[plan.id] ?? { accent: "border-zinc-700" };
  return (
    <div className={`relative bg-zinc-900 border-2 ${style.accent} rounded-2xl p-6 flex flex-col`}>
      {style.badge && (
        <span className="absolute -top-3 left-1/2 -translate-x-1/2 bg-violet-600 text-white text-xs font-semibold px-3 py-0.5 rounded-full">
          {style.badge}
        </span>
      )}
      <h3 className="text-lg font-semibold text-zinc-100">{plan.name}</h3>
      <p className="text-sm text-zinc-500 mt-1">
        {plan.maxDevices === null ? "Unbegrenzte Geräte" : `Bis zu ${plan.maxDevices} Geräte`}
      </p>

      <ul className="mt-5 space-y-2.5 flex-1">
        {features.map(f => (
          <li key={f} className="flex items-start gap-2 text-sm text-zinc-300">
            <span className="text-green-400 mt-0.5 shrink-0">✓</span>
            {f}
          </li>
        ))}
      </ul>

      <button
        onClick={() => onSelect(plan.id)}
        className={`mt-6 w-full py-2.5 rounded-xl text-sm font-semibold transition-colors ${
          plan.id === "pro"
            ? "bg-violet-600 hover:bg-violet-500 text-white"
            : "bg-zinc-800 hover:bg-zinc-700 text-zinc-100"
        }`}
      >
        14 Tage kostenlos testen
      </button>
    </div>
  );
}

// ── Main landing page ─────────────────────────────────────────────────────────

const PLAN_FEATURES: Record<string, string[]> = {
  starter: [
    "Bis zu 25 Geräte",
    "Hardware- & Software-Inventar",
    "Remote-Zugriff via RustDesk",
    "Windows Update Verwaltung",
    "E-Mail-Alerts",
  ],
  pro: [
    "Bis zu 100 Geräte",
    "Alle Starter-Features",
    "Gruppen & Kundenverwaltung",
    "Software-Blacklist & Alerts",
    "Audit-Log",
    "Prioritäts-Support",
  ],
  enterprise: [
    "Unbegrenzte Geräte",
    "Alle Pro-Features",
    "LDAP/Active Directory",
    "Skript-Vorlagen & Deployment",
    "Benutzerdefinierte Felder",
    "Dedizierter Support",
  ],
};

export function Landing() {
  const navigate = useNavigate();
  const [pricingInfo, setPricingInfo] = useState<PricingInfo | null>(null);
  const [checkoutPlan, setCheckoutPlan] = useState<string | null>(null);

  useEffect(() => {
    if (localStorage.getItem("token")) {
      navigate("/dashboard", { replace: true });
      return;
    }
    checkout.getPricing().then(setPricingInfo).catch(() => {});
  }, [navigate]);

  const plans = pricingInfo?.plans ?? [
    { id: "starter",    name: "Starter",    maxDevices: 25,  features: [] },
    { id: "pro",        name: "Pro",        maxDevices: 100, features: [] },
    { id: "enterprise", name: "Enterprise", maxDevices: null, features: [] },
  ];

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 flex flex-col">

      {/* Header */}
      <header className="sticky top-0 z-40 border-b border-zinc-800/60 bg-zinc-950/80 backdrop-blur">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
          <span className="font-bold text-sm tracking-tight">HackIT Sight</span>
          <nav className="hidden sm:flex items-center gap-6 text-sm text-zinc-400">
            <a href="#features" className="hover:text-zinc-100">Features</a>
            <a href="#pricing" className="hover:text-zinc-100">Preise</a>
          </nav>
          <button
            onClick={() => navigate("/login")}
            className="text-sm text-zinc-400 hover:text-zinc-100"
          >
            Anmelden →
          </button>
        </div>
      </header>

      {/* Hero */}
      <section className="flex-1 flex flex-col items-center justify-center text-center px-6 py-24">
        <div className="max-w-3xl">
          <div className="inline-block bg-zinc-800 text-zinc-400 text-xs px-3 py-1 rounded-full mb-6">
            Für MSPs & IT-Teams · Made in Germany
          </div>
          <h1 className="text-4xl sm:text-5xl font-bold tracking-tight leading-tight mb-6">
            Windows-Geräte{" "}
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-violet-400 to-blue-400">
              zentral verwalten
            </span>
          </h1>
          <p className="text-lg text-zinc-400 mb-10 max-w-xl mx-auto leading-relaxed">
            Inventar, Remote-Zugriff, Software-Verwaltung und Alerts — alles in einer
            Self-Hosted-Plattform. Für jeden Kunden eine eigene Instanz.
          </p>
          <div className="flex flex-col sm:flex-row gap-3 justify-center">
            <a
              href="#pricing"
              className="bg-zinc-100 hover:bg-white text-zinc-900 font-semibold px-6 py-3 rounded-xl text-sm"
            >
              14 Tage kostenlos testen
            </a>
            <button
              onClick={() => navigate("/login")}
              className="border border-zinc-700 hover:border-zinc-500 text-zinc-300 hover:text-zinc-100 px-6 py-3 rounded-xl text-sm"
            >
              Bereits Kunde? Anmelden
            </button>
          </div>
        </div>
      </section>

      {/* Features */}
      <section id="features" className="py-20 px-6 bg-zinc-900/40">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-2xl font-bold text-center mb-12">Alles was IT-Teams brauchen</h2>
          <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
            {FEATURES.map(f => (
              <div key={f.title} className="bg-zinc-900 border border-zinc-800 rounded-xl p-5">
                <div className="text-2xl mb-3">{f.icon}</div>
                <h3 className="font-semibold text-zinc-100 mb-1">{f.title}</h3>
                <p className="text-sm text-zinc-500 leading-relaxed">{f.desc}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Pricing */}
      <section id="pricing" className="py-20 px-6">
        <div className="max-w-5xl mx-auto">
          <h2 className="text-2xl font-bold text-center mb-3">Transparente Preise</h2>
          <p className="text-center text-zinc-500 text-sm mb-12">
            14 Tage kostenlos testen — keine Kreditkarte sofort nötig, Kündigung jederzeit möglich.
          </p>
          <div className="grid sm:grid-cols-3 gap-6">
            {plans.map(plan => (
              <PlanCard
                key={plan.id}
                plan={plan}
                features={PLAN_FEATURES[plan.id] ?? plan.features}
                onSelect={setCheckoutPlan}
              />
            ))}
          </div>
          <p className="text-center text-xs text-zinc-600 mt-8">
            Alle Preise zzgl. MwSt. · Jährliche Zahlung spart 2 Monate.
          </p>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-zinc-800 py-8 px-6">
        <div className="max-w-6xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">
          <span className="text-sm font-semibold">HackIT Sight</span>
          <div className="flex gap-6 text-xs text-zinc-500">
            <button onClick={() => navigate("/login")} className="hover:text-zinc-300">Anmelden</button>
            <a href="#pricing" className="hover:text-zinc-300">Preise</a>
            <a href="#features" className="hover:text-zinc-300">Features</a>
          </div>
        </div>
      </footer>

      {checkoutPlan && (
        <CheckoutModal
          initialPlan={checkoutPlan}
          pricingInfo={pricingInfo}
          onClose={() => setCheckoutPlan(null)}
        />
      )}
    </div>
  );
}

const FEATURES = [
  {
    icon: "🖥️",
    title: "Hardware-Inventar",
    desc: "CPU, RAM, Festplatten, Netzwerkadapter und BIOS-Infos — alles automatisch erfasst.",
  },
  {
    icon: "🔒",
    title: "Sicherheit & Compliance",
    desc: "Windows Defender Status, Blacklist-Alerts für unerwünschte Software und Audit-Log.",
  },
  {
    icon: "🖱️",
    title: "Remote-Zugriff",
    desc: "Integrierte RustDesk-Verwaltung für sicheren Remote-Zugriff direkt aus dem Dashboard.",
  },
  {
    icon: "📦",
    title: "Software-Deployment",
    desc: "Pakete zentral verteilen und auf jedem Gerät ausführen — mit Deployment-Verlauf.",
  },
  {
    icon: "🔔",
    title: "Alerts & E-Mail",
    desc: "Benachrichtigungen bei Offline-Geräten, ablaufenden Lizenzen und Software-Alerts.",
  },
  {
    icon: "🏢",
    title: "Multi-Mandant",
    desc: "Jeder Kunde erhält eine eigene isolierte Instanz mit eigener Datenbank und Subdomain.",
  },
];
