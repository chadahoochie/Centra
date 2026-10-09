import { Injectable, signal } from '@angular/core';

export interface NotificationToast {
  id: string;
  type: 'success' | 'error' | 'info' | 'warning';
  title?: string;
  message: string;
  timestamp: Date;
}

@Injectable({
  providedIn: 'root'
})
export class NotificationService {
  readonly toasts = signal<NotificationToast[]>([]);

  show(type: 'success' | 'error' | 'info' | 'warning', message: string, title?: string, durationMs: number = 4000): void {
    const id = Math.random().toString(36).substring(2, 9);
    const toast: NotificationToast = {
      id,
      type,
      title,
      message,
      timestamp: new Date()
    };

    this.toasts.update(list => [toast, ...list].slice(0, 5));

    if (durationMs > 0 && typeof window !== 'undefined') {
      setTimeout(() => {
        this.remove(id);
      }, durationMs);
    }
  }

  success(message: string, title?: string): void {
    this.show('success', message, title);
  }

  error(message: string, title?: string): void {
    this.show('error', message, title, 6000);
  }

  info(message: string, title?: string): void {
    this.show('info', message, title);
  }

  warning(message: string, title?: string): void {
    this.show('warning', message, title);
  }

  remove(id: string): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }
}
