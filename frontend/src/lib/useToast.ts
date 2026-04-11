import { useState, useCallback } from "react";

export interface ToastMessage {
  id: string;
  title: string;
  description?: string;
  variant?: "default" | "success" | "warning";
}

// Simple global store pattern
const listeners = new Set<(toasts: ToastMessage[]) => void>();
let toasts: ToastMessage[] = [];

function notify() {
  listeners.forEach(l => l([...toasts]));
}

export function toast(msg: Omit<ToastMessage, "id">) {
  const id = Math.random().toString(36).slice(2);
  toasts = [...toasts, { ...msg, id }];
  notify();
  // Auto-remove after 4s
  setTimeout(() => {
    toasts = toasts.filter(t => t.id !== id);
    notify();
  }, 4000);
}

export function useToast() {
  const [messages, setMessages] = useState<ToastMessage[]>([]);

  const subscribe = useCallback(() => {
    const handler = (t: ToastMessage[]) => setMessages(t);
    listeners.add(handler);
    return () => { listeners.delete(handler); };
  }, []);

  return { messages, subscribe };
}
