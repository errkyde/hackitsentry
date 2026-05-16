import { useState } from "react";
import { platformAdmin, type ProvisionResult } from "@/lib/adminApi";

interface Props {
  onClose: () => void;
  onCreated: () => void;
}

export function CreateTenantModal({ onClose, onCreated }: Props) {
  const [companyName, setCompanyName] = useState("");
  const [adminEmail, setAdminEmail] = useState("");
  const [plan, setPlan] = useState("starter");
  const [trialDays, setTrialDays] = useState(14);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState<ProvisionResult | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const res = await platformAdmin.createTenant({
        companyName,
        adminEmail,
        plan,
        trialDays: plan === "free" ? 0 : trialDays,
      });
      setResult(res);
      onCreated();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-zinc-900 border border-zinc-800 rounded-xl w-full max-w-md p-6 space-y-4">
        {!result ? (
          <>
            <h3 className="text-base font-semibold text-zinc-100">Neuen Tenant erstellen</h3>
            <form onSubmit={handleSubmit} className="space-y-3">
              <div>
                <label className="text-xs text-zinc-400">Firmenname</label>
                <input
                  className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
                  value={companyName}
                  onChange={e => setCompanyName(e.target.value)}
                  required autoFocus
                />
              </div>
              <div>
                <label className="text-xs text-zinc-400">Admin E-Mail</label>
                <input
                  type="email"
                  className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
                  value={adminEmail}
                  onChange={e => setAdminEmail(e.target.value)}
                  required
                />
              </div>
              <div>
                <label className="text-xs text-zinc-400">Plan</label>
                <select
                  className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none"
                  value={plan}
                  onChange={e => setPlan(e.target.value)}
                >
                  <option value="free">Free (dauerhaft kostenlos, unbegrenzt)</option>
                  <option value="starter">Starter (25 Geräte)</option>
                  <option value="pro">Pro (100 Geräte)</option>
                  <option value="enterprise">Enterprise (unbegrenzt)</option>
                </select>
              </div>
              {plan !== "free" && (
                <div>
                  <label className="text-xs text-zinc-400">Trial-Tage</label>
                  <input
                    type="number"
                    min="0"
                    max="365"
                    className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
                    value={trialDays}
                    onChange={e => setTrialDays(Number(e.target.value))}
                  />
                </div>
              )}
              {error && <p className="text-xs text-red-400">{error}</p>}
              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 border border-zinc-700 text-zinc-400 rounded-lg py-2 text-sm hover:text-zinc-100"
                >
                  Abbrechen
                </button>
                <button
                  type="submit"
                  disabled={loading}
                  className="flex-1 bg-zinc-100 text-zinc-900 rounded-lg py-2 text-sm font-semibold hover:bg-white disabled:opacity-50"
                >
                  {loading ? "Erstelle..." : "Erstellen"}
                </button>
              </div>
            </form>
          </>
        ) : (
          <>
            <h3 className="text-base font-semibold text-zinc-100">✓ Tenant erstellt</h3>
            <div className="space-y-2 text-sm">
              <InfoRow label="Login-URL" value={result.loginUrl} mono />
              <InfoRow label="Benutzername" value={result.adminUsername} mono />
              <InfoRow label="Passwort" value={result.adminPassword} mono highlight />
              <InfoRow label="Deploy-Key" value={result.deployKeyToken} mono />
            </div>
            <p className="text-xs text-zinc-500">Eine Willkommens-E-Mail wurde an die Admin-Adresse gesendet (falls SMTP konfiguriert).</p>
            <button
              onClick={onClose}
              className="w-full bg-zinc-100 text-zinc-900 rounded-lg py-2 text-sm font-semibold hover:bg-white mt-2"
            >
              Schließen
            </button>
          </>
        )}
      </div>
    </div>
  );
}

function InfoRow({ label, value, mono, highlight }: { label: string; value: string; mono?: boolean; highlight?: boolean }) {
  return (
    <div className="flex gap-2">
      <span className="text-zinc-500 w-28 shrink-0">{label}</span>
      <span className={`break-all ${mono ? "font-mono" : ""} ${highlight ? "text-red-300" : "text-zinc-200"}`}>{value}</span>
    </div>
  );
}
