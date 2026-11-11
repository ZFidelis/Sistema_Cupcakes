import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { AdminProduct } from '../../core/admin/admin-products.service';
import { ProductCreatePage } from './product-create-page';

describe('Administrative product editing', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const user = signal<UserSession | null>(null);
  const restoreSession = vi.fn();
  const product: AdminProduct = { id: 'product-1', name: 'Original', flavor: 'Morango', description: 'Descricao original',
    imagePath: '/images/products/cupcake.svg', price: 20, promotionalPrice: 18, effectivePrice: 18,
    stock: 9, isActive: true, isAvailable: true };
  const page = () => harness.routeNativeElement!;
  const input = (field: string) => page().querySelector<HTMLInputElement>('#product-' + field)!;

  beforeEach(async () => {
    user.set({ id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] }); restoreSession.mockReset();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{ path: 'admin/produtos/:productId/editar', component: ProductCreatePage }]),
      { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());
  async function open() { await harness.navigateByUrl('/admin/produtos/product-1/editar'); return http.expectOne('/api/admin/products'); }
  function fill(field: string, value: string) { input(field).value = value; input(field).dispatchEvent(new Event('input')); }
  function submit() { page().querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true })); harness.detectChanges(); }
  async function settle() {
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(page().textContent).not.toContain('Carregando dados do produto');
      expect(page().querySelector<HTMLFieldSetElement>('fieldset')?.disabled).not.toBe(true);
    });
  }
  async function put() {
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'edit-token' });
    await Promise.resolve();
    return http.expectOne('/api/admin/products/product-1');
  }

  it('loads the current product on a direct route and fills all fields', async () => {
    const request = await open();
    expect(page().textContent).toContain('Carregando dados');
    expect(page().querySelector('form')).toBeNull();
    request.flush([product]); await settle();
    expect(page().querySelector('h1')?.textContent).toBe('Editar cupcake');
    expect(input('name').value).toBe('Original');
    expect(input('flavor').value).toBe('Morango');
    expect(input('description').value).toBe('Descricao original');
    expect(input('price').value).toBe('20,00');
    expect(input('promotion').value).toBe('18,00');
    expect(input('stock').value).toBe('9');
  });

  it('saves a full normalized PUT, removes promotion and prevents duplicate submits', async () => {
    (await open()).flush([product]); await settle();
    fill('name', ' Novo '); fill('price', '15,50'); fill('promotion', ' '); fill('stock', '0');
    submit(); submit();
    const request = await put();
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('edit-token');
    expect(request.request.body).toEqual({ name: 'Novo', flavor: 'Morango', description: 'Descricao original',
      imagePath: product.imagePath, price: 15.5, promotionalPrice: null, stock: 0 });
    request.flush({ ...product, name: 'Novo', price: 15.5, promotionalPrice: null, effectivePrice: 15.5, stock: 0, isAvailable: false });
    await settle();
    expect(page().textContent).toContain('Produto atualizado');
    expect(page().textContent).toContain('15,50');
    expect(page().querySelector('a.button[href="/admin/produtos"]')).not.toBeNull();
    http.expectNone('/api/admin/products');
  });

  it('edits inactive product without claiming it is in the public catalog', async () => {
    (await open()).flush([{ ...product, isActive: false, isAvailable: false }]); await settle(); submit();
    const request = await put();
    expect(request.request.body.isActive).toBeUndefined();
    request.flush({ ...product, isActive: false, isAvailable: false }); await settle();
    expect(page().textContent).toContain('continua inativo, fora da vitrine');
    expect(page().textContent).not.toContain('já está na vitrine');
  });

  it('shows missing product without offering an empty creation form', async () => {
    (await open()).flush([]); await settle();
    expect(page().textContent).toContain('Produto não encontrado');
    expect(page().querySelector('form')).toBeNull();
  });

  it('retries a failed initial read', async () => {
    (await open()).flush(null, { status: 503, statusText: 'Unavailable' }); await settle();
    page().querySelector<HTMLButtonElement>('button')!.click();
    http.expectOne('/api/admin/products').flush([product]); await settle();
    expect(input('name').value).toBe('Original');
  });

  it.each([400, 500])('keeps draft after save error %s without retrying automatically', async status => {
    (await open()).flush([product]); await settle(); fill('name', 'Alterado'); submit();
    (await put()).flush(null, { status, statusText: 'Failure' }); await settle();
    expect(input('name').value).toBe('Alterado');
    expect(page().querySelector('[role=alert]')).not.toBeNull();
    http.expectNone('/api/admin/products/product-1');
  });

  it('handles product disappearing before save', async () => {
    (await open()).flush([product]); await settle(); submit();
    (await put()).flush(null, { status: 404, statusText: 'Missing' }); await settle();
    expect(page().textContent).toContain('Produto não encontrado');
    expect(page().querySelector('form')).toBeNull();
  });

  it.each([401, 403])('hides form after denied loading %s', async status => {
    (await open()).flush(null, { status, statusText: 'Denied' }); await settle();
    expect(page().querySelector('form')).toBeNull();
    expect(page().querySelector('a[href="/entrar"]')).not.toBeNull();
  });

  it('loads new route params and ignores the older read response', async () => {
    const old = await open();
    await harness.navigateByUrl('/admin/produtos/product-2/editar');
    http.expectOne('/api/admin/products').flush([{ ...product, id: 'product-2', name: 'Outro', promotionalPrice: null }]); await settle();
    old.flush([product]); await settle();
    expect(input('name').value).toBe('Outro'); expect(input('promotion').value).toBe('');
  });

  it('hides draft and ignores delayed save after logout', async () => {
    (await open()).flush([product]); await settle(); submit(); const request = await put();
    user.set(null); harness.detectChanges(); request.flush(product); await settle();
    expect(page().querySelector('form')).toBeNull();
    expect(page().textContent).not.toContain('Produto atualizado');
  });
});
