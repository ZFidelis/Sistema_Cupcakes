import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CheckoutAddress, CheckoutService, ConfirmationRequest } from './checkout.service';

describe('CheckoutService', () => {
  let service: CheckoutService;
  let http: HttpTestingController;
  const address: CheckoutAddress = { recipient: 'Pessoa', street: 'Rua', number: 's/n', neighborhood: 'Bairro', city: 'Cidade', state: 'SP', postalCode: '01234567', complement: null };
  const request: ConfirmationRequest = { confirmationId: 'id-pendente', address, paymentMethod: 'Pix', simulation: 'Aprovar',
    expectedItems: [{ productId: 'p1', quantity: 2, price: 14, effectivePrice: 11.9 }] };

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CheckoutService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); sessionStorage.clear(); });

  it.each(['preview', 'confirm'] as const)('posts %s with the existing API token', async action => {
    const pending = action === 'preview' ? service.preview(address) : service.confirm(request);
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'checkout-token' });
    await Promise.resolve();
    const call = http.expectOne('/api/checkout/' + action);
    expect(call.request.method).toBe('POST');
    expect(call.request.headers.get('X-XSRF-TOKEN')).toBe('checkout-token');
    expect(call.request.body).toEqual(action === 'preview' ? { address } : request);
    call.flush({}); await pending;
  });

  it('restores the same pending identifier and payload in a new instance for the owner only', () => {
    service.remember('owner', request);
    const reloaded = TestBed.runInInjectionContext(() => new CheckoutService());
    expect(reloaded.pending('owner')).toEqual(request);
    expect(reloaded.pending('other')).toBeNull();
    reloaded.forget('owner');
    expect(TestBed.runInInjectionContext(() => new CheckoutService()).pending('owner')).toBeNull();
  });

  it('ignores malformed browser storage', () => {
    sessionStorage.setItem('cupcakes.checkout.pending.owner', '{invalid');
    expect(service.pending('owner')).toBeNull();
  });
});
