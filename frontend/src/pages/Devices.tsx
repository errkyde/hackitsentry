import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { Search, Wifi, WifiOff, ChevronRight, RefreshCw } from "lucide-react";
import { devices, customers, groups, type Device, type Customer, type Group } from "@/lib/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { cn } from "@/lib/utils";

function StatusBadge({ online }: { online: boolean }) {
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium",
      online
        ? "bg-emerald-500/15 text-emerald-400 ring-1 ring-emerald-500/30"
        : "bg-rose-500/10 text-rose-400 ring-1 ring-rose-500/20"
    )}>
      {online
        ? <><span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />Online</>
        : <><span className="h-1.5 w-1.5 rounded-full bg-rose-400" />Offline</>
      }
    </span>
  );
}

export function Devices() {
  const navigate = useNavigate();
  const [deviceList, setDeviceList] = useState<Device[]>([]);
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);

  // Filters
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [groupFilter, setGroupFilter] = useState("all");
  const [customerFilter, setCustomerFilter] = useState("all");

  const fetchDevices = useCallback(async () => {
    const params: Record<string, string> = {};
    if (search) params.search = search;
    if (groupFilter !== "all") params.groupId = groupFilter;
    if (customerFilter !== "all") params.customerId = customerFilter;
    if (statusFilter !== "all") params.status = statusFilter;

    const data = await devices.list(params);
    setDeviceList(data);
  }, [search, groupFilter, customerFilter, statusFilter]);

  useEffect(() => {
    Promise.all([customers.list(), groups.list()])
      .then(([c, g]) => { setCustomerList(c); setGroupList(g); });
  }, []);

  useEffect(() => {
    setLoading(true);
    fetchDevices().finally(() => setLoading(false));
  }, [fetchDevices]);

  const formatLastSeen = (lastSeenAt: string | null) => {
    if (!lastSeenAt) return "Nie";
    const diff = Date.now() - new Date(lastSeenAt).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 1) return "Gerade eben";
    if (mins < 60) return `vor ${mins} Min.`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `vor ${hours} Std.`;
    return `vor ${Math.floor(hours / 24)} Tagen`;
  };

  return (
    <div className="p-6 space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Geräte</h1>
          <p className="text-sm text-muted-foreground">
            {deviceList.length} Gerät{deviceList.length !== 1 ? "e" : ""} gefunden
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => fetchDevices()}>
          <RefreshCw className="h-3.5 w-3.5 mr-1.5" />
          Aktualisieren
        </Button>
      </div>

      {/* Filter bar */}
      <div className="flex flex-wrap gap-3">
        <div className="relative flex-1 min-w-48">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
          <Input
            placeholder="Hostname, Beschreibung suchen..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-36">
            <SelectValue placeholder="Status" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle Status</SelectItem>
            <SelectItem value="online">Online</SelectItem>
            <SelectItem value="offline">Offline</SelectItem>
          </SelectContent>
        </Select>
        <Select value={groupFilter} onValueChange={setGroupFilter}>
          <SelectTrigger className="w-40">
            <SelectValue placeholder="Gruppe" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle Gruppen</SelectItem>
            {groupList.map((g) => (
              <SelectItem key={g.id} value={g.id}>{g.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={customerFilter} onValueChange={setCustomerFilter}>
          <SelectTrigger className="w-40">
            <SelectValue placeholder="Kunde" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle Kunden</SelectItem>
            {customerList.map((c) => (
              <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Table */}
      <div className="rounded-lg border border-border overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/30">
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Status</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Hostname</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Windows</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Kunde</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Gruppe</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">RAM</th>
              <th className="text-left px-4 py-3 font-medium text-muted-foreground">Letzter Check-in</th>
              <th className="w-10"></th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={8} className="px-4 py-12 text-center text-muted-foreground">
                  Laden...
                </td>
              </tr>
            ) : deviceList.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-4 py-12 text-center text-muted-foreground">
                  Keine Geräte gefunden
                </td>
              </tr>
            ) : (
              deviceList.map((device) => (
                <tr
                  key={device.id}
                  className="border-b border-border/50 hover:bg-accent/30 cursor-pointer transition-colors"
                  onClick={() => navigate(`/devices/${device.id}`)}
                >
                  <td className="px-4 py-3">
                    <StatusBadge online={device.isOnline} />
                  </td>
                  <td className="px-4 py-3">
                    <div className="font-medium">{device.hostname}</div>
                    {device.description && (
                      <div className="text-xs text-muted-foreground">{device.description}</div>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    <div className="max-w-[160px] truncate">{device.windowsVersion}</div>
                    {device.windowsBuild && (
                      <div className="text-xs opacity-60">{device.windowsBuild}</div>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {device.customer?.name ?? <span className="opacity-40">—</span>}
                  </td>
                  <td className="px-4 py-3">
                    {device.group ? (
                      <Badge
                        variant="outline"
                        className="text-xs"
                        style={device.group.color ? {
                          borderColor: device.group.color + "60",
                          color: device.group.color,
                          backgroundColor: device.group.color + "15"
                        } : {}}
                      >
                        {device.group.name}
                      </Badge>
                    ) : <span className="text-muted-foreground opacity-40">—</span>}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {device.ramTotalGB > 0 ? `${device.ramTotalGB} GB` : "—"}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground text-xs">
                    {formatLastSeen(device.lastSeenAt)}
                  </td>
                  <td className="px-4 py-3">
                    <ChevronRight className="h-4 w-4 text-muted-foreground" />
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
