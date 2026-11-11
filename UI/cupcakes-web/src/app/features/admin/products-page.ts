import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminProduct, AdminProductsService } from '../../core/admin/admin-products.service';
import { AuthService } from '../../core/auth/auth.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-admin-products-page', imports: [CurrencyPipe, RouterLink],
  templateUrl: './products-page.html', styleUrl: './products-page.css',
})
export class ProductsPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly products = inject(AdminProductsService);
  protected readonly isAdmin = computed(() => this.auth.user()?.roles.includes('Administrador') === true);
  protected readonly items = signal<AdminProduct[]>([]);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'denied'>('loading');
  protected readonly message = signal('');
  private version = 0;

  constructor() {
    effect(() => {
      this.auth.user();
      this.version++;
      this.items.set([]); this.message.set('');
      if (this.isAdmin()) void this.load();
      else this.state.set('denied');
    });
  }

  ngOnDestroy() { this.version++; }

  protected async load() {
    if (!this.isAdmin()) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.isAdmin();
    this.items.set([]); this.message.set(''); this.state.set('loading');
    try {
      const items = await this.products.list();
      if (current()) { this.items.set(items); this.state.set('ready'); }
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.state.set('denied');
        this.message.set(error.status === 401 ? 'Sua sessão expirou. Entre novamente.' : 'Acesso administrativo negado. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else this.state.set('error');
    }
  }
}
