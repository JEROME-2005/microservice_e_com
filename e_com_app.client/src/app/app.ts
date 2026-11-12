import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './core/auth.service';

/** App shell: header (brand, notification bell, account links) + routed page. */
@Component({
  selector: 'app-root',
  standalone: false,
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('LankaMart');
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  signOut(): void {
    this.auth.signOut();
    this.router.navigateByUrl('/');
  }
}
