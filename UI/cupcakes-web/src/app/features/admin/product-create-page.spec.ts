import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { ProductCreatePage } from './product-create-page';

describe('Administrative product creation', () => {
  let fixture: ComponentFixture<ProductCreatePage>;
  let page: HTMLElement;
  let http: HttpTestingController;
  const admin: UserSession = { id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] };
  const user = signal<UserSession | null>(admin);
  const restoreSession = vi.fn();
  const product = { id: 'new-product', name: 'Cupcake teste', flavor: 'Chocolate', description: 'Descricao',
    imagePath: '/images/products/cupcake.svg', price: 15.5, promotionalPrice: 12.9, effectivePrice: 12.9,
    stock: 0, isAvailable: false };

  beforeEach(() => {
    user.set(admin); restoreSession.mockReset();
    TestBed.configureTestingModule({ imports: [ProductCreatePage], providers: [provideHttpClient(),
      provideHttpClientTesting(), provideRouter([]),
      { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    fixture = TestBed.createComponent(ProductCreatePage);
    page = fixture.nativeElement; http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });
  afterEach(() => http.verify());

  function fill(id: string, value: string) {
    const input = page.querySelector<HTMLInputElement>(`#product-${id}`)!;
    input.value = value; input.dispatchEvent(new Event('input'));
  }
  function valid() {
    fill('name', ' Cupcake teste '); fill('flavor', ' Chocolate '); fill('description', ' Descricao ');
    fill('price', '15,50'); fill('promotion', '12.90'); fill('stock', '0');
  }
  function submit() {
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }
  async function request() {
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'test-token' });
    await Promise.resolve();
    return http.expectOne('/api/admin/products');
  }
  async function settled() {
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector<HTMLFieldSetElement>('fieldset')?.disabled).not.toBe(true);
    });
  }

  it.each([null, { id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] }])('hides form for unauthorized account %s', account => {
    user.set(account); fixture.detectChanges();
    expect(page.querySelector('form')).toBeNull();
    expect(page.textContent).toContain('exclusiva para administradores');
    http.expectNone('/api/auth/antiforgery');
  });

  it('sends normalized values with existing token, prevents duplicate submits and shows server response', async () => {
    valid(); submit(); submit();
    expect(page.querySelector<HTMLFieldSetElement>('fieldset')!.disabled).toBe(true);
    const post = await request();
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.get('X-XSRF-TOKEN')).toBe('test-token');
    expect(post.request.body).toEqual({ name: 'Cupcake teste', flavor: 'Chocolate', description: 'Descricao',
      imagePath: '/images/products/cupcake.svg', price: 15.5, promotionalPrice: 12.9, stock: 0 });
    post.flush(product); await settled();
    expect(page.textContent).toContain('Produto cadastrado');
    expect(page.textContent).toContain('12,90');
    expect(page.textContent).toContain('estoque zero');
    expect(page.querySelector('form')).toBeNull();
    page.querySelector<HTMLButtonElement>('button')!.click(); fixture.detectChanges();
    expect(page.querySelector<HTMLInputElement>('#product-name')!.value).toBe('');
    expect(page.querySelector('select')!.value).toBe('/images/products/cupcake.svg');
  });

  it('sends null when optional promotion is blank', async () => {
    valid(); fill('promotion', ' '); submit();
    const post = await request();
    expect(post.request.body.promotionalPrice).toBeNull();
    post.flush({ ...product, promotionalPrice: null, effectivePrice: 15.5 });
    await fixture.whenStable();
  });

  it.each([
    ['name', '   '], ['description', ''], ['price', '0'], ['price', '1.001'],
    ['price', '10000000000'], ['promotion', '15,50'], ['stock', '-1'], ['stock', '1.5'], ['stock', '2147483648'],
  ])('rejects invalid %s=%s without a request', (field, value) => {
    valid(); fill(field, value); submit();
    expect(page.querySelector('#product-errors [role=alert]')).not.toBeNull();
    http.expectNone('/api/auth/antiforgery');
  });

  it('preserves fields after server validation failure', async () => {
    valid(); submit();
    (await request()).flush({ errors: { price: ['invalid'] } }, { status: 400, statusText: 'Bad Request' });
    await settled();
    expect(page.querySelector('[role=alert]')?.textContent).toContain('Cadastro rejeitado');
    expect(page.querySelector<HTMLInputElement>('#product-price')!.value).toBe('15,50');
    expect(page.querySelector<HTMLFieldSetElement>('fieldset')!.disabled).toBe(false);
  });

  it.each([401, 403])('hides form and refreshes session after %s', async status => {
    valid(); submit();
    (await request()).flush({}, { status, statusText: 'Denied' });
    await settled();
    expect(page.querySelector('form')).toBeNull();
    expect(restoreSession).toHaveBeenCalledOnce();
    expect(page.querySelector('a[href="/entrar"]')).not.toBeNull();
  });

  it('warns about uncertain outcome without automatically repeating a write', async () => {
    valid(); submit();
    (await request()).flush(null, { status: 500, statusText: 'Failure' });
    await settled();
    expect(page.querySelector('[role=alert]')?.textContent).toContain('pode ter sido gravado');
    http.expectNone('/api/admin/products');
  });

  it('clears draft on account change and ignores delayed success', async () => {
    valid(); submit(); const post = await request();
    user.set({ id: 'other', email: 'other@example.invalid', roles: ['Administrador'] }); fixture.detectChanges();
    post.flush(product); await fixture.whenStable(); fixture.detectChanges();
    expect(page.textContent).not.toContain('Produto cadastrado');
    expect(page.querySelector<HTMLInputElement>('#product-name')!.value).toBe('');
  });
});
