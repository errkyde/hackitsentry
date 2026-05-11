import { useEffect, useState } from "react";
import { Package, Search, Monitor } from "lucide-react";
import { devices, type Device, type Software } from "@/lib/api";
import { Input } from "@/components/ui/input";
import { Card, CardContent } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";

export function SoftwareInventory() {
  const [deviceList, setDeviceList] = useState<Device[]>([]);
  const [selectedDeviceId, setSelectedDeviceId] = useState<string>("");
  const [software, setSoftware] = useState<Software[]>([]);
  const [search, setSearch] = useState("");
  const [loadingDevices, setLoadingDevices] = useState(true);
  const [loadingSoftware, setLoadingSoftware] = useState(false);

  useEffect(() => {
    devices.list().then(d => {
      setDeviceList(d.items);
      setLoadingDevices(false);
    }).catch(() => setLoadingDevices(false));
  }, []);

  useEffect(() => {
    if (!selectedDeviceId) {
      setSoftware([]);
      return;
    }
    setLoadingSoftware(true);
    devices.getSoftware(selectedDeviceId)
      .then(sw => setSoftware(sw))
      .catch(() => setSoftware([]))
      .finally(() => setLoadingSoftware(false));
    setSearch("");
  }, [selectedDeviceId]);

  const filtered = software.filter(s =>
    s.name.toLowerCase().includes(search.toLowerCase()) ||
    s.publisher.toLowerCase().includes(search.toLowerCase())
  );

  const selectedDevice = deviceList.find(d => d.id === selectedDeviceId);

  return (
    <div className="p-6 space-y-5 max-w-5xl">
      <div>
        <h1 className="text-xl font-semibold">Software-Inventar</h1>
        <p className="text-sm text-muted-foreground">Gerät auswählen und installierte Software einsehen</p>
      </div>

      {/* Device selector */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="flex items-center gap-2 min-w-[280px]">
          <Monitor className="h-4 w-4 text-muted-foreground shrink-0" />
          <Select
            value={selectedDeviceId}
            onValueChange={setSelectedDeviceId}
            disabled={loadingDevices}
          >
            <SelectTrigger className="flex-1">
              <SelectValue placeholder={loadingDevices ? "Laden..." : "Gerät auswählen..."} />
            </SelectTrigger>
            <SelectContent>
              {deviceList.map(d => (
                <SelectItem key={d.id} value={d.id}>
                  <div className="flex items-center gap-2">
                    <span
                      className={`h-1.5 w-1.5 rounded-full shrink-0 ${d.isOnline ? "bg-emerald-500" : "bg-rose-500"}`}
                    />
                    <span>{d.hostname}</span>
                    {d.customer && (
                      <span className="text-muted-foreground text-xs">– {d.customer.name}</span>
                    )}
                  </div>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {selectedDeviceId && (
          <div className="relative flex-1 min-w-[200px]">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
            <Input
              placeholder="Software suchen..."
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="pl-9"
            />
          </div>
        )}
      </div>

      {/* Content */}
      {!selectedDeviceId ? (
        <div className="flex flex-col items-center justify-center py-20 text-muted-foreground gap-3">
          <Monitor className="h-10 w-10 opacity-30" />
          <p className="text-sm">Gerät auswählen um die installierte Software anzuzeigen</p>
        </div>
      ) : loadingSoftware ? (
        <div className="text-center text-muted-foreground py-12 text-sm">Laden...</div>
      ) : (
        <Card>
          <CardContent className="pt-0 pb-0">
            <div className="flex items-center justify-between px-4 py-3 border-b border-border">
              <div className="flex items-center gap-2 text-sm">
                <Package className="h-3.5 w-3.5 text-muted-foreground" />
                <span className="font-medium">{selectedDevice?.hostname}</span>
                {selectedDevice?.customer && (
                  <span className="text-muted-foreground">– {selectedDevice.customer.name}</span>
                )}
              </div>
              <Badge variant="secondary">{filtered.length} Programme</Badge>
            </div>
            <div className="overflow-hidden max-h-[560px] overflow-y-auto">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-muted/80 backdrop-blur">
                  <tr className="border-b border-border">
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Name</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Version</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Hersteller</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map(sw => (
                    <tr key={sw.id} className="border-t border-border/50 hover:bg-accent/20 transition-colors">
                      <td className="px-4 py-2">{sw.name}</td>
                      <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{sw.version || "—"}</td>
                      <td className="px-4 py-2 text-muted-foreground">{sw.publisher || "—"}</td>
                    </tr>
                  ))}
                  {filtered.length === 0 && (
                    <tr>
                      <td colSpan={3} className="px-4 py-10 text-center text-muted-foreground">
                        {search ? "Keine übereinstimmenden Programme gefunden." : "Keine Software vorhanden."}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
