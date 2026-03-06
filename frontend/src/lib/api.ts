const BASE_URL = import.meta.env.VITE_API_URL || "http://localhost:5000";

function getToken() {
  return localStorage.getItem("token");
}

async function request<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const token = getToken();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options.headers as Record<string, string>),
  };
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(`${BASE_URL}${path}`, { ...options, headers });

  if (res.status === 401) {
    localStorage.removeItem("token");
    window.location.href = "/login";
    throw new Error("Unauthorized");
  }

  if (!res.ok) {
    const err = await res.json().catch(() => ({ message: res.statusText }));
    throw new Error(err.message || "Request failed");
  }

  if (res.status === 204) return undefined as T;
  return res.json();
}

// Users
export const users = {
  list: () => request<AppUser[]>("/api/users"),
  create: (data: { username: string; password: string }) =>
    request<AppUser>("/api/users", { method: "POST", body: JSON.stringify(data) }),
  resetPassword: (id: string, newPassword: string) =>
    request(`/api/users/${id}/reset-password`, { method: "POST", body: JSON.stringify({ newPassword }) }),
  delete: (id: string) =>
    request(`/api/users/${id}`, { method: "DELETE" }),
};

// Settings
export const settings = {
  get: () => request<{ checkinIntervalMinutes: number }>("/api/settings"),
};

// Auth
export const auth = {
  login: (username: string, password: string) =>
    request<{ token: string; username: string; role: string }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request("/api/auth/change-password", {
      method: "POST",
      body: JSON.stringify({ currentPassword, newPassword }),
    }),
};

// Devices
export const devices = {
  list: (params?: Record<string, string>) => {
    const qs = params ? "?" + new URLSearchParams(params).toString() : "";
    return request<Device[]>(`/api/devices${qs}`);
  },
  get: (id: string) => request<DeviceDetail>(`/api/devices/${id}`),
  patch: (id: string, data: PatchDevice) =>
    request(`/api/devices/${id}`, { method: "PATCH", body: JSON.stringify(data) }),
  getSoftware: (id: string) => request<Software[]>(`/api/devices/${id}/software`),
  requestLicense: (id: string) => request(`/api/devices/${id}/request-license`, { method: "POST" }),
  getLicense: (id: string) => request<LicenseInfo>(`/api/devices/${id}/license`),
  getPending: () => request<PendingDevice[]>("/api/devices/pending"),
  getPendingCount: () => request<{ count: number }>("/api/devices/pending/count"),
  getStats: () => request<{ total: number; online: number; offline: number; pending: number }>("/api/devices/stats"),
  approve: (id: string, data: { customerId?: string; groupId?: string }) =>
    request(`/api/devices/pending/${id}/approve`, { method: "POST", body: JSON.stringify(data) }),
  reject: (id: string) =>
    request(`/api/devices/pending/${id}/reject`, { method: "POST" }),
  delete: (id: string) =>
    request(`/api/devices/${id}`, { method: "DELETE" }),
};

// Customers
export const customers = {
  list: () => request<Customer[]>("/api/customers"),
  create: (data: { name: string; contactEmail: string }) =>
    request<Customer>("/api/customers", { method: "POST", body: JSON.stringify(data) }),
  update: (id: string, data: { name: string; contactEmail: string }) =>
    request<Customer>(`/api/customers/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  delete: (id: string) =>
    request(`/api/customers/${id}`, { method: "DELETE" }),
};

// Groups
export const groups = {
  list: () => request<Group[]>("/api/groups"),
  create: (data: { name: string; description: string; color?: string }) =>
    request<Group>("/api/groups", { method: "POST", body: JSON.stringify(data) }),
  update: (id: string, data: { name: string; description: string; color?: string }) =>
    request<Group>(`/api/groups/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  delete: (id: string) =>
    request(`/api/groups/${id}`, { method: "DELETE" }),
};

// Types
export interface Device {
  id: string;
  hostname: string;
  description: string;
  windowsVersion: string;
  windowsBuild: string;
  windowsEdition: string;
  cpuModel: string;
  cpuCores: number;
  ramTotalGB: number;
  lastSeenAt: string | null;
  licenseType: string;
  isOnline: boolean;
  customer: { id: string; name: string } | null;
  group: { id: string; name: string; color: string | null } | null;
}

export interface DeviceDetail extends Device {
  networkAdaptersJson: string;
  licenseRequested: boolean;
  createdAt: string;
  recentCheckins: Array<{
    checkedInAt: string;
    ramUsedGB: number;
    diskDrivesJson: string;
  }>;
}

export interface PatchDevice {
  description?: string;
  customerId?: string | null;
  groupId?: string | null;
}

export interface Software {
  id: string;
  name: string;
  version: string;
  publisher: string;
  installDate: string;
  updatedAt: string;
}

export interface LicenseInfo {
  id: string;
  windowsKey: string | null;
  licenseType: string;
  officeKey: string | null;
  officeVersion: string;
  fetchedAt: string;
}

export interface PendingDevice {
  id: string;
  hostname: string;
  windowsVersion: string;
  cpuModel: string;
  ramTotalGB: number;
  requestedAt: string;
  status: string;
}

export interface Customer {
  id: string;
  name: string;
  contactEmail: string;
  createdAt: string;
  deviceCount: number;
}

export interface AppUser {
  id: string;
  username: string;
  role: string;
  createdAt: string;
}

export interface Group {
  id: string;
  name: string;
  description: string;
  color: string | null;
  createdAt: string;
  deviceCount: number;
}
