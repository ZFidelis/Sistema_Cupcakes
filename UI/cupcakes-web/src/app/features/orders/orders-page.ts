import { CurrencyPipe, DatePipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { OrderDetail, OrderSummary, OrdersService } from '../../core/orders/orders.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-orders-page', imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './orders-page.html', styleUrl: './orders-page.css',
})
export class OrdersPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly orders = inject(OrdersService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly orderId = computed(() => this.params().get('orderId'));
  protected readonly list = signal<OrderSummary[]>([]);
  protected readonly detail = signal<OrderDetail | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'anonymous' | 'missing'>('loading');
  protected readonly message = signal('');
  private version = 0;

  constructor() {
    effect(() => {
      const user = this.auth.user();
      const id = this.orderId();
      this.version++;
      this.list.set([]); this.detail.set(null); this.message.set('');
      if (user) void this.load(id);
      else this.state.set('anonymous');
    });
  }

  ngOnDestroy() { this.version++; }

  protected async refresh(): Promise<void> { await this.load(this.orderId()); }

  private async load(id: string | null): Promise<void> {
    const owner = this.auth.user()?.id;
    if (!owner) return;
    const version = ++this.version;
    this.list.set([]); this.detail.set(null); this.message.set(''); this.state.set('loading');
    try {
      if (id) {
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
      if (error instanceof HttpErrorResponse && error.status === 401) {
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

  private current(owner: string, version: number) { return this.version === version && this.auth.user()?.id === owner; }
}
