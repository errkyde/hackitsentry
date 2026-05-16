import { Routes, Route, Navigate, Outlet, NavLink } from "react-router-dom";
import { AdminLogin } from "./AdminLogin";
import { AdminDashboard } from "./AdminDashboard";
import { TenantList } from "./TenantList";
import { TenantDetail } from "./TenantDetail";

export const ADMIN_BASE = "/adminpage";

function AdminProtectedRoute({ children }: { children: React.ReactNode }) {
  const token = localStorage.getItem("adminToken");
  if (!token) return <Navigate to={`${ADMIN_BASE}/login`} replace />;
  return <>{children}</>;
}

function AdminLayout() {
  function logout() {
    localStorage.removeItem("adminToken");
    window.location.href = `${ADMIN_BASE}/login`;
  }

  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-6 h-12 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <span className="text-xs font-semibold tracking-wide text-zinc-400 uppercase">
            HITSight · Platform Admin
          </span>
          <nav className="flex gap-4">
            <NavLink
              to={ADMIN_BASE}
              end
              className={({ isActive }) =>
                `text-sm ${isActive ? "text-zinc-100 font-medium" : "text-zinc-500 hover:text-zinc-300"}`
              }
            >
              Dashboard
            </NavLink>
            <NavLink
              to={`${ADMIN_BASE}/tenants`}
              className={({ isActive }) =>
                `text-sm ${isActive ? "text-zinc-100 font-medium" : "text-zinc-500 hover:text-zinc-300"}`
              }
            >
              Tenants
            </NavLink>
          </nav>
        </div>
        <button onClick={logout} className="text-xs text-zinc-500 hover:text-zinc-300">
          Abmelden
        </button>
      </header>
      <main className="p-6 max-w-6xl mx-auto">
        <Outlet />
      </main>
    </div>
  );
}

export function AdminApp() {
  return (
    <Routes>
      <Route path="login" element={<AdminLogin />} />
      <Route
        path="/"
        element={
          <AdminProtectedRoute>
            <AdminLayout />
          </AdminProtectedRoute>
        }
      >
        <Route index element={<AdminDashboard />} />
        <Route path="tenants" element={<TenantList />} />
        <Route path="tenants/:id" element={<TenantDetail />} />
      </Route>
      <Route path="*" element={<Navigate to={ADMIN_BASE} replace />} />
    </Routes>
  );
}
