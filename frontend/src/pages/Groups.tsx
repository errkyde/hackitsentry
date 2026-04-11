import { useEffect, useState } from "react";
import { Plus, Pencil, Trash2, Layers } from "lucide-react";
import { groups, type Group } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";

const PRESET_COLORS = [
  "#3b82f6", "#8b5cf6", "#ec4899", "#ef4444",
  "#f97316", "#eab308", "#22c55e", "#14b8a6",
];

type FormState = { name: string; description: string; color: string };

export function Groups() {
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);
  const [dialog, setDialog] = useState<{ mode: "create" | "edit"; group?: Group } | null>(null);
  const [form, setForm] = useState<FormState>({ name: "", description: "", color: PRESET_COLORS[0] });
  const [saving, setSaving] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<Group | null>(null);

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

  const handleSave = async () => {
    setSaving(true);
    try {
      if (dialog?.mode === "create") {
        await groups.create(form);
      } else if (dialog?.group) {
        await groups.update(dialog.group.id, form);
      }
      setDialog(null);
      await fetchGroups();
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (group: Group) => {
    await groups.delete(group.id);
    setDeleteConfirm(null);
    await fetchGroups();
  };

  return (
    <div className="p-6 space-y-5">
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
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(group)}>
                      <Pencil className="h-3.5 w-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 hover:text-destructive"
                      onClick={() => setDeleteConfirm(group)}
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                {group.description && (
                  <p className="text-sm text-muted-foreground mb-2">{group.description}</p>
                )}
                <p className="text-xs text-muted-foreground">
                  {group.deviceCount} Gerät{group.deviceCount !== 1 ? "e" : ""}
                </p>
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
            <Button onClick={handleSave} disabled={saving || !form.name.trim()}>
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
    </div>
  );
}
