import { useState } from "react";
import { Monitor, Users, Layers, Settings, ChevronRight, ChevronLeft, CheckCircle2, Download } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

interface Step {
  icon: React.ElementType;
  title: string;
  description: string;
  hint: string;
}

const STEPS: Step[] = [
  {
    icon: Download,
    title: "Agent installieren",
    description: "Lade den HITSight Agent auf dem Windows-Gerät herunter und installiere ihn. Der Agent verbindet sich automatisch mit diesem Dashboard.",
    hint: "Den Installations-Link findest du in der Sidebar unter dem Plus-Symbol.",
  },
  {
    icon: Monitor,
    title: "Gerät freigeben",
    description: "Nach der Installation erscheint das Gerät unter 'Ausstehend'. Gib es frei und weise es optional einem Kunden oder einer Gruppe zu.",
    hint: "Neue Geräteanfragen werden dir als Benachrichtigung angezeigt.",
  },
  {
    icon: Users,
    title: "Kunden anlegen",
    description: "Lege Kunden an, um Geräte zu organisieren. Jedes Gerät kann genau einem Kunden zugewiesen werden.",
    hint: "Kunden findest du im Navigationsmenü links.",
  },
  {
    icon: Layers,
    title: "Gruppen nutzen",
    description: "Gruppen sind visuelle Labels für deine Geräte — z.B. nach Standort oder Funktion. Ein Gerät kann einer Gruppe und einem Kunden gleichzeitig angehören.",
    hint: "Gruppen kannst du mit Farben kennzeichnen, um sie schnell zu unterscheiden.",
  },
  {
    icon: Settings,
    title: "Einstellungen konfigurieren",
    description: "Richte E-Mail-Benachrichtigungen ein, damit du bei Ausfällen sofort informiert wirst. Weitere Optionen findest du unter Einstellungen.",
    hint: "Du kannst auch pro Gerät individuelle Benachrichtigungsregeln festlegen.",
  },
];

interface Props {
  open: boolean;
  onClose: () => void;
}

export function OnboardingModal({ open, onClose }: Props) {
  const [step, setStep] = useState(0);
  const current = STEPS[step];
  const Icon = current.icon;
  const isLast = step === STEPS.length - 1;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Willkommen bei HackIT Sight</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col items-center text-center gap-4 py-4">
          <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-primary/15 ring-1 ring-primary/20">
            <Icon className="h-8 w-8 text-primary" />
          </div>
          <div>
            <h3 className="text-lg font-semibold mb-1">{current.title}</h3>
            <p className="text-sm text-muted-foreground leading-relaxed">{current.description}</p>
          </div>
          <p className="text-xs text-muted-foreground bg-muted/60 rounded-md px-3 py-2 w-full text-left">
            💡 {current.hint}
          </p>
        </div>

        {/* Step dots */}
        <div className="flex items-center justify-center gap-1.5 my-1">
          {STEPS.map((_, i) => (
            <div
              key={i}
              className={`h-1.5 rounded-full transition-all ${
                i === step ? "w-5 bg-primary" : "w-1.5 bg-muted-foreground/30"
              }`}
            />
          ))}
        </div>

        <div className="flex items-center justify-between pt-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setStep(s => s - 1)}
            disabled={step === 0}
          >
            <ChevronLeft className="h-4 w-4 mr-1" />
            Zurück
          </Button>

          {isLast ? (
            <Button size="sm" onClick={onClose}>
              <CheckCircle2 className="h-4 w-4 mr-1.5" />
              Fertig
            </Button>
          ) : (
            <Button size="sm" onClick={() => setStep(s => s + 1)}>
              Weiter
              <ChevronRight className="h-4 w-4 ml-1" />
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
