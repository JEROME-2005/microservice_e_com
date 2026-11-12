import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, merge, of, switchMap, timer } from 'rxjs';
import { AuthService } from '../core/auth.service';

export interface InboxItem {
  id: string;
  title: string;
  body: string;
  deepLink?: string | null;
  category: string;
  read: boolean;
  createdAt: string;
}

export interface Preference {
  category: string;
  email: boolean;
  sms: boolean;
  push: boolean;
  inApp: boolean;
}

interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

/** Notification service API (through the gateway: /api/v1/me/*). */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly refresh$ = new Subject<void>();

  /** Unread count: polled every 30 s while signed in (server-sent events arrive with live tracking in Phase 4). */
  readonly unreadCount = toSignal(
    toObservable(this.auth.isSignedIn).pipe(
      switchMap((signedIn) =>
        signedIn
          ? merge(timer(0, 30_000), this.refresh$).pipe(
              switchMap(() =>
                this.http.get<{ count: number }>('/api/v1/me/notifications/unread-count').pipe(
                  map((r) => r.count),
                  catchError(() => of(0)),
                ),
              ),
            )
          : of(0),
      ),
    ),
    { initialValue: 0 },
  );

  inbox(pageSize = 10): Observable<InboxItem[]> {
    return this.http
      .get<Paged<InboxItem>>('/api/v1/me/notifications', { params: { page_size: pageSize } })
      .pipe(map((p) => p.items));
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`/api/v1/me/notifications/${id}/read`, null).pipe(map(() => this.refresh$.next()));
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>('/api/v1/me/notifications/read-all', null).pipe(map(() => this.refresh$.next()));
  }

  preferences(): Observable<Preference[]> {
    return this.http.get<Preference[]>('/api/v1/me/notification-preferences');
  }

  savePreference(p: Preference): Observable<void> {
    return this.http.put<void>('/api/v1/me/notification-preferences', p);
  }
}
