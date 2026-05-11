import { useState } from "react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { CheckCircle2, Save, Eye, EyeOff } from "lucide-react";

type Mode = "global" | "device";

interface YNOption {
  key: string;
  label: string;
  description?: string;
  type: "yn";
}
interface SelectOption {
  key: string;
  label: string;
  description?: string;
  type: "select";
  values: { value: string; label: string }[];
}
interface TextOption {
  key: string;
  label: string;
  description?: string;
  type: "text";
  placeholder?: string;
}
interface PasswordOption {
  key: string;
  label: string;
  description?: string;
  type: "password";
  placeholder?: string;
}
type OptionDef = YNOption | SelectOption | TextOption | PasswordOption;

const GROUPS: { label: string; options: OptionDef[] }[] = [
  {
    label: "Sicherheit & Zugang",
    options: [
      {
        key: "approve-mode",
        label: "Verbindungsannahme",
        description: "Wie eingehende Verbindungsanfragen akzeptiert werden",
        type: "select",
        values: [
          { value: "password", label: "Passwort" },
          { value: "click", label: "Klick-Bestätigung" },
        ],
      },
      {
        key: "verification-method",
        label: "Passwort-Methode",
        description: "Welche Passwort-Typen für die Authentifizierung zulässig sind",
        type: "select",
        values: [
          { value: "UseTemporaryPassword", label: "Temporär" },
          { value: "UsePermanentPassword", label: "Dauerhaft" },
          { value: "UseBothPasswords", label: "Beides" },
          { value: "NoBothPassword", label: "Kein Passwort" },
        ],
      },
      {
        key: "allow-logon-screen-password",
        label: "Passwort am Sperrbildschirm",
        description: "Verbindung auch bei gesperrtem/UAC-Bildschirm erlauben",
        type: "yn",
      },
      {
        key: "lock-after-session-end",
        label: "PC sperren nach Session",
        description: "Windows sperren wenn die Fernsteuerung endet",
        type: "yn",
      },
      {
        key: "allow-remote-config-modification",
        label: "Remote-Konfiguration erlauben",
        description: "Zuschaltende Person kann RustDesk-Einstellungen auf dem Gerät ändern",
        type: "yn",
      },
      {
        key: "permanent-password",
        label: "Dauerhaftes Passwort",
        description: "Festes Passwort für RustDesk (wird über rustdesk.exe --password gesetzt, nicht im TOML gespeichert)",
        type: "password",
        placeholder: "Passwort eingeben…",
      },
      {
        key: "access-pin",
        label: "Einstellungs-PIN",
        description: "PIN-Schutz für die RustDesk-Einstellungen — lokaler Benutzer braucht diesen PIN zum Ändern",
        type: "password",
        placeholder: "PIN eingeben…",
      },
      {
        key: "whitelist",
        label: "IP-Whitelist",
        description: "Nur diese IPs dürfen sich verbinden (kommagetrennt, leer = alle erlaubt)",
        type: "text",
        placeholder: "192.168.1.0/24, 10.0.0.1",
      },
    ],
  },
  {
    label: "Berechtigungen",
    options: [
      { key: "enable-keyboard", label: "Tastatur & Maus", type: "yn" },
      { key: "enable-clipboard", label: "Zwischenablage (Copy/Paste Text)", type: "yn" },
      { key: "enable-file-transfer", label: "Dateiübertragung (Sitzung)", type: "yn" },
      { key: "enable-file-copy-paste", label: "Datei-Kopieren/Einfügen", type: "yn" },
      { key: "enable-audio", label: "Audio übertragen", type: "yn" },
      { key: "enable-terminal", label: "Terminal-Zugang", type: "yn" },
      { key: "enable-tunnel", label: "TCP-Tunnel / Port-Forwarding", type: "yn" },
      { key: "enable-remote-restart", label: "Neustart erlauben", type: "yn" },
      { key: "enable-camera", label: "Kamerazugang", type: "yn" },
      {
        key: "enable-block-input",
        label: "Lokale Eingabe blockieren",
        description: "Lokale Maus/Tastatur während Session sperren (nur Windows)",
        type: "yn",
      },
      { key: "enable-remote-printer", label: "Drucker", type: "yn" },
      { key: "enable-record-session", label: "Sitzungsaufzeichnung", type: "yn" },
    ],
  },
  {
    label: "Anzeige",
    options: [
      {
        key: "view-only",
        label: "Nur-Ansicht-Modus",
        description: "Keine Eingabe vom Remote-Controller möglich",
        type: "yn",
      },
      {
        key: "view-style",
        label: "Ansichtsmodus",
        type: "select",
        values: [
          { value: "adaptive", label: "Adaptiv" },
          { value: "original", label: "Original" },
        ],
      },
      {
        key: "allow-remove-wallpaper",
        label: "Hintergrundbild entfernen",
        description: "Performance-Optimierung während der Session",
        type: "yn",
      },
      { key: "show-remote-cursor", label: "Remote-Mauszeiger anzeigen", type: "yn" },
      { key: "follow-remote-cursor", label: "Remote-Mauszeiger folgen", type: "yn" },
    ],
  },
  {
    label: "Netzwerk",
    options: [
      { key: "direct-server", label: "Direktverbindung (ohne Relay)", type: "yn" },
      { key: "disable-udp", label: "Nur TCP (UDP deaktivieren)", type: "yn" },
      { key: "enable-lan-discovery", label: "LAN-Geräte-Erkennung", type: "yn" },
      { key: "allow-websocket", label: "WebSocket erlauben", type: "yn" },
    ],
  },
];

function SegmentedButtons({
  optKey,
  choices,
  value,
  onChange,
}: {
  optKey: string;
  choices: { value: string; label: string }[];
  value: string;
  onChange: (key: string, val: string) => void;
}) {
  return (
    <div className="flex rounded-md border border-input overflow-hidden text-xs font-medium shrink-0">
      {choices.map(({ value: v, label }, i) => (
        <button
          key={v}
          type="button"
          className={cn(
            "px-2.5 py-1.5 transition-colors whitespace-nowrap",
            value === v
              ? "bg-primary text-primary-foreground"
              : "bg-background text-muted-foreground hover:text-foreground hover:bg-muted",
            i > 0 && "border-l border-input"
          )}
          onClick={() => onChange(optKey, value === v ? "" : v)}
        >
          {label}
        </button>
      ))}
    </div>
  );
}

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mode: Mode;
  options: Record<string, string>;
  onChange: (options: Record<string, string>) => void;
  onSave: () => void | Promise<void>;
  saving?: boolean;
  saved?: boolean;
  title?: string;
  description?: string;
}

export function RustDeskOptionsDialog({ open, onOpenChange, mode, options, onChange, onSave, saving, saved, title, description }: Props) {
  const [showPasswords, setShowPasswords] = useState<Record<string, boolean>>({});
  const unsetLabel = mode === "global" ? "Standard" : "Global";

  const set = (key: string, val: string) => {
    const next = { ...options };
    if (val === "") delete next[key];
    else next[key] = val;
    onChange(next);
  };

  const handleSave = async () => {
    await onSave();
  };

  const activeCount = Object.keys(options).length;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[85vh] flex flex-col p-0 gap-0">
        <DialogHeader className="px-6 pt-6 pb-4 border-b border-border shrink-0">
          <DialogTitle>
            {title ?? (mode === "global" ? "Globale RustDesk-Optionen" : "Gerätespezifische RustDesk-Optionen")}
          </DialogTitle>
          <p className="text-xs text-muted-foreground mt-1">
            {description ?? (mode === "global"
              ? `Standard für alle Geräte. "Standard" = RustDesk-Vorgabe, nicht in Konfiguration geschrieben.`
              : `Überschreibt globale Einstellungen nur für dieses Gerät. "Global" = globale Einstellung verwenden.`)}
            {activeCount > 0 && (
              <span className="ml-1.5 font-medium text-foreground">{activeCount} Option{activeCount !== 1 ? "en" : ""} gesetzt.</span>
            )}
          </p>
        </DialogHeader>

        <div className="flex-1 overflow-y-auto px-6 py-5 space-y-7 min-h-0">
          {GROUPS.map((group) => (
            <div key={group.label}>
              <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">{group.label}</p>
              <div className="space-y-2.5">
                {group.options.map((opt) => (
                  <div key={opt.key} className="flex items-center gap-4 min-h-[38px]">
                    <div className="flex-1 min-w-0">
                      <p className="text-sm leading-tight">{opt.label}</p>
                      {opt.description && (
                        <p className="text-xs text-muted-foreground leading-tight mt-0.5">{opt.description}</p>
                      )}
                    </div>

                    {opt.type === "yn" && (
                      <SegmentedButtons
                        optKey={opt.key}
                        choices={[
                          { value: "", label: unsetLabel },
                          { value: "Y", label: "Ja" },
                          { value: "N", label: "Nein" },
                        ]}
                        value={options[opt.key] ?? ""}
                        onChange={set}
                      />
                    )}

                    {opt.type === "select" && (
                      <SegmentedButtons
                        optKey={opt.key}
                        choices={[{ value: "", label: unsetLabel }, ...opt.values]}
                        value={options[opt.key] ?? ""}
                        onChange={set}
                      />
                    )}

                    {opt.type === "text" && (
                      <Input
                        className="w-52 h-8 text-xs"
                        placeholder={opt.placeholder ?? ""}
                        value={options[opt.key] ?? ""}
                        onChange={(e) => set(opt.key, e.target.value)}
                      />
                    )}

                    {opt.type === "password" && (
                      <div className="flex items-center gap-1 w-52">
                        <Input
                          className="h-8 text-xs flex-1"
                          type={showPasswords[opt.key] ? "text" : "password"}
                          placeholder={opt.placeholder ?? ""}
                          value={options[opt.key] ?? ""}
                          onChange={(e) => set(opt.key, e.target.value)}
                          autoComplete="new-password"
                        />
                        <button
                          type="button"
                          className="shrink-0 text-muted-foreground hover:text-foreground"
                          onClick={() => setShowPasswords(p => ({ ...p, [opt.key]: !p[opt.key] }))}
                        >
                          {showPasswords[opt.key]
                            ? <EyeOff className="h-3.5 w-3.5" />
                            : <Eye className="h-3.5 w-3.5" />}
                        </button>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>

        <div className="px-6 py-4 border-t border-border shrink-0 flex items-center gap-3">
          <Button onClick={handleSave} disabled={saving}>
            <Save className="h-3.5 w-3.5 mr-1.5" />
            {saving ? "Wird gespeichert…" : "Speichern"}
          </Button>
          {saved && (
            <span className="text-xs text-emerald-500 flex items-center gap-1">
              <CheckCircle2 className="h-3.5 w-3.5" />
              Gespeichert
            </span>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
