import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { AdminProduct } from '../../core/admin/admin-products.service';
import { ProductsPage } from './products-page';

describe('Administrative products list', () => {
  let fixture: ComponentFixture<ProductsPage>;
  let page: HTMLElement;
  let http: HttpTestingController;
  const admin: UserSession = { id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] };
  const user = signal<UserSession | null>(admin);
  const restoreSession = vi.fn();
  const regular: AdminProduct = { id: 'regular', name: 'Chocolate', flavor: 'Chocolate', description: 'Descricao de teste',
    imagePath: '/images/products/cupcake.svg', price: 15.5, promotionalPrice: null, effectivePrice: 15.5,
    stock: 3, isActive: true, isAvailable: true };

  beforeEach(() => {
    user.set(admin); restoreSession.mockReset();
    TestBed.configureTestingModule({ imports: [ProductsPage], providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([]), { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    fixture = TestBed.createComponent(ProductsPage);
    page = fixture.nativeElement; http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function start() { fixture.detectChanges(); return http.expectOne('/api/admin/products'); }
  async function settle() { await fixture.whenStable(); fixture.detectChanges(); }

  it.each([null, { id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] }])('does not request administrative data for %s', account => {
    user.set(account); fixture.detectChanges();
    http.expectNone('/api/admin/products');
    expect(page.querySelector('.admin-products')).toBeNull();
    expect(page.textContent).toContain('exclusiva para administradores');
  });

  it('shows loading and uses a GET without a write token', () => {
    const request = start();
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    expect(page.textContent).toContain('Carregando produtos');
    expect(page.querySelector('[aria-busy]')?.getAttribute('aria-busy')).toBe('true');
    request.flush([]);
  });

  it('renders API order, prices, promotion, active state and availability', async () => {
    start().flush([regular,
      { ...regular, id: 'inactive', name: 'Inativo', isActive: false, isAvailable: false, stock: 9, promotionalPrice: 12.9, effectivePrice: 12.9 },
      { ...regular, id: 'empty', name: 'Sem estoque', stock: 0, isAvailable: false }]);
    await settle();
    const cards = page.querySelectorAll('.admin-product');
    expect([...cards].map(card => card.querySelector('h2')?.textContent)).toEqual(['Chocolate', 'Inativo', 'Sem estoque']);
    expect(cards[0].textContent).toContain('Sem promoção');
    expect(cards[0].textContent?.replace(/\s/g, '')).toContain('R$15,50');
    expect(cards[0].textContent).toContain('Disponível');
    expect(cards[1].classList.contains('inactive')).toBe(true);
    expect(cards[1].textContent).toContain('Fora da vitrine');
    expect(cards[1].textContent?.replace(/\s/g, '')).toContain('R$12,90');
    expect(cards[1].textContent).toContain('9 unidade(s)');
    expect(cards[2].textContent).toContain('Sem estoque');
    expect(page.querySelector('a[href="/admin/produtos/novo"]')).not.toBeNull();
  });

  it('offers product creation when the list is empty', async () => {
    start().flush([]); await settle();
    expect(page.textContent).toContain('Nenhum produto cadastrado');
    expect(page.querySelector('.admin-product')).toBeNull();
    expect(page.querySelector('a[href="/admin/produtos/novo"]')).not.toBeNull();
  });

  it('recovers after a failed read with an explicit retry', async () => {
    start().flush(null, { status: 503, statusText: 'Unavailable' }); await settle();
    expect(page.querySelector('[role=alert]')?.textContent).toContain('Não foi possível');
    page.querySelector<HTMLButtonElement>('.retry-list')!.click();
    http.expectOne('/api/admin/products').flush([regular]); await settle();
    expect(page.querySelector('[role=alert]')).toBeNull();
    expect(page.querySelector('.admin-product')).not.toBeNull();
  });

  it('refreshes stock from the API instead of keeping previous data', async () => {
    start().flush([regular]); await settle();
    page.querySelector<HTMLButtonElement>('.actions button')!.click();
    fixture.detectChanges(); expect(page.querySelector('.admin-product')).toBeNull();
    http.expectOne('/api/admin/products').flush([{ ...regular, stock: 0, isAvailable: false }]); await settle();
    expect(page.querySelector('.admin-product')?.textContent).toContain('0 unidade(s)');
    expect(page.querySelector('.admin-product')?.textContent).toContain('Sem estoque');
  });

  it.each([401, 403])('hides list after API denies access with %s', async status => {
    start().flush(null, { status, statusText: 'Denied' }); await settle();
    expect(page.querySelector('.admin-products')).toBeNull();
    expect(page.querySelector('a[href="/entrar"]')).not.toBeNull();
    expect(restoreSession).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  });

  it('clears administrative data after logout and ignores an old response', async () => {
    const request = start(); user.set(null); fixture.detectChanges();
    request.flush([regular]); await settle();
    expect(page.querySelector('.admin-product')).toBeNull();
    expect(page.textContent).not.toContain('Descricao de teste');
  });

  it('ignores responses after navigating away', async () => {
    const request = start(); fixture.destroy();
    request.flush([regular]); await Promise.resolve();
    expect(page.querySelector('.admin-product')).toBeNull();
  });

  it('keeps only the new administrator response after changing accounts', async () => {
    const oldRequest = start();
    user.set({ ...admin, id: 'other' }); fixture.detectChanges();
    http.expectOne('/api/admin/products').flush([{ ...regular, name: 'Atual' }]); await settle();
    oldRequest.flush([regular]); await settle();
    expect(page.querySelector('h2')?.textContent).toBe('Atual');
  });

  function askDeactivate() {
    page.querySelector<HTMLButtonElement>('.deactivate-product')!.click(); fixture.detectChanges();
  }
  async function deleteRequest() {
    page.querySelector<HTMLButtonElement>('.confirm-deactivation')!.click(); fixture.detectChanges();
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'delete-token' });
    await Promise.resolve();
    return http.expectOne(req => req.url === '/api/admin/products/regular' && req.method === 'DELETE');
  }

  it('requires confirmation and cancel does not write', async () => {
    start().flush([regular]); await settle(); askDeactivate();
    expect(page.querySelector('.deactivation-confirm')?.textContent).toContain('Pedidos anteriores serão preservados');
    http.expectNone('/api/auth/antiforgery');
    page.querySelector<HTMLButtonElement>('.cancel-deactivation')!.click(); fixture.detectChanges();
    expect(page.querySelector('.deactivation-confirm')).toBeNull();
    http.expectNone(req => req.method === 'DELETE');
  });

  it('deactivates with token only once and reloads authoritative state', async () => {
    start().flush([regular]); await settle(); askDeactivate();
    const request = await deleteRequest();
    page.querySelector<HTMLButtonElement>('.confirm-deactivation')!.click();
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('delete-token');
    expect(request.request.body).toBeNull();
    expect(page.querySelector<HTMLButtonElement>('.actions button')!.disabled).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => http.expectOne('/api/admin/products').flush([{ ...regular, isActive: false, isAvailable: false }]));
    await settle();
    expect(page.querySelector('.feedback.success')?.textContent).toContain('Chocolate desativado');
    expect(page.querySelector('.admin-product.inactive')).not.toBeNull();
    expect(page.querySelector('.deactivate-product')).toBeNull();
  });

  it('does not offer deactivation for inactive products', async () => {
    start().flush([{ ...regular, isActive: false, isAvailable: false }]); await settle();
    expect(page.querySelector('.deactivate-product')).toBeNull();
    expect(page.querySelector('a[href="/admin/produtos/regular/editar"]')).not.toBeNull();
  });

  it.each([0, 400, 500])('reports failed/uncertain deletion %s without automatic repetition', async status => {
    start().flush([regular]); await settle(); askDeactivate();
    (await deleteRequest()).flush(null, { status, statusText: 'Failure' });
    await vi.waitFor(() => { fixture.detectChanges(); expect(page.querySelector('[role=alert]')?.textContent).toContain('Atualizar lista'); });
    expect(page.querySelector('.admin-product.inactive')).toBeNull();
    expect(page.querySelector('.deactivation-confirm')).toBeNull();
    http.expectNone(req => req.method === 'DELETE');
  });

  it('refreshes after the selected product is no longer found', async () => {
    start().flush([regular]); await settle(); askDeactivate();
    (await deleteRequest()).flush(null, { status: 404, statusText: 'Missing' });
    await vi.waitFor(() => http.expectOne('/api/admin/products').flush([])); await settle();
    expect(page.textContent).toContain('Produto não encontrado');
    expect(page.textContent).toContain('Nenhum produto cadastrado');
  });

  it.each([401, 403])('clears list when deactivation is denied with %s', async status => {
    start().flush([regular]); await settle(); askDeactivate();
    (await deleteRequest()).flush(null, { status, statusText: 'Denied' });
    await vi.waitFor(() => { fixture.detectChanges(); expect(page.querySelector('a[href="/entrar"]')).not.toBeNull(); });
    expect(page.querySelector('.admin-products')).toBeNull();
    expect(restoreSession).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  });

  it('ignores a late deletion response after logout', async () => {
    start().flush([regular]); await settle(); askDeactivate(); const request = await deleteRequest();
    user.set(null); fixture.detectChanges(); request.flush(null, { status: 204, statusText: 'No Content' }); await settle();
    expect(page.querySelector('.admin-products')).toBeNull();
    expect(page.textContent).not.toContain('Chocolate desativado');
    http.expectNone('/api/admin/products');
  });

  it('keeps successful deletion feedback if the following read fails', async () => {
    start().flush([regular]); await settle(); askDeactivate();
    (await deleteRequest()).flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => http.expectOne('/api/admin/products').flush(null, { status: 503, statusText: 'Unavailable' })); await settle();
    expect(page.querySelector('.feedback.success')?.textContent).toContain('desativado');
    expect(page.querySelector('.retry-list')).not.toBeNull();
  });
});
