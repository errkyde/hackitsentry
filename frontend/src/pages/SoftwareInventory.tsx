import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Package, Search, ChevronDown, ChevronRight } from "lucide-react";
import { software, customers, groups, type SoftwareSummaryItem, type SoftwareInventoryItem, type Customer, type Group } from "@/lib/api";
import { Input } from "@/components/ui/input";
import { Card, CardContent } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";

export function SoftwareInventory() {
  const navigate = useNavigate();
  const [summary, setSummary] = useState<SoftwareSummaryItem[]>([]);
  const [details, setDetails] = useState<SoftwareInventoryItem[]>([]);
  const [customerList, setCustomerList] = useState<Customer[]>([]);
  const [groupList, setGroupList] = useState<Group[]>([]);
  const [search, setSearch] = useState("");
  const [selectedCustomer, setSelectedCustomer] = useState("all");
  const [selectedGroup, setSelectedGroup] = useState("all");
  const [expandedRow, setExpandedRow] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<"summary" | "detail">("summary");

  useEffect(() => {
    Promise.all([customers.list(), groups.list()]).then(([c, g]) => {
      setCustomerList(c);
      setGroupList(g);
    });
  }, []);

  const loadData = async () => {
    setLoading(true);
    const params: Record<string, string> = {};
    if (search) params.name = search;
    if (selectedCustomer !== "all") params.customerId = selectedCustomer;
    if (selectedGroup !== "all") params.groupId = selectedGroup;

    if (view === "summary") {
      const data = await software.getSummary(search || undefined);
      setSummary(data);
    } else {
      const data = await software.getInventory(params);
      setDetails(data);
    }
    setLoading(false);
  };

  useEffect(() => { loadData(); }, [search, selectedCustomer, selectedGroup, view]);

  return (
    <div className="p-6 space-y-5 max-w-6xl">
      <div>
        <h1 className="text-xl font-semibold">Software-Inventar</h1>
        <p className="text-sm text-muted-foreground">Geräteübergreifende Ansicht aller installierten Software</p>
      </div>

      {/* Filters */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground" />
          <Input
            placeholder="Software suchen..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>
        <Select value={selectedCustomer} onValueChange={setSelectedCustomer}>
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Alle Kunden" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle Kunden</SelectItem>
            {customerList.map(c => (
              <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={selectedGroup} onValueChange={setSelectedGroup}>
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Alle Gruppen" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Alle Gruppen</SelectItem>
            {groupList.map(g => (
              <SelectItem key={g.id} value={g.id}>{g.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex rounded-md border border-border overflow-hidden text-sm">
          <button
            className={`px-3 py-1.5 ${view === "summary" ? "bg-primary text-primary-foreground" : "bg-background text-muted-foreground hover:bg-accent"}`}
            onClick={() => setView("summary")}
          >
            Zusammenfassung
          </button>
          <button
            className={`px-3 py-1.5 ${view === "detail" ? "bg-primary text-primary-foreground" : "bg-background text-muted-foreground hover:bg-accent"}`}
            onClick={() => setView("detail")}
          >
            Details
          </button>
        </div>
      </div>

      {loading ? (
        <div className="text-center text-muted-foreground py-12">Laden...</div>
      ) : view === "summary" ? (
        <Card>
          <CardContent className="pt-0 pb-0">
            <div className="rounded-md overflow-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/30">
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground w-6"></th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Software</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Hersteller</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Geräte</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Versionen</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.map((item, i) => (
                    <>
                      <tr
                        key={i}
                        className="border-t border-border/50 cursor-pointer hover:bg-accent/30 transition-colors"
                        onClick={() => setExpandedRow(expandedRow === item.name ? null : item.name)}
                      >
                        <td className="px-4 py-2.5 text-muted-foreground">
                          {expandedRow === item.name
                            ? <ChevronDown className="h-3.5 w-3.5" />
                            : <ChevronRight className="h-3.5 w-3.5" />}
                        </td>
                        <td className="px-4 py-2.5 font-medium">{item.name}</td>
                        <td className="px-4 py-2.5 text-muted-foreground">{item.publisher || "—"}</td>
                        <td className="px-4 py-2.5">
                          <Badge variant="secondary">{item.deviceCount}</Badge>
                        </td>
                        <td className="px-4 py-2.5 text-muted-foreground font-mono text-xs">
                          {item.versions.slice(0, 3).join(", ")}
                          {item.versions.length > 3 && ` +${item.versions.length - 3}`}
                        </td>
                      </tr>
                      {expandedRow === item.name && (
                        <tr key={`${i}-detail`} className="bg-muted/20">
                          <td colSpan={5} className="px-8 py-2">
                            <div className="flex flex-wrap gap-1">
                              {item.versions.map((v, vi) => (
                                <span key={vi} className="text-xs bg-muted px-2 py-0.5 rounded font-mono">{v}</span>
                              ))}
                            </div>
                          </td>
                        </tr>
                      )}
                    </>
                  ))}
                  {summary.length === 0 && (
                    <tr><td colSpan={5} className="px-4 py-10 text-center text-muted-foreground">Keine Software gefunden.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="pt-0 pb-0">
            <div className="rounded-md overflow-hidden max-h-[600px] overflow-y-auto">
              <table className="w-full text-sm">
                <thead className="sticky top-0 bg-muted/80 backdrop-blur">
                  <tr className="border-b border-border">
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Software</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Version</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Hersteller</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Gerät</th>
                    <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Kunde</th>
                  </tr>
                </thead>
                <tbody>
                  {details.map(item => (
                    <tr key={item.id} className="border-t border-border/50">
                      <td className="px-4 py-2">{item.name}</td>
                      <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{item.version}</td>
                      <td className="px-4 py-2 text-muted-foreground">{item.publisher}</td>
                      <td className="px-4 py-2">
                        <button
                          className="hover:underline text-primary"
                          onClick={() => navigate(`/devices/${item.device.id}`)}
                        >
                          {item.device.hostname}
                        </button>
                      </td>
                      <td className="px-4 py-2 text-muted-foreground">{item.customer?.name ?? "—"}</td>
                    </tr>
                  ))}
                  {details.length === 0 && (
                    <tr><td colSpan={5} className="px-4 py-10 text-center text-muted-foreground">Keine Einträge gefunden.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
            <p className="text-xs text-muted-foreground px-4 py-2 border-t border-border">{details.length} Einträge</p>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
