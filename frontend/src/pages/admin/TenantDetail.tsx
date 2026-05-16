import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { platformAdmin, type TenantDetail as TenantDetailType, type TenantExtension } from "@/lib/adminApi";
import { ADMIN_BASE } from "./AdminApp";

const PLAN_LABELS: Record<string, string> = {
  free: "Free",
  starter: "Starter (25)",
  pro: "Pro (100)",
  enterprise: "Enterprise (∞)",
};

function planBadge(plan: string) {
  const colors: Record<string, string> = {
    free: "bg-zinc-700 text-zinc-300",
    starter: "bg-blue-900/50 text-blue-300",
    pro: "bg-violet-900/50 text-violet-300",
    enterprise: "bg-amber-900/50 text-amber-300",
  };
  return (
    <span className={`text-xs px-2 py-0.5 rounded font-medium ${colors[plan] ?? "bg-zinc-700 text-zinc-300"}`}>
      {PLAN_LABELS[plan] ?? plan}
    </span>
  );
}

function fmt(d: string | null | undefined) {
  if (!d) return "—";
  return new Date(d).toLocaleDateString("de-DE");
}

export function TenantDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [tenant, setTenant] = useState<TenantDetailType | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [showExtend, setShowExtend] = useState(false);
  const [showDelete, setShowDelete] = useState(false);
  const [error, setError] = useState("");

  const load = () => {
    if (!id) return;
    setLoading(true);
    platformAdmin.getTenant(id).then(setTenant).finally(() => setLoading(false));
  };

  useEffect(load, [id]);

  async function doAction(fn: () => Promise<unknown>) {
    setActionLoading(true);
    setError("");
    try {
      await fn();
      load();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setActionLoading(false);
    }
  }

  if (loading) return <div className="text-zinc-500 text-sm">Laden...</div>;
  if (!tenant) return <div className="text-red-400 text-sm">Tenant nicht gefunden</div>;

  return (
    <div className="space-y-6 max-w-3xl">
      <div className="flex items-center gap-3">
        <button onClick={() => navigate(-1)} className="text-zinc-500 hover:text-zinc-300 text-sm">← Zurück</button>
        <h2 className="text-lg font-semibold text-zinc-100">{tenant.name}</h2>
        {planBadge(tenant.plan)}
        <span className={`text-xs ${tenant.isActive ? "text-green-400" : "text-red-400"}`}>
          {tenant.isActive ? "aktiv" : "inaktiv"}
        </span>
      </div>

      {error && <div className="bg-red-900/20 border border-red-800 rounded-lg px-4 py-3 text-sm text-red-400">{error}</div>}

      {/* Info card */}
      <div className="bg-zinc-900 border border-zinc-800 rounded-xl p-5">
        <h3 className="text-sm font-medium text-zinc-300 mb-4">Details</h3>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm">
          <Row label="Slug" value={tenant.slug} />
          <Row label="Admin E-Mail" value={tenant.adminEmail} />
          <Row label="Geräte-Limit" value={tenant.maxDevices === 2147483647 ? "Unbegrenzt" : String(tenant.maxDevices)} />
          <Row label="Aktive Geräte" value={tenant.deviceCount !== null ? String(tenant.deviceCount) : "—"} />
          <Row label="Status" value={tenant.subscriptionStatus ?? "—"} />
          <Row label="Trial endet" value={fmt(tenant.trialEndsAt)} />
          <Row label="Periode endet" value={fmt(tenant.currentPeriodEndsAt)} />
          <Row label="Erstellt" value={fmt(tenant.createdAt)} />
          {tenant.scheduledDeletionAt && <Row label="Löschung am" value={fmt(tenant.scheduledDeletionAt)} highlight />}
          {tenant.stripeCustomerId && <Row label="Stripe Customer" value={tenant.stripeCustomerId} mono />}
          {tenant.stripeSubscriptionId && <Row label="Stripe Sub" value={tenant.stripeSubscriptionId} mono />}
        </dl>
      </div>

      {/* Actions */}
      <div className="flex flex-wrap gap-2">
        <button
          disabled={actionLoading}
          onClick={() => setShowExtend(true)}
          className="bg-green-700 hover:bg-green-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
        >
          Verlängern / Gutschrift
        </button>

        {tenant.isActive ? (
          <button
            disabled={actionLoading}
            onClick={() => doAction(() => platformAdmin.deactivateTenant(tenant.id))}
            className="bg-zinc-700 hover:bg-zinc-600 text-zinc-200 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
          >
            Deaktivieren
          </button>
        ) : (
          <button
            disabled={actionLoading}
            onClick={() => doAction(() => platformAdmin.activateTenant(tenant.id))}
            className="bg-zinc-700 hover:bg-zinc-600 text-zinc-200 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
          >
            Aktivieren
          </button>
        )}

        {tenant.scheduledDeletionAt && (
          <button
            disabled={actionLoading}
            onClick={() => doAction(() => platformAdmin.cancelDeletion(tenant.id))}
            className="bg-amber-700 hover:bg-amber-600 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
          >
            Löschung abbrechen
          </button>
        )}

        <button
          disabled={actionLoading}
          onClick={() => setShowDelete(true)}
          className="bg-red-800 hover:bg-red-700 text-white rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50"
        >
          Löschen
        </button>
      </div>

      {/* Extensions */}
      <div>
        <h3 className="text-sm font-medium text-zinc-300 mb-3">Verlängerungs-Verlauf</h3>
        {tenant.extensions.length === 0 ? (
          <p className="text-xs text-zinc-500">Keine Gutschriften</p>
        ) : (
          <div className="space-y-2">
            {tenant.extensions.map(ext => (
              <ExtensionRow key={ext.id} ext={ext} />
            ))}
          </div>
        )}
      </div>

      {showExtend && (
        <ExtendModal
          tenantId={tenant.id}
          tenantPlan={tenant.plan}
          onClose={() => setShowExtend(false)}
          onDone={load}
        />
      )}

      {showDelete && (
        <DeleteModal
          tenantName={tenant.name}
          onConfirm={() => doAction(async () => { await platformAdmin.deleteTenant(tenant.id); navigate(`${ADMIN_BASE}/tenants`); })}
          onClose={() => setShowDelete(false)}
        />
      )}
    </div>
  );
}

function Row({ label, value, mono, highlight }: { label: string; value: string; mono?: boolean; highlight?: boolean }) {
  return (
    <>
      <dt className="text-zinc-500">{label}</dt>
      <dd className={`${mono ? "font-mono text-xs" : ""} ${highlight ? "text-red-400 font-medium" : "text-zinc-200"}`}>{value}</dd>
    </>
  );
}

function ExtensionRow({ ext }: { ext: TenantExtension }) {
  return (
    <div className="bg-zinc-900 border border-zinc-800 rounded-lg px-4 py-3 text-sm">
      <div className="flex items-center gap-3">
        <span className="text-green-400 font-medium">+{ext.daysAdded} Tage</span>
        {ext.sendToast && <span className="text-xs bg-zinc-700 text-zinc-300 px-1.5 py-0.5 rounded">Toast</span>}
        {ext.sendEmail && <span className="text-xs bg-zinc-700 text-zinc-300 px-1.5 py-0.5 rounded">E-Mail</span>}
        <span className="text-zinc-500 text-xs ml-auto">{new Date(ext.createdAt).toLocaleString("de-DE")} · {ext.createdByUsername}</span>
      </div>
      {ext.reason && <p className="text-zinc-400 text-xs mt-1">{ext.reason}</p>}
    </div>
  );
}

function ExtendModal({ tenantId, tenantPlan, onClose, onDone }: {
  tenantId: string; tenantPlan: string; onClose: () => void; onDone: () => void;
}) {
  const [days, setDays] = useState(30);
  const [reason, setReason] = useState("");
  const [sendToast, setSendToast] = useState(false);
  const [sendEmail, setSendEmail] = useState(false);
  const [changePlan, setChangePlan] = useState(false);
  const [newPlan, setNewPlan] = useState(tenantPlan);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError("");
    try {
      await platformAdmin.extendTenant(tenantId, {
        daysAdded: days,
        reason: reason || undefined,
        sendToast,
        sendEmail,
        plan: changePlan ? newPlan : undefined,
      });
      onDone();
      onClose();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "Fehler");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-zinc-900 border border-zinc-800 rounded-xl w-full max-w-md p-6 space-y-4">
        <h3 className="text-base font-semibold text-zinc-100">Verlängern / Gutschrift</h3>
        <form onSubmit={handleSubmit} className="space-y-3">
          <div>
            <label className="text-xs text-zinc-400">Tage gutschreiben</label>
            <input
              type="number" min="1" max="3650"
              className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500"
              value={days}
              onChange={e => setDays(Number(e.target.value))}
              required
            />
          </div>
          <div>
            <label className="text-xs text-zinc-400">Begründung (optional)</label>
            <textarea
              className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-zinc-500 resize-none"
              rows={2}
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="z. B. Rabatt für frühzeitige Buchung..."
            />
          </div>
          <div className="space-y-2">
            <label className="flex items-center gap-2 cursor-pointer text-sm text-zinc-300">
              <input type="checkbox" className="rounded" checked={sendToast} onChange={e => setSendToast(e.target.checked)} />
              Toast bei nächster Anmeldung anzeigen
              {sendToast && !reason && <span className="text-xs text-amber-400">(Begründung benötigt)</span>}
            </label>
            <label className="flex items-center gap-2 cursor-pointer text-sm text-zinc-300">
              <input type="checkbox" className="rounded" checked={sendEmail} onChange={e => setSendEmail(e.target.checked)} />
              E-Mail-Benachrichtigung senden
            </label>
          </div>
          <div>
            <label className="flex items-center gap-2 cursor-pointer text-sm text-zinc-300">
              <input type="checkbox" className="rounded" checked={changePlan} onChange={e => setChangePlan(e.target.checked)} />
              Plan ändern
            </label>
            {changePlan && (
              <select
                className="w-full mt-2 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none"
                value={newPlan}
                onChange={e => setNewPlan(e.target.value)}
              >
                <option value="free">Free (unbegrenzt)</option>
                <option value="starter">Starter (25 Geräte)</option>
                <option value="pro">Pro (100 Geräte)</option>
                <option value="enterprise">Enterprise (unbegrenzt)</option>
              </select>
            )}
          </div>
          {error && <p className="text-xs text-red-400">{error}</p>}
          <div className="flex gap-2 pt-2">
            <button type="button" onClick={onClose}
              className="flex-1 border border-zinc-700 text-zinc-400 rounded-lg py-2 text-sm hover:text-zinc-100">
              Abbrechen
            </button>
            <button type="submit" disabled={loading || (sendToast && !reason)}
              className="flex-1 bg-green-700 hover:bg-green-600 text-white rounded-lg py-2 text-sm font-semibold disabled:opacity-50">
              {loading ? "..." : "Anwenden"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

function DeleteModal({ tenantName, onConfirm, onClose }: {
  tenantName: string; onConfirm: () => void; onClose: () => void;
}) {
  const [confirm, setConfirm] = useState("");
  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-zinc-900 border border-zinc-800 rounded-xl w-full max-w-sm p-6 space-y-4">
        <h3 className="text-base font-semibold text-red-400">Tenant löschen</h3>
        <p className="text-sm text-zinc-400">
          Dies löscht die gesamte Datenbank von <strong className="text-zinc-200">{tenantName}</strong> unwiderruflich.
        </p>
        <div>
          <label className="text-xs text-zinc-500">Tippe <code className="text-zinc-300">{tenantName}</code> zur Bestätigung</label>
          <input
            className="w-full mt-1 bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-2 text-sm text-zinc-100 outline-none focus:border-red-700"
            value={confirm}
            onChange={e => setConfirm(e.target.value)}
          />
        </div>
        <div className="flex gap-2">
          <button onClick={onClose}
            className="flex-1 border border-zinc-700 text-zinc-400 rounded-lg py-2 text-sm hover:text-zinc-100">
            Abbrechen
          </button>
          <button
            disabled={confirm !== tenantName}
            onClick={onConfirm}
            className="flex-1 bg-red-700 hover:bg-red-600 text-white rounded-lg py-2 text-sm font-semibold disabled:opacity-50"
          >
            Löschen
          </button>
        </div>
      </div>
    </div>
  );
}
