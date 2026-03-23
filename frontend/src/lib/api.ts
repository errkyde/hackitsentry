const BASE_URL = import.meta.env.VITE_API_URL || "";

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
  create: (data: { username: string; password: string; role?: string }) =>
    request<AppUser>("/api/users", { method: "POST", body: JSON.stringify(data) }),
  resetPassword: (id: string, newPassword: string) =>
    request(`/api/users/${id}/reset-password`, { method: "POST", body: JSON.stringify({ newPassword }) }),
  delete: (id: string) =>
    request(`/api/users/${id}`, { method: "DELETE" }),
};

// Settings
export const settings = {
  get: () => request<{ checkinIntervalMinutes: number }>("/api/settings"),
  saveCheckin: (checkinIntervalMinutes: number) =>
    request<{ message: string; checkinIntervalMinutes: number }>("/api/settings/checkin", {
      method: "PUT", body: JSON.stringify({ checkinIntervalMinutes }),
    }),
  getEmail: () => request<EmailSettings>("/api/settings/email"),
  saveEmail: (data: EmailSettingsInput) =>
    request<{ message: string }>("/api/settings/email", { method: "PUT", body: JSON.stringify(data) }),
  testEmail: () =>
    request<{ message: string }>("/api/settings/email/test", { method: "POST" }),
  getRustDesk: () => request<RustDeskSettings>("/api/settings/rustdesk"),
  saveRustDesk: (data: RustDeskSettings) =>
    request<{ message: string }>("/api/settings/rustdesk", { method: "PUT", body: JSON.stringify(data) }),
};

// Auth
export const auth = {
  login: (username: string, password: string) =>
    request<{ token: string; username: string; role: string }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  setupRequired: () =>
    request<{ required: boolean }>("/api/auth/setup-required"),
  setup: (username: string, password: string) =>
    request<{ token: string; username: string; role: string }>("/api/auth/setup", {
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
  bulkUpdate: (data: { deviceIds: string[]; setCustomerId?: string | null; setGroupId?: string | null }) =>
    request<{ updated: number }>("/api/devices/bulk", { method: "PATCH", body: JSON.stringify(data) }),
  bulkDelete: (deviceIds: string[]) =>
    request<{ deleted: number }>("/api/devices/bulk", { method: "DELETE", body: JSON.stringify({ deviceIds }) }),
  getNotes: (id: string) => request<DeviceNote[]>(`/api/devices/${id}/notes`),
  addNote: (id: string, content: string) =>
    request<DeviceNote>(`/api/devices/${id}/notes`, { method: "POST", body: JSON.stringify({ content }) }),
  deleteNote: (id: string, noteId: string) =>
    request(`/api/devices/${id}/notes/${noteId}`, { method: "DELETE" }),
  getCommands: (id: string) => request<DeviceCommand[]>(`/api/devices/${id}/commands`),
  issueCommand: (id: string, commandType: string, parameters?: string) =>
    request<{ id: string }>(`/api/devices/${id}/commands`, { method: "POST", body: JSON.stringify({ commandType, parameters }) }),
  setLicenseExpiry: (id: string, expiresAt: string | null) =>
    request(`/api/devices/${id}/license/expiry`, { method: "PATCH", body: JSON.stringify({ expiresAt }) }),
  getAlertSettings: () => request<{ diskAlertThresholdPercent: number }>("/api/settings/alerts"),
  saveAlertSettings: (diskAlertThresholdPercent: number) =>
    request<{ message: string }>("/api/settings/alerts", { method: "PUT", body: JSON.stringify({ diskAlertThresholdPercent }) }),
};

// Dashboard
export const dashboard = {
  get: () => request<DashboardData>("/api/dashboard"),
};

// Software inventory
export const software = {
  getInventory: (params?: Record<string, string>) => {
    const qs = params ? "?" + new URLSearchParams(params).toString() : "";
    return request<SoftwareInventoryItem[]>(`/api/software${qs}`);
  },
  getSummary: (name?: string) => {
    const qs = name ? `?name=${encodeURIComponent(name)}` : "";
    return request<SoftwareSummaryItem[]>(`/api/software/summary${qs}`);
  },
  getBlacklist: () => request<BlacklistEntry[]>("/api/software/blacklist"),
  addBlacklist: (data: { namePattern: string; publisher?: string; reason?: string }) =>
    request<{ id: string }>("/api/software/blacklist", { method: "POST", body: JSON.stringify(data) }),
  deleteBlacklist: (id: string) =>
    request(`/api/software/blacklist/${id}`, { method: "DELETE" }),
  getAlerts: (acknowledged?: boolean) => {
    const qs = acknowledged !== undefined ? `?acknowledged=${acknowledged}` : "";
    return request<SoftwareAlertItem[]>(`/api/software/alerts${qs}`);
  },
  acknowledgeAlert: (id: string) =>
    request(`/api/software/alerts/${id}/acknowledge`, { method: "POST" }),
  acknowledgeAll: () =>
    request<{ acknowledged: number }>("/api/software/alerts/acknowledge-all", { method: "POST" }),
};

// Audit
export const audit = {
  list: (params?: { page?: number; pageSize?: number; username?: string; action?: string; entityType?: string }) => {
    const qs = params ? "?" + new URLSearchParams(
      Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined).map(([k, v]) => [k, String(v)]))
    ).toString() : "";
    return request<AuditLogPage>(`/api/audit${qs}`);
  },
};

// Agent versions
export const agentVersions = {
  list: () => request<AgentVersion[]>("/api/agent-versions"),
  create: (data: { version: string; downloadUrl?: string; changelog?: string; isLatest: boolean }) =>
    request<{ id: string }>("/api/agent-versions", { method: "POST", body: JSON.stringify(data) }),
  setLatest: (id: string) =>
    request(`/api/agent-versions/${id}/set-latest`, { method: "PATCH" }),
  delete: (id: string) =>
    request(`/api/agent-versions/${id}`, { method: "DELETE" }),
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
  rustDeskId: string;
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
  rustDeskId?: string;
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
  expiresAt: string | null;
}

export interface PendingDevice {
  id: string;
  hostname: string;
  windowsVersion: string;
  cpuModel: string;
  ramTotalGB: number;
  requestedAt: string;
  status: string;
  invitedByUsername: string | null;
}

export interface InstallToken {
  id: string;
  token: string;
  createdByUsername: string;
  createdAt: string;
  expiresAt: string;
  used: boolean;
  usedAt: string | null;
  expired: boolean;
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

export interface RustDeskSettings {
  relayHost: string;
  publicKey: string;
  autoInstall: boolean;
  downloadUrl: string;
}

export interface EmailSettings {
  host: string;
  port: number;
  username: string;
  hasPassword: boolean;
  from: string;
  to: string;
  useSsl: boolean;
  isConfigured: boolean;
}

export interface EmailSettingsInput {
  host: string;
  port: number;
  username: string;
  password: string;
  from: string;
  to: string;
  useSsl: boolean;
}

export interface Group {
  id: string;
  name: string;
  description: string;
  color: string | null;
  createdAt: string;
  deviceCount: number;
}

export interface DeviceNote {
  id: string;
  content: string;
  authorUsername: string;
  createdAt: string;
}

export interface DeviceCommand {
  id: string;
  commandType: string;
  status: string;
  parameters: string | null;
  issuedByUsername: string;
  createdAt: string;
  executedAt: string | null;
  result: string | null;
}

export interface BlacklistEntry {
  id: string;
  namePattern: string;
  publisher: string | null;
  reason: string | null;
  addedByUsername: string;
  addedAt: string;
}

export interface SoftwareAlertItem {
  id: string;
  softwareName: string;
  softwareVersion: string;
  detectedAt: string;
  acknowledgedAt: string | null;
  acknowledgedByUsername: string | null;
  device: { id: string; hostname: string };
  customer: { id: string; name: string } | null;
  rule: { id: string; namePattern: string; reason: string | null };
}

export interface SoftwareInventoryItem {
  id: string;
  name: string;
  version: string;
  publisher: string;
  installDate: string;
  device: { id: string; hostname: string };
  customer: { id: string; name: string } | null;
}

export interface SoftwareSummaryItem {
  name: string;
  publisher: string;
  deviceCount: number;
  versions: string[];
}

export interface AuditLogEntry {
  id: string;
  username: string;
  action: string;
  entityType: string;
  entityId: string | null;
  details: string | null;
  ipAddress: string | null;
  timestamp: string;
}

export interface AuditLogPage {
  total: number;
  page: number;
  pageSize: number;
  items: AuditLogEntry[];
}

export interface AgentVersion {
  id: string;
  version: string;
  downloadUrl: string | null;
  changelog: string | null;
  isLatest: boolean;
  releasedAt: string;
}

export const installTokens = {
  list: () => request<InstallToken[]>("/api/install-tokens"),
  create: (expiryHours: number) =>
    request<InstallToken>("/api/install-tokens", {
      method: "POST",
      body: JSON.stringify({ expiryHours }),
    }),
  delete: (id: string) => request(`/api/install-tokens/${id}`, { method: "DELETE" }),
  sendEmail: (id: string, email: string) =>
    request<{ message: string }>(`/api/install-tokens/${id}/send-email`, {
      method: "POST",
      body: JSON.stringify({ email }),
    }),
};

export interface DashboardData {
  devices: { total: number; online: number; offline: number; pending: number };
  customers: number;
  groups: number;
  alerts: {
    softwareAlerts: number;
    expiringLicenses: number;
    expiredLicenses: number;
    pendingCommands: number;
  };
  recentAlerts: Array<{
    id: string;
    deviceHostname: string;
    deviceId: string;
    softwareName: string;
    softwareVersion: string;
    detectedAt: string;
    rule: string;
  }>;
  recentAuditLogs: Array<{
    id: string;
    username: string;
    action: string;
    entityType: string;
    entityId: string | null;
    timestamp: string;
  }>;
  devicesByGroup: Array<{ id: string; name: string; color: string | null; deviceCount: number }>;
  devicesByCustomer: Array<{ id: string; name: string; deviceCount: number }>;
}
