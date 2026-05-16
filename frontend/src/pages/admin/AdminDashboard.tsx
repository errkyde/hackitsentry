import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { platformAdmin, type PlatformStats, type TenantSummary } from "@/lib/adminApi";
import { ADMIN_BASE } from "./AdminApp";

function StatCard({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div className="bg-zinc-900 border border-zinc-800 rounded-xl p-4">
      <p className="text-xs text-zinc-500 mb-1">{label}</p>
      <p className={`text-2xl font-bold ${color}`}>{value}</p>
    </div>
  );
}

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

export function AdminDashboard() {
  const [stats, setStats] = useState<PlatformStats | null>(null);
  const [recent, setRecent] = useState<TenantSummary[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      platformAdmin.getStats(),
      platformAdmin.listTenants({ pageSize: 10 }),
    ]).then(([s, p]) => {
      setStats(s);
      setRecent(p.items);
    }).finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-zinc-500 text-sm">Laden...</div>;

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-zinc-100">Übersicht</h2>

      {stats && (
        <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
          <StatCard label="Gesamt" value={stats.total} color="text-zinc-100" />
          <StatCard label="Aktiv" value={stats.active} color="text-green-400" />
          <StatCard label="Trial" value={stats.trialing} color="text-amber-400" />
          <StatCard label="Kostenlos" value={stats.free} color="text-blue-400" />
          <StatCard label="Löschung geplant" value={stats.scheduledDeletion} color="text-red-400" />
        </div>
      )}

      <div>
        <div className="flex items-center justify-between mb-3">
          <h3 className="text-sm font-medium text-zinc-300">Neueste Tenants</h3>
          <Link to={`${ADMIN_BASE}/tenants`} className="text-xs text-zinc-500 hover:text-zinc-300">Alle anzeigen →</Link>
        </div>
        <div className="bg-zinc-900 border border-zinc-800 rounded-xl overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-zinc-800">
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Tenant</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Plan</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Status</th>
                <th className="px-4 py-3 text-left text-xs text-zinc-500 font-medium">Erstellt</th>
              </tr>
            </thead>
            <tbody>
              {recent.map(t => (
                <tr key={t.id} className="border-b border-zinc-800/50 hover:bg-zinc-800/30">
                  <td className="px-4 py-3">
                    <Link to={`${ADMIN_BASE}/tenants/${t.id}`} className="text-zinc-100 hover:text-white font-medium">
                      {t.name}
                    </Link>
                    <p className="text-xs text-zinc-500">{t.slug}</p>
                  </td>
                  <td className="px-4 py-3">{planBadge(t.plan)}</td>
                  <td className="px-4 py-3">
                    <span className={`text-xs ${t.isActive ? "text-green-400" : "text-red-400"}`}>
                      {t.isActive ? (t.subscriptionStatus ?? "aktiv") : "inaktiv"}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-xs text-zinc-500">
                    {new Date(t.createdAt).toLocaleDateString("de-DE")}
                  </td>
                </tr>
              ))}
              {recent.length === 0 && (
                <tr><td colSpan={4} className="px-4 py-6 text-center text-zinc-500 text-xs">Keine Tenants</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
