import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Cart } from '../cart/cart.service';

export interface CheckoutAddress {
  recipient: string; street: string; number: string; neighborhood: string; city: string;
  state: string; postalCode: string; complement: string | null;
}
export interface CheckoutPreview {
  address: CheckoutAddress; cart: Cart; canProceed: boolean;
  issues: { code: string; message: string; productId: string | null }[]; notice: string;
}
export interface ConfirmationRequest {
  confirmationId: string; address: CheckoutAddress; paymentMethod: 'Pix' | 'Cartao'; simulation: 'Aprovar' | 'Recusar';
  expectedItems: { productId: string; quantity: number; price: number; effectivePrice: number }[];
}
export interface ConfirmedOrder {
  id: string; confirmationId: string | null; createdAt: string; status: string;
  paymentMethod: string; paymentResult: string; address: CheckoutAddress;
  items: { productId: string; productName: string; quantity: number; originalUnitPrice: number;
    appliedUnitPrice: number; subtotal: number; discount: number; total: number }[];
  subtotal: number; discount: number; total: number;
}
export interface ConfirmationResponse {
  code: string; message: string; isSimulation: boolean; order: ConfirmedOrder | null; preview: CheckoutPreview | null;
}

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);
  private readonly memory = new Map<string, ConfirmationRequest>();

  preview(address: CheckoutAddress) { return this.post<CheckoutPreview>('/api/checkout/preview', { address }); }
  confirm(request: ConfirmationRequest) { return this.post<ConfirmationResponse>('/api/checkout/confirm', request); }

  remember(owner: string, request: ConfirmationRequest) {
    this.memory.set(owner, request);
    try { sessionStorage.setItem(this.key(owner), JSON.stringify(request)); } catch { /* Memoria mantem recuperacao nesta pagina. */ }
  }

  pending(owner: string): ConfirmationRequest | null {
    const memory = this.memory.get(owner);
    if (memory) return memory;
    try {
      const value = JSON.parse(sessionStorage.getItem(this.key(owner)) ?? 'null');
      if (value && typeof value.confirmationId === 'string' && value.address && Array.isArray(value.expectedItems)) {
        this.memory.set(owner, value);
        return value;
      }
    } catch {  }
    return null;
  }

  forget(owner: string) {
    this.memory.delete(owner);
    try { sessionStorage.removeItem(this.key(owner)); } catch { /* Sem armazenamento disponivel. */ }
  }

  private key(owner: string) { return 'cupcakes.checkout.pending.' + owner; }

  private async post<T>(url: string, body: unknown): Promise<T> {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.post<T>(url, body, { headers: { 'X-XSRF-TOKEN': token.requestToken } }));
  }
}
