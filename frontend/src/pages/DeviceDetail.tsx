import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  ArrowLeft, Cpu, Globe, HardDrive, Package, Key, Save, RefreshCw,
  Trash2, Activity, StickyNote, Terminal, Plus, Send, CheckCircle2
} from "lucide-react";
import {
  devices, customers, groups,
  type DeviceDetail as DeviceDetailType,
  type Software, type LicenseInfo, type Customer, type Group,
  type DeviceNote, type DeviceCommand
} from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

function StatusDot({ online }: { online: boolean }) {
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 text-sm",
      online ? "text-emerald-500" : "text-rose-500"
    )}>
      <span className={cn("h-2 w-2 rounded-full", online ? "bg-emerald-500 animate-pulse" : "bg-rose-500")} />
      {online ? "Online" : "Offline"}
    </span>
  );
}

function InfoRow({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <div className="flex justify-between py-2.5 border-b border-border/50 last:border-0">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium">{value ?? "—"}</span>
    </div>
  );
}

const COMMAND_TYPES = [
  { value: "Restart", label: "Neustart" },
  { value: "Shutdown", label: "Herunterfahren" },
  { value: "RunScript", label: "Script ausführen" },
];

const STATUS_COLORS: Record<string, string> = {
  Pending: "bg-amber-500/15 text-amber-600 dark:text-amber-400",
  Sent: "bg-blue-500/15 text-blue-600 dark:text-blue-400",
  Executed: "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400",
  Failed: "bg-rose-500/15 text-rose-600 dark:text-rose-400",
};

export function DeviceDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [device, setDevice] = useState<DeviceDetailType | null>(null);
  const [software, setSoftware] = useState<Software[]>([]);
  const [license, setLicense] = useState<LicenseInfo | null>(null);
  const [notes, setNotes] = useState<DeviceNote[]>([]);
  const [commands, setCommands] = useState<DeviceCommand[]>([]);
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);
  const [licenseLoading, setLicenseLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [softwareSearch, setSoftwareSearch] = useState("");
  const [deleteDialog, setDeleteDialog] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // Edit state
  const [description, setDescription] = useState("");
  const [selectedCustomer, setSelectedCustomer] = useState("none");
  const [selectedGroup, setSelectedGroup] = useState("none");

  // Notes state
  const [newNote, setNewNote] = useState("");
  const [noteLoading, setNoteLoading] = useState(false);

  // Commands state
  const [commandType, setCommandType] = useState("Restart");
  const [commandParams, setCommandParams] = useState("");
  const [commandLoading, setCommandLoading] = useState(false);

  // License expiry state
  const [expiryInput, setExpiryInput] = useState("");
  const [expirySaving, setExpirySaving] = useState(false);

  useEffect(() => {
    if (!id) return;
    Promise.all([
      devices.get(id),
      devices.getSoftware(id),
      customers.list(),
      groups.list(),
      devices.getNotes(id),
      devices.getCommands(id),
    ]).then(([d, sw, cust, grp, n, cmds]) => {
      setDevice(d);
      setSoftware(sw);
      setCustomerList(cust);
      setGroupList(grp);
      setNotes(n);
      setCommands(cmds);
      setDescription(d.description);
      setSelectedCustomer(d.customer?.id ?? "none");
      setSelectedGroup(d.group?.id ?? "none");
      setLoading(false);
    });
    devices.getLicense(id).then(l => {
      setLicense(l);
      setExpiryInput(l.expiresAt ? new Date(l.expiresAt).toISOString().split("T")[0] : "");
    }).catch(() => {});
  }, [id]);

  const handleSave = async () => {
    if (!id) return;
    setSaving(true);
    await devices.patch(id, {
      description,
      customerId: selectedCustomer === "none" ? null : selectedCustomer,
      groupId: selectedGroup === "none" ? null : selectedGroup,
    }).finally(() => setSaving(false));
    const updated = await devices.get(id);
    setDevice(updated);
  };

  const handleDelete = async () => {
    if (!id) return;
    setDeleting(true);
    await devices.delete(id).catch(() => {});
    navigate("/devices");
  };

  const handleRequestLicense = async () => {
    if (!id) return;
    setLicenseLoading(true);
    await devices.requestLicense(id).catch(() => {});
    setLicenseLoading(false);
    const updated = await devices.get(id);
    setDevice(updated);
  };

  const handleFetchLicense = async () => {
    if (!id) return;
    setLicenseLoading(true);
    try {
      const l = await devices.getLicense(id);
      setLicense(l);
      setExpiryInput(l.expiresAt ? new Date(l.expiresAt).toISOString().split("T")[0] : "");
    } catch {}
    setLicenseLoading(false);
  };

  const handleSaveExpiry = async () => {
    if (!id || !license) return;
    setExpirySaving(true);
    await devices.setLicenseExpiry(id, expiryInput || null).catch(() => {});
    const l = await devices.getLicense(id).catch(() => null);
    if (l) setLicense(l);
    setExpirySaving(false);
  };

  const handleAddNote = async () => {
    if (!id || !newNote.trim()) return;
    setNoteLoading(true);
    const note = await devices.addNote(id, newNote.trim()).catch(() => null);
    if (note) setNotes(prev => [note, ...prev]);
    setNewNote("");
    setNoteLoading(false);
  };

  const handleDeleteNote = async (noteId: string) => {
    if (!id) return;
    await devices.deleteNote(id, noteId).catch(() => {});
    setNotes(prev => prev.filter(n => n.id !== noteId));
  };

  const handleIssueCommand = async () => {
    if (!id) return;
    setCommandLoading(true);
    const result = await devices.issueCommand(id, commandType, commandParams || undefined).catch(() => null);
    if (result) {
      const updatedCmds = await devices.getCommands(id).catch(() => commands);
      setCommands(updatedCmds);
    }
    setCommandParams("");
    setCommandLoading(false);
  };

  if (loading) {
    return <div className="flex items-center justify-center h-full text-muted-foreground">Laden...</div>;
  }

  if (!device) return null;

  const networkAdapters = JSON.parse(device.networkAdaptersJson || "[]");
  const filteredSoftware = software.filter(s =>
    s.name.toLowerCase().includes(softwareSearch.toLowerCase()) ||
    s.publisher.toLowerCase().includes(softwareSearch.toLowerCase())
  );

  return (
    <div className="p-6 space-y-5 max-w-5xl">
      {/* Header */}
      <div className="flex items-start gap-4">
        <Button variant="ghost" size="icon" onClick={() => navigate("/devices")} className="mt-0.5">
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div className="flex-1">
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-semibold">{device.hostname}</h1>
            <StatusDot online={device.isOnline} />
          </div>
          {device.description && (
            <p className="text-sm text-muted-foreground mt-0.5">{device.description}</p>
          )}
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="text-muted-foreground hover:text-destructive"
          onClick={() => setDeleteDialog(true)}
        >
          <Trash2 className="h-4 w-4 mr-1.5" />
          Löschen
        </Button>
      </div>

      {/* Edit fields */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-sm font-medium text-muted-foreground uppercase tracking-wider">
            Gerätezuordnung
          </CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="space-y-1.5">
            <Label>Beschreibung</Label>
            <Input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="z.B. Empfang-PC"
            />
          </div>
          <div className="space-y-1.5">
            <Label>Kunde</Label>
            <Select value={selectedCustomer} onValueChange={setSelectedCustomer}>
              <SelectTrigger>
                <SelectValue placeholder="Kein Kunde" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Kein Kunde</SelectItem>
                {customerList.map(c => (
                  <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>Gruppe</Label>
            <Select value={selectedGroup} onValueChange={setSelectedGroup}>
              <SelectTrigger>
                <SelectValue placeholder="Keine Gruppe" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Keine Gruppe</SelectItem>
                {groupList.map(g => (
                  <SelectItem key={g.id} value={g.id}>{g.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="sm:col-span-3 flex justify-end">
            <Button size="sm" onClick={handleSave} disabled={saving}>
              <Save className="h-3.5 w-3.5 mr-1.5" />
              {saving ? "Speichern..." : "Speichern"}
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Tabs */}
      <Tabs defaultValue="hardware">
        <TabsList className="w-full justify-start flex-wrap">
          <TabsTrigger value="hardware"><Cpu className="h-3.5 w-3.5 mr-1.5" />Hardware</TabsTrigger>
          <TabsTrigger value="network"><Globe className="h-3.5 w-3.5 mr-1.5" />Netzwerk</TabsTrigger>
          <TabsTrigger value="disks"><HardDrive className="h-3.5 w-3.5 mr-1.5" />Festplatten</TabsTrigger>
          <TabsTrigger value="software"><Package className="h-3.5 w-3.5 mr-1.5" />Software ({software.length})</TabsTrigger>
          <TabsTrigger value="licenses"><Key className="h-3.5 w-3.5 mr-1.5" />Lizenzen</TabsTrigger>
          <TabsTrigger value="notes">
            <StickyNote className="h-3.5 w-3.5 mr-1.5" />
            Notizen {notes.length > 0 && <Badge variant="secondary" className="ml-1 h-4 px-1 text-xs">{notes.length}</Badge>}
          </TabsTrigger>
          <TabsTrigger value="commands">
            <Terminal className="h-3.5 w-3.5 mr-1.5" />
            Befehle {commands.filter(c => c.status === "Pending" || c.status === "Sent").length > 0 && (
              <Badge variant="secondary" className="ml-1 h-4 px-1 text-xs">
                {commands.filter(c => c.status === "Pending" || c.status === "Sent").length}
              </Badge>
            )}
          </TabsTrigger>
          <TabsTrigger value="history"><Activity className="h-3.5 w-3.5 mr-1.5" />Verlauf</TabsTrigger>
        </TabsList>

        {/* Hardware */}
        <TabsContent value="hardware">
          <Card>
            <CardContent className="pt-6 divide-y divide-border/50">
              <InfoRow label="Hostname" value={device.hostname} />
              <InfoRow label="Windows-Version" value={device.windowsVersion} />
              <InfoRow label="Windows-Build" value={device.windowsBuild} />
              <InfoRow label="Windows-Edition" value={device.windowsEdition} />
              <InfoRow label="Lizenztyp" value={device.licenseType} />
              <InfoRow label="CPU" value={device.cpuModel} />
              <InfoRow label="CPU-Kerne" value={device.cpuCores || undefined} />
              <InfoRow label="RAM gesamt" value={device.ramTotalGB ? `${device.ramTotalGB} GB` : undefined} />
              <InfoRow label="Zuletzt gesehen" value={device.lastSeenAt ? new Date(device.lastSeenAt).toLocaleString("de-DE") : undefined} />
              <InfoRow label="Registriert am" value={new Date(device.createdAt).toLocaleString("de-DE")} />
            </CardContent>
          </Card>
        </TabsContent>

        {/* Network */}
        <TabsContent value="network">
          <Card>
            <CardContent className="pt-6">
              {networkAdapters.length === 0 ? (
                <p className="text-sm text-muted-foreground">Keine Netzwerkadapter gefunden.</p>
              ) : (
                <div className="space-y-4">
                  {networkAdapters.map((adapter: any, i: number) => (
                    <div key={i} className="rounded-md border border-border p-4">
                      <div className="font-medium text-sm mb-2">{adapter.name}</div>
                      <div className="grid grid-cols-2 gap-1 text-sm">
                        <span className="text-muted-foreground">IP-Adresse</span>
                        <span>{adapter.ipAddress || "—"}</span>
                        <span className="text-muted-foreground">MAC-Adresse</span>
                        <span className="font-mono text-xs">{adapter.macAddress || "—"}</span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Disks */}
        <TabsContent value="disks">
          <Card>
            <CardContent className="pt-6">
              {device.recentCheckins.length === 0 ? (
                <p className="text-sm text-muted-foreground">Noch kein Check-in empfangen.</p>
              ) : (() => {
                const latest = device.recentCheckins[0];
                const disks = JSON.parse(latest.diskDrivesJson || "[]");
                return (
                  <div className="space-y-3">
                    {disks.map((disk: any, i: number) => {
                      const used = disk.totalGB - disk.freeGB;
                      const pct = disk.totalGB > 0 ? (used / disk.totalGB) * 100 : 0;
                      return (
                        <div key={i} className="rounded-md border border-border p-4">
                          <div className="flex justify-between text-sm mb-2">
                            <span className="font-medium">{disk.drive}</span>
                            <span className="text-muted-foreground">
                              {used.toFixed(1)} / {disk.totalGB.toFixed(1)} GB
                            </span>
                          </div>
                          <div className="h-2 rounded-full bg-muted overflow-hidden">
                            <div
                              className={cn(
                                "h-full rounded-full transition-all",
                                pct > 90 ? "bg-destructive" : pct > 70 ? "bg-amber-500" : "bg-primary"
                              )}
                              style={{ width: `${pct}%` }}
                            />
                          </div>
                          <div className="text-xs text-muted-foreground mt-1">{pct.toFixed(1)}% belegt</div>
                        </div>
                      );
                    })}
                  </div>
                );
              })()}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Software */}
        <TabsContent value="software">
          <Card>
            <CardContent className="pt-6 space-y-3">
              <div className="relative">
                <Package className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
                <Input
                  placeholder="Software suchen..."
                  value={softwareSearch}
                  onChange={(e) => setSoftwareSearch(e.target.value)}
                  className="pl-9"
                />
              </div>
              <div className="rounded-md border border-border overflow-hidden max-h-96 overflow-y-auto">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-muted/50">
                    <tr>
                      <th className="text-left px-3 py-2 font-medium text-muted-foreground">Name</th>
                      <th className="text-left px-3 py-2 font-medium text-muted-foreground">Version</th>
                      <th className="text-left px-3 py-2 font-medium text-muted-foreground">Hersteller</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredSoftware.map((sw) => (
                      <tr key={sw.id} className="border-t border-border/50">
                        <td className="px-3 py-2">{sw.name}</td>
                        <td className="px-3 py-2 text-muted-foreground font-mono text-xs">{sw.version}</td>
                        <td className="px-3 py-2 text-muted-foreground">{sw.publisher}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {filteredSoftware.length === 0 && (
                  <p className="text-sm text-muted-foreground text-center py-8">Keine Software gefunden.</p>
                )}
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        {/* Licenses */}
        <TabsContent value="licenses">
          <Card>
            <CardContent className="pt-6 space-y-4">
              <div className="flex items-center gap-3">
                {!license && (
                  <Button
                    onClick={handleRequestLicense}
                    disabled={licenseLoading || device.licenseRequested}
                    size="sm"
                  >
                    <Key className="h-3.5 w-3.5 mr-1.5" />
                    {device.licenseRequested ? "Anfrage ausstehend..." : "Keys abrufen"}
                  </Button>
                )}
                {device.licenseRequested && !license && (
                  <Button variant="outline" size="sm" onClick={handleFetchLicense} disabled={licenseLoading}>
                    <RefreshCw className="h-3.5 w-3.5 mr-1.5" />
                    Aktualisieren
                  </Button>
                )}
              </div>

              {device.licenseRequested && !license && (
                <div className="rounded-md bg-amber-500/10 border border-amber-500/20 px-4 py-3 text-sm text-amber-500">
                  Warte auf Antwort des Agents beim nächsten Check-in...
                </div>
              )}

              {license ? (
                <div className="space-y-3">
                  <div className="rounded-md border border-border p-4 space-y-3">
                    <h3 className="text-sm font-medium">Windows-Lizenz</h3>
                    <div className="grid grid-cols-2 gap-2 text-sm">
                      <span className="text-muted-foreground">Produktkey</span>
                      <span className="font-mono text-xs bg-muted px-2 py-1 rounded">
                        {license.windowsKey || "Nicht verfügbar"}
                      </span>
                      <span className="text-muted-foreground">Lizenztyp</span>
                      <span>{license.licenseType || "—"}</span>
                    </div>
                  </div>

                  {(license.officeKey || license.officeVersion) && (
                    <div className="rounded-md border border-border p-4 space-y-3">
                      <h3 className="text-sm font-medium">Microsoft Office</h3>
                      <div className="grid grid-cols-2 gap-2 text-sm">
                        <span className="text-muted-foreground">Produktkey</span>
                        <span className="font-mono text-xs bg-muted px-2 py-1 rounded">
                          {license.officeKey || "Nicht verfügbar"}
                        </span>
                        <span className="text-muted-foreground">Version</span>
                        <span>{license.officeVersion || "—"}</span>
                      </div>
                    </div>
                  )}

                  {/* Expiry */}
                  <div className="rounded-md border border-border p-4 space-y-3">
                    <h3 className="text-sm font-medium">Ablaufdatum</h3>
                    {license.expiresAt && new Date(license.expiresAt) <= new Date() && (
                      <div className="text-xs text-rose-500 font-medium">Lizenz ist abgelaufen!</div>
                    )}
                    {license.expiresAt && new Date(license.expiresAt) > new Date() &&
                      new Date(license.expiresAt) <= new Date(Date.now() + 30 * 24 * 60 * 60 * 1000) && (
                      <div className="text-xs text-amber-500 font-medium">
                        Läuft ab am {new Date(license.expiresAt).toLocaleDateString("de-DE")}
                      </div>
                    )}
                    <div className="flex items-center gap-2">
                      <Input
                        type="date"
                        value={expiryInput}
                        onChange={e => setExpiryInput(e.target.value)}
                        className="w-44"
                      />
                      <Button size="sm" variant="outline" onClick={handleSaveExpiry} disabled={expirySaving}>
                        {expirySaving ? "..." : "Speichern"}
                      </Button>
                      {expiryInput && (
                        <Button size="sm" variant="ghost" onClick={() => { setExpiryInput(""); devices.setLicenseExpiry(id!, null); }}>
                          Löschen
                        </Button>
                      )}
                    </div>
                  </div>

                  <p className="text-xs text-muted-foreground">
                    Abgerufen: {new Date(license.fetchedAt).toLocaleString("de-DE")}
                  </p>
                </div>
              ) : !device.licenseRequested && (
                <p className="text-sm text-muted-foreground">
                  Noch keine Lizenzinformationen verfügbar. Klicke auf "Keys abrufen" um die Anforderung zu senden.
                </p>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Notes */}
        <TabsContent value="notes">
          <Card>
            <CardContent className="pt-6 space-y-4">
              <div className="flex gap-2">
                <Textarea
                  placeholder="Neue Notiz hinzufügen..."
                  value={newNote}
                  onChange={e => setNewNote(e.target.value)}
                  className="min-h-[80px] resize-none"
                  onKeyDown={e => {
                    if (e.key === "Enter" && e.ctrlKey) handleAddNote();
                  }}
                />
                <Button
                  size="sm"
                  onClick={handleAddNote}
                  disabled={noteLoading || !newNote.trim()}
                  className="self-end"
                >
                  <Send className="h-3.5 w-3.5" />
                </Button>
              </div>
              <p className="text-xs text-muted-foreground">Strg+Enter zum Senden</p>

              {notes.length === 0 ? (
                <p className="text-sm text-muted-foreground">Noch keine Notizen vorhanden.</p>
              ) : (
                <div className="space-y-3">
                  {notes.map(note => (
                    <div key={note.id} className="rounded-md border border-border p-4">
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center gap-2">
                          <span className="text-xs font-medium">{note.authorUsername}</span>
                          <span className="text-xs text-muted-foreground">
                            {new Date(note.createdAt).toLocaleString("de-DE")}
                          </span>
                        </div>
                        <Button
                          variant="ghost"
                          size="icon"
                          className="h-6 w-6 hover:text-destructive"
                          onClick={() => handleDeleteNote(note.id)}
                        >
                          <Trash2 className="h-3 w-3" />
                        </Button>
                      </div>
                      <p className="text-sm whitespace-pre-wrap">{note.content}</p>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* Commands */}
        <TabsContent value="commands">
          <Card>
            <CardContent className="pt-6 space-y-4">
              {/* Issue command */}
              <div className="rounded-md border border-border p-4 space-y-3">
                <h3 className="text-sm font-medium flex items-center gap-2">
                  <Plus className="h-3.5 w-3.5" />
                  Befehl senden
                </h3>
                <div className="flex gap-2 items-end">
                  <div className="space-y-1.5 flex-1">
                    <Label>Befehlstyp</Label>
                    <Select value={commandType} onValueChange={setCommandType}>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {COMMAND_TYPES.map(ct => (
                          <SelectItem key={ct.value} value={ct.value}>{ct.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  {commandType === "RunScript" && (
                    <div className="space-y-1.5 flex-2">
                      <Label>Parameter / Script</Label>
                      <Input
                        value={commandParams}
                        onChange={e => setCommandParams(e.target.value)}
                        placeholder="Script-Inhalt oder Pfad"
                      />
                    </div>
                  )}
                  <Button
                    onClick={handleIssueCommand}
                    disabled={commandLoading}
                    className="shrink-0"
                  >
                    <Send className="h-3.5 w-3.5 mr-1.5" />
                    {commandLoading ? "Senden..." : "Senden"}
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">
                  Der Befehl wird beim nächsten Check-in des Agents ausgeführt.
                </p>
              </div>

              {/* Command history */}
              {commands.length === 0 ? (
                <p className="text-sm text-muted-foreground">Noch keine Befehle gesendet.</p>
              ) : (
                <div className="rounded-md border border-border overflow-hidden">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-border bg-muted/30">
                        <th className="text-left px-3 py-2.5 font-medium text-muted-foreground">Befehl</th>
                        <th className="text-left px-3 py-2.5 font-medium text-muted-foreground">Status</th>
                        <th className="text-left px-3 py-2.5 font-medium text-muted-foreground">Gesendet von</th>
                        <th className="text-left px-3 py-2.5 font-medium text-muted-foreground">Zeitpunkt</th>
                        <th className="text-left px-3 py-2.5 font-medium text-muted-foreground">Ergebnis</th>
                      </tr>
                    </thead>
                    <tbody>
                      {commands.map(cmd => (
                        <tr key={cmd.id} className="border-t border-border/50">
                          <td className="px-3 py-2.5 font-medium">{cmd.commandType}</td>
                          <td className="px-3 py-2.5">
                            <span className={cn("text-xs px-2 py-0.5 rounded-full font-medium", STATUS_COLORS[cmd.status] ?? "bg-muted text-muted-foreground")}>
                              {cmd.status}
                            </span>
                          </td>
                          <td className="px-3 py-2.5 text-muted-foreground">{cmd.issuedByUsername}</td>
                          <td className="px-3 py-2.5 text-xs text-muted-foreground">
                            {new Date(cmd.createdAt).toLocaleString("de-DE")}
                          </td>
                          <td className="px-3 py-2.5 text-xs text-muted-foreground max-w-[200px] truncate">
                            {cmd.result || (cmd.status === "Executed" ? <CheckCircle2 className="h-3.5 w-3.5 text-emerald-500" /> : "—")}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>

        {/* History */}
        <TabsContent value="history">
          <Card>
            <CardContent className="pt-6">
              {device.recentCheckins.length === 0 ? (
                <p className="text-sm text-muted-foreground">Noch keine Check-in-Daten vorhanden.</p>
              ) : (
                <div className="space-y-4">
                  <div>
                    <p className="text-xs text-muted-foreground mb-2">RAM-Auslastung (letzte Check-ins)</p>
                    <div className="flex items-end gap-0.5 h-16">
                      {[...device.recentCheckins].reverse().map((c, i) => {
                        const pct = device.ramTotalGB > 0 ? (c.ramUsedGB / device.ramTotalGB) * 100 : 0;
                        return (
                          <div
                            key={i}
                            title={`${c.ramUsedGB.toFixed(1)} / ${device.ramTotalGB} GB — ${new Date(c.checkedInAt).toLocaleString("de-DE")}`}
                            className={cn(
                              "flex-1 min-w-[4px] rounded-sm transition-all",
                              pct > 90 ? "bg-destructive" : pct > 70 ? "bg-amber-500" : "bg-primary"
                            )}
                            style={{ height: `${Math.max(4, pct)}%` }}
                          />
                        );
                      })}
                    </div>
                  </div>

                  <div className="rounded-md border border-border overflow-hidden max-h-72 overflow-y-auto">
                    <table className="w-full text-sm">
                      <thead className="sticky top-0 bg-muted/50">
                        <tr>
                          <th className="text-left px-3 py-2 font-medium text-muted-foreground">Zeitpunkt</th>
                          <th className="text-left px-3 py-2 font-medium text-muted-foreground">RAM belegt</th>
                          <th className="text-left px-3 py-2 font-medium text-muted-foreground">Festplatten</th>
                        </tr>
                      </thead>
                      <tbody>
                        {device.recentCheckins.map((c, i) => {
                          const disks: Array<{ drive: string; freeGB: number; totalGB: number }> = JSON.parse(c.diskDrivesJson || "[]");
                          return (
                            <tr key={i} className="border-t border-border/50">
                              <td className="px-3 py-2 text-xs text-muted-foreground">
                                {new Date(c.checkedInAt).toLocaleString("de-DE")}
                              </td>
                              <td className="px-3 py-2">
                                {c.ramUsedGB.toFixed(1)} / {device.ramTotalGB} GB
                              </td>
                              <td className="px-3 py-2 text-xs text-muted-foreground">
                                {disks.map(d => `${d.drive} ${(d.totalGB - d.freeGB).toFixed(0)}/${d.totalGB.toFixed(0)}GB`).join(" · ")}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>

      {/* Delete confirmation dialog */}
      <Dialog open={deleteDialog} onOpenChange={setDeleteDialog}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Gerät löschen</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Soll <strong className="text-foreground">{device.hostname}</strong> wirklich gelöscht werden?
            Alle zugehörigen Daten (Check-ins, Software, Lizenzen) werden unwiderruflich entfernt.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteDialog(false)}>Abbrechen</Button>
            <Button variant="destructive" onClick={handleDelete} disabled={deleting}>
              {deleting ? "Wird gelöscht..." : "Löschen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
