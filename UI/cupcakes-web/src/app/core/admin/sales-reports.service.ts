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

  exportXlsx(query: SalesQuery) {
    return firstValueFrom(this.http.get('/api/admin/reports/sales/export.xlsx', {
      params: { ...query }, responseType: 'blob',
    }));
  }

  exportPdf(query: SalesQuery) {
    return firstValueFrom(this.http.get('/api/admin/reports/sales/export.pdf', {
      params: { ...query }, responseType: 'blob',
    }));
  }
}
