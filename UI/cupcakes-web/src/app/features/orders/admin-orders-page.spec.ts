import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { OrderDetail, OrderSummary } from '../../core/orders/orders.service';
import { OrdersPage } from './orders-page';

describe('Administrative orders interface', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const user = signal<UserSession | null>(null);
  const restoreSession = vi.fn();
  const summary: OrderSummary = { id: 'first', createdAt: '2026-11-11T12:00:00Z', status: 'EmPreparo',
    paymentMethod: 'Pix', paymentResult: 'Aprovado', itemCount: 2, subtotal: 28, discount: 4.2, total: 23.8, isSimulation: true };
  const detail: OrderDetail = { isSimulation: true, notice: 'Pagamento inteiramente simulado.',
    order: { ...summary, confirmationId: 'confirmation', address: { recipient: 'Pessoa Ficticia', street: 'Rua Historica',
      number: 's/n', complement: 'Casa', neighborhood: 'Bairro', city: 'Cidade', state: 'SP', postalCode: '01234567' },
      items: [{ productId: 'product', productName: 'Morango historico', quantity: 2, originalUnitPrice: 14,
        appliedUnitPrice: 11.9, subtotal: 28, discount: 4.2, total: 23.8 }] },
    history: [{ status: 'Recebido', changedAt: '2026-11-11T12:00:00Z' }, { status: 'EmPreparo', changedAt: '2026-11-11T12:01:00Z' }] };

  beforeEach(async () => {
    user.set({ id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] }); restoreSession.mockReset();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([
        { path: 'admin/pedidos', component: OrdersPage, data: { administrative: true } },
        { path: 'admin/pedidos/:orderId', component: OrdersPage, data: { administrative: true } },
        { path: 'pedidos', component: OrdersPage },
        { path: 'pedidos/:orderId', component: OrdersPage },
      ]), { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    http = TestBed.inject(HttpTestingController); harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());
  const page = () => harness.routeNativeElement!;
  async function open(path = '/admin/pedidos') { await harness.navigateByUrl(path); }
  async function render() { await harness.fixture.whenStable(); harness.detectChanges(); }

  it.each([null, { id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] }])('denies administrative view without querying for %s', async account => {
    user.set(account); await open();
    http.expectNone(req => req.url.startsWith('/api/admin/orders'));
    expect(page().textContent).toContain('exclusiva para administradores');
    expect(page().querySelector('.order-card')).toBeNull();
  });

  it('loads administrative list with GET and no write token', async () => {
    await open();
    expect(page().textContent).toContain('Carregando pedidos da loja');
    const request = http.expectOne('/api/admin/orders');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    request.flush([]);
  });

  it('renders different customers in API order with totals and administrative links', async () => {
    await open(); http.expectOne('/api/admin/orders').flush([
      { customerEmail: 'second@example.invalid', order: { ...summary, id: 'second' } },
      { customerEmail: 'first@example.invalid', order: summary }]); await render();
    const cards = page().querySelectorAll('.order-card');
    expect(cards).toHaveLength(2);
    expect(cards[0].querySelector('h2')?.textContent).toContain('second');
    expect(cards[0].textContent).toContain('second@example.invalid');
    expect(cards[1].textContent).toContain('first@example.invalid');
    expect(cards[0].textContent).toContain('23,80');
    expect(cards[0].textContent).toContain('Em preparo');
    expect(cards[1].querySelector('a')?.getAttribute('href')).toBe('/admin/pedidos/first');
  });

  it('renders detail with customer email, historical prices, address and history', async () => {
    await open('/admin/pedidos/first');
    http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    expect(page().textContent).toContain('first@example.invalid');
    expect(page().textContent).toContain('Morango historico');
    expect(page().querySelector('s')?.textContent).toContain('14,00');
    expect(page().querySelector('address')?.textContent).toContain('Rua Historica');
    expect(page().querySelector('address')?.textContent).toContain('01234567');
    expect(page().querySelector('[data-testid=order-total]')?.textContent).toContain('23,80');
    expect(page().querySelector('.status-history')?.textContent).toContain('Em preparo');
    expect(page().querySelector('a[href="/admin/pedidos"]')).not.toBeNull();
  });

  it('shows a store-wide empty state', async () => {
    await open(); http.expectOne('/api/admin/orders').flush([]); await render();
    expect(page().textContent).toContain('Nenhum pedido registrado na loja');
  });

  it.each(['/admin/pedidos', '/admin/pedidos/first'])('retries read failure on %s', async path => {
    const apiPath = path.replace('/admin/pedidos', '/api/admin/orders');
    await open(path); http.expectOne(apiPath).flush(null, { status: 503, statusText: 'Unavailable' }); await render();
    page().querySelector<HTMLButtonElement>('#retry-orders')!.click();
    http.expectOne(apiPath).flush(path.endsWith('first') ? { customerEmail: 'first@example.invalid', detail } : []); await render();
    expect(page().querySelector('#retry-orders')).toBeNull();
  });

  it.each([401, 403])('hides data on denied administrative response %s', async status => {
    await open(); http.expectOne('/api/admin/orders').flush(null, { status, statusText: 'Denied' }); await render();
    expect(page().querySelector('.order-card')).toBeNull();
    expect(page().querySelector('a[href="/entrar"]')).not.toBeNull();
    expect(restoreSession).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  });

  it('shows missing detail without stale data', async () => {
    await open('/admin/pedidos/missing'); http.expectOne('/api/admin/orders/missing').flush(null, { status: 404, statusText: 'Missing' }); await render();
    expect(page().textContent).toContain('Pedido não encontrado');
    expect(page().querySelector('address')).toBeNull();
  });

  it('ignores a late detail after logout', async () => {
    await open('/admin/pedidos/first'); const request = http.expectOne('/api/admin/orders/first');
    user.set(null); harness.detectChanges(); request.flush({ customerEmail: 'private@example.invalid', detail }); await render();
    expect(page().textContent).not.toContain('private@example.invalid');
    expect(page().querySelector('address')).toBeNull();
  });

  it('ignores old detail after changing route parameters', async () => {
    await open('/admin/pedidos/first'); const old = http.expectOne('/api/admin/orders/first');
    await harness.navigateByUrl('/admin/pedidos/second');
    http.expectOne('/api/admin/orders/second').flush({ customerEmail: 'second@example.invalid', detail: { ...detail, order: { ...detail.order, id: 'second' } } }); await render();
    old.flush({ customerEmail: 'first@example.invalid', detail }); await render();
    expect(page().querySelector('#order-number')?.textContent).toContain('second');
    expect(page().textContent).not.toContain('first@example.invalid');
  });

  it('returns to the personal mode without keeping customer emails or using admin endpoint', async () => {
    await open(); http.expectOne('/api/admin/orders').flush([{ customerEmail: 'private@example.invalid', order: summary }]); await render();
    await harness.navigateByUrl('/pedidos'); http.expectOne('/api/orders').flush([]); await render();
    expect(page().querySelector('h1')?.textContent).toBe('Meus pedidos');
    expect(page().textContent).not.toContain('private@example.invalid');
  });

  async function statusRequest() {
    page().querySelector<HTMLButtonElement>('#advance-status')!.click(); harness.detectChanges();
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'status-token' });
    await Promise.resolve();
    return http.expectOne('/api/admin/orders/first/status');
  }
  async function statusSettled() {
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page().querySelector('#advance-status')?.textContent).not.toContain('Salvando');
    });
  }

  it('advances all three steps from server responses and stops when delivered', async () => {
    await open('/admin/pedidos/first');
    http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail: {
      ...detail, order: { ...detail.order, status: 'Recebido' }, history: detail.history.slice(0, 1) } }); await render();
    const history = [...detail.history.slice(0, 1)];
    for (const next of ['EmPreparo', 'SaiuParaEntrega', 'Entregue']) {
      const request = await statusRequest();
      page().querySelector<HTMLButtonElement>('#advance-status')!.click();
      expect(page().querySelector<HTMLButtonElement>('#refresh-orders')!.disabled).toBe(true);
      expect(request.request.method).toBe('PUT');
      expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('status-token');
      expect(request.request.body).toEqual({ status: next });
      history.push({ status: next, changedAt: '2026-11-11T12:02:00Z' });
      request.flush({ orderId: 'first', status: next, history });
      await vi.waitFor(() => { harness.detectChanges(); expect(page().querySelector('.status-history li:last-child')?.textContent).toContain(
        next === 'EmPreparo' ? 'Em preparo' : next === 'SaiuParaEntrega' ? 'Saiu para entrega' : 'Entregue'); });
      expect(page().querySelector('[data-testid=order-total]')?.textContent).toContain('23,80');
    }
    expect(page().querySelector('#advance-status')).toBeNull();
    expect(page().textContent).toContain('Não há outras etapas');
    expect(page().querySelectorAll('.status-history li')).toHaveLength(4);
  });

  it.each([0, 400, 409, 500])('requires a fresh read after uncertain/rejected status write %s', async status => {
    await open('/admin/pedidos/first'); http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    (await statusRequest()).flush(null, { status, statusText: 'Failure' }); await statusSettled();
    expect(page().querySelector<HTMLButtonElement>('#advance-status')!.disabled).toBe(true);
    expect(page().querySelector('[role=alert]')?.textContent).toContain('Atualizar pedido');
    http.expectNone('/api/admin/orders/first');
    page().querySelector<HTMLButtonElement>('#refresh-orders')!.click();
    http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail: { ...detail,
      order: { ...detail.order, status: 'SaiuParaEntrega' } } }); await render();
    expect(page().querySelector<HTMLButtonElement>('#advance-status')!.disabled).toBe(false);
    expect(page().querySelector('#advance-status')?.textContent).toContain('Entregue');
  });

  it.each([401, 403])('hides detail when status write is denied with %s', async status => {
    await open('/admin/pedidos/first'); http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    (await statusRequest()).flush(null, { status, statusText: 'Denied' });
    await vi.waitFor(() => { harness.detectChanges(); expect(page().querySelector('address')).toBeNull(); });
    expect(page().querySelector('a[href="/entrar"]')).not.toBeNull();
    expect(restoreSession).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  });

  it('handles missing order while advancing', async () => {
    await open('/admin/pedidos/first'); http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    (await statusRequest()).flush(null, { status: 404, statusText: 'Missing' });
    await vi.waitFor(() => { harness.detectChanges(); expect(page().textContent).toContain('Pedido não encontrado'); });
    expect(page().querySelector('address')).toBeNull();
  });

  it('discards delayed status response after logout', async () => {
    await open('/admin/pedidos/first'); http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    const request = await statusRequest(); user.set(null); harness.detectChanges();
    request.flush({ orderId: 'first', status: 'SaiuParaEntrega', history: detail.history }); await render();
    expect(page().querySelector('address')).toBeNull();
    expect(page().querySelector('.feedback.success')).toBeNull();
  });

  it('discards delayed status response after selecting a different order', async () => {
    await open('/admin/pedidos/first'); http.expectOne('/api/admin/orders/first').flush({ customerEmail: 'first@example.invalid', detail }); await render();
    const request = await statusRequest();
    await harness.navigateByUrl('/admin/pedidos/second');
    http.expectOne('/api/admin/orders/second').flush({ customerEmail: 'second@example.invalid', detail: { ...detail, order: { ...detail.order, id: 'second' } } }); await render();
    request.flush({ orderId: 'first', status: 'Entregue', history: [] }); await render();
    expect(page().querySelector('#order-number')?.textContent).toContain('second');
    expect(page().querySelector('.order-status')?.textContent).toContain('Em preparo');
  });

  it('does not offer status writes in the personal detail, even for an administrator', async () => {
    await open('/pedidos/first'); http.expectOne('/api/orders/first').flush(detail); await render();
    expect(page().querySelector('#advance-status')).toBeNull();
    http.expectNone('/api/auth/antiforgery');
  });
});
