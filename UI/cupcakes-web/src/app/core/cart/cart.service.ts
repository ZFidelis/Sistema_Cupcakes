import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CartItem {
  productId: string;
  name: string;
  imagePath: string;
  quantity: number;
  price: number;
  promotionalPrice: number | null;
  effectivePrice: number;
  subtotal: number;
  discount: number;
  total: number;
  stock: number;
  isAvailable: boolean;
  availabilityMessage: string | null;
}

export interface Cart {
  items: CartItem[];
  itemCount: number;
  subtotal: number;
  discount: number;
  total: number;
  hasUnavailableItems: boolean;
  pricingNotice: string;
}

@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly http = inject(HttpClient);

  get() {
    return firstValueFrom(this.http.get<Cart>('/api/cart'));
  }

  add(productId: string, quantity = 1) {
    return this.write('POST', '/api/cart/items', { productId, quantity });
  }

  update(productId: string, quantity: number) {
    return this.write('PUT', '/api/cart/items/' + encodeURIComponent(productId), { quantity });
  }

  remove(productId: string) {
    return this.write('DELETE', '/api/cart/items/' + encodeURIComponent(productId));
  }

  private async write(method: string, url: string, body?: unknown): Promise<Cart> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.request<Cart>(method, url, {
      body, headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }
}
