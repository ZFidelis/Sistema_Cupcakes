import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface Product {
  id: string;
  name: string;
  flavor: string;
  description: string;
  imagePath: string;
  price: number;
  promotionalPrice: number | null;
  effectivePrice: number;
  stock: number;
  isAvailable: boolean;
}

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);

  list(filters: CatalogFilters = {}) {
    let params = new HttpParams();
    if (filters.flavor) params = params.set('flavor', filters.flavor);
    if (filters.minPrice !== undefined) params = params.set('minPrice', filters.minPrice);
    if (filters.maxPrice !== undefined) params = params.set('maxPrice', filters.maxPrice);
    if (filters.onPromotion !== undefined) params = params.set('onPromotion', filters.onPromotion);
    return this.http.get<Product[]>('/api/products', { params });
  }
}

export interface CatalogFilters {
  flavor?: string;
  minPrice?: number;
  maxPrice?: number;
  onPromotion?: boolean;
}
