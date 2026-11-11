import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CatalogFilters, CatalogService, Product } from '../../core/catalog/catalog.service';
import { AuthService } from '../../core/auth/auth.service';
import { CartService } from '../../core/cart/cart.service';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { Subscription } from 'rxjs';

function priceValue(value: string): number | undefined {
  return value.trim() === '' ? undefined : Number(value.trim().replace(',', '.'));
}

const moneyFilter: ValidatorFn = control => {
  const text = (control.value as string).trim();
  return text === '' || (/^\d+([.,]\d{1,2})?$/.test(text) && priceValue(text)! <= 9999999999.99)
    ? null : { money: true };
};

const priceRange: ValidatorFn = control => {
  const min = priceValue(control.get('minPrice')!.value);
  const max = priceValue(control.get('maxPrice')!.value);
  return min !== undefined && max !== undefined && min > max ? { priceRange: true } : null;
};

registerLocaleData(localePt);

@Component({
  selector: 'app-home',
  imports: [CurrencyPipe, RouterLink, ReactiveFormsModule],
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
  private request?: Subscription;
  protected readonly appliedFilters = signal<CatalogFilters>({});
  protected readonly filterSubmitted = signal(false);
  protected readonly filterForm = inject(FormBuilder).nonNullable.group({
    flavor: ['', Validators.maxLength(60)],
    minPrice: ['', moneyFilter],
    maxPrice: ['', moneyFilter],
    promotion: ['all'],
  }, { validators: priceRange });

  protected get hasFilters(): boolean {
    return Object.keys(this.appliedFilters()).length > 0;
  }

  protected applyFilters() {
    this.filterSubmitted.set(true);
    if (this.filterForm.invalid) return;
    const values = this.filterForm.getRawValue();
    const filters: CatalogFilters = {};
    if (values.flavor.trim()) filters.flavor = values.flavor.trim();
    const min = priceValue(values.minPrice);
    const max = priceValue(values.maxPrice);
    if (min !== undefined) filters.minPrice = min;
    if (max !== undefined) filters.maxPrice = max;
    if (values.promotion !== 'all') filters.onPromotion = values.promotion === 'yes';
    this.appliedFilters.set(filters);
    this.load();
  }

  protected clearFilters() {
    this.filterForm.reset();
    this.filterSubmitted.set(false);
    this.appliedFilters.set({});
    this.load();
  }

  ngOnInit() {
    this.load();
  }

  protected load() {
    this.request?.unsubscribe();
    this.state.set('loading');
    this.request = this.catalog.list(this.appliedFilters()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
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
