import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { OrderDetail, OrderSummary, OrdersService } from '../../core/orders/orders.service';
import { OrdersPage } from './orders-page';

describe('OrdersPage', () => {
  let harness: RouterTestingHarness;
  const user = signal<UserSession | null>(null);
  const sessionState = signal('ready');
  const api = { list: vi.fn(), detail: vi.fn() };
  const auth = { user, sessionState, restoreSession: vi.fn() };
  const summary: OrderSummary = { id: 'p1', createdAt: '2026-11-10T12:00:00Z', status: 'EmPreparo',
    paymentMethod: 'Cartao', paymentResult: 'Aprovado', itemCount: 2, subtotal: 28, discount: 4.2, total: 23.8, isSimulation: true };
  const detail: OrderDetail = { isSimulation: true, notice: 'Pagamento inteiramente simulado.',
    order: { ...summary, confirmationId: 'confirmation', address: { recipient: 'Pessoa Ficticia', street: 'Rua Historica',
      number: 's/n', complement: 'Casa', neighborhood: 'Bairro', city: 'Cidade', state: 'SP', postalCode: '01234567' },
      items: [{ productId: 'product', productName: 'Morango historico', quantity: 2, originalUnitPrice: 14,
        appliedUnitPrice: 11.9, subtotal: 28, discount: 4.2, total: 23.8 }] },
    history: [{ status: 'Recebido', changedAt: '2026-11-10T12:00:00Z' }, { status: 'EmPreparo', changedAt: '2026-11-10T12:01:00Z' }] };

  beforeEach(() => {
    vi.resetAllMocks(); user.set({ id: 'owner', email: 'owner@example.invalid', roles: ['Cliente'] }); sessionState.set('ready');
    api.list.mockResolvedValue([summary]); api.detail.mockResolvedValue(detail);
    TestBed.configureTestingModule({ providers: [
      provideRouter([{ path: 'pedidos', component: OrdersPage }, { path: 'pedidos/:orderId', component: OrdersPage }]),
      { provide: AuthService, useValue: auth }, { provide: OrdersService, useValue: api },
    ] });
  });

  const page = () => harness.routeNativeElement!;
  async function render() { harness.detectChanges(); await harness.fixture.whenStable(); harness.detectChanges(); }
  async function open(path = '/pedidos') {
    harness = await RouterTestingHarness.create(); await harness.navigateByUrl(path, OrdersPage); await render();
  }

  it('asks anonymous users to log in without querying orders', async () => {
    user.set(null); await open();
    expect(page().textContent).toContain('Entre na sua conta'); expect(api.list).not.toHaveBeenCalled();
  });

  it('shows loading while the list is pending', async () => {
    let resolve!: (orders: OrderSummary[]) => void;
    api.list.mockReturnValue(new Promise<OrderSummary[]>(done => resolve = done));
    await open(); expect(page().textContent).toContain('Carregando');
    resolve([]); await render(); expect(page().textContent).toContain('ainda não tem pedidos');
  });

  it('shows an empty state with a link to the catalog', async () => {
    api.list.mockResolvedValue([]); await open();
    expect(page().textContent).toContain('ainda não tem pedidos'); expect(page().querySelector('a[href="/"]')).not.toBeNull();
  });

  it('preserves server ordering and renders dates, status labels, BRL values and detail links', async () => {
    api.list.mockResolvedValue([{ ...summary, id: 'newest' }, summary]); await open();
    const cards = page().querySelectorAll('.order-list .order-card');
    expect(cards.length).toBe(2); expect(cards[0].querySelector('h2')?.textContent).toContain('newest');
    expect(cards[1].querySelector('a')?.getAttribute('href')).toBe('/pedidos/p1');
    expect(cards[0].textContent).toContain('Em preparo'); expect(cards[0].textContent).toContain('Cartão simulado');
    expect(cards[0].textContent).toContain('10/11/2026'); expect(cards[0].textContent).toContain('23,80');
  });

  it('renders historical item prices, full address, totals and existing status history', async () => {
    await open('/pedidos/p1');
    expect(page().textContent).toContain('Morango historico'); expect(page().querySelector('s')?.textContent).toContain('14,00');
    expect(page().querySelector('address')?.textContent).toContain('Rua Historica');
    expect(page().querySelector('address')?.textContent).toContain('Casa');
    expect(page().querySelector('address')?.textContent).toContain('01234567');
    expect(page().querySelector('[data-testid=order-total]')?.textContent).toContain('23,80');
    expect(page().querySelector('.status-history')?.textContent).toContain('Recebido');
    expect(page().querySelector('.status-history')?.textContent).toContain('Em preparo');
    expect(api.detail).toHaveBeenCalledWith('p1');
  });

  it('shows the same neutral missing state for an unavailable detail', async () => {
    api.detail.mockRejectedValue(new HttpErrorResponse({ status: 404 })); await open('/pedidos/foreign');
    expect(page().textContent).toContain('Pedido não encontrado ou indisponível');
    expect(page().querySelector('address')).toBeNull(); expect(page().querySelector('[data-testid=order-total]')).toBeNull();
  });

  it.each(['/pedidos', '/pedidos/p1'])('offers a successful retry after read failure on %s', async path => {
    const method = path === '/pedidos' ? api.list : api.detail;
    method.mockRejectedValueOnce(new HttpErrorResponse({ status: 503 }));
    await open(path); expect(page().textContent).toContain('Não foi possível consultar');
    page().querySelector<HTMLButtonElement>('#retry-orders')!.click(); await render();
    expect(page().querySelector('#retry-orders')).toBeNull(); expect(method).toHaveBeenCalledTimes(2);
  });

  it('hides historical data when refresh discovers an expired session', async () => {
    await open('/pedidos/p1'); api.detail.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    page().querySelector<HTMLButtonElement>('#refresh-orders')!.click(); await render();
    expect(page().querySelector('address')).toBeNull(); expect(page().textContent).toContain('sessão expirou');
    expect(auth.restoreSession).toHaveBeenCalledTimes(1);
  });

  it('ignores a delayed detail after account exit', async () => {
    let resolve!: (order: OrderDetail) => void;
    api.detail.mockReturnValue(new Promise<OrderDetail>(done => resolve = done));
    await open('/pedidos/p1'); user.set(null); harness.detectChanges(); resolve(detail); await render();
    expect(page().textContent).toContain('Entre na sua conta'); expect(page().textContent).not.toContain('Pessoa Ficticia');
  });

  it('loads changed route parameters and ignores the previous delayed detail', async () => {
    let resolve!: (order: OrderDetail) => void;
    api.detail.mockReturnValueOnce(new Promise<OrderDetail>(done => resolve = done));
    api.detail.mockResolvedValueOnce({ ...detail, order: { ...detail.order, id: 'p2' } });
    await open('/pedidos/p1'); await harness.navigateByUrl('/pedidos/p2', OrdersPage); await render();
    resolve(detail); await render();
    expect(api.detail).toHaveBeenLastCalledWith('p2'); expect(page().querySelector('#order-number')?.textContent).toContain('p2');
  });
});
