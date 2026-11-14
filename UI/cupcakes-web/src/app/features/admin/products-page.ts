import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal, untracked } from '@angular/core';
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
  protected readonly pendingId = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly actionMessage = signal('');
  protected readonly actionError = signal('');
  private version = 0;

  constructor() {
    effect(() => {
      this.auth.user();
      this.version++;
      this.items.set([]); this.message.set('');
      this.pendingId.set(null); this.busyId.set(null); this.actionMessage.set(''); this.actionError.set('');
      if (this.isAdmin()) untracked(() => void this.load());
      else this.state.set('denied');
    });
  }

  ngOnDestroy() { this.version++; }

  protected async load() {
    if (!this.isAdmin() || this.busyId()) return;
    this.pendingId.set(null);
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

  protected askDeactivate(product: AdminProduct) {
    if (!this.isAdmin() || !product.isActive || this.busyId()) return;
    this.pendingId.set(product.id); this.actionError.set(''); this.actionMessage.set('');
  }

  protected async activate(product: AdminProduct) {
    if (!this.isAdmin() || product.isActive || this.busyId()) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.isAdmin();
    this.pendingId.set(null);
    this.busyId.set(product.id); this.actionError.set(''); this.actionMessage.set('');
    try {
      await this.products.activate(product.id);
      if (!current()) return;
      this.busyId.set(null);
      this.actionMessage.set(product.name + ' ativado.');
      await this.load();
    } catch (error) {
      if (!current()) return;
      this.busyId.set(null);
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.items.set([]); this.state.set('denied'); this.message.set('Acesso indisponível. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else if (error instanceof HttpErrorResponse && error.status === 404) {
        this.actionError.set('Produto não encontrado. A lista será atualizada.');
        await this.load();
      } else {
        this.actionError.set('Não foi possível confirmar a ativação. Use Atualizar lista para conferir a situação antes de tentar novamente.');
      }
    }
  }

  protected cancelDeactivate() {
    if (!this.busyId()) this.pendingId.set(null);
  }

  protected async confirmDeactivate(product: AdminProduct) {
    if (!this.isAdmin() || this.busyId() || this.pendingId() !== product.id || !product.isActive) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.isAdmin();
    this.busyId.set(product.id); this.actionError.set('');
    try {
      await this.products.deactivate(product.id);
      if (!current()) return;
      this.busyId.set(null); this.pendingId.set(null);
      this.actionMessage.set(product.name + ' desativado.');
      await this.load();
    } catch (error) {
      if (!current()) return;
      this.pendingId.set(null); this.busyId.set(null);
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.items.set([]); this.state.set('denied'); this.message.set('Acesso indisponível. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else if (error instanceof HttpErrorResponse && error.status === 404) {
        this.actionError.set('Produto não encontrado. A lista será atualizada.');
        await this.load();
      } else {
        this.actionError.set('Não foi possível confirmar a desativação. Use Atualizar lista para conferir a situação antes de tentar novamente.');
      }
    }
  }
}
