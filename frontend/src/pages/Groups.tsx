import { useEffect, useState } from "react";
import { Plus, Pencil, Trash2, Layers, Monitor, Bell, BellOff } from "lucide-react";
import { groups, type Group } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import { RustDeskOptionsDialog } from "@/components/RustDeskOptionsDialog";
import { TriToggle } from "@/components/TriToggle";
import { toast } from "@/lib/useToast";

const PRESET_COLORS = [
  "#3b82f6", "#8b5cf6", "#ec4899", "#ef4444",
  "#f97316", "#eab308", "#22c55e", "#14b8a6",
];

type FormState = { name: string; description: string; color: string };

type NotifState = {
  alertOnOffline: boolean | null;
  alertOnOnline: boolean | null;
  alertOnSoftwareAlert: boolean | null;
  alertOnDiskFull: boolean | null;
  offlineAlertDelayMinutes: number | null;
};

const DEFAULT_NOTIF: NotifState = {
  alertOnOffline: true,
  alertOnOnline: false,
  alertOnSoftwareAlert: true,
  alertOnDiskFull: true,
  offlineAlertDelayMinutes: null,
};

export function Groups() {
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);
  const [dialog, setDialog] = useState<{ mode: "create" | "edit"; group?: Group } | null>(null);
  const [form, setForm] = useState<FormState>({ name: "", description: "", color: PRESET_COLORS[0] });
  const [saving, setSaving] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<Group | null>(null);

  // RustDesk sync state
  const [rdGroup, setRdGroup] = useState<Group | null>(null);
  const [rdOptions, setRdOptions] = useState<Record<string, string>>({});
  const [rdSaving, setRdSaving] = useState(false);
  const [rdSaved, setRdSaved] = useState(false);

  // Notification sync state
  const [notifGroup, setNotifGroup] = useState<Group | null>(null);
  const [notif, setNotif] = useState<NotifState>(DEFAULT_NOTIF);
  const [notifSaving, setNotifSaving] = useState(false);

  const fetchGroups = async () => {
    const data = await groups.list();
    setGroupList(data);
  };

  useEffect(() => {
    fetchGroups().finally(() => setLoading(false));
  }, []);

  const openCreate = () => {
    setForm({ name: "", description: "", color: PRESET_COLORS[0] });
    setDialog({ mode: "create" });
  };

  const openEdit = (group: Group) => {
    setForm({ name: group.name, description: group.description, color: group.color ?? PRESET_COLORS[0] });
    setDialog({ mode: "edit", group });
  };

  const handleSave = async (andSync = false) => {
    setSaving(true);
    try {
      let savedGroupId: string | undefined;
      if (dialog?.mode === "create") {
        const res = await groups.create(form);
        savedGroupId = res.id;
      } else if (dialog?.group) {
        await groups.update(dialog.group.id, form);
        savedGroupId = dialog.group.id;
      }
      if (andSync && savedGroupId) {
        const result = await groups.syncRustDesk(savedGroupId, null);
        toast({ title: "RustDesk synchronisiert", description: `${result.updated} Gerät${result.updated !== 1 ? "e" : ""} aktualisiert.` });
      } else {
        toast({ title: "Gespeichert", description: `Gruppe „${form.name}" wurde gespeichert.` });
      }
      setDialog(null);
      await fetchGroups();
    } catch {
      toast({ title: "Fehler", description: "Speichern fehlgeschlagen.", variant: "warning" });
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (group: Group) => {
    await groups.delete(group.id);
    setDeleteConfirm(null);
    await fetchGroups();
  };

  const openRustDesk = (group: Group) => {
    setRdOptions({});
    setRdSaved(false);
    setRdGroup(group);
  };

  const handleSaveRustDesk = async () => {
    if (!rdGroup) return;
    setRdSaving(true);
    try {
      const options = Object.keys(rdOptions).length > 0 ? rdOptions : null;
      const result = await groups.syncRustDesk(rdGroup.id, options);
      setRdSaved(true);
      toast({ title: "RustDesk synchronisiert", description: `${result.updated} Gerät${result.updated !== 1 ? "e" : ""} aktualisiert.` });
      setTimeout(() => { setRdSaved(false); setRdGroup(null); }, 1200);
    } catch {
      toast({ title: "Fehler", description: "Synchronisierung fehlgeschlagen.", variant: "warning" });
    } finally {
      setRdSaving(false);
    }
  };

  const openNotif = (group: Group) => {
    let loaded = DEFAULT_NOTIF;
    if (group.notificationSettingsJson) {
      try {
        const parsed = JSON.parse(group.notificationSettingsJson);
        loaded = {
          alertOnOffline: parsed.alertOnOffline ?? parsed.AlertOnOffline ?? DEFAULT_NOTIF.alertOnOffline,
          alertOnOnline: parsed.alertOnOnline ?? parsed.AlertOnOnline ?? DEFAULT_NOTIF.alertOnOnline,
          alertOnSoftwareAlert: parsed.alertOnSoftwareAlert ?? parsed.AlertOnSoftwareAlert ?? DEFAULT_NOTIF.alertOnSoftwareAlert,
          alertOnDiskFull: parsed.alertOnDiskFull ?? parsed.AlertOnDiskFull ?? DEFAULT_NOTIF.alertOnDiskFull,
          offlineAlertDelayMinutes: parsed.offlineAlertDelayMinutes ?? parsed.OfflineAlertDelayMinutes ?? null,
        };
      } catch { /* ignore */ }
    }
    setNotif(loaded);
    setNotifGroup(group);
  };

  const handleSaveNotif = async () => {
    if (!notifGroup) return;
    setNotifSaving(true);
    try {
      const result = await groups.syncNotifications(notifGroup.id, {
        ...notif,
        offlineAlertDelayMinutes: notif.offlineAlertDelayMinutes,
      });
      toast({ title: "Benachrichtigungen synchronisiert", description: `${result.updated} Gerät${result.updated !== 1 ? "e" : ""} aktualisiert.` });
      setNotifGroup(null);
    } catch {
      toast({ title: "Fehler", description: "Synchronisierung fehlgeschlagen.", variant: "warning" });
    } finally {
      setNotifSaving(false);
    }
  };

  const handleClearNotif = async () => {
    if (!notifGroup) return;
    setNotifSaving(true);
    try {
      const result = await groups.clearNotifications(notifGroup.id);
      toast({ title: "Benachrichtigungen zurückgesetzt", description: `${result.removed} Override${result.removed !== 1 ? "s" : ""} entfernt.` });
      setNotifGroup(null);
    } catch {
      toast({ title: "Fehler", description: "Reset fehlgeschlagen.", variant: "warning" });
    } finally {
      setNotifSaving(false);
    }
  };

  return (
    <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Gruppen</h1>
          <p className="text-sm text-muted-foreground">{groupList.length} Gruppen</p>
        </div>
        <Button size="sm" onClick={openCreate}>
          <Plus className="h-4 w-4 mr-1.5" />
          Neue Gruppe
        </Button>
      </div>

      {loading ? (
        <p className="text-muted-foreground">Laden...</p>
      ) : groupList.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <Layers className="h-10 w-10 text-muted-foreground/30 mb-3" />
          <p className="text-muted-foreground">Noch keine Gruppen erstellt</p>
          <Button size="sm" variant="outline" className="mt-4" onClick={openCreate}>
            <Plus className="h-3.5 w-3.5 mr-1.5" />
            Erste Gruppe erstellen
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {groupList.map((group) => (
            <Card key={group.id}>
              <CardHeader className="pb-2">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2.5">
                    {group.color && (
                      <div
                        className="h-3 w-3 rounded-full flex-shrink-0"
                        style={{ backgroundColor: group.color }}
                      />
                    )}
                    <CardTitle className="text-base">{group.name}</CardTitle>
                  </div>
                  <div className="flex gap-1">
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(group)} title="Bearbeiten">
                      <Pencil className="h-3.5 w-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 hover:text-destructive"
                      onClick={() => setDeleteConfirm(group)}
                      title="Löschen"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                {group.description && (
                  <p className="text-sm text-muted-foreground mb-3">{group.description}</p>
                )}
                <p className="text-xs text-muted-foreground mb-2">
                  {group.deviceCount} Gerät{group.deviceCount !== 1 ? "e" : ""}
                </p>
                {group.notificationSettingsJson && (() => {
                  try {
                    const s = JSON.parse(group.notificationSettingsJson);
                    const active = [
                      (s.alertOnOffline ?? s.AlertOnOffline) === true && "Offline",
                      (s.alertOnOnline ?? s.AlertOnOnline) === true && "Online",
                      (s.alertOnSoftwareAlert ?? s.AlertOnSoftwareAlert) === true && "Software",
                      (s.alertOnDiskFull ?? s.AlertOnDiskFull) === true && "Disk",
                    ].filter(Boolean) as string[];
                    if (active.length === 0) return null;
                    return (
                      <div className="flex gap-1 flex-wrap mb-3">
                        {active.map(label => (
                          <span key={label} className="text-[10px] px-1.5 py-0.5 rounded-full bg-amber-500/15 text-amber-600 dark:text-amber-400 font-medium">
                            {label}
                          </span>
                        ))}
                      </div>
                    );
                  } catch { return null; }
                })()}
                <div className="flex gap-2 flex-wrap">
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-7 text-xs"
                    onClick={() => openRustDesk(group)}
                    title="RustDesk-Optionen auf alle Geräte dieser Gruppe anwenden"
                  >
                    <Monitor className="h-3.5 w-3.5 mr-1" />
                    RustDesk sync
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-7 text-xs"
                    onClick={() => openNotif(group)}
                    title="Benachrichtigungen für alle Geräte dieser Gruppe anpassen"
                  >
                    <Bell className="h-3.5 w-3.5 mr-1" />
                    Benachrichtigungen
                  </Button>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {/* Create/Edit Dialog */}
      <Dialog open={!!dialog} onOpenChange={(open) => !open && setDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>{dialog?.mode === "create" ? "Neue Gruppe" : "Gruppe bearbeiten"}</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Name</Label>
              <Input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="z.B. Systemadmin"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Beschreibung</Label>
              <Input
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
                placeholder="Optionale Beschreibung"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Farbe</Label>
              <div className="flex gap-2 flex-wrap">
                {PRESET_COLORS.map((color) => (
                  <button
                    key={color}
                    type="button"
                    className="h-7 w-7 rounded-full transition-transform hover:scale-110 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
                    style={{ backgroundColor: color, outline: form.color === color ? `2px solid ${color}` : "none", outlineOffset: "2px" }}
                    onClick={() => setForm({ ...form, color })}
                  />
                ))}
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDialog(null)}>Abbrechen</Button>
            {dialog?.mode === "edit" && (
              <Button variant="outline" onClick={() => handleSave(true)} disabled={saving || !form.name.trim()}>
                <Monitor className="h-3.5 w-3.5 mr-1.5" />
                {saving ? "..." : "Speichern & Sync"}
              </Button>
            )}
            <Button onClick={() => handleSave(false)} disabled={saving || !form.name.trim()}>
              {saving ? "Speichern..." : "Speichern"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete Confirm */}
      <Dialog open={!!deleteConfirm} onOpenChange={(open) => !open && setDeleteConfirm(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Gruppe löschen</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Soll die Gruppe <strong className="text-foreground">{deleteConfirm?.name}</strong> wirklich gelöscht werden?
            {(deleteConfirm?.deviceCount ?? 0) > 0 && (
              <> Die {deleteConfirm?.deviceCount} zugeordneten Geräte werden keiner Gruppe mehr zugewiesen.</>
            )}
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteConfirm(null)}>Abbrechen</Button>
            <Button variant="destructive" onClick={() => deleteConfirm && handleDelete(deleteConfirm)}>
              Löschen
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* RustDesk Sync Dialog */}
      <RustDeskOptionsDialog
        open={!!rdGroup}
        onOpenChange={(open) => !open && setRdGroup(null)}
        mode="device"
        options={rdOptions}
        onChange={setRdOptions}
        onSave={handleSaveRustDesk}
        saving={rdSaving}
        saved={rdSaved}
        title={rdGroup ? `RustDesk sync — ${rdGroup.name}` : undefined}
        description={rdGroup ? `Optionen werden auf ${rdGroup.deviceCount} Gerät${rdGroup.deviceCount !== 1 ? "e" : ""} angewendet. Leer lassen = gerätespezifische Overrides löschen.` : undefined}
      />

      {/* Notification Sync Dialog */}
      <Dialog open={!!notifGroup} onOpenChange={(open) => !open && setNotifGroup(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Bell className="h-4 w-4" />
              Benachrichtigungen — {notifGroup?.name}
            </DialogTitle>
          </DialogHeader>
          <p className="text-xs text-muted-foreground -mt-1">
            Wird auf {notifGroup?.deviceCount} Gerät{notifGroup?.deviceCount !== 1 ? "e" : ""} angewendet.
            „Global" übernimmt die systemweite Einstellung.
          </p>
          <div className="rounded-md border border-border overflow-hidden">
            <TriToggle
              label="Offline-Alarm"
              value={notif.alertOnOffline}
              onChange={(v) => setNotif(s => ({ ...s, alertOnOffline: v }))}
            />
            <TriToggle
              label="Wieder-Online-Alarm"
              value={notif.alertOnOnline}
              onChange={(v) => setNotif(s => ({ ...s, alertOnOnline: v }))}
            />
            <TriToggle
              label="Software-Alarm"
              value={notif.alertOnSoftwareAlert}
              onChange={(v) => setNotif(s => ({ ...s, alertOnSoftwareAlert: v }))}
            />
            <TriToggle
              label="Festplatten-Alarm"
              value={notif.alertOnDiskFull}
              onChange={(v) => setNotif(s => ({ ...s, alertOnDiskFull: v }))}
            />
          </div>
          <div className="flex items-center justify-between rounded-md border border-border px-3 py-2.5">
            <div>
              <p className="text-sm font-medium">Offline-Verzögerung</p>
              <p className="text-xs text-muted-foreground">Minuten nach Offline-Erkennung bis zum Alert</p>
            </div>
            <div className="flex items-center gap-2">
              <Input
                type="number"
                min={0}
                max={1440}
                value={notif.offlineAlertDelayMinutes ?? ""}
                onChange={e => setNotif(s => ({ ...s, offlineAlertDelayMinutes: e.target.value === "" ? null : Number(e.target.value) }))}
                placeholder="Global"
                className="w-24 h-8 text-sm text-right"
              />
              <span className="text-xs text-muted-foreground">Min.</span>
            </div>
          </div>
          <DialogFooter className="gap-2">
            <Button
              variant="ghost"
              size="sm"
              onClick={handleClearNotif}
              disabled={notifSaving}
              title="Alle gerätespezifischen Overrides in dieser Gruppe entfernen"
            >
              <BellOff className="h-3.5 w-3.5 mr-1.5" />
              Overrides löschen
            </Button>
            <Button variant="outline" onClick={() => setNotifGroup(null)}>Abbrechen</Button>
            <Button onClick={handleSaveNotif} disabled={notifSaving}>
              {notifSaving ? "Wird angewendet..." : "Anwenden"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
