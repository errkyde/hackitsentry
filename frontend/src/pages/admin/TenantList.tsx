import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router-dom";
import { platformAdmin, type TenantSummary } from "@/lib/adminApi";
import { CreateTenantModal } from "./CreateTenantModal";
import { ADMIN_BASE } from "./AdminApp";

function planBadge(plan: string) {
  const colors: Record<string, string> = {
    free: "bg-zinc-700 text-zinc-300",
    starter: "bg-blue-900/50 text-blue-300",
    pro: "bg-violet-900/50 text-violet-300",
    enterprise: "bg-amber-900/50 text-amber-300",
  };
  return (
    <span className={`text-xs px-2 py-0.5 rounded font-medium ${colors[plan] ?? "bg-zinc-700 text-zinc-300"}`}>
      {plan}
    </span>
  );
}

export function TenantList() {
  const [items, setItems] = useState<TenantSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [plan, setPlan] = useState("");
  const [status, setStatus] = useState("");
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const pageSize = 25;

  const load = useCallback(() => {
    setLoading(true);
    platformAdmin.listTenants({ search: search || undefined, plan: plan || undefined, status: status || undefined, page, pageSize })
      .then(res => { setItems(res.items); setTotal(res.total); })
      .finally(() => setLoading(false));
  }, [search, plan, status, page]);

  useEffect(() => { load(); }, [load]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-zinc-100">Tenants ({total})</h2>
        <button
          onClick={() => setShowCreate(true)}
          className="bg-zinc-100 text-zinc-900 rounded-lg px-4 py-2 text-sm font-semibold hover:bg-white"
        >
          + Neu
        </button>
      </div>

      <div className="flex gap-2 flex-wrap">
        <input
          className="bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-1.5 text-sm text-zinc-100 outline-none focus:border-zinc-500 w-56"
          placeholder="Suchen..."
          value={search}
          onChange={e => { setSearch(e.target.value); setPage(1); }}
        />
        <select
          className="bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-1.5 text-sm text-zinc-100 outline-none"
          value={plan}
          onChange={e => { setPlan(e.target.value); setPage(1); }}
        >
          <option value="">Alle Pläne</option>
          <option value="free">Free</option>
          <option value="starter">Starter</option>
          <option value="pro">Pro</option>
          <option value="enterprise">Enterprise</option>
        </select>
        <select
          className="bg-zinc-800 border border-zinc-700 rounded-lg px-3 py-1.5 text-sm text-zinc-100 outline-none"
          value={status}
          onChange={e => { setStatus(e.target.value); setPage(1); }}
        >
          <option value="">Alle Status</option>
          <option value="active">Aktiv</option>
          <option value="trialing">Trial</option>
          <option value="inactive">Inaktiv</option>
          <option value="deletion">Löschung</option>
        </select>
      </div>

      <div className="bg-zinc-900 border border-zinc-800 rounded-xl overflow-hidden">
        {loading ? (
          <div className="p-8 text-center text-zinc-500 text-sm">Laden...</div>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-zinc-800">
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Tenant</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Plan</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Status</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Laufzeit bis</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Erstellt</th>
              </tr>
            </thead>
            <tbody>
              {items.map(t => (
                <tr key={t.id} className="border-b border-zinc-800/50 hover:bg-zinc-800/30">
                  <td className="px-4 py-3">
                    <Link to={`${ADMIN_BASE}/tenants/${t.id}`} className="text-zinc-100 hover:text-white font-medium block">
                      {t.name}
                    </Link>
                    <span className="text-xs text-zinc-500">{t.slug} · {t.adminEmail}</span>
                  </td>
                  <td className="px-4 py-3">{planBadge(t.plan)}</td>
                  <td className="px-4 py-3">
                    {t.scheduledDeletionAt ? (
                      <span className="text-xs text-red-400">Löschung {new Date(t.scheduledDeletionAt).toLocaleDateString("de-DE")}</span>
                    ) : (
                      <span className={`text-xs ${t.isActive ? "text-green-400" : "text-red-400"}`}>
                        {t.isActive ? (t.subscriptionStatus ?? "aktiv") : "inaktiv"}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-xs text-zinc-400">
                    {t.trialEndsAt ? new Date(t.trialEndsAt).toLocaleDateString("de-DE")
                      : t.currentPeriodEndsAt ? new Date(t.currentPeriodEndsAt).toLocaleDateString("de-DE")
                      : t.plan === "free" ? "∞" : "—"}
                  </td>
                  <td className="px-4 py-3 text-xs text-zinc-500">
                    {new Date(t.createdAt).toLocaleDateString("de-DE")}
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-8 text-center text-zinc-500 text-xs">Keine Tenants gefunden</td></tr>
              )}
            </tbody>
          </table>
        )}
      </div>

      {total > pageSize && (
        <div className="flex gap-2 justify-center text-sm">
          <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}
            className="px-3 py-1 rounded border border-zinc-700 text-zinc-400 hover:text-zinc-100 disabled:opacity-30">
            ←
          </button>
          <span className="px-3 py-1 text-zinc-500">Seite {page} / {Math.ceil(total / pageSize)}</span>
          <button disabled={page >= Math.ceil(total / pageSize)} onClick={() => setPage(p => p + 1)}
            className="px-3 py-1 rounded border border-zinc-700 text-zinc-400 hover:text-zinc-100 disabled:opacity-30">
            →
          </button>
        </div>
      )}

      {showCreate && <CreateTenantModal onClose={() => setShowCreate(false)} onCreated={load} />}
    </div>
  );
}
