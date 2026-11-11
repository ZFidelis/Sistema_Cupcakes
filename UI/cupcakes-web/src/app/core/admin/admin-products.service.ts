import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Product } from '../catalog/catalog.service';

export interface CreateProduct {
  name: string;
  flavor: string;
  description: string;
  imagePath: string;
  price: number;
  promotionalPrice: number | null;
  stock: number;
}

@Injectable({ providedIn: 'root' })
export class AdminProductsService {
  private readonly http = inject(HttpClient);

  async create(product: CreateProduct): Promise<Product> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.post<Product>('/api/admin/products', product, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }
}
