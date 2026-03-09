import { useEffect, useState } from "react";
import {
  KeyRound, UserPlus, Trash2, RefreshCw, Mail, Send, CheckCircle2, XCircle,
  ShieldAlert, Plus, Clock, Download, ChevronLeft, ChevronRight, AlertTriangle,
  Tag
} from "lucide-react";
import {
  auth, users, settings, software, audit, agentVersions, devices as devicesApi,
  type AppUser, type EmailSettingsInput, type BlacklistEntry,
  type AuditLogEntry, type AgentVersion
} from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";

export function Settings() {
  const currentUsername = localStorage.getItem("username") ?? "admin";

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
  const [createError, setCreateError] = useState("");
  const [createLoading, setCreateLoading] = useState(false);
  const [resetDialog, setResetDialog] = useState<AppUser | null>(null);
  const [resetPw, setResetPw] = useState("");
  const [resetLoading, setResetLoading] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<AppUser | null>(null);

  // --- Email settings ---
  const [emailForm, setEmailForm] = useState<EmailSettingsInput>({
    host: "", port: 587, username: "", password: "", from: "sentry@localhost", to: "", useSsl: false,
  });
  const [emailHasPassword, setEmailHasPassword] = useState(false);
  const [emailLoading, setEmailLoading] = useState(false);
  const [emailSaveMsg, setEmailSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [testLoading, setTestLoading] = useState(false);
  const [testMsg, setTestMsg] = useState<{ ok: boolean; text: string } | null>(null);

  // --- Alert settings ---
  const [diskThreshold, setDiskThreshold] = useState(10);
  const [alertSaveMsg, setAlertSaveMsg] = useState<{ ok: boolean; text: string } | null>(null);

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

  // --- Agent Versions ---
  const [agentVers, setAgentVers] = useState<AgentVersion[]>([]);
  const [versionDialog, setVersionDialog] = useState(false);
  const [verVersion, setVerVersion] = useState("");
  const [verUrl, setVerUrl] = useState("");
  const [verChangelog, setVerChangelog] = useState("");
  const [verIsLatest, setVerIsLatest] = useState(true);
  const [verLoading, setVerLoading] = useState(false);

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

  const handleSaveAlerts = async () => {
    setAlertSaveMsg(null);
    try {
      const res = await devicesApi.saveAlertSettings(diskThreshold);
      setAlertSaveMsg({ ok: true, text: res.message });
    } catch (err: any) {
      setAlertSaveMsg({ ok: false, text: err.message || "Fehler" });
    }
  };

  const fetchUsers = async () => {
    const data = await users.list();
    setUserList(data);
  };

  const handleCreate = async () => {
    setCreateError("");
    setCreateLoading(true);
    try {
      await users.create({ username: newUsername, password: newPassword });
      setCreateDialog(false);
      setNewUsername(""); setNewPassword("");
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

  const auditTotalPages = Math.ceil(auditTotal / AUDIT_PAGE_SIZE);

  return (
    <div className="p-6 max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold">Einstellungen</h1>
        <p className="text-sm text-muted-foreground">Angemeldet als <strong>{currentUsername}</strong></p>
      </div>

      {/* Change password */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <KeyRound className="h-4 w-4" />
            Passwort ändern
          </CardTitle>
          <CardDescription>Ändere dein eigenes Anmelde-Passwort.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleChangePassword} className="space-y-3">
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

      {/* Email alerting */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Mail className="h-4 w-4" />
            E-Mail Benachrichtigungen
          </CardTitle>
          <CardDescription>
            Automatische Alerts wenn Geräte offline gehen oder sich wieder verbinden.
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
                <Input placeholder="sentry@example.com" value={emailForm.from} onChange={e => setEmailForm(f => ({ ...f, from: e.target.value }))} />
              </div>
              <div className="space-y-1.5">
                <Label>Empfänger (To)</Label>
                <Input placeholder="admin@example.com" value={emailForm.to} onChange={e => setEmailForm(f => ({ ...f, to: e.target.value }))} />
              </div>
            </div>
            <div className="flex items-center gap-2">
              <input id="useSsl" type="checkbox" className="h-4 w-4 rounded border-border" checked={emailForm.useSsl} onChange={e => setEmailForm(f => ({ ...f, useSsl: e.target.checked }))} />
              <Label htmlFor="useSsl" className="cursor-pointer">SSL direkt (Port 465); unkontrolliert = STARTTLS</Label>
            </div>
            {emailSaveMsg && (
              <div className={`flex items-center gap-2 text-sm ${emailSaveMsg.ok ? "text-emerald-500" : "text-destructive"}`}>
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
              <div className={`flex items-center gap-2 text-sm ${testMsg.ok ? "text-emerald-500" : "text-destructive"}`}>
                {testMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                {testMsg.text}
              </div>
            )}
          </form>
        </CardContent>
      </Card>

      {/* Alert thresholds */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <AlertTriangle className="h-4 w-4" />
            Alert-Schwellwerte
          </CardTitle>
          <CardDescription>Konfiguriere Grenzwerte für automatische E-Mail-Benachrichtigungen.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-end gap-3">
            <div className="space-y-1.5 flex-1">
              <Label>Festplatten-Alert: Freier Speicher unter</Label>
              <div className="flex items-center gap-2">
                <Input
                  type="number"
                  min={1}
                  max={99}
                  value={diskThreshold}
                  onChange={e => setDiskThreshold(Number(e.target.value))}
                  className="w-24"
                />
                <span className="text-sm text-muted-foreground">%</span>
              </div>
            </div>
            <Button onClick={handleSaveAlerts}>Speichern</Button>
          </div>
          {alertSaveMsg && (
            <div className={`flex items-center gap-2 text-sm ${alertSaveMsg.ok ? "text-emerald-500" : "text-destructive"}`}>
              {alertSaveMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
              {alertSaveMsg.text}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Software Blacklist */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <ShieldAlert className="h-4 w-4" />
                Software-Blacklist
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

      {/* Agent Versions */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="flex items-center gap-2 text-base">
                <Tag className="h-4 w-4" />
                Agent-Versionen
              </CardTitle>
              <CardDescription>Verwalte verfügbare Agent-Versionen. Die aktuelle Version wird den Agents beim Check-in gemeldet.</CardDescription>
            </div>
            <Button size="sm" onClick={() => { setVerVersion(""); setVerUrl(""); setVerChangelog(""); setVerIsLatest(true); setVersionDialog(true); }}>
              <Plus className="h-3.5 w-3.5 mr-1.5" />
              Version hinzufügen
            </Button>
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
                    <tr key={v.id} className="border-t border-border/50">
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-2">
                          <span className="font-mono text-xs">{v.version}</span>
                          {v.isLatest && <Badge variant="secondary" className="text-xs">aktuell</Badge>}
                        </div>
                      </td>
                      <td className="px-4 py-2.5 text-muted-foreground text-xs truncate max-w-[200px]">
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
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* User management */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="text-base">Benutzer</CardTitle>
              <CardDescription>Admin-Accounts verwalten.</CardDescription>
            </div>
            <Button size="sm" onClick={() => { setNewUsername(""); setNewPassword(""); setCreateError(""); setCreateDialog(true); }}>
              <UserPlus className="h-3.5 w-3.5 mr-1.5" />
              Neuer Benutzer
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <div className="rounded-md border border-border overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/30">
                  <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Benutzername</th>
                  <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Erstellt</th>
                  <th className="w-24"></th>
                </tr>
              </thead>
              <tbody>
                {userList.map(user => (
                  <tr key={user.id} className="border-t border-border/50">
                    <td className="px-4 py-2.5 font-medium">
                      {user.username}
                      {user.username === currentUsername && (
                        <span className="ml-2 text-xs text-muted-foreground">(du)</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-muted-foreground text-xs">
                      {new Date(user.createdAt).toLocaleDateString("de-DE")}
                    </td>
                    <td className="px-4 py-2.5">
                      <div className="flex gap-1 justify-end">
                        <Button variant="ghost" size="icon" className="h-7 w-7" title="Passwort zurücksetzen"
                          onClick={() => { setResetPw(""); setResetDialog(user); }}>
                          <RefreshCw className="h-3.5 w-3.5" />
                        </Button>
                        <Button variant="ghost" size="icon" className="h-7 w-7 hover:text-destructive"
                          onClick={() => setDeleteConfirm(user)}
                          disabled={user.username === currentUsername}>
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

      {/* Audit Log */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Clock className="h-4 w-4" />
            Audit-Log
          </CardTitle>
          <CardDescription>Protokoll aller administrativen Aktionen.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="relative">
            <Input
              placeholder="Benutzer suchen..."
              value={auditSearch}
              onChange={e => { setAuditSearch(e.target.value); setAuditPage(1); }}
              className="max-w-sm"
            />
          </div>
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

      {/* Dialogs */}
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
    </div>
  );
}
