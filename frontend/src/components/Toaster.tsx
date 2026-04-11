import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  ToastProvider, ToastViewport, Toast, ToastTitle, ToastDescription, ToastClose,
} from "@/components/ui/toast";
import { useToast } from "@/lib/useToast";

export function Toaster() {
  const { messages, subscribe } = useToast();

  useEffect(() => {
    const unsubscribe = subscribe();
    return unsubscribe;
  }, [subscribe]);

  return (
    <ToastProvider>
      {messages.map((msg) => (
        <Toast key={msg.id} variant={msg.variant} open>
          <div className="flex-1 min-w-0">
            <ToastTitle>{msg.title}</ToastTitle>
            {msg.description && <ToastDescription>{msg.description}</ToastDescription>}
          </div>
          <ToastClose />
        </Toast>
      ))}
      <ToastViewport />
    </ToastProvider>
  );
}
