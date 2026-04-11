import { useEffect, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  LayoutDashboard, Monitor, Clock, Users, Layers, LogOut,
  Shield, Settings, Package, Sun, Moon, Link, Plus
} from "lucide-react";
import { devices } from "@/lib/api";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Toaster } from "@/components/Toaster";
import { toast } from "@/lib/useToast";
import { InstallTokenDialog } from "@/components/InstallTokenDialog";

export function Layout() {
  const navigate = useNavigate();
  const [pendingCount, setPendingCount] = useState(0);
  const [isDark, setIsDark] = useState(() => localStorage.getItem("theme") !== "light");
  const [installDialog, setInstallDialog] = useState(false);

  useEffect(() => {
    let lastCount: number | null = null;
    const fetchCount = async () => {
      try {
        const data = await devices.getPendingCount();
        if (lastCount !== null && data.count > lastCount) {
          const diff = data.count - lastCount;
          toast({
            title: `${diff} neue${diff > 1 ? " Geräte" : "s Gerät"} wartet auf Freigabe`,
            description: "Unter 'Ausstehend' findest du die Anfragen.",
            variant: "warning",
          });
          try {
            const ctx = new AudioContext();
            const gain = ctx.createGain();
            gain.gain.setValueAtTime(0.25, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.5);
            gain.connect(ctx.destination);
            [880, 1100].forEach((freq, i) => {
              const osc = ctx.createOscillator();
              osc.type = "sine";
              osc.frequency.setValueAtTime(freq, ctx.currentTime + i * 0.12);
              osc.connect(gain);
              osc.start(ctx.currentTime + i * 0.12);
              osc.stop(ctx.currentTime + i * 0.12 + 0.3);
            });
          } catch { /* AudioContext not available */ }
        }
        lastCount = data.count;
        setPendingCount(data.count);
      } catch {}
    };
    fetchCount();
    const interval = setInterval(fetchCount, 30_000);
    return () => clearInterval(interval);
  }, []);

  const toggleTheme = () => {
    const next = !isDark;
    setIsDark(next);
    localStorage.setItem("theme", next ? "dark" : "light");
    document.documentElement.classList.toggle("dark", next);
  };

  const handleLogout = () => {
    localStorage.removeItem("token");
    localStorage.removeItem("username");
    localStorage.removeItem("role");
    navigate("/login");
  };

  const navItems = [
    { to: "/dashboard", icon: LayoutDashboard, label: "Dashboard" },
    { to: "/devices", icon: Monitor, label: "Geräte" },
    {
      to: "/pending",
      icon: Clock,
      label: "Ausstehend",
      badge: pendingCount > 0 ? pendingCount : undefined,
    },
    { to: "/software", icon: Package, label: "Software" },
    { to: "/groups", icon: Layers, label: "Gruppen" },
    { to: "/customers", icon: Users, label: "Kunden" },
    { to: "/settings", icon: Settings, label: "Einstellungen" },
  ];

  return (
    <div className="flex h-screen overflow-hidden">
      {/* Sidebar */}
      <aside className="w-60 flex-shrink-0 border-r border-border bg-card flex flex-col">
        {/* Logo */}
        <div className="flex items-center gap-2.5 px-5 py-4 border-b border-border">
          <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary/20">
            <Shield className="h-4 w-4 text-primary" />
          </div>
          <div>
            <div className="text-sm font-semibold text-foreground">HackIT Sentry</div>
            <div className="text-xs text-muted-foreground">Device Manager</div>
          </div>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-3 py-4 space-y-1">
          {navItems.map(({ to, icon: Icon, label, badge }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                cn(
                  "flex items-center justify-between gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                  isActive
                    ? "bg-primary/15 text-primary"
                    : "text-muted-foreground hover:bg-accent hover:text-foreground"
                )
              }
            >
              <span className="flex items-center gap-2.5">
                <Icon className="h-4 w-4" />
                {label}
              </span>
              {badge !== undefined && (
                <Badge variant="destructive" className="h-5 min-w-[1.25rem] px-1.5 text-xs">
                  {badge}
                </Badge>
              )}
            </NavLink>
          ))}
        </nav>

        {/* Bottom: quick panel + theme toggle + logout */}
        <div className="p-3 border-t border-border space-y-1">

          {/* Install token quick panel */}
          <div className="rounded-md border border-border bg-muted/40 p-2.5 mb-2">
            <div className="flex items-center justify-between mb-1.5">
              <span className="flex items-center gap-1.5 text-xs font-medium text-foreground">
                <Link className="h-3.5 w-3.5" />
                Installationslinks
              </span>
              <Button
                size="icon"
                variant="ghost"
                className="h-6 w-6"
                onClick={() => setInstallDialog(true)}
              >
                <Plus className="h-3.5 w-3.5" />
              </Button>
            </div>
            <button
              className="w-full text-left text-xs text-muted-foreground hover:text-foreground transition-colors"
              onClick={() => setInstallDialog(true)}
            >
              Gerät per Link hinzufügen →
            </button>
          </div>

          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start text-muted-foreground hover:text-foreground"
            onClick={toggleTheme}
          >
            {isDark
              ? <><Sun className="h-4 w-4 mr-2" />Light Mode</>
              : <><Moon className="h-4 w-4 mr-2" />Dark Mode</>
            }
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start text-muted-foreground hover:text-foreground"
            onClick={handleLogout}
          >
            <LogOut className="h-4 w-4 mr-2" />
            Abmelden
          </Button>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
      <Toaster />
      <InstallTokenDialog open={installDialog} onClose={() => setInstallDialog(false)} />
    </div>
  );
}
