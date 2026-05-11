import { cn } from "@/lib/utils";

export function TriToggle({
  label,
  value,
  onChange,
  offLabel = "Aus",
  nullLabel = "Global",
  onLabel = "An",
}: {
  label: string;
  value: boolean | null;
  onChange: (v: boolean | null) => void;
  offLabel?: string;
  nullLabel?: string;
  onLabel?: string;
}) {
  return (
    <div className="flex items-center justify-between py-2.5 px-3 border-b border-border/50 last:border-0">
      <span className="text-sm">{label}</span>
      <div className="flex rounded-md border border-border overflow-hidden text-xs">
        <button
          type="button"
          className={cn("px-2.5 py-1 transition-colors", value === false ? "bg-rose-500/20 text-rose-600 dark:text-rose-400 font-medium" : "hover:bg-muted")}
          onClick={() => onChange(false)}
        >{offLabel}</button>
        <button
          type="button"
          className={cn("px-2.5 py-1 border-x border-border transition-colors", value === null ? "bg-muted font-medium" : "hover:bg-muted")}
          onClick={() => onChange(null)}
        >{nullLabel}</button>
        <button
          type="button"
          className={cn("px-2.5 py-1 transition-colors", value === true ? "bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 font-medium" : "hover:bg-muted")}
          onClick={() => onChange(true)}
        >{onLabel}</button>
      </div>
    </div>
  );
}
