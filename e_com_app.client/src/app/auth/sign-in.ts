import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-sign-in',
  standalone: false,
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.css',
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  submit(): void {
    if (!this.email.trim() || !this.password) {
      this.error.set('Enter your email and password to sign in.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.auth.signIn(this.email.trim(), this.password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        this.router.navigateByUrl(returnUrl?.startsWith('/') ? returnUrl : '/');
      },
      error: (e: unknown) => {
        this.busy.set(false);
        const status = e instanceof HttpErrorResponse ? e.status : 0;
        this.error.set(
          status === 401
            ? "That email and password don't match an account. Check them and try again."
            : status === 429
              ? 'Too many attempts. Wait a few minutes, then try again.'
              : "We couldn't reach LankaMart. Check your connection and try again.",
        );
      },
    });
  }
}
