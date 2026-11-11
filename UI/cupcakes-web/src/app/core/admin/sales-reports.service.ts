import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SalesDay {
  date: string; orderCount: number; itemCount: number; subtotal: number; discount: number; total: number;
}
export interface SalesReport {
  period: 'day' | 'week'; startDate: string; endDate: string; timeZone: string; currency: string;
  orderCount: number; itemCount: number; subtotal: number; discount: number; total: number;
  isSimulation: boolean; isEmpty: boolean; days: SalesDay[];
}
export interface SalesQuery { date: string; period: 'day' | 'week'; }

@Injectable({ providedIn: 'root' })
export class SalesReportsService {
  private readonly http = inject(HttpClient);
  get(query: SalesQuery) {
    return firstValueFrom(this.http.get<SalesReport>('/api/admin/reports/sales', { params: { ...query } }));
  }
}
