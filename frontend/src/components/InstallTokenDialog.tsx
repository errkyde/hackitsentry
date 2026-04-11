import { useEffect, useState } from "react";
import { Download, Trash2, Plus, CheckCircle2, Link, Mail, Send } from "lucide-react";
import { installTokens, type InstallToken } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

interface Props {
  open: boolean;
  onClose: () => void;
}

function getDownloadUrl(token: string) {
  return `${window.location.origin}/install/${token}`;
}

export function InstallTokenDialog({ open, onClose }: Props) {
  const [tokens, setTokens] = useState<InstallToken[]>([]);
  const [expiry, setExpiry] = useState(24);
  const [loading, setLoading] = useState(false);
  const [copied, setCopied] = useState<string | null>(null);
  const [emailInputs, setEmailInputs] = useState<Record<string, string>>({});
  const [emailSent, setEmailSent] = useState<string | null>(null);
  const [emailError, setEmailError] = useState<string | null>(null);

  useEffect(() => {
    if (open) installTokens.list().then(setTokens).catch(() => {});
  }, [open]);

  const handleCreate = async () => {
    setLoading(true);
    try {
      const t = await installTokens.create(expiry);
      setTokens(prev => [t, ...prev]);
    } catch {}
    setLoading(false);
  };

  const handleDelete = async (id: string) => {
    await installTokens.delete(id).catch(() => {});
    setTokens(prev => prev.filter(t => t.id !== id));
  };

  const copy = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopied(id);
    setTimeout(() => setCopied(null), 2000);
  };

  const handleSendEmail = async (t: InstallToken) => {
    const email = emailInputs[t.id]?.trim();
    if (!email) return;
    setEmailError(null);
    try {
      await installTokens.sendEmail(t.id, email);
      setEmailSent(t.id);
      setEmailInputs(prev => ({ ...prev, [t.id]: "" }));
      setTimeout(() => setEmailSent(null), 3000);
    } catch (err: any) {
      setEmailError(err.message || "Fehler beim Senden");
    }
  };

  const activeCount = tokens.filter(t => !t.used && !t.expired).length;

  return (
    <Dialog open={open} onOpenChange={v => !v && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Link className="h-4 w-4" />
            Installationslinks
          </DialogTitle>
        </DialogHeader>

        {/* Create row */}
        <div className="flex items-center gap-2">
          <select
            value={expiry}
            onChange={e => setExpiry(Number(e.target.value))}
            className="h-9 flex-1 rounded-md border border-input bg-background px-3 text-sm"
          >
            <option value={1}>1 Stunde</option>
            <option value={6}>6 Stunden</option>
            <option value={24}>24 Stunden</option>
            <option value={72}>3 Tage</option>
            <option value={168}>7 Tage</option>
          </select>
          <Button size="sm" onClick={handleCreate} disabled={loading}>
            <Plus className="h-4 w-4 mr-1" />
            Link erstellen
          </Button>
        </div>

        {/* Token list */}
        <div className="space-y-2 max-h-80 overflow-y-auto">
          {tokens.length === 0 && (
            <p className="text-sm text-muted-foreground text-center py-6">
              Noch keine Links erstellt.
            </p>
          )}
          {tokens.map(t => {
            const url = getDownloadUrl(t.token);
            const inactive = t.used || t.expired;
            return (
              <div key={t.id} className={`rounded-md border text-sm ${inactive ? "opacity-50" : ""}`}>
                <div className="flex items-center gap-2 p-3">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-1.5 mb-0.5">
                      {t.used && <Badge variant="secondary" className="text-xs">Verwendet</Badge>}
                      {!t.used && t.expired && <Badge variant="destructive" className="text-xs">Abgelaufen</Badge>}
                      {!t.used && !t.expired && <Badge variant="outline" className="text-xs text-green-600 border-green-600">Aktiv</Badge>}
                      <span className="font-mono text-xs text-muted-foreground truncate">{t.token}</span>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      Von <strong>{t.createdByUsername}</strong> · {new Date(t.expiresAt).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })}
                      {t.used && t.usedAt && ` · Verwendet: ${new Date(t.usedAt).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })}`}
                    </div>
                  </div>
                  {!inactive && (
                    <Button size="icon" variant="ghost" className="h-7 w-7 shrink-0" title="Link kopieren" onClick={() => copy(url, t.id)}>
                      {copied === t.id ? <CheckCircle2 className="h-4 w-4 text-green-600" /> : <Download className="h-4 w-4" />}
                    </Button>
                  )}
                  <Button size="icon" variant="ghost" className="h-7 w-7 shrink-0 text-destructive hover:text-destructive" onClick={() => handleDelete(t.id)}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
                {!inactive && (
                  <div className="flex gap-1.5 px-3 pb-3">
                    <Input
                      type="email"
                      placeholder="Per E-Mail versenden..."
                      className="h-7 text-xs"
                      value={emailInputs[t.id] ?? ""}
                      onChange={e => setEmailInputs(prev => ({ ...prev, [t.id]: e.target.value }))}
                      onKeyDown={e => e.key === "Enter" && handleSendEmail(t)}
                    />
                    <Button size="sm" variant="outline" className="h-7 px-2 shrink-0" onClick={() => handleSendEmail(t)}>
                      {emailSent === t.id ? <CheckCircle2 className="h-3.5 w-3.5 text-green-600" /> : <Send className="h-3.5 w-3.5" />}
                    </Button>
                  </div>
                )}
              </div>
            );
          })}
        </div>

        {emailError && <p className="text-xs text-destructive">{emailError}</p>}
        <p className="text-xs text-muted-foreground">
          {activeCount} aktiver Link{activeCount !== 1 ? "s" : ""} · Link kopieren oder per E-Mail versenden
        </p>
      </DialogContent>
    </Dialog>
  );
}

export function useInstallTokenCount() {
  const [count, setCount] = useState(0);
  useEffect(() => {
    installTokens.list()
      .then(ts => setCount(ts.filter(t => !t.used && !t.expired).length))
      .catch(() => {});
  }, []);
  return count;
}
