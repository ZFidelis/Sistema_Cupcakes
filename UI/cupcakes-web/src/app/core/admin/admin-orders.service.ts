import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { OrderDetail, OrderSummary } from '../orders/orders.service';

export interface AdminOrderSummary { customerEmail: string; order: OrderSummary; }
export interface AdminOrderDetail { customerEmail: string; detail: OrderDetail; }

@Injectable({ providedIn: 'root' })
export class AdminOrdersService {
  private readonly http = inject(HttpClient);
  list() { return firstValueFrom(this.http.get<AdminOrderSummary[]>('/api/admin/orders')); }
  detail(id: string) { return firstValueFrom(this.http.get<AdminOrderDetail>('/api/admin/orders/' + encodeURIComponent(id))); }
}
