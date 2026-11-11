import { CurrencyPipe, DatePipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { OrderDetail, OrderSummary, OrdersService } from '../../core/orders/orders.service';
import { AdminOrdersService } from '../../core/admin/admin-orders.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-orders-page', imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './orders-page.html', styleUrl: './orders-page.css',
})
export class OrdersPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly orders = inject(OrdersService);
  private readonly adminOrders = inject(AdminOrdersService);
  private readonly route = inject(ActivatedRoute);
  protected readonly administrative = this.route.snapshot.data['administrative'] === true;
  protected readonly listRoute = this.administrative ? '/admin/pedidos' : '/pedidos';
  protected readonly canRead = computed(() => !!this.auth.user() && (!this.administrative || this.auth.user()!.roles.includes('Administrador')));
  protected readonly customerEmails = signal<Record<string, string>>({});
  protected readonly customerEmail = signal('');
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly orderId = computed(() => this.params().get('orderId'));
  protected readonly list = signal<OrderSummary[]>([]);
  protected readonly detail = signal<OrderDetail | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'anonymous' | 'missing' | 'denied'>('loading');
  protected readonly message = signal('');
  private version = 0;

  constructor() {
    effect(() => {
      const user = this.auth.user();
      const id = this.orderId();
      this.version++;
      this.list.set([]); this.detail.set(null); this.message.set('');
      this.customerEmails.set({}); this.customerEmail.set('');
      if (this.administrative && !this.canRead()) this.state.set('denied');
      else if (user) void this.load(id);
      else this.state.set('anonymous');
    });
  }

  ngOnDestroy() { this.version++; }

  protected async refresh(): Promise<void> { await this.load(this.orderId()); }

  private async load(id: string | null): Promise<void> {
    const owner = this.auth.user()?.id;
    if (!owner || !this.canRead()) return;
    const version = ++this.version;
    this.list.set([]); this.detail.set(null); this.message.set(''); this.state.set('loading');
    this.customerEmails.set({}); this.customerEmail.set('');
    try {
      if (this.administrative && id) {
        const result = await this.adminOrders.detail(id);
        if (!this.current(owner, version)) return;
        this.detail.set(result.detail); this.customerEmail.set(result.customerEmail);
      } else if (this.administrative) {
        const result = await this.adminOrders.list();
        if (!this.current(owner, version)) return;
        this.list.set(result.map(value => value.order));
        this.customerEmails.set(Object.fromEntries(result.map(value => [value.order.id, value.customerEmail])));
      } else if (id) {
        const result = await this.orders.detail(id);
        if (!this.current(owner, version)) return;
        this.detail.set(result);
      } else {
        const result = await this.orders.list();
        if (!this.current(owner, version)) return;
        this.list.set(result);
      }
      this.state.set('ready');
    } catch (error) {
      if (!this.current(owner, version)) return;
      if (this.administrative && error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.state.set('denied'); this.message.set('Acesso administrativo indisponível. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else if (error instanceof HttpErrorResponse && error.status === 401) {
        this.state.set('anonymous'); this.message.set('Sua sessão expirou. Entre novamente para consultar seus pedidos.');
        void this.auth.restoreSession();
      } else if (id && error instanceof HttpErrorResponse && error.status === 404) {
        this.state.set('missing');
      } else {
        this.state.set('error');
      }
    }
  }

  protected statusLabel(status: string): string {
    const labels: Record<string, string> = { Recebido: 'Recebido', EmPreparo: 'Em preparo', SaiuParaEntrega: 'Saiu para entrega', Entregue: 'Entregue' };
    return labels[status] ?? status;
  }

  private current(owner: string, version: number) { return this.version === version && this.auth.user()?.id === owner && this.canRead(); }
}
