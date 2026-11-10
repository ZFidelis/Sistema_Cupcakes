import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Cart, CartItem, CartService } from '../../core/cart/cart.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-cart-page',
  imports: [CurrencyPipe, RouterLink],
  templateUrl: './cart-page.html',
  styleUrl: './cart-page.css',
})
export class CartPage {
  protected readonly auth = inject(AuthService);
  private readonly carts = inject(CartService);
  protected readonly cart = signal<Cart | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'anonymous'>('loading');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  private requestVersion = 0;

  constructor() {
    effect(() => {
      const user = this.auth.user();
      this.requestVersion++;
      this.cart.set(null);
      this.notice.set('');
      this.busy.set(false);
      if (user) {
        this.error.set('');
        void this.load();
      }
      else this.state.set('anonymous');
    });
  }

  protected async load(): Promise<void> {
    const owner = this.auth.user()?.id;
    if (!owner) return;
    const version = ++this.requestVersion;
    this.state.set('loading');
    try {
      const cart = await this.carts.get();
      if (version !== this.requestVersion || owner !== this.auth.user()?.id) return;
      this.cart.set(cart);
      this.state.set('ready');
    } catch (error) {
      if (version !== this.requestVersion || owner !== this.auth.user()?.id) return;
      if (error instanceof HttpErrorResponse && error.status === 401) this.expired();
      else this.state.set('error');
    }
  }

  protected async save(event: Event, item: CartItem, value: string): Promise<void> {
    event.preventDefault();
    const quantity = Number(value);
    if (!Number.isInteger(quantity) || quantity < 1 || quantity > 2147483647) {
      this.error.set('Informe uma quantidade inteira maior que zero. Para excluir, use Remover.');
      return;
    }
    await this.change(() => this.carts.update(item.productId, quantity), 'Quantidade atualizada.');
  }

  protected async remove(item: CartItem): Promise<void> {
    await this.change(() => this.carts.remove(item.productId), 'Produto removido do carrinho.');
  }

  private async change(operation: () => Promise<Cart>, message: string): Promise<void> {
    if (this.busy() || !this.auth.user()) return;
    const owner = this.auth.user()!.id;
    const version = ++this.requestVersion;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const cart = await operation();
      if (version !== this.requestVersion || owner !== this.auth.user()?.id) return;
      this.cart.set(cart);
      this.notice.set(message);
    } catch (error) {
      if (version !== this.requestVersion || owner !== this.auth.user()?.id) return;
      if (error instanceof HttpErrorResponse && error.status === 401) this.expired();
      else if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 404)) {
        this.error.set(error.status === 409
          ? 'O estoque ou a disponibilidade mudou. Confira os dados atualizados e ajuste ou remova o item.'
          : 'Este item não está mais no carrinho. Confira os dados atualizados.');
        await this.load();
      } else {
        this.error.set('Não foi possível confirmar a alteração. Atualize o carrinho para conferir os dados antes de tentar novamente.');
      }
    } finally {
      if (owner === this.auth.user()?.id) this.busy.set(false);
    }
  }

  private expired() {
    this.cart.set(null);
    this.state.set('anonymous');
    this.error.set('Sua sessão expirou. Entre novamente para acessar seu carrinho.');
    void this.auth.restoreSession();
  }
}
