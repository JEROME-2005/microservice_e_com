import { Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { InboxItem, NotificationsService } from './notifications.service';

/** The in-app inbox bell (Phase 2 §3.9). Text is rendered as TEXT ({{ }} interpolation), never as HTML,
 *  so a hostile product or customer name can't inject script. */
@Component({
  selector: 'app-notification-bell',
  standalone: false,
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.css',
})
export class NotificationBell {
  protected readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationsService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly open = signal(false);
  protected readonly items = signal<InboxItem[] | null>(null);
  protected readonly error = signal(false);

  toggle(): void {
    this.open.update((o) => !o);
    if (this.open()) {
      this.load();
    }
  }

  load(): void {
    this.error.set(false);
    this.items.set(null);
    this.notifications.inbox().subscribe({
      next: (items) => this.items.set(items),
      error: () => this.error.set(true),
    });
  }

  openItem(item: InboxItem): void {
    if (!item.read) {
      this.notifications.markRead(item.id).subscribe();
    }
    this.open.set(false);
    if (item.deepLink?.startsWith('/')) {          // only in-app paths; never navigate to an absolute URL from data
      this.router.navigateByUrl(item.deepLink);
    }
  }

  markAllRead(): void {
    this.notifications.markAllRead().subscribe(() => this.items.update((list) => list?.map((i) => ({ ...i, read: true })) ?? null));
  }

  @HostListener('document:keydown.escape')
  close(): void {
    this.open.set(false);
  }

  @HostListener('document:click', ['$event'])
  closeOnOutsideClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
