import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { ConfirmedOrder } from '../checkout/checkout.service';

export interface OrderSummary {
  id: string; createdAt: string; status: string; paymentMethod: string; paymentResult: string;
  itemCount: number; subtotal: number; discount: number; total: number; isSimulation: boolean;
}

export interface OrderDetail {
  order: ConfirmedOrder;
  history: { status: string; changedAt: string }[];
  isSimulation: boolean;
  notice: string;
}

@Injectable({ providedIn: 'root' })
export class OrdersService {
  private readonly http = inject(HttpClient);
  list() { return firstValueFrom(this.http.get<OrderSummary[]>('/api/orders')); }
  detail(id: string) { return firstValueFrom(this.http.get<OrderDetail>('/api/orders/' + encodeURIComponent(id))); }
}
