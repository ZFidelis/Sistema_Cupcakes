import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Cart, CartService } from './cart.service';

describe('CartService', () => {
  let service: CartService;
  let http: HttpTestingController;
  const cart: Cart = { items: [], itemCount: 0, subtotal: 0, discount: 0, total: 0, hasUnavailableItems: false, pricingNotice: 'BRL' };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CartService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('reads the current cart without sending a user ID', async () => {
    const pending = service.get();
    http.expectOne('/api/cart').flush(cart);
    expect(await pending).toEqual(cart);
  });

  it.each(['POST', 'PUT', 'DELETE'])('sends %s with the API token and no client prices', async method => {
    const pending = method === 'POST' ? service.add('product', 2)
      : method === 'PUT' ? service.update('product', 2) : service.remove('product');
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'cart-token' });
    await Promise.resolve();
    const request = http.expectOne(method === 'POST' ? '/api/cart/items' : '/api/cart/items/product');
    expect(request.request.method).toBe(method);
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('cart-token');
    if (method === 'POST') expect(request.request.body).toEqual({ productId: 'product', quantity: 2 });
    if (method === 'PUT') expect(request.request.body).toEqual({ quantity: 2 });
    request.flush(cart);
    expect(await pending).toEqual(cart);
  });
});
