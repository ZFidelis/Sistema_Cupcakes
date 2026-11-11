import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { ReviewsPage } from './reviews-page';

describe('Product reviews interface', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;
  const user = signal<UserSession | null>(null);
  const restoreSession = vi.fn();
  const product = { id: 'p1', name: 'Chocolate', flavor: 'Chocolate', description: 'Teste', imagePath: '/images/products/cupcake.svg', price: 12.5,
    promotionalPrice: null, effectivePrice: 12.5, stock: 0, isAvailable: false };
  const review = { id: 'r1', productId: 'p1', rating: 5, comment: 'Muito bom', createdAt: '2026-11-11T12:00:00Z', updatedAt: '2026-11-11T12:00:00Z' };
  beforeEach(async () => {
    user.set(null); restoreSession.mockReset();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      provideRouter([{ path: 'produtos/:productId/avaliacoes', component: ReviewsPage }]),
      { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    http = TestBed.inject(HttpTestingController); harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());
  const page = () => harness.routeNativeElement!;
  async function render() {
    // HTTP observables resolve async service methods before Angular renders their signals.
    await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
    await harness.fixture.whenStable(); harness.detectChanges();
  }
  async function open(authenticated = false) {
    if (authenticated) user.set({ id: 'owner', email: 'owner@example.invalid', roles: ['Cliente'] });
    await harness.navigateByUrl('/produtos/p1/avaliacoes');
  }
  function publicData(values: unknown[] = []) { http.expectOne('/api/products').flush([product]); http.expectOne('/api/products/p1/reviews').flush(values); }
  async function ownMissing() {
    await vi.waitFor(() => http.expectOne('/api/products/p1/reviews/mine').flush(null, { status: 404, statusText: 'Missing' })); await render();
  }
  function comment(value: string) { const input = page().querySelector<HTMLTextAreaElement>('#review-comment')!; input.value = value; input.dispatchEvent(new Event('input')); }
  function submit() { page().querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true })); harness.detectChanges(); }
  async function write() {
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'review-token' }); await Promise.resolve();
    return http.expectOne(req => req.url === '/api/products/p1/reviews/mine' && req.method === 'PUT');
  }

  it('lets visitors read comments as literal text without an editing form', async () => {
    await open(); publicData([{ ...review, comment: '<img src=x onerror=alert(1)><script>test</script>' }]); await render();
    expect(page().textContent).toContain('Chocolate');
    expect(page().querySelector('.review-comment')?.textContent).toContain('<script>test</script>');
    expect(page().querySelector('.review-card img, .review-card script')).toBeNull();
    expect(page().querySelector('form')).toBeNull(); http.expectNone('/api/products/p1/reviews/mine');
  });

  it('shows an empty public state', async () => {
    await open(); publicData(); await render(); expect(page().textContent).toContain('ainda não tem avaliações');
  });

  it('creates a review without purchase, prevents duplicate submission and refreshes public comments', async () => {
    await open(true); publicData(); await ownMissing(); comment(' Muito bom '); submit(); submit();
    const request = await write();
    expect(request.request.body).toEqual({ rating: 5, comment: 'Muito bom' });
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('review-token');
    expect(page().querySelector<HTMLFieldSetElement>('fieldset')!.disabled).toBe(true);
    request.flush(review);
    await vi.waitFor(() => http.expectOne('/api/products/p1/reviews').flush([review])); await render();
    expect(page().querySelector('.feedback.success')?.textContent).toContain('foi salva');
    expect(page().querySelectorAll('.review-card')).toHaveLength(1);
    expect(page().textContent).toContain('Sua avaliação');
    expect(page().querySelector('legend')?.textContent).toContain('Editar');
  });

  it('loads own fields and updates the same review', async () => {
    await open(true); publicData([review]);
    await vi.waitFor(() => http.expectOne('/api/products/p1/reviews/mine').flush(review)); await render();
    expect(page().querySelector<HTMLTextAreaElement>('#review-comment')!.value).toBe('Muito bom');
    comment('Atualizado'); submit(); const request = await write(); request.flush({ ...review, comment: 'Atualizado' });
    await vi.waitFor(() => http.expectOne('/api/products/p1/reviews').flush([{ ...review, comment: 'Atualizado' }])); await render();
    expect(page().querySelectorAll('.review-card')).toHaveLength(1);
    expect(page().querySelector('.review-comment')?.textContent).toBe('Atualizado');
  });

  it.each(['', '   ', 'x'.repeat(1001)])('rejects invalid comment without sending', async value => {
    await open(true); publicData(); await ownMissing(); comment(value); submit();
    http.expectNone('/api/auth/antiforgery'); expect(page().querySelector('#review-errors [role=alert]')).not.toBeNull();
  });

  it('keeps draft after server rejection', async () => {
    await open(true); publicData(); await ownMissing(); comment('Rascunho'); submit();
    (await write()).flush(null, { status: 400, statusText: 'Invalid' });
    await vi.waitFor(() => { harness.detectChanges(); expect(page().querySelector('[role=alert]')?.textContent).toContain('Não foi possível confirmar'); });
    expect(page().querySelector<HTMLTextAreaElement>('#review-comment')!.value).toBe('Rascunho');
  });

  it('handles product deactivation during save', async () => {
    await open(true); publicData(); await ownMissing(); comment('Teste'); submit();
    (await write()).flush(null, { status: 409, statusText: 'Inactive' });
    await vi.waitFor(() => { harness.detectChanges(); expect(page().textContent).toContain('Produto indisponível'); });
    expect(page().querySelector('form')).toBeNull();
  });

  it('offers retry when public data cannot load', async () => {
    await open(); http.expectOne('/api/products').flush([product]);
    http.expectOne('/api/products/p1/reviews').flush(null, { status: 503, statusText: 'Unavailable' }); await render();
    page().querySelector<HTMLButtonElement>('#retry-reviews')!.click(); publicData([review]); await render();
    expect(page().querySelector('.review-card')).not.toBeNull();
  });

  it('does not enable a blank editor when own review lookup fails', async () => {
    await open(true); publicData([review]);
    await vi.waitFor(() => http.expectOne('/api/products/p1/reviews/mine').flush(null, { status: 500, statusText: 'Failure' })); await render();
    expect(page().querySelector('form')).toBeNull(); expect(page().querySelector('.review-card')).not.toBeNull();
  });

  it('clears private draft on logout during save and ignores the late response', async () => {
    await open(true); publicData(); await ownMissing(); comment('Privado'); submit(); const request = await write();
    user.set(null); harness.detectChanges();
    publicData(); request.flush(review); await render();
    expect(page().querySelector('form')).toBeNull(); expect(page().textContent).not.toContain('Privado');
    expect(page().querySelector('.feedback.success')).toBeNull();
  });

  it('shows missing product instead of an editor', async () => {
    await open(); http.expectOne('/api/products').flush([]);
    http.expectOne('/api/products/p1/reviews').flush(null, { status: 404, statusText: 'Missing' }); await render();
    expect(page().textContent).toContain('Produto indisponível'); expect(page().querySelector('form')).toBeNull();
  });
});
