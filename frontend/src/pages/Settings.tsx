import { useEffect, useState } from "react";
import { toast } from "@/lib/useToast";
import {
  KeyRound, UserPlus, Trash2, RefreshCw, Mail, Send, CheckCircle2, XCircle,
  ShieldAlert, Plus, Clock, Download, ChevronLeft, ChevronRight, AlertTriangle,
  Tag, Monitor, Settings2, Users, FileText, Cpu, Bell, Pencil, Link,
  Building2, Server, Laptop, Copy, Terminal, Package,
} from "lucide-react";
import {
  auth, users, settings, software, audit, agentVersions, devices as devicesApi,
  notifications, customFields, deployKeys as deployKeysApi, scriptTemplates,
  softwarePackages, tenantInfo as tenantInfoApi,
  type AppUser, type EmailSettingsInput, type BlacklistEntry,
  type AuditLogEntry, type AgentVersion, type RustDeskSettings,
  type NotificationDefaults, type DeviceNotificationOverride,
  type CustomFieldDefinition, type DeployKey, type ScriptTemplate,
  type SoftwarePackage as SoftwarePackageType, type LdapSettings, type TenantInfo,
} from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { RustDeskOptionsDialog } from "@/components/RustDeskOptionsDialog";

type Section =
  | "allgemein" | "agent" | "deploykeys"
  | "email" | "benachrichtigungen"
  | "felder" | "software"
  | "fernzugriff" | "scripts" | "packages"
  | "ldap"
  | "benutzer" | "protokoll"
  | "konto" | "abonnement";

const adminNavGroups: { label: string | null; items: { id: Section; label: string; icon: React.ElementType }[] }[] = [
  {
    label: "System",
    items: [
      { id: "allgemein", label: "Allgemein", icon: Settings2 },
      { id: "email", label: "E-Mail & SMTP", icon: Mail },
      { id: "benachrichtigungen", label: "Benachrichtigungen", icon: Bell },
    ],
  },
  {
    label: "Agent",
    items: [
      { id: "agent", label: "Versionen", icon: Cpu },
      { id: "deploykeys", label: "Deploy-Keys", icon: KeyRound },
      { id: "scripts", label: "Script-Bibliothek", icon: Terminal },
      { id: "packages", label: "Software-Pakete", icon: Package },
    ],
  },
  {
    label: "Geräte",
    items: [
      { id: "felder", label: "Felder", icon: Tag },
      { id: "software", label: "Blacklist", icon: ShieldAlert },
      { id: "fernzugriff", label: "Fernzugriff", icon: Monitor },
    ],
  },
  {
    label: "Verwaltung",
    items: [
      { id: "benutzer", label: "Benutzer", icon: Users },
      { id: "ldap", label: "LDAP / AD", icon: Server },
      { id: "protokoll", label: "Protokoll", icon: FileText },
    ],
  },
];

export function Settings() {
  const currentUsername = localStorage.getItem("username") ?? "admin";
  const isAdmin = localStorage.getItem("role") === "Admin";

  const [activeSection, setActiveSection] = useState<Section>(isAdmin ? "allgemein" : "konto");

  // --- Change password ---
  const [pwCurrent, setPwCurrent] = useState("");
  const [pwNext, setPwNext] = useState("");
  const [pwConfirm, setPwConfirm] = useState("");
  const [pwLoading, setPwLoading] = useState(false);
  const [pwError, setPwError] = useState("");
  const [pwSuccess, setPwSuccess] = useState(false);

  const handleChangePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    setPwError("");
    setPwSuccess(false);
    if (pwNext !== pwConfirm) { setPwError("Passwörter stimmen nicht überein."); return; }
    if (pwNext.length < 6) { setPwError("Mindestens 6 Zeichen erforderlich."); return; }
    setPwLoading(true);
    try {
      await auth.changePassword(pwCurrent, pwNext);
      setPwSuccess(true);
      setPwCurrent(""); setPwNext(""); setPwConfirm("");
    } catch (err: any) {
      setPwError(err.message || "Fehler");
    } finally {
      setPwLoading(false);
    }
  };

  // --- User management ---
  const [userList, setUserList] = useState<AppUser[]>([]);
  const [createDialog, setCreateDialog] = useState(false);
  const [newUsername, setNewUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newRole, setNewRole] = useState("User");
  const [createError, setCreateError] = useState("");
  const [createLoading, setCreateLoading] = useState(false);
  const [resetDialog, setResetDialog] = useState<AppUser | null>(null);
  const [resetPw, setResetPw] = useState("");
  const [resetLoading, setResetLoading] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<AppUser | null>(null);

  // --- Email settings ---
  const [emailForm, setEmailForm] = useState<EmailSettingsInput>({
    host: "", port: 587, username: "", password: "", from: "hitsight@localhost", to: "", useSsl: false,
  });
  const [emailHasPassword, setEmailHasPassword] = useState(false);
  const [emailLoading, setEmailLoading] = useState(false);
  const [emailSaveMsg, setEmailSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [testLoading, setTestLoading] = useState(false);
  const [testMsg, setTestMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Checkin interval ---
  const [checkinInterval, setCheckinInterval] = useState(30);
  const [checkinSaveMsg, setCheckinSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Agent server URL ---
  const [agentServerUrl, setAgentServerUrl] = useState("");
  const [serverUrlSaveMsg, setServerUrlSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Alert settings ---
  const [diskThreshold, setDiskThreshold] = useState(10);
  const [alertSaveMsg, setAlertSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Notification settings ---
  const [notifyDefaults, setNotifyDefaults] = useState<NotificationDefaults>({
    deviceOffline: true, deviceOnline: true, newPending: true, softwareAlert: true, diskFull: true, offlineAlertDelayMinutes: 0, avSignatureAgeAlertDays: 7,
  });
  const [notifyOverrides, setNotifyOverrides] = useState<DeviceNotificationOverride[]>([]);
  const [notifySaveMsg, setNotifySaveMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [notifyOverrideDialog, setNotifyOverrideDialog] = useState<DeviceNotificationOverride | null | "new">(null);
  const [overrideDeviceSearch, setOverrideDeviceSearch] = useState("");
  const [overrideDeviceId, setOverrideDeviceId] = useState("");
  const [overrideValues, setOverrideValues] = useState({ alertOnOffline: null as boolean | null, alertOnOnline: null as boolean | null, alertOnSoftwareAlert: null as boolean | null, alertOnDiskFull: null as boolean | null, offlineAlertDelayMinutes: null as number | null });
  const [allDevices, setAllDevices] = useState<{ id: string; hostname: string; description: string }[]>([]);

  // --- RustDesk settings ---
  const [rustDesk, setRustDesk] = useState<RustDeskSettings>({
    relayHost: "", publicKey: "", autoInstall: false, downloadUrl: "", globalOptions: {},
  });
  const [rustDeskSaveMsg, setRustDeskSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [rustDeskLoading, setRustDeskLoading] = useState(false);
  const [rdOptionsOpen, setRdOptionsOpen] = useState(false);
  const [rdForceApplyLoading, setRdForceApplyLoading] = useState(false);
  const [rdForceApplyMsg, setRdForceApplyMsg] = useState<string | null>(null);
  const [rdClearLoading, setRdClearLoading] = useState(false);
  const [rdClearMsg, setRdClearMsg] = useState<string | null>(null);

  // --- Software Blacklist ---
  const [blacklist, setBlacklist] = useState<BlacklistEntry[]>([]);
  const [blacklistDialog, setBlacklistDialog] = useState(false);
  const [blPattern, setBlPattern] = useState("");
  const [blPublisher, setBlPublisher] = useState("");
  const [blReason, setBlReason] = useState("");
  const [blLoading, setBlLoading] = useState(false);

  // --- Audit Log ---
  const [auditLogs, setAuditLogs] = useState<AuditLogEntry[]>([]);
  const [auditTotal, setAuditTotal] = useState(0);
  const [auditPage, setAuditPage] = useState(1);
  const [auditSearch, setAuditSearch] = useState("");
  const AUDIT_PAGE_SIZE = 20;

  // --- Agent auto-update ---
  const [autoUpdate, setAutoUpdate] = useState(false);
  const [autoUpdateSaveMsg, setAutoUpdateSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Custom Fields ---
  const [fieldDefs, setFieldDefs] = useState<CustomFieldDefinition[]>([]);
  const [newFieldName, setNewFieldName] = useState("");
  const [fieldLoading, setFieldLoading] = useState(false);

  // --- Deploy Keys ---
  const [dkList, setDkList] = useState<DeployKey[]>([]);
  const [dkName, setDkName] = useState("");
  const [dkLoading, setDkLoading] = useState(false);
  const [dkCopied, setDkCopied] = useState<string | null>(null);
  const [dkNewKey, setDkNewKey] = useState<DeployKey | null>(null);

  // --- Script Library ---
  const [scriptList, setScriptList] = useState<ScriptTemplate[]>([]);
  const [scriptDialog, setScriptDialog] = useState<"new" | ScriptTemplate | null>(null);
  const [packageList, setPackageList] = useState<SoftwarePackageType[]>([]);
  const [packageDialog, setPackageDialog] = useState<"new" | SoftwarePackageType | null>(null);
  const [pkgName, setPkgName] = useState("");
  const [pkgVersion, setPkgVersion] = useState("");
  const [pkgType, setPkgType] = useState("winget");
  const [pkgInstallCmd, setPkgInstallCmd] = useState("");
  const [pkgUninstallCmd, setPkgUninstallCmd] = useState("");
  const [pkgDesc, setPkgDesc] = useState("");
  const [scriptName, setScriptName] = useState("");
  const [scriptDesc, setScriptDesc] = useState("");
  const [scriptBody, setScriptBody] = useState("");
  const [scriptLoading, setScriptLoading] = useState(false);

  // --- Tenant subscription info ---
  const [tInfo, setTInfo] = useState<TenantInfo | null>(null);
  const isPlatform = !!(import.meta.env.VITE_PLATFORM_DOMAIN);

  // --- LDAP ---
  const [ldap, setLdap] = useState<LdapSettings>({
    enabled: false, host: "", port: 389, transport: "TCP" as const, ignoreCertificateErrors: false,
    baseDn: "", bindDn: "", hasBindPassword: false, hasCaCertificate: false,
    userSearchBase: "", userFilter: "(&(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={0})))",
    adminGroup: "", viewerGroup: "", requireGroup: false, useNestedGroups: false,
  });
  const [ldapBindPassword, setLdapBindPassword] = useState("");
  const [ldapSaveMsg, setLdapSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [ldapTestMsg, setLdapTestMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [ldapSaving, setLdapSaving] = useState(false);
  const [ldapTesting, setLdapTesting] = useState(false);
  const [ldapCaPem, setLdapCaPem] = useState("");
  const [ldapCaMsg, setLdapCaMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [ldapCaUploading, setLdapCaUploading] = useState(false);
  const [ldapCaDeleting, setLdapCaDeleting] = useState(false);

  // --- Agent Versions ---
  const [agentVers, setAgentVers] = useState<AgentVersion[]>([]);
  const [versionDialog, setVersionDialog] = useState(false);
  const [publishLoading, setPublishLoading] = useState(false);
  const [verVersion, setVerVersion] = useState("");
  const [verUrl, setVerUrl] = useState("");
  const [verChangelog, setVerChangelog] = useState("");
  const [verIsLatest, setVerIsLatest] = useState(true);
  const [verLoading, setVerLoading] = useState(false);
  // changelog edit dialog
  const [changelogEditDialog, setChangelogEditDialog] = useState<{ id: string; text: string } | null>(null);
  const [changelogEditLoading, setChangelogEditLoading] = useState(false);
  // expanded changelog rows
  const [expandedChangelogs, setExpandedChangelogs] = useState<Set<string>>(new Set());

  useEffect(() => {
    settings.getEmail().then(data => {
      setEmailForm(f => ({
        ...f,
        host: data.host, port: data.port, username: data.username,
        from: data.from, to: data.to, useSsl: data.useSsl, password: "",
      }));
      setEmailHasPassword(data.hasPassword);
    }).catch(() => {});

    users.list().then(setUserList).catch(() => {});
    software.getBlacklist().then(setBlacklist).catch(() => {});
    agentVersions.list().then(setAgentVers).catch(() => {});
    devicesApi.getAlertSettings().then(s => setDiskThreshold(s.diskAlertThresholdPercent)).catch(() => {});
    settings.get().then(s => {
      setCheckinInterval(s.checkinIntervalMinutes);
      setAgentServerUrl(s.agentServerUrl || "");
    }).catch(() => {});
    settings.getRustDesk().then(setRustDesk).catch(() => {});
    settings.getAgentSettings().then(s => setAutoUpdate(s.autoUpdate)).catch(() => {});
    customFields.getDefinitions().then(setFieldDefs).catch(() => {});
    deployKeysApi.list().then(setDkList).catch(() => {});
    scriptTemplates.list().then(setScriptList).catch(() => {});
    softwarePackages.list().then(setPackageList).catch(() => {});
    notifications.getDefaults().then(setNotifyDefaults).catch(() => {});
    notifications.getDeviceOverrides().then(setNotifyOverrides).catch(() => {});
    settings.getLdap().then(setLdap).catch(() => {});
    devicesApi.list().then(list => setAllDevices(list.items.map(d => ({ id: d.id, hostname: d.hostname, description: d.description })))).catch(() => {});
    tenantInfoApi.get().then(setTInfo).catch(() => {});
  }, []);

  useEffect(() => {
    audit.list({ page: auditPage, pageSize: AUDIT_PAGE_SIZE, username: auditSearch || undefined })
      .then(data => { setAuditLogs(data.items); setAuditTotal(data.total); })
      .catch(() => {});
  }, [auditPage, auditSearch]);

  const handleSaveEmail = async (e: React.FormEvent) => {
    e.preventDefault();
    setEmailSaveMsg(null);
    setEmailLoading(true);
    try {
      const res = await settings.saveEmail(emailForm);
      setEmailSaveMsg({ ok: true, text: res.message });
      if (emailForm.password) setEmailHasPassword(true);
      setEmailForm(f => ({ ...f, password: "" }));
    } catch (err: any) {
      setEmailSaveMsg({ ok: false, text: err.message || "Fehler beim Speichern." });
    } finally {
      setEmailLoading(false);
    }
  };

  const handleTestEmail = async () => {
    setTestMsg(null);
    setTestLoading(true);
    try {
      const res = await settings.testEmail();
      setTestMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setTestMsg({ ok: false, text: err.message || "Test fehlgeschlagen." });
    } finally {
      setTestLoading(false);
    }
  };

  const handleSaveCheckin = async () => {
    setCheckinSaveMsg(null);
    try {
      const res = await settings.saveCheckin(checkinInterval);
      setCheckinSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setCheckinSaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const handleSaveServerUrl = async () => {
    setServerUrlSaveMsg(null);
    try {
      const res = await settings.saveServerUrl(agentServerUrl);
      setServerUrlSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setServerUrlSaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const handleSaveAlerts = async () => {
    setAlertSaveMsg(null);
    try {
      const res = await devicesApi.saveAlertSettings(diskThreshold);
      setAlertSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setAlertSaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const handleSaveRustDesk = async () => {
    setRustDeskSaveMsg(null);
    setRustDeskLoading(true);
    try {
      const res = await settings.saveRustDesk(rustDesk);
      setRustDeskSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setRustDeskSaveMsg({ ok: false, text: err.message || "Fehler" });
    } finally {
      setRustDeskLoading(false);
    }
  };

  const handleForceApplyRustDesk = async () => {
    setRdForceApplyLoading(true);
    setRdForceApplyMsg(null);
    try {
      await settings.forceApplyRustDesk();
      setRdForceApplyMsg("Alle Agents konfigurieren beim nächsten Check-in neu.");
      setTimeout(() => setRdForceApplyMsg(null), 4000);
    } catch { setRdForceApplyMsg("Fehler"); } finally { setRdForceApplyLoading(false); }
  };

  const handleClearDeviceOverrides = async () => {
    if (!confirm("Alle gerätespezifischen RustDesk-Overrides löschen? Geräte verwenden dann nur noch die globalen Einstellungen.")) return;
    setRdClearLoading(true);
    setRdClearMsg(null);
    try {
      await settings.clearDeviceRustDeskOverrides();
      setRdClearMsg("Alle Overrides gelöscht.");
      setTimeout(() => setRdClearMsg(null), 4000);
    } catch { setRdClearMsg("Fehler"); } finally { setRdClearLoading(false); }
  };

  const fetchUsers = async () => {
    const data = await users.list();
    setUserList(data);
  };

  const handleCreate = async () => {
    setCreateError("");
    setCreateLoading(true);
    try {
      await users.create({ username: newUsername, password: newPassword, role: newRole });
      setCreateDialog(false);
      setNewUsername(""); setNewPassword(""); setNewRole("User");
      await fetchUsers();
    } catch (err: any) {
      setCreateError(err.message || "Fehler");
    } finally {
      setCreateLoading(false);
    }
  };

  const handleResetPassword = async () => {
    if (!resetDialog) return;
    setResetLoading(true);
    await users.resetPassword(resetDialog.id, resetPw).catch(() => {});
    setResetDialog(null);
    setResetPw("");
    setResetLoading(false);
  };

  const handleDeleteUser = async (user: AppUser) => {
    await users.delete(user.id).catch(() => {});
    setDeleteConfirm(null);
    await fetchUsers();
  };

  const handleAddBlacklist = async () => {
    if (!blPattern.trim()) return;
    setBlLoading(true);
    await software.addBlacklist({
      namePattern: blPattern.trim(),
      publisher: blPublisher.trim() || undefined,
      reason: blReason.trim() || undefined,
    }).catch(() => {});
    const updated = await software.getBlacklist().catch(() => blacklist);
    setBlacklist(updated);
    setBlPattern(""); setBlPublisher(""); setBlReason("");
    setBlacklistDialog(false);
    setBlLoading(false);
  };

  const handleDeleteBlacklist = async (id: string) => {
    await software.deleteBlacklist(id).catch(() => {});
    setBlacklist(prev => prev.filter(e => e.id !== id));
  };

  const handleAddVersion = async () => {
    if (!verVersion.trim()) return;
    setVerLoading(true);
    await agentVersions.create({
      version: verVersion.trim(),
      downloadUrl: verUrl.trim() || undefined,
      changelog: verChangelog.trim() || undefined,
      isLatest: verIsLatest,
    }).catch(() => {});
    const updated = await agentVersions.list().catch(() => agentVers);
    setAgentVers(updated);
    setVerVersion(""); setVerUrl(""); setVerChangelog(""); setVerIsLatest(true);
    setVersionDialog(false);
    setVerLoading(false);
  };

  const handleSetLatest = async (id: string) => {
    await agentVersions.setLatest(id).catch(() => {});
    const updated = await agentVersions.list().catch(() => agentVers);
    setAgentVers(updated);
  };

  const handleDeleteVersion = async (id: string) => {
    await agentVersions.delete(id).catch(() => {});
    setAgentVers(prev => prev.filter(v => v.id !== id));
  };

  const handleSaveNotifyDefaults = async () => {
    setNotifySaveMsg(null);
    try {
      const res = await notifications.saveDefaults(notifyDefaults);
      setNotifySaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setNotifySaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const openNewOverride = () => {
    setOverrideDeviceId("");
    setOverrideDeviceSearch("");
    setOverrideValues({ alertOnOffline: null, alertOnOnline: null, alertOnSoftwareAlert: null, alertOnDiskFull: null, offlineAlertDelayMinutes: null });
    setNotifyOverrideDialog("new");
  };

  const openEditOverride = (o: DeviceNotificationOverride) => {
    setOverrideDeviceId(o.device.id);
    setOverrideDeviceSearch(o.device.hostname);
    setOverrideValues({ alertOnOffline: o.alertOnOffline, alertOnOnline: o.alertOnOnline, alertOnSoftwareAlert: o.alertOnSoftwareAlert, alertOnDiskFull: o.alertOnDiskFull, offlineAlertDelayMinutes: null });
    setNotifyOverrideDialog(o);
  };

  const handleSaveOverride = async () => {
    if (!overrideDeviceId) return;
    await notifications.upsertDeviceOverride({ deviceId: overrideDeviceId, ...overrideValues }).catch(() => {});
    const updated = await notifications.getDeviceOverrides().catch(() => notifyOverrides);
    setNotifyOverrides(updated);
    setNotifyOverrideDialog(null);
  };

  const handleDeleteOverride = async (deviceId: string) => {
    await notifications.deleteDeviceOverride(deviceId).catch(() => {});
    setNotifyOverrides(prev => prev.filter(o => o.device.id !== deviceId));
  };

  const handleSaveAutoUpdate = async () => {
    setAutoUpdateSaveMsg(null);
    try {
      const res = await settings.saveAgentSettings(autoUpdate);
      setAutoUpdateSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setAutoUpdateSaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const openNewScript = () => { setScriptName(""); setScriptDesc(""); setScriptBody(""); setScriptDialog("new"); };
  const openEditScript = (t: ScriptTemplate) => { setScriptName(t.name); setScriptDesc(t.description); setScriptBody(t.script); setScriptDialog(t); };
  const handleSaveScript = async () => {
    if (!scriptName.trim() || !scriptBody.trim()) return;
    setScriptLoading(true);
    if (scriptDialog === "new") {
      await scriptTemplates.create({ name: scriptName, description: scriptDesc, script: scriptBody }).catch(() => {});
    } else if (scriptDialog && typeof scriptDialog === "object") {
      await scriptTemplates.update(scriptDialog.id, { name: scriptName, description: scriptDesc, script: scriptBody }).catch(() => {});
    }
    setScriptList(await scriptTemplates.list().catch(() => []));
    setScriptDialog(null);
    setScriptLoading(false);
  };
  const handleDeleteScript = async (id: string) => {
    await scriptTemplates.delete(id).catch(() => {});
    setScriptList(await scriptTemplates.list().catch(() => []));
  };

  const openNewPackage = () => { setPkgName(""); setPkgVersion(""); setPkgType("winget"); setPkgInstallCmd(""); setPkgUninstallCmd(""); setPkgDesc(""); setPackageDialog("new"); };
  const openEditPackage = (p: SoftwarePackageType) => {
    setPkgName(p.name); setPkgVersion(p.version); setPkgType(p.type);
    setPkgInstallCmd(p.installCmd); setPkgUninstallCmd(p.uninstallCmd ?? ""); setPkgDesc(p.description);
    setPackageDialog(p);
  };
  const handleSavePackage = async () => {
    const data = { name: pkgName, version: pkgVersion, type: pkgType, installCmd: pkgInstallCmd, uninstallCmd: pkgUninstallCmd || undefined, description: pkgDesc };
    if (packageDialog === "new") {
      await softwarePackages.create(data).catch(() => {});
    } else if (packageDialog && typeof packageDialog === "object") {
      await softwarePackages.update(packageDialog.id, data).catch(() => {});
    }
    setPackageList(await softwarePackages.list().catch(() => []));
    setPackageDialog(null);
  };
  const handleDeletePackage = async (id: string) => {
    await softwarePackages.delete(id).catch(() => {});
    setPackageList(await softwarePackages.list().catch(() => []));
  };

  const handleCreateDeployKey = async () => {
    if (!dkName.trim()) return;
    setDkLoading(true);
    try {
      const dk = await deployKeysApi.create(dkName.trim());
      setDkList(prev => [dk, ...prev]);
      setDkNewKey(dk);
      setDkName("");
    } catch (err: any) {
      toast({ title: "Fehler", description: err.message || "Deploy-Key konnte nicht erstellt werden.", variant: "warning" });
    }
    setDkLoading(false);
  };

  const handleDeleteDeployKey = async (id: string) => {
    await deployKeysApi.delete(id).catch(() => {});
    setDkList(prev => prev.filter(k => k.id !== id));
    if (dkNewKey?.id === id) setDkNewKey(null);
  };

  const handleCopyDk = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setDkCopied(id);
    setTimeout(() => setDkCopied(null), 2000);
  };

  const handleDownloadPs1 = (content: string, filename: string) => {
    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleAddField = async () => {
    if (!newFieldName.trim()) return;
    setFieldLoading(true);
    try {
      const def = await customFields.createDefinition(newFieldName.trim());
      setFieldDefs(prev => [...prev, def]);
      setNewFieldName("");
    } catch {}
    setFieldLoading(false);
  };

  const handleDeleteField = async (id: string) => {
    await customFields.deleteDefinition(id).catch(() => {});
    setFieldDefs(prev => prev.filter(d => d.id !== id));
  };

  const handleSaveLdap = async () => {
    setLdapSaveMsg(null);
    setLdapSaving(true);
    try {
      const res = await settings.saveLdap({ ...ldap, bindPassword: ldapBindPassword || undefined });
      setLdapBindPassword("");
      setLdapSaveMsg({ ok: true, text: res.message });
      setLdap(prev => ({ ...prev, hasBindPassword: prev.hasBindPassword || !!ldapBindPassword }));
    } catch (err: any) {
      setLdapSaveMsg({ ok: false, text: err.message || "Fehler beim Speichern." });
    } finally {
      setLdapSaving(false);
    }
  };

  const handleTestLdap = async () => {
    setLdapTestMsg(null);
    setLdapTesting(true);
    try {
      const res = await settings.testLdap();
      setLdapTestMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setLdapTestMsg({ ok: false, text: err.message || "Verbindungstest fehlgeschlagen." });
    } finally {
      setLdapTesting(false);
    }
  };

  const handlePublishAgent = async () => {
    setPublishLoading(true);
    try {
      const res = await agentVersions.publish();
      const updated = await agentVersions.list().catch(() => agentVers);
      setAgentVers(updated);
      toast({ title: "Agent veröffentlicht", description: `Version ${res.version} ist jetzt verfügbar.` });
    } catch (err: any) {
      toast({ title: "Publish fehlgeschlagen", description: err.message || "Fehler", variant: "warning" });
    } finally {
      setPublishLoading(false);
    }
  };

  const handleSaveChangelog = async () => {
    if (!changelogEditDialog) return;
    setChangelogEditLoading(true);
    try {
      await agentVersions.updateChangelog(changelogEditDialog.id, changelogEditDialog.text);
      setAgentVers(prev => prev.map(v => v.id === changelogEditDialog.id ? { ...v, changelog: changelogEditDialog.text } : v));
      setChangelogEditDialog(null);
      toast({ title: "Changelog gespeichert" });
    } catch (err: any) {
      toast({ title: "Fehler", description: err.message || "Changelog konnte nicht gespeichert werden.", variant: "warning" });
    } finally {
      setChangelogEditLoading(false);
    }
  };

  const auditTotalPages = Math.ceil(auditTotal / AUDIT_PAGE_SIZE);

  // ── Sidebar nav item ────────────────────────────────────────────────────────
  function NavItem({ id, label, icon: Icon }: { id: Section; label: string; icon: React.ElementType }) {
    return (
      <button
        onClick={() => setActiveSection(id)}
        className={cn(
          "flex items-center gap-2.5 w-full rounded-md px-3 py-2 text-sm font-medium transition-colors text-left",
          activeSection === id
            ? "bg-primary/15 text-primary"
            : "text-muted-foreground hover:bg-accent hover:text-foreground"
        )}
      >
        <Icon className="h-4 w-4 shrink-0" />
        {label}
      </button>
    );
  }

  // ── Feedback row helper ─────────────────────────────────────────────────────
  function SaveFeedback({ msg }: { msg: { ok: boolean; text: string } | null }) {
    if (!msg) return null;
    return (
      <div className={cn("flex items-center gap-2 text-sm mt-3", msg.ok ? "text-emerald-500" : "text-destructive")}>
        {msg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
        {msg.text}
      </div>
    );
  }

  return (
    <div className="flex h-full">

      {/* ── Settings sidebar ──────────────────────────────────────────────── */}
      <aside className="w-52 shrink-0 border-r bg-card flex flex-col overflow-y-auto">
        <div className="px-5 py-4 border-b">
          <h2 className="text-sm font-semibold">Einstellungen</h2>
          <p className="text-xs text-muted-foreground mt-0.5">{currentUsername}</p>
        </div>

        <nav className="flex-1 px-2 py-3 space-y-4">
          {isAdmin && adminNavGroups.map((group, gi) => (
            <div key={gi}>
              {group.label && (
                <div className="px-3 mb-1 text-[10px] font-semibold text-muted-foreground uppercase tracking-widest">
                  {group.label}
                </div>
              )}
              <div className="space-y-0.5">
                {group.items.map(item => (
                  <NavItem key={item.id} {...item} />
                ))}
              </div>
            </div>
          ))}
        </nav>

        <div className="px-2 py-3 border-t space-y-0.5">
          {isPlatform && <NavItem id="abonnement" label="Abonnement" icon={Building2} />}
          <NavItem id="konto" label="Konto" icon={KeyRound} />
        </div>
      </aside>

      {/* ── Section content ───────────────────────────────────────────────── */}
      <div className="flex-1 overflow-y-auto">
        <div className="p-4 sm:p-6 max-w-3xl space-y-4 sm:space-y-5">

          {/* ── Allgemein ─────────────────────────────────────────────── */}
          {activeSection === "allgemein" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Allgemein</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Clock className="h-4 w-4" />
                    Check-in-Intervall
                  </CardTitle>
                  <CardDescription>
                    Wie oft der Agent den Server kontaktiert. Ändert sich beim nächsten Check-in automatisch.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="flex items-end gap-3">
                    <div className="space-y-1.5">
                      <Label>Intervall</Label>
                      <div className="flex items-center gap-2">
                        <Input
                          type="number" min={1} max={1440}
                          value={checkinInterval}
                          onChange={e => setCheckinInterval(Number(e.target.value))}
                          className="w-24"
                        />
                        <span className="text-sm text-muted-foreground">Minuten</span>
                      </div>
                    </div>
                    <Button onClick={handleSaveCheckin}>Speichern</Button>
                  </div>
                  <SaveFeedback msg={checkinSaveMsg} />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Link className="h-4 w-4" />
                    Agent-Server-URL
                  </CardTitle>
                  <CardDescription>
                    Öffentliche URL des API-Servers (Outpost), über die Agents erreichbar sind. Wird für Installationslinks verwendet.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="flex items-end gap-3">
                    <div className="space-y-1.5 flex-1">
                      <Label>URL</Label>
                      <Input
                        placeholder="https://api.example.com"
                        value={agentServerUrl}
                        onChange={e => setAgentServerUrl(e.target.value)}
                      />
                    </div>
                    <Button onClick={handleSaveServerUrl}>Speichern</Button>
                  </div>
                  <SaveFeedback msg={serverUrlSaveMsg} />
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <AlertTriangle className="h-4 w-4" />
                    Alert-Schwellwerte
                  </CardTitle>
                  <CardDescription>Grenzwerte für automatische E-Mail-Benachrichtigungen.</CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="flex items-end gap-3">
                    <div className="space-y-1.5">
                      <Label>Festplatte: Alert wenn freier Speicher unter</Label>
                      <div className="flex items-center gap-2">
                        <Input
                          type="number" min={1} max={99}
                          value={diskThreshold}
                          onChange={e => setDiskThreshold(Number(e.target.value))}
                          className="w-24"
                        />
                        <span className="text-sm text-muted-foreground">%</span>
                      </div>
                    </div>
                    <Button onClick={handleSaveAlerts}>Speichern</Button>
                  </div>
                  <SaveFeedback msg={alertSaveMsg} />
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Deploy-Keys ───────────────────────────────────────────── */}
          {activeSection === "deploykeys" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Deploy-Keys</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <KeyRound className="h-4 w-4" />
                    Permanente Installer-Links
                  </CardTitle>
                  <CardDescription>
                    Deploy-Keys ermöglichen einen dauerhaften Download-Endpunkt für Custom Images, MDM und SCCM-Deployments.
                    Der Key wird im HTTP-Header übergeben – die URL allein reicht nicht aus.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="flex gap-2">
                    <Input
                      placeholder="Name, z.B. SCCM-Production"
                      value={dkName}
                      onChange={e => setDkName(e.target.value)}
                      onKeyDown={e => e.key === "Enter" && handleCreateDeployKey()}
                      className="max-w-xs"
                    />
                    <Button onClick={handleCreateDeployKey} disabled={dkLoading || !dkName.trim()}>
                      <Plus className="h-3.5 w-3.5 mr-1.5" />
                      Key erstellen
                    </Button>
                  </div>

                  {dkNewKey && (
                    <div className="rounded-md border border-emerald-500/30 bg-emerald-500/5 p-4 space-y-3">
                      <p className="text-sm font-medium text-emerald-600 flex items-center gap-1.5">
                        <CheckCircle2 className="h-4 w-4" />
                        Deploy-Key erstellt — Key und Skripte nur jetzt sichtbar
                      </p>
                      <div className="flex items-center gap-2">
                        <code className="flex-1 text-xs font-mono break-all bg-muted rounded px-2 py-1.5">
                          {dkNewKey.key}
                        </code>
                        <Button size="sm" variant="outline" onClick={() => handleCopyDk(dkNewKey.key, dkNewKey.id)}>
                          {dkCopied === dkNewKey.id ? <CheckCircle2 className="h-3.5 w-3.5 text-green-600" /> : <Download className="h-3.5 w-3.5" />}
                        </Button>
                      </div>

                      <div className="space-y-1.5">
                        <p className="text-xs font-semibold text-emerald-700 dark:text-emerald-400 uppercase tracking-wide">GPO Startup Script (fertig ausgefüllt)</p>
                        <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2 text-foreground">{`[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wc = [System.Net.WebClient]::new()
$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')
$wc.DownloadFile('${agentServerUrl || "https://api.example.com"}/install/deploy/download', '\\\\dc\\SYSVOL\\domain\\scripts\\HackIT-Install.ps1')`}</pre>
                        <div className="flex gap-2">
                          <Button size="sm" variant="outline" onClick={() => handleCopyDk(
                            `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\n$wc = [System.Net.WebClient]::new()\n$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')\n$wc.DownloadFile('${agentServerUrl || "https://api.example.com"}/install/deploy/download', '\\\\dc\\SYSVOL\\domain\\scripts\\HackIT-Install.ps1')`,
                            dkNewKey.id + "-script"
                          )}>
                            {dkCopied === dkNewKey.id + "-script" ? <CheckCircle2 className="h-3.5 w-3.5 mr-1.5 text-green-600" /> : <Copy className="h-3.5 w-3.5 mr-1.5" />}
                            Kopieren
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => handleDownloadPs1(
                            `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\r\n$wc = [System.Net.WebClient]::new()\r\n$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')\r\n$wc.DownloadFile('${agentServerUrl || "https://api.example.com"}/install/deploy/download', '\\\\dc\\SYSVOL\\domain\\scripts\\HackIT-Install.ps1')`,
                            'HackIT-GPO-Install.ps1'
                          )}>
                            <Download className="h-3.5 w-3.5 mr-1.5" />
                            Herunterladen
                          </Button>
                        </div>
                      </div>

                      <div className="space-y-1.5">
                        <p className="text-xs font-semibold text-emerald-700 dark:text-emerald-400 uppercase tracking-wide">Direkt ausführen (kein AD)</p>
                        <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2 text-foreground">{`[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wc = [System.Net.WebClient]::new()
$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')
$wc.DownloadString('${agentServerUrl || "https://api.example.com"}/install/deploy/download') | Invoke-Expression`}</pre>
                        <div className="flex gap-2">
                          <Button size="sm" variant="outline" onClick={() => handleCopyDk(
                            `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\n$wc = [System.Net.WebClient]::new()\n$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')\n$wc.DownloadString('${agentServerUrl || "https://api.example.com"}/install/deploy/download') | Invoke-Expression`,
                            dkNewKey.id + "-invoke"
                          )}>
                            {dkCopied === dkNewKey.id + "-invoke" ? <CheckCircle2 className="h-3.5 w-3.5 mr-1.5 text-green-600" /> : <Copy className="h-3.5 w-3.5 mr-1.5" />}
                            Kopieren
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => handleDownloadPs1(
                            `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\r\n$wc = [System.Net.WebClient]::new()\r\n$wc.Headers.Add('X-Deploy-Key', '${dkNewKey.key}')\r\n$wc.DownloadString('${agentServerUrl || "https://api.example.com"}/install/deploy/download') | Invoke-Expression`,
                            'HackIT-DirectInstall.ps1'
                          )}>
                            <Download className="h-3.5 w-3.5 mr-1.5" />
                            Herunterladen
                          </Button>
                        </div>
                      </div>
                    </div>
                  )}

                  {dkList.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Noch keine Deploy-Keys vorhanden.</p>
                  ) : (
                    <div className="rounded-md border border-border overflow-hidden">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-border bg-muted/30">
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Name</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Erstellt von</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Zuletzt verwendet</th>
                            <th className="w-12"></th>
                          </tr>
                        </thead>
                        <tbody>
                          {dkList.map(k => (
                            <tr key={k.id} className="border-t border-border/50">
                              <td className="px-4 py-2.5 font-medium">{k.name}</td>
                              <td className="px-4 py-2.5 text-muted-foreground text-xs">{k.createdByUsername}</td>
                              <td className="px-4 py-2.5 text-muted-foreground text-xs">
                                {k.lastUsedAt
                                  ? new Date(k.lastUsedAt).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })
                                  : "—"}
                              </td>
                              <td className="px-4 py-2.5">
                                <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeleteDeployKey(k.id)}>
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {/* Deployment recommendation */}
                  <div className="rounded-md border border-border overflow-hidden">
                    <div className="bg-muted/30 px-4 py-2.5 border-b border-border">
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Welche Methode passt zum Kunden?</p>
                    </div>
                    <div className="divide-y divide-border/50">
                      <div className="flex items-start gap-3 px-4 py-3">
                        <Building2 className="h-4 w-4 text-blue-500 mt-0.5 shrink-0" />
                        <div className="flex-1 min-w-0">
                          <p className="text-sm font-medium">Active Directory (Domäne vorhanden)</p>
                          <p className="text-xs text-muted-foreground mt-0.5">GPO Startup Script → einmal auf SYSVOL ablegen, automatisch auf alle PCs</p>
                        </div>
                        <span className="text-xs font-medium text-blue-600 dark:text-blue-400 bg-blue-500/10 px-2 py-0.5 rounded-full shrink-0">Empfohlen</span>
                      </div>
                      <div className="flex items-start gap-3 px-4 py-3">
                        <Server className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" />
                        <div className="flex-1 min-w-0">
                          <p className="text-sm font-medium">SCCM, Intune oder MDT</p>
                          <p className="text-xs text-muted-foreground mt-0.5">MSI direkt — Properties über die Deployment-Infrastruktur übergeben. <span className="text-amber-600 dark:text-amber-400">GPO Software Installation funktioniert hier nicht</span> (keine Property-Übergabe möglich).</p>
                        </div>
                      </div>
                      <div className="flex items-start gap-3 px-4 py-3">
                        <Laptop className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" />
                        <div className="flex-1 min-w-0">
                          <p className="text-sm font-medium">Kein AD, kein MDM (Einzelplatz / Workgroup)</p>
                          <p className="text-xs text-muted-foreground mt-0.5">PS1 direkt als Administrator ausführen, oder per RMM-Tool verteilen (z.B. NinjaRMM, Datto, Atera)</p>
                        </div>
                      </div>
                    </div>
                  </div>

                  <div className="rounded-md border border-border bg-muted/20 p-4 space-y-4">

                    <div className="space-y-2">
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">GPO Startup Script — empfohlen für AD-Umgebungen</p>
                      <p className="text-xs text-muted-foreground">
                        Script herunterladen, auf SYSVOL ablegen, per GPO als Computer-Startup-Script einbinden.
                        Läuft bei jedem Boot — erkennt automatisch ob der Agent bereits installiert ist.
                        <span className="font-medium text-amber-600 dark:text-amber-400"> Neuen Key erstellen → fertiges Skript wird oben angezeigt.</span>
                      </p>
                      <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2">{`[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wc = [System.Net.WebClient]::new()
$wc.Headers.Add('X-Deploy-Key', 'DEPLOY_KEY')
$wc.DownloadFile('${agentServerUrl || "https://api.example.com"}/install/deploy/download', '\\\\dc\\SYSVOL\\domain\\scripts\\HackIT-Install.ps1')`}</pre>
                      <p className="text-xs text-muted-foreground">Kein AD? Script direkt als Administrator ausführen oder per RMM verteilen.</p>
                      <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2">{`[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wc = [System.Net.WebClient]::new()
$wc.Headers.Add('X-Deploy-Key', 'DEPLOY_KEY')
$wc.DownloadString('${agentServerUrl || "https://api.example.com"}/install/deploy/download') | Invoke-Expression`}</pre>
                    </div>

                    <div className="space-y-2 pt-1 border-t border-border/50">
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">SCCM / Intune / MDT (MSI direkt)</p>
                      <p className="text-xs text-muted-foreground">
                        MSI einmalig herunterladen und in der Deployment-Infrastruktur hinterlegen.
                        <strong className="text-foreground"> Wichtig:</strong> SERVERURL und DEPLOYKEY müssen als msiexec-Properties übergeben werden — GPO Software Installation reicht dafür nicht.
                      </p>
                      <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2">{`Invoke-WebRequest -Uri "${agentServerUrl || "https://api.example.com"}/install/deploy/msi" \`
  -Headers @{ "X-Deploy-Key" = "DEPLOY_KEY" } \`
  -OutFile "HITSight-Setup.msi"

msiexec /i "HITSight-Setup.msi" \`
  SERVERURL="${agentServerUrl || "https://api.example.com"}" \`
  DEPLOYKEY="DEPLOY_KEY" \`
  /quiet /norestart`}</pre>
                    </div>

                    <div className="space-y-2 pt-1 border-t border-border/50">
                      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Fallback (EXE, ohne MSI)</p>
                      <pre className="text-xs font-mono overflow-x-auto whitespace-pre bg-background border rounded px-3 py-2">{`Invoke-WebRequest -Uri "${agentServerUrl || "https://api.example.com"}/install/deploy/download" \`
  -Headers @{ "X-Deploy-Key" = "DEPLOY_KEY" } \`
  -OutFile "$env:TEMP\\HITSight-Setup.exe"
Start-Process "$env:TEMP\\HITSight-Setup.exe" -Verb RunAs -Wait`}</pre>
                    </div>

                  </div>
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Agent ─────────────────────────────────────────────────── */}
          {activeSection === "agent" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Agent</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <RefreshCw className="h-4 w-4" />
                    Automatische Updates
                  </CardTitle>
                  <CardDescription>
                    Agents erhalten beim nächsten Check-in automatisch einen Update-Befehl, sobald eine neuere Version verfügbar ist.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="flex items-center gap-3 mb-4">
                    <input
                      id="auto-update"
                      type="checkbox"
                      checked={autoUpdate}
                      onChange={e => setAutoUpdate(e.target.checked)}
                      className="h-4 w-4 rounded border-border accent-primary"
                    />
                    <Label htmlFor="auto-update" className="font-normal cursor-pointer">
                      Agents automatisch aktualisieren
                    </Label>
                  </div>
                  <div className="flex items-center gap-3">
                    <Button onClick={handleSaveAutoUpdate}>Speichern</Button>
                    <SaveFeedback msg={autoUpdateSaveMsg} />
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <div>
                      <CardTitle className="flex items-center gap-2 text-base">
                        <Tag className="h-4 w-4" />
                        Agent-Versionen
                      </CardTitle>
                      <CardDescription>
                        Die als „aktuell" markierte Version wird den Agents beim Check-in gemeldet und löst ein automatisches Update aus.
                      </CardDescription>
                    </div>
                    <div className="flex gap-2">
                      <Button size="sm" variant="outline" onClick={handlePublishAgent} disabled={publishLoading}>
                        <Download className="h-3.5 w-3.5 mr-1.5" />
                        {publishLoading ? "Wird veröffentlicht..." : "Publishen"}
                      </Button>
                      <Button size="sm" onClick={() => { setVerVersion(""); setVerUrl(""); setVerChangelog(""); setVerIsLatest(true); setVersionDialog(true); }}>
                        <Plus className="h-3.5 w-3.5 mr-1.5" />
                        Manuell
                      </Button>
                    </div>
                  </div>
                </CardHeader>
                <CardContent>
                  {agentVers.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Noch keine Versionen registriert.</p>
                  ) : (
                    <div className="rounded-md border border-border overflow-hidden">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-border bg-muted/30">
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Version</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Download URL</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Veröffentlicht</th>
                            <th className="w-32"></th>
                          </tr>
                        </thead>
                        <tbody>
                          {agentVers.map(v => (
                            <>
                              <tr key={v.id} className="border-t border-border/50">
                                <td className="px-4 py-2.5">
                                  <div className="flex items-center gap-2">
                                    <span className="font-mono text-xs">{v.version}</span>
                                    {v.isLatest && <Badge variant="secondary" className="text-xs">aktuell</Badge>}
                                  </div>
                                </td>
                                <td className="px-4 py-2.5 text-muted-foreground text-xs truncate max-w-[220px]">
                                  {v.downloadUrl ? (
                                    <a href={v.downloadUrl} target="_blank" rel="noopener noreferrer" className="text-primary hover:underline">
                                      {v.downloadUrl}
                                    </a>
                                  ) : "—"}
                                </td>
                                <td className="px-4 py-2.5 text-muted-foreground text-xs">
                                  {new Date(v.releasedAt).toLocaleDateString("de-DE")}
                                </td>
                                <td className="px-4 py-2.5">
                                  <div className="flex gap-1 justify-end">
                                    <Button
                                      variant="ghost" size="icon" className="h-7 w-7 text-muted-foreground"
                                      title="Changelog bearbeiten"
                                      onClick={() => setChangelogEditDialog({ id: v.id, text: v.changelog ?? "" })}
                                    >
                                      <Pencil className="h-3.5 w-3.5" />
                                    </Button>
                                    {v.changelog && (
                                      <Button
                                        variant="ghost" size="icon" className="h-7 w-7 text-muted-foreground"
                                        title="Changelog anzeigen/verbergen"
                                        onClick={() => setExpandedChangelogs(prev => {
                                          const next = new Set(prev);
                                          next.has(v.id) ? next.delete(v.id) : next.add(v.id);
                                          return next;
                                        })}
                                      >
                                        <FileText className="h-3.5 w-3.5" />
                                      </Button>
                                    )}
                                    {!v.isLatest && (
                                      <Button variant="ghost" size="sm" className="h-7 text-xs" onClick={() => handleSetLatest(v.id)}>
                                        Als aktuell
                                      </Button>
                                    )}
                                    <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeleteVersion(v.id)}>
                                      <Trash2 className="h-3.5 w-3.5" />
                                    </Button>
                                  </div>
                                </td>
                              </tr>
                              {v.changelog && expandedChangelogs.has(v.id) && (
                                <tr key={`${v.id}-changelog`} className="bg-muted/20">
                                  <td colSpan={4} className="px-4 py-2 text-xs text-muted-foreground whitespace-pre-wrap border-t border-dashed border-border/40">
                                    {v.changelog}
                                  </td>
                                </tr>
                              )}
                            </>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── E-Mail & SMTP ─────────────────────────────────────────── */}
          {activeSection === "email" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">E-Mail & SMTP</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Mail className="h-4 w-4" />
                    SMTP-Konfiguration
                  </CardTitle>
                  <CardDescription>
                    Ausgehende E-Mails für Alerts und Installationslinks.
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <form onSubmit={handleSaveEmail} className="space-y-4">
                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1.5">
                        <Label>SMTP Host</Label>
                        <Input placeholder="smtp.example.com" value={emailForm.host} onChange={e => setEmailForm(f => ({ ...f, host: e.target.value }))} />
                      </div>
                      <div className="space-y-1.5">
                        <Label>Port</Label>
                        <Input type="number" value={emailForm.port} onChange={e => setEmailForm(f => ({ ...f, port: Number(e.target.value) }))} />
                      </div>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1.5">
                        <Label>Benutzername</Label>
                        <Input placeholder="user@example.com" value={emailForm.username} onChange={e => setEmailForm(f => ({ ...f, username: e.target.value }))} autoComplete="off" />
                      </div>
                      <div className="space-y-1.5">
                        <Label>Passwort {emailHasPassword && <span className="text-xs text-muted-foreground">(gesetzt)</span>}</Label>
                        <Input type="password" placeholder={emailHasPassword ? "Leer lassen um beizubehalten" : ""} value={emailForm.password} onChange={e => setEmailForm(f => ({ ...f, password: e.target.value }))} autoComplete="new-password" />
                      </div>
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1.5">
                        <Label>Absender (From)</Label>
                        <Input placeholder="hitsight@example.com" value={emailForm.from} onChange={e => setEmailForm(f => ({ ...f, from: e.target.value }))} />
                      </div>
                      <div className="space-y-1.5">
                        <Label>Empfänger (To)</Label>
                        <Input placeholder="admin@example.com, it@example.com" value={emailForm.to} onChange={e => setEmailForm(f => ({ ...f, to: e.target.value }))} />
                        <p className="text-xs text-muted-foreground">Mehrere Adressen kommagetrennt eingeben</p>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <input id="useSsl" type="checkbox" className="h-4 w-4 rounded border-border" checked={emailForm.useSsl} onChange={e => setEmailForm(f => ({ ...f, useSsl: e.target.checked }))} />
                      <Label htmlFor="useSsl" className="cursor-pointer font-normal">SSL direkt (Port 465) — ohne Haken: STARTTLS</Label>
                    </div>
                    {emailSaveMsg && (
                      <div className={cn("flex items-center gap-2 text-sm", emailSaveMsg.ok ? "text-emerald-500" : "text-destructive")}>
                        {emailSaveMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                        {emailSaveMsg.text}
                      </div>
                    )}
                    <div className="flex gap-2">
                      <Button type="submit" disabled={emailLoading}>{emailLoading ? "Speichern..." : "Speichern"}</Button>
                      <Button type="button" variant="outline" onClick={handleTestEmail} disabled={testLoading}>
                        <Send className="h-3.5 w-3.5 mr-1.5" />
                        {testLoading ? "Wird gesendet..." : "Test-E-Mail"}
                      </Button>
                    </div>
                    {testMsg && (
                      <div className={cn("flex items-center gap-2 text-sm", testMsg.ok ? "text-emerald-500" : "text-destructive")}>
                        {testMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                        {testMsg.text}
                      </div>
                    )}
                  </form>
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Benachrichtigungen ────────────────────────────────────── */}
          {activeSection === "benachrichtigungen" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Benachrichtigungen</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Bell className="h-4 w-4" />
                    Standard-Benachrichtigungen
                  </CardTitle>
                  <CardDescription>
                    Diese Einstellungen gelten für alle Geräte, sofern keine geräteindividuelle Regel greift.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  {([
                    { key: "deviceOffline", label: "Gerät geht offline" },
                    { key: "deviceOnline", label: "Gerät ist wieder online" },
                    { key: "newPending", label: "Neue Geräteregistrierung (ausstehend)" },
                    { key: "softwareAlert", label: "Blacklisted Software erkannt" },
                    { key: "diskFull", label: "Festplatte fast voll" },
                  ] as const).map(({ key, label }) => (
                    <div key={key} className="flex items-center gap-3">
                      <input
                        id={`notify-${key}`}
                        type="checkbox"
                        checked={notifyDefaults[key]}
                        onChange={e => setNotifyDefaults(d => ({ ...d, [key]: e.target.checked }))}
                        className="h-4 w-4 rounded border-border accent-primary"
                      />
                      <Label htmlFor={`notify-${key}`} className="font-normal cursor-pointer">{label}</Label>
                    </div>
                  ))}
                  <div className="pt-2 border-t border-border/50 mt-2">
                    <Label className="text-sm font-medium">Offline-Alert Verzögerung</Label>
                    <p className="text-xs text-muted-foreground mb-2">Minuten nach dem letzten Check-in abwarten, bevor eine Offline-Warnung gesendet wird.</p>
                    <div className="flex items-center gap-3">
                      <select
                        value={notifyDefaults.offlineAlertDelayMinutes}
                        onChange={e => setNotifyDefaults(d => ({ ...d, offlineAlertDelayMinutes: Number(e.target.value) }))}
                        className="h-8 rounded-md border border-input bg-background px-2 text-sm"
                      >
                        {[0, 10, 30, 60, 120].map(m => (
                          <option key={m} value={m}>{m === 0 ? "Sofort" : `${m} Minuten`}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                  <div className="pt-2 border-t border-border/50 mt-2">
                    <Label className="text-sm font-medium">Antivirus Signatur-Alter Schwellwert</Label>
                    <p className="text-xs text-muted-foreground mb-2">Alert senden, wenn Antivirus-Signaturen älter als X Tage sind (0 = deaktiviert).</p>
                    <div className="flex items-center gap-3">
                      <select
                        value={notifyDefaults.avSignatureAgeAlertDays}
                        onChange={e => setNotifyDefaults(d => ({ ...d, avSignatureAgeAlertDays: Number(e.target.value) }))}
                        className="h-8 rounded-md border border-input bg-background px-2 text-sm"
                      >
                        {[0, 3, 7, 14, 30].map(d => (
                          <option key={d} value={d}>{d === 0 ? "Deaktiviert" : `${d} Tage`}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                  <div className="flex items-center gap-3 pt-2">
                    <Button onClick={handleSaveNotifyDefaults}>Speichern</Button>
                    <SaveFeedback msg={notifySaveMsg} />
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <div>
                      <CardTitle className="flex items-center gap-2 text-base">
                        <Monitor className="h-4 w-4" />
                        Geräteindividuelle Einstellungen
                      </CardTitle>
                      <CardDescription>
                        Überschreibe Benachrichtigungsregeln für einzelne Geräte.
                      </CardDescription>
                    </div>
                    <Button size="sm" onClick={openNewOverride}>
                      <Plus className="h-3.5 w-3.5 mr-1.5" />
                      Gerät hinzufügen
                    </Button>
                  </div>
                </CardHeader>
                <CardContent>
                  {notifyOverrides.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Keine geräteindividuellen Einstellungen vorhanden.</p>
                  ) : (
                    <div className="space-y-2">
                      {notifyOverrides.map(o => (
                        <div key={o.id} className="flex items-center justify-between rounded-md border px-3 py-2 text-sm">
                          <div className="flex-1 min-w-0">
                            <div className="font-medium truncate">{o.device.hostname}</div>
                            {o.device.customer && (
                              <div className="text-xs text-muted-foreground">{o.device.customer.name}</div>
                            )}
                          </div>
                          <div className="flex gap-3 text-xs text-muted-foreground mx-4 flex-wrap">
                            {([
                              { val: o.alertOnOffline, label: "Offline" },
                              { val: o.alertOnOnline, label: "Online" },
                              { val: o.alertOnSoftwareAlert, label: "Software" },
                              { val: o.alertOnDiskFull, label: "Disk" },
                            ]).map(({ val, label }) => (
                              <span key={label} className={`px-1.5 py-0.5 rounded ${val === null ? "bg-muted text-muted-foreground" : val ? "bg-emerald-500/10 text-emerald-600" : "bg-red-500/10 text-red-500"}`}>
                                {label}: {val === null ? "Standard" : val ? "An" : "Aus"}
                              </span>
                            ))}
                          </div>
                          <div className="flex gap-1.5">
                            <Button size="sm" variant="ghost" onClick={() => openEditOverride(o)}>
                              <Pencil className="h-3.5 w-3.5" />
                            </Button>
                            <Button size="sm" variant="ghost" onClick={() => handleDeleteOverride(o.device.id)}>
                              <Trash2 className="h-3.5 w-3.5 text-destructive" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Felder ────────────────────────────────────────────────── */}
          {activeSection === "felder" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Benutzerdefinierte Felder</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Tag className="h-4 w-4" />
                    Gerätefelder
                  </CardTitle>
                  <CardDescription>
                    Felder die du hier definierst erscheinen auf jeder Geräteseite (z.B. „Standort", „Vertragsnummer", „Ansprechpartner").
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="flex gap-2">
                    <input
                      className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                      placeholder="Feldname, z.B. Standort"
                      value={newFieldName}
                      onChange={e => setNewFieldName(e.target.value)}
                      onKeyDown={e => e.key === "Enter" && handleAddField()}
                    />
                    <Button onClick={handleAddField} disabled={fieldLoading || !newFieldName.trim()}>
                      <Plus className="h-3.5 w-3.5 mr-1.5" />
                      Hinzufügen
                    </Button>
                  </div>
                  {fieldDefs.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Noch keine Felder definiert.</p>
                  ) : (
                    <div className="rounded-md border border-border overflow-hidden">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-border bg-muted/30">
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Feldname</th>
                            <th className="w-12"></th>
                          </tr>
                        </thead>
                        <tbody>
                          {fieldDefs.map(f => (
                            <tr key={f.id} className="border-t border-border/50">
                              <td className="px-4 py-2.5 font-medium">{f.name}</td>
                              <td className="px-4 py-2.5">
                                <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeleteField(f.id)}>
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Software-Blacklist ────────────────────────────────────── */}
          {activeSection === "software" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Software-Blacklist</h1>

              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <div>
                      <CardTitle className="flex items-center gap-2 text-base">
                        <ShieldAlert className="h-4 w-4" />
                        Blacklist-Einträge
                      </CardTitle>
                      <CardDescription>Software-Namen oder Muster, die einen Alert auslösen wenn erkannt.</CardDescription>
                    </div>
                    <Button size="sm" onClick={() => { setBlPattern(""); setBlPublisher(""); setBlReason(""); setBlacklistDialog(true); }}>
                      <Plus className="h-3.5 w-3.5 mr-1.5" />
                      Eintrag hinzufügen
                    </Button>
                  </div>
                </CardHeader>
                <CardContent>
                  {blacklist.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Keine Einträge vorhanden.</p>
                  ) : (
                    <div className="rounded-md border border-border overflow-hidden">
                      <table className="w-full text-sm">
                        <thead>
                          <tr className="border-b border-border bg-muted/30">
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Muster</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Hersteller</th>
                            <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Grund</th>
                            <th className="w-12"></th>
                          </tr>
                        </thead>
                        <tbody>
                          {blacklist.map(entry => (
                            <tr key={entry.id} className="border-t border-border/50">
                              <td className="px-4 py-2.5 font-medium font-mono text-xs">{entry.namePattern}</td>
                              <td className="px-4 py-2.5 text-muted-foreground">{entry.publisher ?? "—"}</td>
                              <td className="px-4 py-2.5 text-muted-foreground">{entry.reason ?? "—"}</td>
                              <td className="px-4 py-2.5">
                                <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeleteBlacklist(entry.id)}>
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Fernzugriff ───────────────────────────────────────────── */}
          {activeSection === "fernzugriff" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Fernzugriff</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Monitor className="h-4 w-4" />
                    RustDesk
                  </CardTitle>
                  <CardDescription>
                    Self-hosted RustDesk-Relay konfigurieren. Agents übernehmen die Einstellungen beim nächsten Check-in.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-1.5">
                      <Label>Relay-Host</Label>
                      <Input
                        placeholder="z.B. hitsight.example.com"
                        value={rustDesk.relayHost}
                        onChange={e => setRustDesk(r => ({ ...r, relayHost: e.target.value }))}
                      />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Download-URL (Installer)</Label>
                      <Input
                        placeholder="https://…/rustdesk-1.x.x.exe"
                        value={rustDesk.downloadUrl}
                        onChange={e => setRustDesk(r => ({ ...r, downloadUrl: e.target.value }))}
                      />
                    </div>
                  </div>
                  <div className="space-y-1.5">
                    <Label>Öffentlicher Schlüssel (Public Key)</Label>
                    <Input
                      placeholder="Base64-kodierter Ed25519-Key aus id_ed25519.pub"
                      value={rustDesk.publicKey}
                      onChange={e => setRustDesk(r => ({ ...r, publicKey: e.target.value }))}
                      className="font-mono text-xs"
                    />
                    <p className="text-xs text-muted-foreground">
                      Aus dem Container auslesen:{" "}
                      <code className="bg-muted px-1 rounded">docker exec &lt;rustdesk-hbbs&gt; cat /root/id_ed25519.pub</code>
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <input
                      id="rd-autoinstall"
                      type="checkbox"
                      checked={rustDesk.autoInstall}
                      onChange={e => setRustDesk(r => ({ ...r, autoInstall: e.target.checked }))}
                      className="h-4 w-4 rounded border-border accent-primary"
                    />
                    <Label htmlFor="rd-autoinstall" className="font-normal cursor-pointer">
                      Automatisch installieren — Agent installiert RustDesk, wenn noch nicht vorhanden
                    </Label>
                  </div>

                  <div className="flex items-center gap-3">
                    <Button variant="outline" onClick={() => setRdOptionsOpen(true)}>
                      <Settings2 className="h-4 w-4 mr-2" />
                      Optionen konfigurieren
                      {Object.keys(rustDesk.globalOptions ?? {}).length > 0 && (
                        <span className="ml-2 text-xs bg-primary text-primary-foreground rounded-full px-1.5 py-0.5">
                          {Object.keys(rustDesk.globalOptions ?? {}).length}
                        </span>
                      )}
                    </Button>
                    <p className="text-xs text-muted-foreground">
                      Gilt für alle Geräte — gerätespezifisch überschreibbar.
                      <code className="ml-1 bg-muted px-1 rounded text-xs">allow-remote-config-modification</code> wird immer gesetzt.
                    </p>
                  </div>

                  <div className="flex flex-wrap items-center gap-3 pt-1">
                    <Button onClick={handleSaveRustDesk} disabled={rustDeskLoading}>
                      {rustDeskLoading ? "Wird gespeichert..." : "Speichern"}
                    </Button>
                    <SaveFeedback msg={rustDeskSaveMsg} />
                  </div>

                  <div className="rounded-md border border-border bg-muted/20 p-3 space-y-2">
                    <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">Auf Geräte anwenden</p>
                    <div className="flex flex-wrap gap-2">
                      <Button size="sm" variant="outline" onClick={handleForceApplyRustDesk} disabled={rdForceApplyLoading}>
                        <RefreshCw className={cn("h-3.5 w-3.5 mr-1.5", rdForceApplyLoading && "animate-spin")} />
                        {rdForceApplyLoading ? "Wird ausgelöst..." : "Alle Agents neu konfigurieren"}
                      </Button>
                      <Button size="sm" variant="outline" className="hover:text-destructive hover:border-destructive" onClick={handleClearDeviceOverrides} disabled={rdClearLoading}>
                        <Trash2 className="h-3.5 w-3.5 mr-1.5" />
                        {rdClearLoading ? "Wird gelöscht..." : "Geräte-Overrides löschen"}
                      </Button>
                    </div>
                    {rdForceApplyMsg && <p className="text-xs text-emerald-600 dark:text-emerald-400">{rdForceApplyMsg}</p>}
                    {rdClearMsg && <p className="text-xs text-emerald-600 dark:text-emerald-400">{rdClearMsg}</p>}
                    <p className="text-xs text-muted-foreground">
                      <span className="font-medium text-foreground">Alle Agents neu konfigurieren</span> — Agents schreiben RustDesk-Config beim nächsten Check-in neu, auch wenn sich nichts geändert hat.
                      {" "}<span className="font-medium text-foreground">Geräte-Overrides löschen</span> — entfernt alle gerätespezifischen Einstellungen, Geräte nutzen dann nur die globalen.
                    </p>
                  </div>
                </CardContent>
              </Card>

              <RustDeskOptionsDialog
                open={rdOptionsOpen}
                onOpenChange={setRdOptionsOpen}
                mode="global"
                options={rustDesk.globalOptions ?? {}}
                onChange={(opts) => setRustDesk(r => ({ ...r, globalOptions: opts }))}
                onSave={handleSaveRustDesk}
                saving={rustDeskLoading}
                saved={rustDeskSaveMsg?.ok === true}
              />
            </>
          )}

          {/* ── LDAP / Active Directory ───────────────────────────────── */}
          {activeSection === "ldap" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">LDAP / Active Directory</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Users className="h-4 w-4" />
                    LDAP-Authentifizierung
                  </CardTitle>
                  <CardDescription>
                    Ermöglicht die Anmeldung mit Active Directory / LDAP-Konten. Lokale Accounts funktionieren weiterhin unabhängig davon.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-5">
                  <div className="flex items-center gap-3">
                    <input
                      id="ldap-enabled"
                      type="checkbox"
                      className="h-4 w-4 rounded border-border"
                      checked={ldap.enabled}
                      onChange={e => setLdap(p => ({ ...p, enabled: e.target.checked }))}
                    />
                    <Label htmlFor="ldap-enabled" className="font-normal cursor-pointer">LDAP aktivieren</Label>
                  </div>

                  <div className="grid grid-cols-3 gap-4">
                    <div className="space-y-1.5 col-span-2">
                      <Label>Host / Server</Label>
                      <Input value={ldap.host} onChange={e => setLdap(p => ({ ...p, host: e.target.value }))} placeholder="dc01.example.com" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Port</Label>
                      <Input type="number" value={ldap.port} onChange={e => setLdap(p => ({ ...p, port: +e.target.value }))} placeholder="389" />
                    </div>
                  </div>

                  <div className="space-y-1.5">
                    <Label>Transport</Label>
                    <div className="flex gap-4">
                      {(["TCP", "STARTTLS", "LDAPS"] as const).map(t => (
                        <label key={t} className="flex items-center gap-2 cursor-pointer">
                          <input
                            type="radio"
                            name="ldap-transport"
                            value={t}
                            checked={ldap.transport === t}
                            onChange={() => setLdap(p => ({
                              ...p,
                              transport: t,
                              port: p.port === 389 || p.port === 636
                                ? (t === "LDAPS" ? 636 : 389)
                                : p.port,
                            }))}
                            className="h-4 w-4"
                          />
                          <span className="text-sm">
                            {t === "TCP" && "TCP (unverschlüsselt, Port 389)"}
                            {t === "STARTTLS" && "STARTTLS (Port 389)"}
                            {t === "LDAPS" && "LDAPS / SSL (Port 636)"}
                          </span>
                        </label>
                      ))}
                    </div>
                    {ldap.transport === "TCP" && (
                      <p className="text-xs text-amber-600 dark:text-amber-400">Warnung: Anmeldedaten werden unverschlüsselt übertragen.</p>
                    )}
                  </div>

                  {ldap.transport !== "TCP" && (
                    <div className="space-y-3">
                      <div className="space-y-2">
                        <div className="flex items-center justify-between">
                          <Label>CA-Zertifikat <span className="text-xs text-muted-foreground">(PEM, für selbst signierte / interne CAs)</span></Label>
                          {ldap.hasCaCertificate && (
                            <span className="flex items-center gap-1 text-xs text-emerald-600 dark:text-emerald-400 font-medium">
                              <CheckCircle2 className="h-3.5 w-3.5" /> gespeichert
                            </span>
                          )}
                        </div>
                        <textarea
                          className="w-full min-h-[100px] rounded-md border border-border bg-background px-3 py-2 text-xs font-mono resize-y placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                          placeholder={"-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----"}
                          value={ldapCaPem}
                          onChange={e => { setLdapCaPem(e.target.value); setLdapCaMsg(null); }}
                        />
                        <div className="flex items-center gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={ldapCaUploading || !ldapCaPem.trim()}
                            onClick={async () => {
                              setLdapCaMsg(null);
                              setLdapCaUploading(true);
                              try {
                                const res = await settings.uploadLdapCaCert(ldapCaPem.trim());
                                setLdapCaMsg({ ok: true, text: res.message });
                                setLdapCaPem("");
                                setLdap(p => ({ ...p, hasCaCertificate: true }));
                              } catch (err: unknown) {
                                setLdapCaMsg({ ok: false, text: err instanceof Error ? err.message : "Fehler beim Hochladen." });
                              } finally {
                                setLdapCaUploading(false);
                              }
                            }}
                          >
                            {ldapCaUploading ? "Hochladen..." : "Zertifikat hochladen"}
                          </Button>
                          {ldap.hasCaCertificate && (
                            <Button
                              size="sm"
                              variant="ghost"
                              className="text-destructive hover:text-destructive"
                              disabled={ldapCaDeleting}
                              onClick={async () => {
                                setLdapCaMsg(null);
                                setLdapCaDeleting(true);
                                try {
                                  const res = await settings.deleteLdapCaCert();
                                  setLdapCaMsg({ ok: true, text: res.message });
                                  setLdap(p => ({ ...p, hasCaCertificate: false }));
                                } catch (err: unknown) {
                                  setLdapCaMsg({ ok: false, text: err instanceof Error ? err.message : "Fehler beim Entfernen." });
                                } finally {
                                  setLdapCaDeleting(false);
                                }
                              }}
                            >
                              {ldapCaDeleting ? "Entfernen..." : "Zertifikat entfernen"}
                            </Button>
                          )}
                        </div>
                        {ldapCaMsg && (
                          <div className={cn("flex items-center gap-2 text-sm", ldapCaMsg.ok ? "text-emerald-500" : "text-destructive")}>
                            {ldapCaMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                            {ldapCaMsg.text}
                          </div>
                        )}
                      </div>
                      <div className="flex items-center gap-3">
                        <input
                          id="ldap-ignore-cert"
                          type="checkbox"
                          className="h-4 w-4 rounded border-border"
                          checked={ldap.ignoreCertificateErrors}
                          onChange={e => setLdap(p => ({ ...p, ignoreCertificateErrors: e.target.checked }))}
                        />
                        <Label htmlFor="ldap-ignore-cert" className="font-normal cursor-pointer text-amber-600 dark:text-amber-400">
                          Zertifikatsfehler ignorieren (kein Zertifikat hinterlegt)
                        </Label>
                      </div>
                    </div>
                  )}

                  <div className="space-y-1.5">
                    <Label>Base DN</Label>
                    <Input value={ldap.baseDn} onChange={e => setLdap(p => ({ ...p, baseDn: e.target.value }))} placeholder="DC=example,DC=com" />
                  </div>

                  <div className="border-t border-border pt-4 space-y-3">
                    <p className="text-sm font-medium">Service-Account (für Suche)</p>
                    <div className="space-y-1.5">
                      <Label>Bind DN <span className="text-xs text-muted-foreground">(leer = anonymer Bind)</span></Label>
                      <Input value={ldap.bindDn} onChange={e => setLdap(p => ({ ...p, bindDn: e.target.value }))} placeholder="CN=svc-hitsight,OU=Service,DC=example,DC=com" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>
                        Bind-Passwort
                        {ldap.hasBindPassword && <span className="ml-2 text-xs text-muted-foreground">(gesetzt — leer lassen zum Beibehalten)</span>}
                      </Label>
                      <Input
                        type="password"
                        value={ldapBindPassword}
                        onChange={e => setLdapBindPassword(e.target.value)}
                        placeholder={ldap.hasBindPassword ? "••••••••" : "Passwort"}
                        autoComplete="new-password"
                      />
                    </div>
                  </div>

                  <div className="border-t border-border pt-4 space-y-3">
                    <p className="text-sm font-medium">Benutzersuche</p>
                    <div className="space-y-1.5">
                      <Label>User Search Base <span className="text-xs text-muted-foreground">(leer = Base DN)</span></Label>
                      <Input value={ldap.userSearchBase} onChange={e => setLdap(p => ({ ...p, userSearchBase: e.target.value }))} placeholder="OU=Users,DC=example,DC=com" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>User Filter <span className="text-xs text-muted-foreground">({"{0}"} wird durch den Benutzernamen ersetzt)</span></Label>
                      <Input value={ldap.userFilter} onChange={e => setLdap(p => ({ ...p, userFilter: e.target.value }))} placeholder="(&(objectClass=user)(|(sAMAccountName={0})(userPrincipalName={0})))" />
                    </div>
                  </div>

                  <div className="border-t border-border pt-4 space-y-3">
                    <p className="text-sm font-medium">Rollen-Mapping</p>
                    <div className="space-y-1.5">
                      <Label>Admin-Gruppe <span className="text-xs text-muted-foreground">(DN oder CN)</span></Label>
                      <Input value={ldap.adminGroup} onChange={e => setLdap(p => ({ ...p, adminGroup: e.target.value }))} placeholder="CN=HackIT-Admins,OU=Groups,DC=example,DC=com" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Viewer-Gruppe <span className="text-xs text-muted-foreground">(leer = alle authentifizierten Benutzer sind Viewer)</span></Label>
                      <Input value={ldap.viewerGroup} onChange={e => setLdap(p => ({ ...p, viewerGroup: e.target.value }))} placeholder="CN=HackIT-Users,OU=Groups,DC=example,DC=com" />
                    </div>
                    <div className="flex items-center gap-3">
                      <input
                        id="ldap-require-group"
                        type="checkbox"
                        className="h-4 w-4 rounded border-border"
                        checked={ldap.requireGroup}
                        onChange={e => setLdap(p => ({ ...p, requireGroup: e.target.checked }))}
                      />
                      <Label htmlFor="ldap-require-group" className="font-normal cursor-pointer">
                        Zugriff nur für Mitglieder der Admin- oder Viewer-Gruppe
                      </Label>
                    </div>
                    <div className="flex items-center gap-3">
                      <input
                        id="ldap-nested-groups"
                        type="checkbox"
                        className="h-4 w-4 rounded border-border"
                        checked={ldap.useNestedGroups}
                        onChange={e => setLdap(p => ({ ...p, useNestedGroups: e.target.checked }))}
                      />
                      <Label htmlFor="ldap-nested-groups" className="font-normal cursor-pointer">
                        Verschachtelte Gruppen berücksichtigen (Active Directory)
                      </Label>
                    </div>
                  </div>

                  <div className="flex items-center gap-3 pt-1">
                    <Button onClick={handleSaveLdap} disabled={ldapSaving}>
                      {ldapSaving ? "Speichern..." : "Speichern"}
                    </Button>
                    <Button variant="outline" onClick={handleTestLdap} disabled={ldapTesting}>
                      {ldapTesting ? "Teste..." : "Verbindung testen"}
                    </Button>
                  </div>
                  <SaveFeedback msg={ldapSaveMsg} />
                  {ldapTestMsg && (
                    <div className={cn("flex items-center gap-2 text-sm", ldapTestMsg.ok ? "text-emerald-500" : "text-destructive")}>
                      {ldapTestMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                      {ldapTestMsg.text}
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Benutzer ──────────────────────────────────────────────── */}
          {activeSection === "benutzer" && isAdmin && (
            <>
              <div className="flex items-center justify-between">
                <h1 className="text-lg font-semibold">Benutzer</h1>
                <Button size="sm" onClick={() => { setNewUsername(""); setNewPassword(""); setNewRole("User"); setCreateError(""); setCreateDialog(true); }}>
                  <UserPlus className="h-3.5 w-3.5 mr-1.5" />
                  Neuer Benutzer
                </Button>
              </div>

              <Card>
                <CardContent className="pt-4">
                  <div className="rounded-md border border-border overflow-hidden">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="border-b border-border bg-muted/30">
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Benutzername</th>
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Rolle</th>
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Erstellt</th>
                          <th className="w-24"></th>
                        </tr>
                      </thead>
                      <tbody>
                        {userList.map(user => (
                          <tr key={user.id} className="border-t border-border/50">
                            <td className="px-4 py-2.5 font-medium">
                              <div className="flex items-center gap-2">
                                {user.displayName || user.username}
                                {user.username === currentUsername && (
                                  <span className="text-xs text-muted-foreground">(du)</span>
                                )}
                                {!user.isLocal && (
                                  <Badge variant="outline" className="text-[10px] px-1.5 py-0 font-normal">AD</Badge>
                                )}
                              </div>
                              {user.displayName && (
                                <div className="text-xs text-muted-foreground font-normal">{user.username}</div>
                              )}
                            </td>
                            <td className="px-4 py-2.5">
                              <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${user.role === "Admin" ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground"}`}>
                                {user.role === "Admin" ? "Admin" : "Viewer"}
                              </span>
                            </td>
                            <td className="px-4 py-2.5 text-muted-foreground text-xs">
                              {new Date(user.createdAt).toLocaleDateString("de-DE")}
                            </td>
                            <td className="px-4 py-2.5">
                              <div className="flex gap-1 justify-end">
                                {user.isLocal && (
                                  <Button variant="ghost" size="icon" className="h-7 w-7" title="Passwort zurücksetzen" onClick={() => { setResetPw(""); setResetDialog(user); }}>
                                    <RefreshCw className="h-3.5 w-3.5" />
                                  </Button>
                                )}
                                <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => setDeleteConfirm(user)} disabled={user.username === currentUsername}>
                                  <Trash2 className="h-3.5 w-3.5" />
                                </Button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Script-Bibliothek ─────────────────────────────────────── */}
          {activeSection === "scripts" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Script-Bibliothek</h1>
              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <div>
                      <CardTitle className="flex items-center gap-2 text-base">
                        <Terminal className="h-4 w-4" />
                        PowerShell-Scripts
                      </CardTitle>
                      <CardDescription>Wiederverwendbare Scripts für Remote-Ausführung auf Geräten.</CardDescription>
                    </div>
                    <Button size="sm" onClick={openNewScript}>
                      <Plus className="h-3.5 w-3.5 mr-1.5" />
                      Neu
                    </Button>
                  </div>
                </CardHeader>
                <CardContent>
                  {scriptList.length === 0 ? (
                    <p className="text-sm text-muted-foreground">Noch keine Scripts vorhanden.</p>
                  ) : (
                    <div className="space-y-2">
                      {scriptList.map(t => (
                        <div key={t.id} className="flex items-start justify-between rounded-md border px-3 py-2.5 text-sm gap-3">
                          <div className="flex-1 min-w-0">
                            <div className="font-medium">{t.name}</div>
                            {t.description && <div className="text-xs text-muted-foreground mt-0.5">{t.description}</div>}
                            <div className="text-xs text-muted-foreground mt-0.5">von {t.createdBy}</div>
                          </div>
                          <div className="flex gap-1 shrink-0">
                            <Button variant="ghost" size="icon" className="h-7 w-7" onClick={() => openEditScript(t)}>
                              <Pencil className="h-3.5 w-3.5" />
                            </Button>
                            <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeleteScript(t.id)}>
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </CardContent>
              </Card>

              {/* Script create/edit dialog */}
              {scriptDialog !== null && (
                <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setScriptDialog(null)}>
                  <div className="bg-background border border-border rounded-lg p-6 w-full max-w-2xl space-y-4 shadow-xl" onClick={e => e.stopPropagation()}>
                    <h2 className="text-base font-semibold">{scriptDialog === "new" ? "Neues Script" : "Script bearbeiten"}</h2>
                    <div className="space-y-1.5">
                      <Label>Name</Label>
                      <Input value={scriptName} onChange={e => setScriptName(e.target.value)} placeholder="z.B. Disk-Cleanup" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Beschreibung (optional)</Label>
                      <Input value={scriptDesc} onChange={e => setScriptDesc(e.target.value)} placeholder="Kurze Beschreibung..." />
                    </div>
                    <div className="space-y-1.5">
                      <Label>PowerShell-Script</Label>
                      <textarea
                        value={scriptBody}
                        onChange={e => setScriptBody(e.target.value)}
                        rows={12}
                        className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm font-mono resize-y"
                        placeholder="# PowerShell script..."
                      />
                    </div>
                    <div className="flex justify-end gap-2">
                      <Button variant="outline" onClick={() => setScriptDialog(null)}>Abbrechen</Button>
                      <Button onClick={handleSaveScript} disabled={scriptLoading || !scriptName.trim() || !scriptBody.trim()}>
                        {scriptLoading ? "Speichern..." : "Speichern"}
                      </Button>
                    </div>
                  </div>
                </div>
              )}
            </>
          )}

          {/* ── Software-Pakete ───────────────────────────────────────── */}
          {activeSection === "packages" && (
            <>
              <div className="flex items-center justify-between">
                <h1 className="text-lg font-semibold">Software-Pakete</h1>
                {isAdmin && (
                  <Button size="sm" onClick={openNewPackage}>
                    <Plus className="h-3.5 w-3.5 mr-1.5" />
                    Neues Paket
                  </Button>
                )}
              </div>
              <Card>
                <CardContent className="pt-4 space-y-2">
                  {packageList.length === 0 ? (
                    <p className="text-sm text-muted-foreground py-4 text-center">Keine Pakete definiert.</p>
                  ) : (
                    packageList.map(p => (
                      <div key={p.id} className="flex items-center justify-between rounded-md border border-border/60 px-3 py-2.5">
                        <div className="min-w-0">
                          <div className="font-medium text-sm">{p.name} {p.version && <span className="text-muted-foreground text-xs">v{p.version}</span>}</div>
                          <div className="text-xs text-muted-foreground">{p.type} · {p.installCmd}</div>
                          {p.description && <div className="text-xs text-muted-foreground mt-0.5 truncate">{p.description}</div>}
                        </div>
                        {isAdmin && (
                          <div className="flex items-center gap-1 shrink-0 ml-3">
                            <Button variant="ghost" size="icon" className="h-7 w-7" onClick={() => openEditPackage(p)}>
                              <Pencil className="h-3.5 w-3.5" />
                            </Button>
                            <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive" onClick={() => handleDeletePackage(p.id)}>
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          </div>
                        )}
                      </div>
                    ))
                  )}
                </CardContent>
              </Card>
              {packageDialog !== null && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
                  <div className="bg-card rounded-lg border border-border shadow-xl p-5 w-full max-w-lg space-y-4">
                    <h2 className="text-base font-semibold">{packageDialog === "new" ? "Neues Paket" : "Paket bearbeiten"}</h2>
                    <div className="grid grid-cols-2 gap-3">
                      <div className="space-y-1 col-span-2">
                        <Label className="text-sm">Name *</Label>
                        <Input value={pkgName} onChange={e => setPkgName(e.target.value)} placeholder="7-Zip" />
                      </div>
                      <div className="space-y-1">
                        <Label className="text-sm">Version</Label>
                        <Input value={pkgVersion} onChange={e => setPkgVersion(e.target.value)} placeholder="24.09" />
                      </div>
                      <div className="space-y-1">
                        <Label className="text-sm">Typ</Label>
                        <select value={pkgType} onChange={e => setPkgType(e.target.value)} className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm">
                          <option value="winget">winget</option>
                          <option value="script">PowerShell Script</option>
                        </select>
                      </div>
                      <div className="space-y-1 col-span-2">
                        <Label className="text-sm">Install-Befehl / Script *</Label>
                        {pkgType === "script" ? (
                          <textarea
                            value={pkgInstallCmd}
                            onChange={e => setPkgInstallCmd(e.target.value)}
                            rows={4}
                            placeholder="# PowerShell install script&#10;winget install -e --id 7zip.7zip"
                            className="w-full min-h-[80px] rounded-md border border-input bg-background px-3 py-2 text-sm font-mono resize-y"
                          />
                        ) : (
                          <Input value={pkgInstallCmd} onChange={e => setPkgInstallCmd(e.target.value)} placeholder="-e --id 7zip.7zip" />
                        )}
                      </div>
                      <div className="space-y-1 col-span-2">
                        <Label className="text-sm">Uninstall-Befehl (optional)</Label>
                        <Input value={pkgUninstallCmd} onChange={e => setPkgUninstallCmd(e.target.value)} placeholder="-e --id 7zip.7zip" />
                      </div>
                      <div className="space-y-1 col-span-2">
                        <Label className="text-sm">Beschreibung</Label>
                        <Input value={pkgDesc} onChange={e => setPkgDesc(e.target.value)} placeholder="Kurze Beschreibung..." />
                      </div>
                    </div>
                    <div className="flex justify-end gap-2 pt-1">
                      <Button variant="outline" onClick={() => setPackageDialog(null)}>Abbrechen</Button>
                      <Button onClick={handleSavePackage} disabled={!pkgName.trim() || !pkgInstallCmd.trim()}>Speichern</Button>
                    </div>
                  </div>
                </div>
              )}
            </>
          )}

          {/* ── Protokoll ─────────────────────────────────────────────── */}
          {activeSection === "protokoll" && isAdmin && (
            <>
              <h1 className="text-lg font-semibold">Audit-Protokoll</h1>

              <Card>
                <CardContent className="pt-4 space-y-3">
                  <Input
                    placeholder="Benutzer suchen..."
                    value={auditSearch}
                    onChange={e => { setAuditSearch(e.target.value); setAuditPage(1); }}
                    className="max-w-sm"
                  />
                  <div className="rounded-md border border-border overflow-hidden">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="border-b border-border bg-muted/30">
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Zeitpunkt</th>
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Benutzer</th>
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Aktion</th>
                          <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Details</th>
                        </tr>
                      </thead>
                      <tbody>
                        {auditLogs.length === 0 ? (
                          <tr><td colSpan={4} className="px-4 py-8 text-center text-muted-foreground text-xs">Keine Einträge gefunden.</td></tr>
                        ) : auditLogs.map(log => (
                          <tr key={log.id} className="border-t border-border/50">
                            <td className="px-4 py-2 text-xs text-muted-foreground whitespace-nowrap">
                              {new Date(log.timestamp).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })}
                            </td>
                            <td className="px-4 py-2 font-medium">{log.username}</td>
                            <td className="px-4 py-2 font-mono text-xs text-primary">{log.action}</td>
                            <td className="px-4 py-2 text-muted-foreground text-xs truncate max-w-[200px]">
                              {log.entityType}{log.details ? ` — ${log.details}` : ""}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {auditTotalPages > 1 && (
                    <div className="flex items-center justify-between text-xs text-muted-foreground">
                      <span>Seite {auditPage} von {auditTotalPages} ({auditTotal} Einträge)</span>
                      <div className="flex gap-1">
                        <Button variant="outline" size="icon" className="h-7 w-7" disabled={auditPage <= 1} onClick={() => setAuditPage(p => p - 1)}>
                          <ChevronLeft className="h-3.5 w-3.5" />
                        </Button>
                        <Button variant="outline" size="icon" className="h-7 w-7" disabled={auditPage >= auditTotalPages} onClick={() => setAuditPage(p => p + 1)}>
                          <ChevronRight className="h-3.5 w-3.5" />
                        </Button>
                      </div>
                    </div>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {/* ── Konto ─────────────────────────────────────────────────── */}
          {activeSection === "abonnement" && isPlatform && (
            <>
              <h1 className="text-lg font-semibold">Abonnement</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Building2 className="h-4 w-4" />
                    Aktueller Plan
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {tInfo ? (
                    <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm max-w-sm">
                      <dt className="text-muted-foreground">Plan</dt>
                      <dd className="font-medium capitalize">{tInfo.plan}</dd>

                      <dt className="text-muted-foreground">Geräte</dt>
                      <dd className={tInfo.maxDevices !== null && tInfo.deviceCount >= tInfo.maxDevices ? "font-medium text-destructive" : "font-medium"}>
                        {tInfo.deviceCount}{tInfo.maxDevices !== null ? ` / ${tInfo.maxDevices}` : ""}
                      </dd>

                      {tInfo.subscriptionStatus && (
                        <>
                          <dt className="text-muted-foreground">Status</dt>
                          <dd className="font-medium capitalize">{tInfo.subscriptionStatus}</dd>
                        </>
                      )}

                      {tInfo.trialEndsAt && (
                        <>
                          <dt className="text-muted-foreground">Trial bis</dt>
                          <dd className="font-medium">{new Date(tInfo.trialEndsAt).toLocaleDateString("de-DE")}</dd>
                        </>
                      )}

                      {tInfo.currentPeriodEndsAt && (
                        <>
                          <dt className="text-muted-foreground">Verlängert am</dt>
                          <dd className="font-medium">{new Date(tInfo.currentPeriodEndsAt).toLocaleDateString("de-DE")}</dd>
                        </>
                      )}
                    </dl>
                  ) : (
                    <p className="text-sm text-muted-foreground">Lade Abonnementinformationen…</p>
                  )}
                </CardContent>
              </Card>
            </>
          )}

          {activeSection === "konto" && (
            <>
              <h1 className="text-lg font-semibold">Konto</h1>

              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <KeyRound className="h-4 w-4" />
                    Passwort ändern
                  </CardTitle>
                  <CardDescription>Ändere dein eigenes Anmelde-Passwort.</CardDescription>
                </CardHeader>
                <CardContent>
                  <form onSubmit={handleChangePassword} className="space-y-3 max-w-sm">
                    <div className="space-y-1.5">
                      <Label>Aktuelles Passwort</Label>
                      <Input type="password" value={pwCurrent} onChange={e => setPwCurrent(e.target.value)} autoComplete="current-password" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Neues Passwort</Label>
                      <Input type="password" value={pwNext} onChange={e => setPwNext(e.target.value)} autoComplete="new-password" />
                    </div>
                    <div className="space-y-1.5">
                      <Label>Neues Passwort bestätigen</Label>
                      <Input type="password" value={pwConfirm} onChange={e => setPwConfirm(e.target.value)} autoComplete="new-password" />
                    </div>
                    {pwError && <p className="text-sm text-destructive">{pwError}</p>}
                    {pwSuccess && <p className="text-sm text-emerald-500">Passwort erfolgreich geändert.</p>}
                    <Button type="submit" disabled={pwLoading || !pwCurrent || !pwNext || !pwConfirm}>
                      {pwLoading ? "Wird geändert..." : "Passwort ändern"}
                    </Button>
                  </form>
                </CardContent>
              </Card>
            </>
          )}

        </div>
      </div>

      {/* ── Dialogs ───────────────────────────────────────────────────────── */}
      <Dialog open={createDialog} onOpenChange={setCreateDialog}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Neuer Benutzer</DialogTitle></DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Benutzername</Label>
              <Input value={newUsername} onChange={e => setNewUsername(e.target.value)} autoFocus />
            </div>
            <div className="space-y-1.5">
              <Label>Passwort</Label>
              <Input type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label>Rolle</Label>
              <div className="flex gap-4">
                {(["User", "Admin"] as const).map(r => (
                  <label key={r} className="flex items-center gap-2 cursor-pointer text-sm">
                    <input type="radio" name="role" value={r} checked={newRole === r} onChange={() => setNewRole(r)} className="h-4 w-4" />
                    <span>{r === "Admin" ? "Administrator" : "Benutzer (nur lesen)"}</span>
                  </label>
                ))}
              </div>
            </div>
            {createError && <p className="text-sm text-destructive">{createError}</p>}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateDialog(false)}>Abbrechen</Button>
            <Button onClick={handleCreate} disabled={createLoading || !newUsername || !newPassword}>
              {createLoading ? "Erstellen..." : "Erstellen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!resetDialog} onOpenChange={open => !open && setResetDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Passwort zurücksetzen</DialogTitle></DialogHeader>
          <p className="text-sm text-muted-foreground">
            Neues Passwort für <strong className="text-foreground">{resetDialog?.username}</strong>
          </p>
          <Input type="password" value={resetPw} onChange={e => setResetPw(e.target.value)} placeholder="Neues Passwort" />
          <DialogFooter>
            <Button variant="outline" onClick={() => setResetDialog(null)}>Abbrechen</Button>
            <Button onClick={handleResetPassword} disabled={resetLoading || resetPw.length < 6}>
              {resetLoading ? "Wird gesetzt..." : "Zurücksetzen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!deleteConfirm} onOpenChange={open => !open && setDeleteConfirm(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Benutzer löschen</DialogTitle></DialogHeader>
          <p className="text-sm text-muted-foreground">
            Benutzer <strong className="text-foreground">{deleteConfirm?.username}</strong> wirklich löschen?
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteConfirm(null)}>Abbrechen</Button>
            <Button variant="destructive" onClick={() => deleteConfirm && handleDeleteUser(deleteConfirm)}>Löschen</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={blacklistDialog} onOpenChange={setBlacklistDialog}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Blacklist-Eintrag hinzufügen</DialogTitle></DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Name-Muster <span className="text-xs text-muted-foreground">(Teilstring-Suche)</span></Label>
              <Input value={blPattern} onChange={e => setBlPattern(e.target.value)} placeholder="z.B. TeamViewer" autoFocus />
            </div>
            <div className="space-y-1.5">
              <Label>Hersteller <span className="text-xs text-muted-foreground">(optional)</span></Label>
              <Input value={blPublisher} onChange={e => setBlPublisher(e.target.value)} placeholder="z.B. TeamViewer GmbH" />
            </div>
            <div className="space-y-1.5">
              <Label>Grund <span className="text-xs text-muted-foreground">(optional)</span></Label>
              <Input value={blReason} onChange={e => setBlReason(e.target.value)} placeholder="z.B. Nicht erlaubt laut Policy" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setBlacklistDialog(false)}>Abbrechen</Button>
            <Button onClick={handleAddBlacklist} disabled={blLoading || !blPattern.trim()}>
              {blLoading ? "Hinzufügen..." : "Hinzufügen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={versionDialog} onOpenChange={setVersionDialog}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Agent-Version hinzufügen</DialogTitle></DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Version</Label>
              <Input value={verVersion} onChange={e => setVerVersion(e.target.value)} placeholder="z.B. 1.2.0" autoFocus />
            </div>
            <div className="space-y-1.5">
              <Label>Download-URL <span className="text-xs text-muted-foreground">(optional)</span></Label>
              <Input value={verUrl} onChange={e => setVerUrl(e.target.value)} placeholder="https://..." />
            </div>
            <div className="space-y-1.5">
              <Label>Changelog <span className="text-xs text-muted-foreground">(optional)</span></Label>
              <Input value={verChangelog} onChange={e => setVerChangelog(e.target.value)} placeholder="Was ist neu?" />
            </div>
            <div className="flex items-center gap-2">
              <input id="verLatest" type="checkbox" className="h-4 w-4 rounded border-border" checked={verIsLatest} onChange={e => setVerIsLatest(e.target.checked)} />
              <Label htmlFor="verLatest" className="cursor-pointer">Als aktuelle Version markieren</Label>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setVersionDialog(false)}>Abbrechen</Button>
            <Button onClick={handleAddVersion} disabled={verLoading || !verVersion.trim()}>
              {verLoading ? "Hinzufügen..." : "Hinzufügen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* ── Changelog-Edit-Dialog ─────────────────────────────────────────── */}
      <Dialog open={!!changelogEditDialog} onOpenChange={open => { if (!open) setChangelogEditDialog(null); }}>
        <DialogContent className="max-w-md">
          <DialogHeader><DialogTitle>Changelog bearbeiten</DialogTitle></DialogHeader>
          <div className="space-y-1.5">
            <Label>Changelog</Label>
            <textarea
              className="w-full min-h-[120px] rounded-md border border-input bg-background px-3 py-2 text-sm font-mono resize-y focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              placeholder={"- Neue Funktion\n- Bugfix"}
              value={changelogEditDialog?.text ?? ""}
              onChange={e => setChangelogEditDialog(prev => prev ? { ...prev, text: e.target.value } : null)}
              autoFocus
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setChangelogEditDialog(null)} disabled={changelogEditLoading}>Abbrechen</Button>
            <Button onClick={handleSaveChangelog} disabled={changelogEditLoading}>
              {changelogEditLoading ? "Speichern..." : "Speichern"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={notifyOverrideDialog !== null} onOpenChange={open => !open && setNotifyOverrideDialog(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>{notifyOverrideDialog === "new" ? "Gerät hinzufügen" : "Einstellungen bearbeiten"}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            {notifyOverrideDialog === "new" && (
              <div className="space-y-1.5">
                <Label>Gerät</Label>
                <Input
                  placeholder="Hostname suchen..."
                  value={overrideDeviceSearch}
                  onChange={e => {
                    setOverrideDeviceSearch(e.target.value);
                    setOverrideDeviceId("");
                  }}
                  autoFocus
                />
                {overrideDeviceSearch && !overrideDeviceId && (
                  <div className="rounded-md border border-border bg-popover shadow-md max-h-40 overflow-y-auto">
                    {allDevices
                      .filter(d => d.hostname.toLowerCase().includes(overrideDeviceSearch.toLowerCase()))
                      .filter(d => !notifyOverrides.some(o => o.device.id === d.id))
                      .map(d => (
                        <button
                          key={d.id}
                          className="w-full text-left px-3 py-2 text-sm hover:bg-muted transition-colors"
                          onClick={() => { setOverrideDeviceId(d.id); setOverrideDeviceSearch(d.hostname); }}
                        >
                          <span className="font-medium">{d.hostname}</span>
                          {d.description && <span className="text-muted-foreground ml-2 text-xs">{d.description}</span>}
                        </button>
                      ))}
                  </div>
                )}
                {overrideDeviceId && (
                  <p className="text-xs text-emerald-500 flex items-center gap-1">
                    <CheckCircle2 className="h-3.5 w-3.5" /> Gerät ausgewählt
                  </p>
                )}
              </div>
            )}
            {notifyOverrideDialog !== "new" && (
              <div className="text-sm font-medium">{(notifyOverrideDialog as DeviceNotificationOverride)?.device.hostname}</div>
            )}
            <div className="space-y-2">
              <Label className="text-xs text-muted-foreground uppercase tracking-wide">Benachrichtigungen (Standard = globale Einstellung)</Label>
              {([
                { key: "alertOnOffline" as const, label: "Gerät offline" },
                { key: "alertOnOnline" as const, label: "Gerät wieder online" },
                { key: "alertOnSoftwareAlert" as const, label: "Blacklisted Software" },
                { key: "alertOnDiskFull" as const, label: "Festplatte voll" },
              ]).map(({ key, label }) => (
                <div key={key} className="flex items-center gap-3">
                  <select
                    className="text-sm border rounded px-2 py-1 bg-background"
                    value={overrideValues[key] === null ? "default" : overrideValues[key] ? "on" : "off"}
                    onChange={e => {
                      const v = e.target.value === "default" ? null : e.target.value === "on";
                      setOverrideValues(prev => ({ ...prev, [key]: v }));
                    }}
                  >
                    <option value="default">Standard</option>
                    <option value="on">An</option>
                    <option value="off">Aus</option>
                  </select>
                  <span className="text-sm">{label}</span>
                </div>
              ))}
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setNotifyOverrideDialog(null)}>Abbrechen</Button>
            <Button onClick={handleSaveOverride} disabled={!overrideDeviceId && notifyOverrideDialog === "new"}>
              Speichern
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
