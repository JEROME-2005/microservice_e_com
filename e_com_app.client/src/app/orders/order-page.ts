import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

interface Tracking {
  orderNumber: string;
  status: string;
  timeline: { status: string; note?: string | null; at: string }[];
}

/** /orders/:id — where a notification's deep link lands. Minimal tracking view (GET /api/v1/orders/{id}/tracking). */
@Component({
  selector: 'app-order-page',
  standalone: false,
  templateUrl: './order-page.html',
})
export class OrderPage implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);

  protected readonly tracking = signal<Tracking | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.error.set(null);
    this.http.get<Tracking>(`/api/v1/orders/${encodeURIComponent(id)}/tracking`).subscribe({
      next: (t) => this.tracking.set(t),
      error: () => this.error.set("We couldn't find that order. It may belong to another account, or the link is out of date."),
    });
  }
}
