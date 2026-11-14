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

export interface AdminProduct extends Product {
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class AdminProductsService {
  private readonly http = inject(HttpClient);

  list(): Promise<AdminProduct[]> {
    return firstValueFrom(this.http.get<AdminProduct[]>('/api/admin/products'));
  }

  async deactivate(id: string): Promise<void> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    await firstValueFrom(this.http.delete<void>('/api/admin/products/' + encodeURIComponent(id), {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }

  async activate(id: string): Promise<void> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    await firstValueFrom(this.http.post<void>('/api/admin/products/' + encodeURIComponent(id) + '/activate', null, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }

  async update(id: string, product: CreateProduct): Promise<AdminProduct> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.put<AdminProduct>('/api/admin/products/' + encodeURIComponent(id), product, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }

  async create(product: CreateProduct): Promise<Product> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.post<Product>('/api/admin/products', product, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }
}
