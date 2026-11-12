import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';

/** Public category list from the Catalog service (GET /api/v1/categories through the gateway). */
export interface Category {
  id: string;
  slug: string;
  name: string;
}

@Component({
  selector: 'app-home-page',
  standalone: false,
  templateUrl: './home-page.html',
})
export class HomePage implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly categories = signal<Category[] | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.loadCategories();
  }

  loadCategories(): void {
    this.error.set(null);
    this.http.get<Category[]>('/api/v1/categories').subscribe({
      next: (result) => this.categories.set(result),
      error: () => this.error.set('Could not reach the LankaMart gateway. Start the backend with the Aspire AppHost, then try again.'),
    });
  }
}
