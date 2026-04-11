import { useEffect, useState } from "react";
import { Plus, Pencil, Trash2, Users } from "lucide-react";
import { customers, type Customer } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";

type FormState = { name: string; contactEmail: string };

export function Customers() {
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(true);
  const [dialog, setDialog] = useState<{ mode: "create" | "edit"; customer?: Customer } | null>(null);
  const [form, setForm] = useState<FormState>({ name: "", contactEmail: "" });
  const [saving, setSaving] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<Customer | null>(null);

  const fetchCustomers = async () => {
    const data = await customers.list();
    setCustomerList(data);
  };

  useEffect(() => {
    fetchCustomers().finally(() => setLoading(false));
  }, []);

  const openCreate = () => {
    setForm({ name: "", contactEmail: "" });
    setDialog({ mode: "create" });
  };

  const openEdit = (customer: Customer) => {
    setForm({ name: customer.name, contactEmail: customer.contactEmail });
    setDialog({ mode: "edit", customer });
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      if (dialog?.mode === "create") {
        await customers.create(form);
      } else if (dialog?.customer) {
        await customers.update(dialog.customer.id, form);
      }
      setDialog(null);
      await fetchCustomers();
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (customer: Customer) => {
    await customers.delete(customer.id);
    setDeleteConfirm(null);
    await fetchCustomers();
  };

  return (
    <div className="p-6 space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Kunden</h1>
          <p className="text-sm text-muted-foreground">{customerList.length} Kunden</p>
        </div>
        <Button size="sm" onClick={openCreate}>
          <Plus className="h-4 w-4 mr-1.5" />
          Neuer Kunde
        </Button>
      </div>

      {loading ? (
        <p className="text-muted-foreground">Laden...</p>
      ) : customerList.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <Users className="h-10 w-10 text-muted-foreground/30 mb-3" />
          <p className="text-muted-foreground">Noch keine Kunden angelegt</p>
          <Button size="sm" variant="outline" className="mt-4" onClick={openCreate}>
            <Plus className="h-3.5 w-3.5 mr-1.5" />
            Ersten Kunden erstellen
          </Button>
        </div>
      ) : (
        <div className="rounded-lg border border-border overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border bg-muted/30">
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Name</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">E-Mail</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Geräte</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Erstellt</th>
                <th className="w-24"></th>
              </tr>
            </thead>
            <tbody>
              {customerList.map((customer) => (
                <tr key={customer.id} className="border-b border-border/50 hover:bg-accent/20">
                  <td className="px-4 py-3 font-medium">{customer.name}</td>
                  <td className="px-4 py-3 text-muted-foreground">{customer.contactEmail || "—"}</td>
                  <td className="px-4 py-3 text-muted-foreground">{customer.deviceCount}</td>
                  <td className="px-4 py-3 text-muted-foreground text-xs">
                    {new Date(customer.createdAt).toLocaleDateString("de-DE")}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex gap-1 justify-end">
                      <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(customer)}>
                        <Pencil className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8 hover:text-destructive"
                        onClick={() => setDeleteConfirm(customer)}
                      >
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

      {/* Create/Edit Dialog */}
      <Dialog open={!!dialog} onOpenChange={(open) => !open && setDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>{dialog?.mode === "create" ? "Neuer Kunde" : "Kunde bearbeiten"}</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Firmenname</Label>
              <Input
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                placeholder="Musterfirma GmbH"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Kontakt-E-Mail</Label>
              <Input
                type="email"
                value={form.contactEmail}
                onChange={(e) => setForm({ ...form, contactEmail: e.target.value })}
                placeholder="kontakt@musterfirma.de"
              />
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
            <DialogTitle>Kunde löschen</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Soll der Kunde <strong className="text-foreground">{deleteConfirm?.name}</strong> wirklich gelöscht werden?
            {(deleteConfirm?.deviceCount ?? 0) > 0 && (
              <> Die {deleteConfirm?.deviceCount} zugeordneten Geräte werden keinem Kunden mehr zugewiesen.</>
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
