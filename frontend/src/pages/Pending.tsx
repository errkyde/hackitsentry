import { useEffect, useState } from "react";
import { Clock, Check, X, Cpu, MemoryStick, Monitor } from "lucide-react";
import { devices, customers, groups, type PendingDevice, type Customer, type Group } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";

export function Pending() {
  const [pendingList, setPendingList] = useState<PendingDevice[]>([]);
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);

  // Approve dialog
  const [approveDialog, setApproveDialog] = useState<{ id: string; hostname: string } | null>(null);
  const [selectedCustomer, setSelectedCustomer] = useState("none");
  const [selectedGroup, setSelectedGroup] = useState("none");
  const [approving, setApproving] = useState(false);

  const fetchAll = async () => {
    const [pending, cust, grp] = await Promise.all([
      devices.getPending(),
      customers.list(),
      groups.list(),
    ]);
    setPendingList(pending);
    setCustomerList(cust);
    setGroupList(grp);
  };

  useEffect(() => {
    fetchAll().finally(() => setLoading(false));
  }, []);

  const handleApprove = async () => {
    if (!approveDialog) return;
    setApproving(true);
    await devices.approve(approveDialog.id, {
      customerId: selectedCustomer !== "none" ? selectedCustomer : undefined,
      groupId: selectedGroup !== "none" ? selectedGroup : undefined,
    });
    setApproveDialog(null);
    setSelectedCustomer("none");
    setSelectedGroup("none");
    setApproving(false);
    await fetchAll();
  };

  const handleReject = async (id: string) => {
    await devices.reject(id);
    await fetchAll();
  };

  const timeAgo = (dateStr: string) => {
    const diff = Date.now() - new Date(dateStr).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return "Gerade eben";
    if (mins < 60) return `vor ${mins} Min.`;
    return `vor ${Math.floor(mins / 60)} Std.`;
  };

  return (
    <div className="p-6 space-y-5">
      <div>
        <h1 className="text-xl font-semibold">Ausstehende Geräte</h1>
        <p className="text-sm text-muted-foreground">
          {pendingList.length} ausstehende Registrierungsanfrage{pendingList.length !== 1 ? "n" : ""}
        </p>
      </div>

      {loading ? (
        <p className="text-muted-foreground">Laden...</p>
      ) : pendingList.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <Clock className="h-10 w-10 text-muted-foreground/30 mb-3" />
          <p className="text-muted-foreground">Keine ausstehenden Anfragen</p>
          <p className="text-sm text-muted-foreground/60 mt-1">
            Sobald ein neuer Agent registriert, erscheint er hier.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {pendingList.map((device) => (
            <Card key={device.id} className="relative">
              <CardHeader className="pb-3">
                <div className="flex items-start justify-between">
                  <CardTitle className="text-base">{device.hostname}</CardTitle>
                  <span className="text-xs text-muted-foreground">{timeAgo(device.requestedAt)}</span>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="space-y-1.5 text-sm">
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Monitor className="h-3.5 w-3.5" />
                    <span className="truncate">{device.windowsVersion || "—"}</span>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Cpu className="h-3.5 w-3.5" />
                    <span className="truncate">{device.cpuModel || "—"}</span>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <MemoryStick className="h-3.5 w-3.5" />
                    <span>{device.ramTotalGB > 0 ? `${device.ramTotalGB} GB RAM` : "—"}</span>
                  </div>
                </div>
                <div className="flex gap-2 pt-1">
                  <Button
                    size="sm"
                    className="flex-1"
                    onClick={() => {
                      setApproveDialog({ id: device.id, hostname: device.hostname });
                      setSelectedCustomer("none");
                      setSelectedGroup("none");
                    }}
                  >
                    <Check className="h-3.5 w-3.5 mr-1" />
                    Annehmen
                  </Button>
                  <Button
                    size="sm"
                    variant="destructive"
                    className="flex-1"
                    onClick={() => handleReject(device.id)}
                  >
                    <X className="h-3.5 w-3.5 mr-1" />
                    Ablehnen
                  </Button>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {/* Approve Dialog */}
      <Dialog open={!!approveDialog} onOpenChange={(open) => !open && setApproveDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Gerät annehmen</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            <strong className="text-foreground">{approveDialog?.hostname}</strong> wird als aktives Gerät registriert.
          </p>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label>Kunde zuweisen (optional)</Label>
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
              <Label>Gruppe zuweisen (optional)</Label>
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
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setApproveDialog(null)}>Abbrechen</Button>
            <Button onClick={handleApprove} disabled={approving}>
              {approving ? "Wird angenommen..." : "Annehmen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
