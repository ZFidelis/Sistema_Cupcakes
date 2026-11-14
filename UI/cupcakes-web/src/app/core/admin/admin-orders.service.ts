import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { OrderDetail, OrderSummary, OrderFilters, orderFilterParams } from '../orders/orders.service';

export interface AdminOrderSummary { customerEmail: string; order: OrderSummary; }
export interface AdminOrderDetail { customerEmail: string; detail: OrderDetail; }
export interface OrderStatusUpdate { orderId: string; status: string; history: OrderDetail['history']; }

@Injectable({ providedIn: 'root' })
export class AdminOrdersService {
  private readonly http = inject(HttpClient);
  list(filters: OrderFilters = {}) { return firstValueFrom(this.http.get<AdminOrderSummary[]>('/api/admin/orders', { params: orderFilterParams(filters) })); }
  detail(id: string) { return firstValueFrom(this.http.get<AdminOrderDetail>('/api/admin/orders/' + encodeURIComponent(id))); }
  async updateStatus(id: string, status: string): Promise<OrderStatusUpdate> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.put<OrderStatusUpdate>('/api/admin/orders/' + encodeURIComponent(id) + '/status',
      { status }, { headers: { 'X-XSRF-TOKEN': token.requestToken } }));
  }
}
