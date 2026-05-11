import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Monitor, Wifi, WifiOff, Clock, Users, Layers,
  AlertTriangle, ShieldAlert, CalendarX, Terminal, CheckCircle2, Download
} from "lucide-react";
import { dashboard, software, type DashboardData, type SoftwareAlertItem } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

function StatCard({
  icon: Icon,
  label,
  value,
  sub,
  color,
  onClick,
}: {
  icon: React.ElementType;
  label: string;
  value: number | string;
  sub?: string;
  color?: string;
  onClick?: () => void;
}) {
  return (
    <Card
      className={cn("transition-colors", onClick && "cursor-pointer hover:bg-accent/50")}
      onClick={onClick}
    >
      <CardContent className="pt-5 pb-4">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{label}</p>
            <p className={cn("text-3xl font-bold mt-1", color)}>{value}</p>
            {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
          </div>
          <div className={cn("p-2 rounded-md bg-muted", color && `bg-opacity-10`)}>
            <Icon className={cn("h-5 w-5 text-muted-foreground", color)} />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export function Dashboard() {
  const navigate = useNavigate();
  const [data, setData] = useState<DashboardData | null>(null);
  const [alerts, setAlerts] = useState<SoftwareAlertItem[]>([]);
  const [ackLoading, setAckLoading] = useState<string | null>(null);

  const load = async () => {
    const [d, a] = await Promise.all([
      dashboard.get(),
      software.getAlerts(false),
    ]);
    setData(d);
    setAlerts(a);
  };

  useEffect(() => { load(); }, []);

  const handleAcknowledge = async (id: string) => {
    setAckLoading(id);
    await software.acknowledgeAlert(id).catch(() => {});
    setAckLoading(null);
    setAlerts(prev => prev.filter(a => a.id !== id));
    setData(prev => prev ? {
      ...prev,
      alerts: { ...prev.alerts, softwareAlerts: prev.alerts.softwareAlerts - 1 }
    } : prev);
  };

  const handleAcknowledgeAll = async () => {
    await software.acknowledgeAll().catch(() => {});
    setAlerts([]);
    setData(prev => prev ? { ...prev, alerts: { ...prev.alerts, softwareAlerts: 0 } } : prev);
  };

  if (!data) {
    return <div className="flex items-center justify-center h-full text-muted-foreground">Laden...</div>;
  }

  const onlinePct = data.devices.total > 0
    ? Math.round((data.devices.online / data.devices.total) * 100)
    : 0;

  return (
    <div className="p-4 sm:p-6 space-y-4 sm:space-y-6 max-w-7xl">
      <div>
        <h1 className="text-xl font-semibold">Dashboard</h1>
        <p className="text-sm text-muted-foreground">Übersicht über alle Geräte und Aktivitäten</p>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
        <StatCard
          icon={Monitor}
          label="Geräte gesamt"
          value={data.devices.total}
          onClick={() => navigate("/devices")}
        />
        <StatCard
          icon={Wifi}
          label="Online"
          value={data.devices.online}
          sub={`${onlinePct}%`}
          color="text-emerald-500"
          onClick={() => navigate("/devices?status=online")}
        />
        <StatCard
          icon={WifiOff}
          label="Offline"
          value={data.devices.offline}
          color={data.devices.offline > 0 ? "text-rose-500" : undefined}
          onClick={() => navigate("/devices?status=offline")}
        />
        <StatCard
          icon={Clock}
          label="Ausstehend"
          value={data.devices.pending}
          color={data.devices.pending > 0 ? "text-amber-500" : undefined}
          onClick={() => navigate("/pending")}
        />
        <StatCard
          icon={Users}
          label="Kunden"
          value={data.customers}
          onClick={() => navigate("/customers")}
        />
        <StatCard
          icon={Layers}
          label="Gruppen"
          value={data.groups}
          onClick={() => navigate("/groups")}
        />
      </div>

      {/* Alert overview */}
      {(data.alerts.softwareAlerts > 0 || data.alerts.expiringLicenses > 0 ||
        data.alerts.expiredLicenses > 0 || data.alerts.pendingCommands > 0 ||
        data.alerts.devicesWithUpdates > 0) && (
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          {data.alerts.softwareAlerts > 0 && (
            <Card className="border-amber-500/30 bg-amber-500/5">
              <CardContent className="pt-4 pb-3 flex items-center gap-3">
                <ShieldAlert className="h-5 w-5 text-amber-500 shrink-0" />
                <div>
                  <p className="text-sm font-medium text-amber-500">{data.alerts.softwareAlerts} Software-Alert{data.alerts.softwareAlerts !== 1 ? "s" : ""}</p>
                  <p className="text-xs text-muted-foreground">Blacklisted software detected</p>
                </div>
              </CardContent>
            </Card>
          )}
          {data.alerts.expiredLicenses > 0 && (
            <Card className="border-rose-500/30 bg-rose-500/5">
              <CardContent className="pt-4 pb-3 flex items-center gap-3">
                <CalendarX className="h-5 w-5 text-rose-500 shrink-0" />
                <div>
                  <p className="text-sm font-medium text-rose-500">{data.alerts.expiredLicenses} Lizenz{data.alerts.expiredLicenses !== 1 ? "en" : ""} abgelaufen</p>
                  <p className="text-xs text-muted-foreground">Erneuerung erforderlich</p>
                </div>
              </CardContent>
            </Card>
          )}
          {data.alerts.expiringLicenses > 0 && (
            <Card className="border-amber-500/30 bg-amber-500/5">
              <CardContent className="pt-4 pb-3 flex items-center gap-3">
                <AlertTriangle className="h-5 w-5 text-amber-500 shrink-0" />
                <div>
                  <p className="text-sm font-medium text-amber-500">{data.alerts.expiringLicenses} Lizenz{data.alerts.expiringLicenses !== 1 ? "en" : ""} laufen bald ab</p>
                  <p className="text-xs text-muted-foreground">Innerhalb 30 Tage</p>
                </div>
              </CardContent>
            </Card>
          )}
          {data.alerts.devicesWithUpdates > 0 && (
            <Card className="border-amber-500/30 bg-amber-500/5">
              <CardContent className="pt-4 pb-3 flex items-center gap-3">
                <Download className="h-5 w-5 text-amber-500 shrink-0" />
                <div>
                  <p className="text-sm font-medium text-amber-500">{data.alerts.devicesWithUpdates} Gerät{data.alerts.devicesWithUpdates !== 1 ? "e" : ""} mit ausstehenden Updates</p>
                  <p className="text-xs text-muted-foreground">Windows Updates verfügbar</p>
                </div>
              </CardContent>
            </Card>
          )}
          {data.alerts.pendingCommands > 0 && (
            <Card className="border-blue-500/30 bg-blue-500/5">
              <CardContent className="pt-4 pb-3 flex items-center gap-3">
                <Terminal className="h-5 w-5 text-blue-500 shrink-0" />
                <div>
                  <p className="text-sm font-medium text-blue-500">{data.alerts.pendingCommands} Befehl{data.alerts.pendingCommands !== 1 ? "e" : ""} ausstehend</p>
                  <p className="text-xs text-muted-foreground">Warten auf Agent</p>
                </div>
              </CardContent>
            </Card>
          )}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Software Alerts */}
        <div className="lg:col-span-2 space-y-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold">Aktive Software-Alerts</h2>
            {alerts.length > 1 && (
              <Button variant="outline" size="sm" onClick={handleAcknowledgeAll}>
                Alle bestätigen
              </Button>
            )}
          </div>
          {alerts.length === 0 ? (
            <Card>
              <CardContent className="pt-6 pb-5 flex items-center gap-3 text-muted-foreground">
                <CheckCircle2 className="h-4 w-4 text-emerald-500" />
                <span className="text-sm">Keine aktiven Software-Alerts</span>
              </CardContent>
            </Card>
          ) : (
            <div className="space-y-2">
              {alerts.map(alert => (
                <Card key={alert.id} className="border-amber-500/20">
                  <CardContent className="pt-3 pb-3 flex items-center justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <ShieldAlert className="h-3.5 w-3.5 text-amber-500 shrink-0" />
                        <span className="text-sm font-medium truncate">{alert.softwareName}</span>
                        {alert.softwareVersion && (
                          <span className="text-xs text-muted-foreground font-mono">{alert.softwareVersion}</span>
                        )}
                      </div>
                      <div className="text-xs text-muted-foreground mt-0.5">
                        <button
                          className="hover:underline text-foreground/70"
                          onClick={() => navigate(`/devices/${alert.device.id}`)}
                        >
                          {alert.device.hostname}
                        </button>
                        {alert.customer && <span> · {alert.customer.name}</span>}
                        <span> · Regel: {alert.rule.namePattern}</span>
                        <span> · {new Date(alert.detectedAt).toLocaleString("de-DE")}</span>
                      </div>
                    </div>
                    <Button
                      variant="ghost"
                      size="sm"
                      disabled={ackLoading === alert.id}
                      onClick={() => handleAcknowledge(alert.id)}
                      className="shrink-0"
                    >
                      {ackLoading === alert.id ? "..." : "OK"}
                    </Button>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}

          {/* Recent Audit Log */}
          <h2 className="text-sm font-semibold pt-2">Letzte Aktivitäten</h2>
          <Card>
            <CardContent className="pt-4 pb-2">
              {data.recentAuditLogs.length === 0 ? (
                <p className="text-sm text-muted-foreground py-2">Noch keine Aktivitäten.</p>
              ) : (
                <div className="space-y-0">
                  {data.recentAuditLogs.map(log => (
                    <div key={log.id} className="flex items-center gap-3 py-2 border-b border-border/50 last:border-0 text-sm">
                      <span className="text-muted-foreground w-28 shrink-0 text-xs">
                        {new Date(log.timestamp).toLocaleString("de-DE", { dateStyle: "short", timeStyle: "short" })}
                      </span>
                      <span className="font-medium w-24 shrink-0">{log.username}</span>
                      <span className="font-mono text-xs text-primary">{log.action}</span>
                      <span className="text-muted-foreground truncate">{log.entityType}</span>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        {/* Side: Devices by Group + Customer */}
        <div className="space-y-4">
          <Card>
            <CardHeader className="pb-2 pt-4">
              <CardTitle className="text-sm">Geräte nach Gruppe</CardTitle>
            </CardHeader>
            <CardContent className="pt-0 pb-3">
              {data.devicesByGroup.length === 0 ? (
                <p className="text-xs text-muted-foreground">Keine Gruppen.</p>
              ) : (
                <div className="space-y-2">
                  {data.devicesByGroup.slice(0, 6).map(g => (
                    <div key={g.id} className="flex items-center gap-2">
                      <div
                        className="h-2.5 w-2.5 rounded-full shrink-0"
                        style={{ backgroundColor: g.color ?? "#64748b" }}
                      />
                      <span className="text-sm truncate flex-1">{g.name}</span>
                      <Badge variant="secondary" className="text-xs">{g.deviceCount}</Badge>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-2 pt-4">
              <CardTitle className="text-sm">Geräte nach Kunde</CardTitle>
            </CardHeader>
            <CardContent className="pt-0 pb-3">
              {data.devicesByCustomer.length === 0 ? (
                <p className="text-xs text-muted-foreground">Keine Kunden.</p>
              ) : (
                <div className="space-y-2">
                  {data.devicesByCustomer.slice(0, 8).map(c => (
                    <div key={c.id} className="flex items-center gap-2">
                      <span className="text-sm truncate flex-1">{c.name}</span>
                      <Badge variant="secondary" className="text-xs">{c.deviceCount}</Badge>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
