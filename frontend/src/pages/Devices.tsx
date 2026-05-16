import { useEffect, useState, useCallback, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import {
  Search, ChevronRight, ChevronLeft, RefreshCw, Monitor, Wifi, WifiOff, Clock,
  Download, Trash2, Users, Layers, X, Link, MonitorCheck, ShieldCheck, ShieldAlert, Send
} from "lucide-react";
import {
  devices, customers, groups,
  type Device, type Customer, type Group
} from "@/lib/api";
import { InstallTokenDialog } from "@/components/InstallTokenDialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";

function defenderStatus(json: string): "ok" | "warn" | "unknown" {
  try {
    const d = JSON.parse(json);
    if (!d || (!d.avProducts?.length && d.realTimeProtectionEnabled == null)) return "unknown";
    if (d.realTimeProtectionEnabled === false) return "warn";
    if (typeof d.signatureAgeDays === "number" && d.signatureAgeDays > 7) return "warn";
    if (d.avProducts?.some((p: { enabled: boolean; upToDate: boolean }) => !p.enabled || !p.upToDate)) return "warn";
    return "ok";
  } catch { return "unknown"; }
}

function SecurityBadge({ json }: { json: string }) {
  const status = defenderStatus(json);
  if (status === "unknown") return null;
  if (status === "warn") return (
    <span title="Sicherheitsproblem erkannt">
      <ShieldAlert className="h-3.5 w-3.5 text-rose-500 shrink-0" />
    </span>
  );
  return (
    <span title="Antivirus OK">
      <ShieldCheck className="h-3.5 w-3.5 text-emerald-500 shrink-0" />
    </span>
  );
}

function UpdatesBadge({ count }: { count: number }) {
  if (count <= 0) return null;
  return (
    <span
      title={`${count} ausstehende Windows-Updates`}
      className="inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 text-[10px] font-semibold bg-amber-500/15 text-amber-600 dark:text-amber-400 ring-1 ring-amber-500/30 shrink-0"
    >
      <Download className="h-2.5 w-2.5" />{count}
    </span>
  );
}

function StatusBadge({ online }: { online: boolean }) {
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium",
      online
        ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 ring-1 ring-emerald-500/30"
        : "bg-rose-500/10 text-rose-600 dark:text-rose-400 ring-1 ring-rose-500/20"
    )}>
      {online
        ? <><span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-pulse" />Online</>
        : <><span className="h-1.5 w-1.5 rounded-full bg-rose-500" />Offline</>
      }
    </span>
  );
}

export function Devices() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [deviceList, setDeviceList] = useState<Device[]>([]);
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [loading, setLoading] = useState(true);
  const [stats, setStats] = useState({ total: 0, online: 0, offline: 0, pending: 0 });
  const [page, setPage] = useState(1);
  const [totalDevices, setTotalDevices] = useState(0);
  const currentPageRef = useRef(1);

  // Filters
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState(searchParams.get("status") ?? "all");
  const [groupFilter, setGroupFilter] = useState("all");
  const [customerFilter, setCustomerFilter] = useState("all");
  const [osFilter, setOsFilter] = useState("all");
  const [ramFilter, setRamFilter] = useState("all");

  // Bulk selection
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkAssignDialog, setBulkAssignDialog] = useState<"customer" | "group" | null>(null);
  const [bulkAssignValue, setBulkAssignValue] = useState("none");
  const [bulkDeleteDialog, setBulkDeleteDialog] = useState(false);
  const [bulkLoading, setBulkLoading] = useState(false);
  const [installDialog, setInstallDialog] = useState(false);
  const [bulkCmdDialog, setBulkCmdDialog] = useState(false);
  const [bulkCmdType, setBulkCmdType] = useState("Restart");
  const [bulkCmdParams, setBulkCmdParams] = useState("");

  // Debounce search input by 400 ms to avoid a request per keystroke
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 400);
    return () => clearTimeout(t);
  }, [search]);

  // Keep ref current so the silent interval always refreshes the visible page
  useEffect(() => { currentPageRef.current = page; }, [page]);

  const PAGE_SIZE = 100;

  const RAM_RANGES: Record<string, { minRam?: number; maxRam?: number }> = {
    "lt4":   { maxRam: 3.9 },
    "4to8":  { minRam: 4, maxRam: 8 },
    "8to16": { minRam: 8.1, maxRam: 16 },
    "16to32":{ minRam: 16.1, maxRam: 32 },
    "gt32":  { minRam: 32.1 },
  };

  const fetchDevices = useCallback(async (p: number, silent = false) => {
    const params: Record<string, string> = {
      page: String(p),
      pageSize: String(PAGE_SIZE),
    };
    if (debouncedSearch) params.search = debouncedSearch;
    if (groupFilter !== "all") params.groupId = groupFilter;
    if (customerFilter !== "all") params.customerId = customerFilter;
    if (statusFilter !== "all") params.status = statusFilter;
    if (osFilter !== "all") params.os = osFilter;
    if (ramFilter !== "all") {
      const range = RAM_RANGES[ramFilter];
      if (range?.minRam) params.minRam = String(range.minRam);
      if (range?.maxRam) params.maxRam = String(range.maxRam);
    }

    const [data, s] = await Promise.all([devices.list(params), devices.getStats()]);
    setDeviceList(data.items);
    setTotalDevices(data.total);
    setPage(data.page);
    setStats(s);
    if (!silent) setSelected(new Set());
  }, [debouncedSearch, groupFilter, customerFilter, statusFilter, osFilter, ramFilter]);

  const loadPage = useCallback((p: number) => {
    setLoading(true);
    fetchDevices(p).finally(() => setLoading(false));
  }, [fetchDevices]);

  useEffect(() => {
    Promise.all([customers.list(), groups.list()])
      .then(([c, g]) => { setCustomerList(c); setGroupList(g); });
  }, []);

  // Filter change → reset subtitle immediately and reload from page 1
  useEffect(() => {
    setPage(1);
    loadPage(1);
  }, [loadPage]);

  // Silent auto-refresh every 60 s, stays on whichever page the user is viewing
  useEffect(() => {
    const id = setInterval(() => fetchDevices(currentPageRef.current, true), 60_000);
    return () => clearInterval(id);
  }, [fetchDevices]);

  const goToPage = (p: number) => loadPage(p);

  const exportCsv = () => {
    const headers = ["Hostname", "Beschreibung", "Status", "Windows", "CPU", "RAM (GB)", "Kunde", "Gruppe", "Letzter Check-in"];
    const rows = deviceList.map(d => [
      d.hostname, d.description, d.isOnline ? "Online" : "Offline",
      d.windowsVersion, d.cpuModel, d.ramTotalGB,
      d.customer?.name ?? "", d.group?.name ?? "",
      d.lastSeenAt ? new Date(d.lastSeenAt).toLocaleString("de-DE") : "",
    ]);
    const csv = [headers, ...rows]
      .map(row => row.map(v => `"${String(v).replace(/"/g, '""')}"`).join(";"))
      .join("\n");
    const blob = new Blob(["\uFEFF" + csv], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `hitsight-geraete-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

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

  const toggleSelect = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selected.size === deviceList.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(deviceList.map(d => d.id)));
    }
  };

  const handleBulkAssign = async () => {
    setBulkLoading(true);
    const ids = Array.from(selected);
    const value = bulkAssignValue === "none" ? null : bulkAssignValue;
    await devices.bulkUpdate({
      deviceIds: ids,
      ...(bulkAssignDialog === "customer" ? { setCustomerId: value } : { setGroupId: value }),
    }).catch(() => {});
    setBulkAssignDialog(null);
    setBulkAssignValue("none");
    setBulkLoading(false);
    setSelected(new Set());
    loadPage(page);
  };

  const handleBulkCommand = async () => {
    setBulkLoading(true);
    await devices.bulkCommand({
      deviceIds: Array.from(selected),
      commandType: bulkCmdType,
      parameters: bulkCmdParams || undefined,
    }).catch(() => {});
    setBulkCmdDialog(false);
    setBulkCmdParams("");
    setBulkLoading(false);
  };

  const handleBulkDelete = async () => {
    setBulkLoading(true);
    await devices.bulkDelete(Array.from(selected)).catch(() => {});
    setBulkDeleteDialog(false);
    setBulkLoading(false);
    setSelected(new Set());
    loadPage(page);
  };

  return (
    <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold">Geräte</h1>
          <p className="text-sm text-muted-foreground">
            {totalDevices > 0
              ? `${(page - 1) * PAGE_SIZE + 1}–${Math.min(page * PAGE_SIZE, totalDevices)} von ${totalDevices} Gerät${totalDevices !== 1 ? "en" : ""}`
              : "Keine Geräte gefunden"}
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => setInstallDialog(true)}>
            <Link className="h-3.5 w-3.5 sm:mr-1.5" />
            <span className="hidden sm:inline">Gerät hinzufügen</span>
          </Button>
          <Button variant="outline" size="sm" onClick={exportCsv} disabled={deviceList.length === 0}>
            <Download className="h-3.5 w-3.5 sm:mr-1.5" />
            <span className="hidden sm:inline">CSV</span>
          </Button>
          <Button variant="outline" size="sm" onClick={() => loadPage(page)}>
            <RefreshCw className={cn("h-3.5 w-3.5 sm:mr-1.5", loading && "animate-spin")} />
            <span className="hidden sm:inline">Aktualisieren</span>
          </Button>
        </div>
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        {[
          { label: "Geräte gesamt", value: stats.total, icon: Monitor, color: "text-foreground" },
          { label: "Online", value: stats.online, icon: Wifi, color: "text-emerald-500" },
          { label: "Offline", value: stats.offline, icon: WifiOff, color: "text-rose-500" },
          { label: "Ausstehend", value: stats.pending, icon: Clock, color: "text-amber-500" },
        ].map(({ label, value, icon: Icon, color }) => (
          <div key={label} className="rounded-lg border border-border bg-card px-4 py-3 flex items-center gap-3">
            <Icon className={`h-5 w-5 flex-shrink-0 ${color}`} />
            <div>
              <div className="text-2xl font-semibold leading-none">{value}</div>
              <div className="text-xs text-muted-foreground mt-1">{label}</div>
            </div>
          </div>
        ))}
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
        <Select value={osFilter} onValueChange={setOsFilter}>
          <SelectTrigger className="w-40">
            <SelectValue placeholder="Betriebssystem" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle OS</SelectItem>
            <SelectItem value="Windows 11">Windows 11</SelectItem>
            <SelectItem value="Windows 10">Windows 10</SelectItem>
            <SelectItem value="Windows Server 2025">Server 2025</SelectItem>
            <SelectItem value="Windows Server 2022">Server 2022</SelectItem>
            <SelectItem value="Windows Server 2019">Server 2019</SelectItem>
            <SelectItem value="Windows Server 2016">Server 2016</SelectItem>
          </SelectContent>
        </Select>
        <Select value={ramFilter} onValueChange={setRamFilter}>
          <SelectTrigger className="w-36">
            <SelectValue placeholder="RAM" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle RAM</SelectItem>
            <SelectItem value="lt4">&lt; 4 GB</SelectItem>
            <SelectItem value="4to8">4 – 8 GB</SelectItem>
            <SelectItem value="8to16">8 – 16 GB</SelectItem>
            <SelectItem value="16to32">16 – 32 GB</SelectItem>
            <SelectItem value="gt32">&gt; 32 GB</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Bulk action toolbar */}
      {selected.size > 0 && (
        <div className="flex items-center gap-3 rounded-lg border border-primary/30 bg-primary/5 px-4 py-2.5">
          <span className="text-sm font-medium">{selected.size} ausgewählt</span>
          <Button variant="outline" size="sm" onClick={() => { setBulkAssignValue("none"); setBulkAssignDialog("customer"); }}>
            <Users className="h-3.5 w-3.5 mr-1.5" />
            Kunde zuweisen
          </Button>
          <Button variant="outline" size="sm" onClick={() => { setBulkAssignValue("none"); setBulkAssignDialog("group"); }}>
            <Layers className="h-3.5 w-3.5 mr-1.5" />
            Gruppe zuweisen
          </Button>
          <Button variant="outline" size="sm" onClick={() => { setBulkCmdType("Restart"); setBulkCmdParams(""); setBulkCmdDialog(true); }}>
            <Send className="h-3.5 w-3.5 mr-1.5" />
            Befehl senden
          </Button>
          <Button variant="outline" size="sm" className="text-destructive hover:text-destructive" onClick={() => setBulkDeleteDialog(true)}>
            <Trash2 className="h-3.5 w-3.5 mr-1.5" />
            Löschen
          </Button>
          <Button variant="ghost" size="sm" onClick={() => setSelected(new Set())} className="ml-auto">
            <X className="h-3.5 w-3.5 mr-1" />
            Abwählen
          </Button>
        </div>
      )}

      {/* Desktop Table */}
      <div className="hidden md:block rounded-lg border border-border overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/30">
              <th className="px-3 py-3 w-10">
                <input
                  type="checkbox"
                  checked={deviceList.length > 0 && selected.size === deviceList.length}
                  onChange={toggleSelectAll}
                  className="h-4 w-4 rounded border-border cursor-pointer"
                />
              </th>
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
                <td colSpan={9} className="px-4 py-12 text-center text-muted-foreground">Laden...</td>
              </tr>
            ) : deviceList.length === 0 ? (
              <tr>
                <td colSpan={9} className="px-4 py-12 text-center text-muted-foreground">Keine Geräte gefunden</td>
              </tr>
            ) : (
              deviceList.map((device) => (
                <tr
                  key={device.id}
                  className={cn(
                    "border-b border-border/50 hover:bg-accent/30 cursor-pointer transition-colors",
                    selected.has(device.id) && "bg-primary/5"
                  )}
                  onClick={() => navigate(`/devices/${device.id}`)}
                >
                  <td className="px-3 py-3 w-10" onClick={(e) => toggleSelect(device.id, e)}>
                    <input
                      type="checkbox"
                      checked={selected.has(device.id)}
                      onChange={() => {}}
                      className="h-4 w-4 rounded border-border cursor-pointer"
                    />
                  </td>
                  <td className="px-4 py-3">
                    <StatusBadge online={device.isOnline} />
                  </td>
                  <td className="px-4 py-3">
                    <div className="font-medium flex items-center gap-1.5">
                      {device.hostname}
                      {device.rustDeskId && (
                        <span title={`RustDesk: ${device.rustDeskId}`}><MonitorCheck className="h-3.5 w-3.5 text-blue-500 shrink-0" /></span>
                      )}
                      <SecurityBadge json={device.defenderStatusJson} />
                      <UpdatesBadge count={device.pendingUpdatesCount} />
                    </div>
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

      {/* Mobile Card List */}
      <div className="md:hidden space-y-2">
        {loading ? (
          <div className="py-12 text-center text-muted-foreground text-sm">Laden...</div>
        ) : deviceList.length === 0 ? (
          <div className="py-12 text-center text-muted-foreground text-sm">Keine Geräte gefunden</div>
        ) : (
          deviceList.map((device) => (
            <div
              key={device.id}
              className={cn(
                "rounded-lg border border-border bg-card p-3.5 cursor-pointer active:bg-accent/50 transition-colors",
                selected.has(device.id) && "border-primary/40 bg-primary/5"
              )}
              onClick={() => navigate(`/devices/${device.id}`)}
            >
              <div className="flex items-start gap-3">
                <div
                  className="mt-0.5 shrink-0"
                  onClick={(e) => toggleSelect(device.id, e)}
                >
                  <input
                    type="checkbox"
                    checked={selected.has(device.id)}
                    onChange={() => {}}
                    className="h-4 w-4 rounded border-border cursor-pointer"
                  />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2 mb-1">
                    <div className="flex items-center gap-1.5 min-w-0">
                      <span className="font-medium text-sm truncate">{device.hostname}</span>
                      {device.rustDeskId && (
                        <MonitorCheck className="h-3.5 w-3.5 text-blue-500 shrink-0" />
                      )}
                      <SecurityBadge json={device.defenderStatusJson} />
                      <UpdatesBadge count={device.pendingUpdatesCount} />
                    </div>
                    <StatusBadge online={device.isOnline} />
                  </div>
                  {device.description && (
                    <div className="text-xs text-muted-foreground mb-1.5 truncate">{device.description}</div>
                  )}
                  <div className="flex items-center gap-2 flex-wrap">
                    {device.customer && (
                      <span className="text-xs text-muted-foreground">{device.customer.name}</span>
                    )}
                    {device.group && (
                      <Badge
                        variant="outline"
                        className="text-xs h-4 px-1.5"
                        style={device.group.color ? {
                          borderColor: device.group.color + "60",
                          color: device.group.color,
                          backgroundColor: device.group.color + "15"
                        } : {}}
                      >
                        {device.group.name}
                      </Badge>
                    )}
                    <span className="text-xs text-muted-foreground ml-auto">
                      {formatLastSeen(device.lastSeenAt)}
                    </span>
                  </div>
                </div>
                <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0 mt-0.5" />
              </div>
            </div>
          ))
        )}
      </div>

      {/* Pagination */}
      {totalDevices > PAGE_SIZE && (
        <div className="flex items-center justify-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => goToPage(page - 1)}
            disabled={page <= 1 || loading}
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
          {Array.from({ length: Math.ceil(totalDevices / PAGE_SIZE) }, (_, i) => i + 1)
            .filter(p => p === 1 || p === Math.ceil(totalDevices / PAGE_SIZE) || Math.abs(p - page) <= 2)
            .reduce<(number | "…")[]>((acc, p, i, arr) => {
              if (i > 0 && p - (arr[i - 1] as number) > 1) acc.push("…");
              acc.push(p);
              return acc;
            }, [])
            .map((p, i) =>
              p === "…" ? (
                <span key={`ellipsis-${i}`} className="px-1 text-muted-foreground text-sm">…</span>
              ) : (
                <Button
                  key={p}
                  variant={p === page ? "default" : "outline"}
                  size="sm"
                  className="w-9"
                  onClick={() => goToPage(p as number)}
                  disabled={loading}
                >
                  {p}
                </Button>
              )
            )}
          <Button
            variant="outline"
            size="sm"
            onClick={() => goToPage(page + 1)}
            disabled={page >= Math.ceil(totalDevices / PAGE_SIZE) || loading}
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      )}

      {/* Bulk assign dialog */}
      <Dialog open={!!bulkAssignDialog} onOpenChange={open => !open && setBulkAssignDialog(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>
              {bulkAssignDialog === "customer" ? "Kunde zuweisen" : "Gruppe zuweisen"}
            </DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            {selected.size} Gerät{selected.size !== 1 ? "e" : ""} werden aktualisiert.
          </p>
          {bulkAssignDialog === "customer" ? (
            <Select value={bulkAssignValue} onValueChange={setBulkAssignValue}>
              <SelectTrigger>
                <SelectValue placeholder="Kunden auswählen" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Kein Kunde (entfernen)</SelectItem>
                {customerList.map(c => (
                  <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          ) : (
            <Select value={bulkAssignValue} onValueChange={setBulkAssignValue}>
              <SelectTrigger>
                <SelectValue placeholder="Gruppe auswählen" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Keine Gruppe (entfernen)</SelectItem>
                {groupList.map(g => (
                  <SelectItem key={g.id} value={g.id}>{g.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={() => setBulkAssignDialog(null)}>Abbrechen</Button>
            <Button onClick={handleBulkAssign} disabled={bulkLoading}>
              {bulkLoading ? "Wird gespeichert..." : "Übernehmen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Bulk delete dialog */}
      <Dialog open={bulkDeleteDialog} onOpenChange={setBulkDeleteDialog}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Geräte löschen</DialogTitle></DialogHeader>
          <p className="text-sm text-muted-foreground">
            Sollen <strong className="text-foreground">{selected.size} Gerät{selected.size !== 1 ? "e" : ""}</strong> wirklich gelöscht werden?
            Diese Aktion ist nicht umkehrbar.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setBulkDeleteDialog(false)}>Abbrechen</Button>
            <Button variant="destructive" onClick={handleBulkDelete} disabled={bulkLoading}>
              {bulkLoading ? "Wird gelöscht..." : "Löschen"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Bulk command dialog */}
      <Dialog open={bulkCmdDialog} onOpenChange={open => { if (!open) setBulkCmdDialog(false); }}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Befehl an {selected.size} Gerät{selected.size !== 1 ? "e" : ""} senden</DialogTitle></DialogHeader>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <label className="text-sm font-medium">Befehlstyp</label>
              <Select value={bulkCmdType} onValueChange={setBulkCmdType}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Restart">Neustart</SelectItem>
                  <SelectItem value="Shutdown">Herunterfahren</SelectItem>
                  <SelectItem value="RunScript">Script ausführen</SelectItem>
                  <SelectItem value="ForceCheckin">Sofort einchecken</SelectItem>
                  <SelectItem value="CollectLicense">Lizenzen sammeln</SelectItem>
                  <SelectItem value="InstallUpdates">Windows Updates installieren</SelectItem>
                  <SelectItem value="ForceUpdate">Agent-Update erzwingen</SelectItem>
                </SelectContent>
              </Select>
            </div>
            {(bulkCmdType === "RunScript" || bulkCmdType === "UpdateServerUrl" || bulkCmdType === "ForceUpdate") && (
              <div className="space-y-1.5">
                <label className="text-sm font-medium">
                  {bulkCmdType === "RunScript" ? "Script (PowerShell)" : "Parameter"}
                </label>
                {bulkCmdType === "RunScript" ? (
                  <textarea
                    value={bulkCmdParams}
                    onChange={e => setBulkCmdParams(e.target.value)}
                    rows={5}
                    className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm font-mono resize-y"
                    placeholder="# PowerShell..."
                  />
                ) : (
                  <input
                    value={bulkCmdParams}
                    onChange={e => setBulkCmdParams(e.target.value)}
                    className="w-full h-8 rounded-md border border-input bg-background px-3 text-sm"
                  />
                )}
              </div>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setBulkCmdDialog(false)}>Abbrechen</Button>
            <Button onClick={handleBulkCommand} disabled={bulkLoading}>
              {bulkLoading ? "Wird gesendet..." : "Senden"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <InstallTokenDialog open={installDialog} onClose={() => setInstallDialog(false)} />
    </div>
  );
}
