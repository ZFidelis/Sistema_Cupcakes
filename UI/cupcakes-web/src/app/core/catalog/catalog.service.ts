import { HttpClient } from '@angular/common/http';
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

  list() {
    return this.http.get<Product[]>('/api/products');
  }
}
