import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrdersService } from './orders.service';

describe('OrdersService', () => {
  let service: OrdersService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OrdersService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists the session orders without supplying an owner or write token', async () => {
    const pending = service.list();
    const request = http.expectOne('/api/orders');
    expect(request.request.method).toBe('GET'); expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    request.flush([]); expect(await pending).toEqual([]);
  });

  it('reads the detail using an encoded identifier', async () => {
    const pending = service.detail('order/id');
    const request = http.expectOne('/api/orders/order%2Fid');
    expect(request.request.method).toBe('GET');
    request.flush({ isSimulation: true }); expect((await pending).isSimulation).toBe(true);
  });
});
