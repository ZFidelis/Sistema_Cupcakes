import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CatalogService, Product } from '../../core/catalog/catalog.service';
import { AuthService } from '../../core/auth/auth.service';
import { CartService } from '../../core/cart/cart.service';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';

registerLocaleData(localePt);

@Component({
  selector: 'app-home',
  imports: [CurrencyPipe, RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly carts = inject(CartService);
  protected readonly adding = signal<string | null>(null);
  protected readonly addMessage = signal('');
  protected readonly addError = signal('');
  private readonly catalog = inject(CatalogService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly products = signal<Product[]>([]);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly imageErrors = signal<Set<string>>(new Set());

  ngOnInit() {
    this.load();
  }

  protected load() {
    this.state.set('loading');
    this.catalog.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: products => {
        this.products.set(products);
        this.imageErrors.set(new Set());
        this.state.set('ready');
      },
      error: () => this.state.set('error'),
    });
  }

  protected imageFailed(id: string) {
    this.imageErrors.update(ids => new Set([...ids, id]));
  }

  protected async add(product: Product): Promise<void> {
    if (this.adding() || !this.auth.user()) return;
    const owner = this.auth.user()!.id;
    this.adding.set(product.id);
    this.addMessage.set('');
    this.addError.set('');
    try {
      await this.carts.add(product.id);
      if (owner === this.auth.user()?.id) this.addMessage.set(product.name + ' adicionado ao carrinho.');
    } catch (error) {
      if (owner !== this.auth.user()?.id) return;
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.addError.set('Sua sessão expirou. Entre novamente para adicionar produtos.');
        void this.auth.restoreSession();
      } else if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 404)) {
        this.addError.set('Produto indisponível ou quantidade acima do estoque. Confira a vitrine atualizada e seu carrinho.');
        this.load();
      } else {
        this.addError.set('Não foi possível confirmar a inclusão. Confira seu carrinho antes de tentar novamente.');
      }
    } finally {
      this.adding.set(null);
    }
  }
}
