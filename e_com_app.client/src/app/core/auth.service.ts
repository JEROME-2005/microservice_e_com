import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

export interface Me {
  userId: string;
  email: string;
  roles: string[];
  emailVerified: boolean;
  phoneVerified: boolean;
}

interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

const ACCESS = 'lm.access';
const REFRESH = 'lm.refresh';

/**
 * Minimal sign-in state for the storefront shell (Phase 2 needs it for the notification bell).
 * Tokens live in sessionStorage (cleared when the tab closes) — known gap #4: the httpOnly-cookie refresh flow
 * and single-flight token refresh arrive with the full storefront (docs/adr/0018-angular-instead-of-react.md).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  readonly token = signal<string | null>(sessionStorage.getItem(ACCESS));
  readonly me = signal<Me | null>(null);
  readonly isSignedIn = computed(() => this.token() !== null);

  constructor() {
    if (this.token()) {
      this.loadMe().subscribe({ error: () => this.clear() });
    }
  }

  signIn(email: string, password: string): Observable<void> {
    return this.http.post<TokenResponse>('/api/v1/auth/login', { email, password }).pipe(
      tap((t) => {
        sessionStorage.setItem(ACCESS, t.accessToken);
        sessionStorage.setItem(REFRESH, t.refreshToken);
        this.token.set(t.accessToken);
      }),
      map(() => undefined),
      tap(() => this.loadMe().subscribe()),
    );
  }

  /** Revokes the session on the server (best effort) and forgets it locally either way. */
  signOut(): void {
    const refreshToken = sessionStorage.getItem(REFRESH);
    if (refreshToken && this.token()) {
      this.http.post('/api/v1/auth/logout', { refreshToken }).subscribe({ error: () => undefined });
    }
    this.clear();
  }

  /** Called by the interceptor when the API answers 401: the access token expired or was revoked. */
  clear(): void {
    sessionStorage.removeItem(ACCESS);
    sessionStorage.removeItem(REFRESH);
    this.token.set(null);
    this.me.set(null);
  }

  private loadMe(): Observable<Me> {
    return this.http.get<Me>('/api/v1/auth/me').pipe(tap((me) => this.me.set(me)));
  }
}
