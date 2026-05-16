const BASE = import.meta.env.VITE_API_URL || "";

function getToken() {
  return localStorage.getItem("adminToken");
}

async function req<T>(path: string, options: RequestInit = {}, overrideToken?: string): Promise<T> {
  const token = overrideToken ?? getToken();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options.headers as Record<string, string>),
  };
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(`${BASE}${path}`, { ...options, headers });

  if (res.status === 401) {
    localStorage.removeItem("adminToken");
    window.location.href = "/adminpage/login";
    throw new Error("Unauthorized");
  }

  if (!res.ok) {
    const err = await res.json().catch(() => ({ message: res.statusText }));
    throw new Error(err.message || "Anfrage fehlgeschlagen");
  }

  if (res.status === 204) return undefined as T;
  return res.json();
}

// ── Auth ─────────────────────────────────────────────────────────────────────

export const platformAuth = {
  login: (username: string, password: string) =>
    req<{ tempToken: string; totpEnabled: boolean; totpSetupRequired: boolean }>(
      "/api/platform/auth/login",
      { method: "POST", body: JSON.stringify({ username, password }) }
    ),

  totpSetup: (tempToken: string) =>
    req<{ secret: string; otpAuthUri: string }>(
      "/api/platform/auth/totp-setup",
      { method: "POST" },
      tempToken
    ),

  totpConfirm: (tempToken: string, code: string) =>
    req<{ token: string }>(
      "/api/platform/auth/totp-confirm",
      { method: "POST", body: JSON.stringify({ code }) },
      tempToken
    ),

  totpVerify: (tempToken: string, code: string) =>
    req<{ token: string }>(
      "/api/platform/auth/totp-verify",
      { method: "POST", body: JSON.stringify({ code }) },
      tempToken
    ),
};

// ── Admin API ─────────────────────────────────────────────────────────────────

export const platformAdmin = {
  getStats: () =>
    req<PlatformStats>("/api/platform/admin/stats"),

  listTenants: (params?: { search?: string; plan?: string; status?: string; page?: number; pageSize?: number }) => {
    const qs = params ? "?" + new URLSearchParams(
      Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined).map(([k, v]) => [k, String(v)]))
    ).toString() : "";
    return req<TenantPage>(`/api/platform/admin/tenants${qs}`);
  },

  getTenant: (id: string) =>
    req<TenantDetail>(`/api/platform/admin/tenants/${id}`),

  createTenant: (data: { companyName: string; adminEmail: string; plan: string; maxDevices?: number; trialDays?: number }) =>
    req<ProvisionResult>("/api/platform/admin/tenants", { method: "POST", body: JSON.stringify(data) }),

  updateTenant: (id: string, data: { plan?: string; maxDevices?: number }) =>
    req<{ message: string }>(`/api/platform/admin/tenants/${id}`, { method: "PATCH", body: JSON.stringify(data) }),

  deactivateTenant: (id: string) =>
    req<{ message: string }>(`/api/platform/admin/tenants/${id}/deactivate`, { method: "POST" }),

  activateTenant: (id: string) =>
    req<{ message: string }>(`/api/platform/admin/tenants/${id}/activate`, { method: "POST" }),

  cancelDeletion: (id: string) =>
    req<{ message: string }>(`/api/platform/admin/tenants/${id}/cancel-deletion`, { method: "POST" }),

  deleteTenant: (id: string) =>
    req<{ message: string }>(`/api/platform/admin/tenants/${id}`, { method: "DELETE" }),

  extendTenant: (id: string, data: ExtendRequest) =>
    req<{ message: string; newEndDate: string }>(`/api/platform/admin/tenants/${id}/extend`, {
      method: "POST", body: JSON.stringify(data),
    }),

  getExtensions: (id: string) =>
    req<TenantExtension[]>(`/api/platform/admin/tenants/${id}/extensions`),

  listSuperAdmins: () =>
    req<SuperAdminUser[]>("/api/platform/admin/super-admins"),

  createSuperAdmin: (username: string, password: string) =>
    req<{ message: string }>("/api/platform/admin/super-admins", {
      method: "POST", body: JSON.stringify({ username, password }),
    }),

  deleteSuperAdmin: (id: string) =>
    req<{ message: string }>(`/api/platform/admin/super-admins/${id}`, { method: "DELETE" }),
};

// ── Types ─────────────────────────────────────────────────────────────────────

export interface PlatformStats {
  total: number;
  active: number;
  trialing: number;
  free: number;
  scheduledDeletion: number;
}

export interface TenantSummary {
  id: string;
  slug: string;
  name: string;
  plan: string;
  maxDevices: number;
  isActive: boolean;
  adminEmail: string;
  subscriptionStatus: string | null;
  trialEndsAt: string | null;
  currentPeriodEndsAt: string | null;
  scheduledDeletionAt: string | null;
  createdAt: string;
}

export interface TenantPage {
  total: number;
  page: number;
  pageSize: number;
  items: TenantSummary[];
}

export interface TenantDetail extends TenantSummary {
  deactivatedAt: string | null;
  stripeCustomerId: string | null;
  stripeSubscriptionId: string | null;
  trialReminderSentAt: string | null;
  deviceCount: number | null;
  extensions: TenantExtension[];
}

export interface TenantExtension {
  id: string;
  daysAdded: number;
  reason: string | null;
  sendToast: boolean;
  sendEmail: boolean;
  createdByUsername: string;
  createdAt: string;
}

export interface ExtendRequest {
  daysAdded: number;
  reason?: string;
  sendToast: boolean;
  sendEmail: boolean;
  plan?: string;
  maxDevices?: number;
}

export interface ProvisionResult {
  slug: string;
  loginUrl: string;
  adminUsername: string;
  adminPassword: string;
  deployKeyToken: string;
  msiInstallUrl: string;
}

export interface SuperAdminUser {
  id: string;
  username: string;
  totpEnabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}
