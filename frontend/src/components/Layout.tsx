import { useEffect, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { Monitor, Clock, Users, Layers, LogOut, Shield, Settings } from "lucide-react";
import { devices } from "@/lib/api";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Toaster } from "@/components/Toaster";
import { toast } from "@/lib/useToast";

export function Layout() {
  const navigate = useNavigate();
  const [pendingCount, setPendingCount] = useState(0);

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
        }
        lastCount = data.count;
        setPendingCount(data.count);
      } catch {}
    };
    fetchCount();
    const interval = setInterval(fetchCount, 30_000);
    return () => clearInterval(interval);
  }, []);

  const handleLogout = () => {
    localStorage.removeItem("token");
    localStorage.removeItem("username");
    navigate("/login");
  };

  const navItems = [
    { to: "/devices", icon: Monitor, label: "Geräte" },
    {
      to: "/pending",
      icon: Clock,
      label: "Ausstehend",
      badge: pendingCount > 0 ? pendingCount : undefined,
    },
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

        {/* Logout */}
        <div className="p-3 border-t border-border">
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
    </div>
  );
}
