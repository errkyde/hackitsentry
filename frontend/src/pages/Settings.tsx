import { useEffect, useState } from "react";
import { KeyRound, UserPlus, Trash2, RefreshCw, Mail, Send, CheckCircle2, XCircle } from "lucide-react";
import { auth, users, settings, type AppUser, type EmailSettingsInput } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";

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

  useEffect(() => {
    settings.getEmail().then(data => {
      setEmailForm(f => ({
        ...f,
        host: data.host,
        port: data.port,
        username: data.username,
        from: data.from,
        to: data.to,
        useSsl: data.useSsl,
        password: "",
      }));
      setEmailHasPassword(data.hasPassword);
    }).catch(() => {});
  }, []);

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

  const fetchUsers = async () => {
    const data = await users.list();
    setUserList(data);
  };

  useEffect(() => { fetchUsers(); }, []);

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

  const handleDelete = async (user: AppUser) => {
    await users.delete(user.id).catch(() => {});
    setDeleteConfirm(null);
    await fetchUsers();
  };

  return (
    <div className="p-6 max-w-2xl space-y-6">
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
            {pwSuccess && <p className="text-sm text-emerald-400">Passwort erfolgreich geändert.</p>}
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
            Leer lassen um Benachrichtigungen zu deaktivieren.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSaveEmail} className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>SMTP Host</Label>
                <Input
                  placeholder="smtp.example.com"
                  value={emailForm.host}
                  onChange={e => setEmailForm(f => ({ ...f, host: e.target.value }))}
                />
              </div>
              <div className="space-y-1.5">
                <Label>Port</Label>
                <Input
                  type="number"
                  value={emailForm.port}
                  onChange={e => setEmailForm(f => ({ ...f, port: Number(e.target.value) }))}
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>Benutzername</Label>
                <Input
                  placeholder="user@example.com"
                  value={emailForm.username}
                  onChange={e => setEmailForm(f => ({ ...f, username: e.target.value }))}
                  autoComplete="off"
                />
              </div>
              <div className="space-y-1.5">
                <Label>Passwort {emailHasPassword && <span className="text-xs text-muted-foreground">(gesetzt)</span>}</Label>
                <Input
                  type="password"
                  placeholder={emailHasPassword ? "Leer lassen um beizubehalten" : ""}
                  value={emailForm.password}
                  onChange={e => setEmailForm(f => ({ ...f, password: e.target.value }))}
                  autoComplete="new-password"
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>Absender (From)</Label>
                <Input
                  placeholder="sentry@example.com"
                  value={emailForm.from}
                  onChange={e => setEmailForm(f => ({ ...f, from: e.target.value }))}
                />
              </div>
              <div className="space-y-1.5">
                <Label>Empfänger (To)</Label>
                <Input
                  placeholder="admin@example.com"
                  value={emailForm.to}
                  onChange={e => setEmailForm(f => ({ ...f, to: e.target.value }))}
                />
              </div>
            </div>
            <div className="flex items-center gap-2">
              <input
                id="useSsl"
                type="checkbox"
                className="h-4 w-4 rounded border-border"
                checked={emailForm.useSsl}
                onChange={e => setEmailForm(f => ({ ...f, useSsl: e.target.checked }))}
              />
              <Label htmlFor="useSsl" className="cursor-pointer">SSL direkt (Port 465); unkontrolliert = STARTTLS</Label>
            </div>

            {emailSaveMsg && (
              <div className={`flex items-center gap-2 text-sm ${emailSaveMsg.ok ? "text-emerald-400" : "text-destructive"}`}>
                {emailSaveMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                {emailSaveMsg.text}
              </div>
            )}

            <div className="flex gap-2">
              <Button type="submit" disabled={emailLoading}>
                {emailLoading ? "Speichern..." : "Speichern"}
              </Button>
              <Button type="button" variant="outline" onClick={handleTestEmail} disabled={testLoading}>
                <Send className="h-3.5 w-3.5 mr-1.5" />
                {testLoading ? "Wird gesendet..." : "Test-E-Mail"}
              </Button>
            </div>
            {testMsg && (
              <div className={`flex items-center gap-2 text-sm ${testMsg.ok ? "text-emerald-400" : "text-destructive"}`}>
                {testMsg.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                {testMsg.text}
              </div>
            )}
          </form>
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

      {/* Create user dialog */}
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

      {/* Reset password dialog */}
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

      {/* Delete confirm */}
      <Dialog open={!!deleteConfirm} onOpenChange={open => !open && setDeleteConfirm(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Benutzer löschen</DialogTitle></DialogHeader>
          <p className="text-sm text-muted-foreground">
            Benutzer <strong className="text-foreground">{deleteConfirm?.username}</strong> wirklich löschen?
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteConfirm(null)}>Abbrechen</Button>
            <Button variant="destructive" onClick={() => deleteConfirm && handleDelete(deleteConfirm)}>Löschen</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
