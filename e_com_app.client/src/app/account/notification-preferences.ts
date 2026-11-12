import { Component, OnInit, inject, signal } from '@angular/core';
import { AuthService } from '../core/auth.service';
import { NotificationsService, Preference } from '../notifications/notifications.service';

type ChannelKey = 'email' | 'sms' | 'push' | 'inApp';

/** /account/notifications — GET/PUT /api/v1/me/notification-preferences. Each change saves immediately. */
@Component({
  selector: 'app-notification-preferences',
  standalone: false,
  templateUrl: './notification-preferences.html',
  styleUrl: './notification-preferences.css',
})
export class NotificationPreferences implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(NotificationsService);

  protected readonly prefs = signal<Preference[] | null>(null);
  protected readonly loadError = signal(false);
  protected readonly status = signal<string | null>(null);

  protected readonly channels: { key: ChannelKey; label: string }[] = [
    { key: 'email', label: 'Email' },
    { key: 'sms', label: 'SMS' },
    { key: 'push', label: 'Push' },
    { key: 'inApp', label: 'In-app' },
  ];

  protected readonly categoryLabels: Record<string, string> = {
    orders: 'Orders and deliveries',
    promos: 'Offers and promotions',
    system: 'Account updates',
  };

  ngOnInit(): void {
    if (this.auth.isSignedIn()) {
      this.load();
    }
  }

  load(): void {
    this.loadError.set(false);
    this.api.preferences().subscribe({
      next: (p) => this.prefs.set(p),
      error: () => this.loadError.set(true),
    });
  }

  toggle(category: string, channel: ChannelKey, value: boolean): void {
    const before = this.prefs();
    const current = before?.find((p) => p.category === category);
    if (!before || !current) {
      return;
    }
    const updated: Preference = { ...current, [channel]: value };
    this.prefs.set(before.map((p) => (p.category === category ? updated : p)));
    this.status.set(null);
    this.api.savePreference(updated).subscribe({
      next: () => this.status.set('Saved.'),
      error: () => {
        this.prefs.set(before);                       // roll back so the screen shows what is really stored
        this.status.set("That change wasn't saved. Check your connection and try again.");
      },
    });
  }
}
