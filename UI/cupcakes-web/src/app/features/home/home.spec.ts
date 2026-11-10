import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Product } from '../../core/catalog/catalog.service';
import { Home } from './home';

describe('Home catalog', () => {
  let fixture: ComponentFixture<Home>;
  let http: HttpTestingController;
  let page: HTMLElement;
  const regular: Product = {
    id: 'chocolate', name: 'Chocolate gourmet', flavor: 'Chocolate', description: 'Cupcake de chocolate.',
    imagePath: '/images/products/cupcake.svg', price: 12.50, promotionalPrice: null,
    effectivePrice: 12.50, stock: 5, isAvailable: true,
  };
  const promotion: Product = {
    ...regular, id: 'morango', name: 'Morango gourmet', flavor: 'Morango', price: 14,
    promotionalPrice: 11.90, effectivePrice: 11.90, stock: 0, isAvailable: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Home], providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(Home);
    http = TestBed.inject(HttpTestingController);
    page = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('shows loading while the public GET request is pending', () => {
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Carregando');
    expect(page.querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    const request = http.expectOne('/api/products');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('renders API data, BRL prices, promotion and unavailable stock with descriptive images', () => {
    http.expectOne('/api/products').flush([regular, promotion]);
    fixture.detectChanges();
    const cards = page.querySelectorAll('.product-card');
    expect(cards.length).toBe(2);
    expect(cards[0].querySelector('h2')?.textContent).toBe(regular.name);
    expect(cards[0].querySelector('.description')?.textContent).toBe(regular.description);
    expect(cards[0].querySelector('.price')?.textContent?.replace(/\s/g, '')).toBe('R$12,50');
    expect(cards[0].querySelector('.promotion')).toBeNull();
    expect(cards[0].querySelector('.availability')?.textContent).toContain('Em estoque');
    expect(cards[1].querySelector('s')?.textContent?.replace(/\s/g, '')).toBe('R$14,00');
    expect(cards[1].querySelector('.price')?.textContent?.replace(/\s/g, '')).toBe('PorR$11,90');
    expect(cards[1].querySelector('.promotion')?.textContent).toBe('Promoção');
    expect(cards[1].querySelector('.unavailable')?.textContent).toContain('Indisponível');
    expect(cards[0].querySelector('img')?.getAttribute('src')).toBe(regular.imagePath);
    expect(cards[0].querySelector('img')?.alt).toContain(regular.name);
    expect(page.querySelector('section')?.getAttribute('aria-busy')).toBe('false');
  });

  it('shows a useful empty state without product cards', () => {
    http.expectOne('/api/products').flush([]);
    fixture.detectChanges();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Ainda não há cupcakes');
    expect(page.querySelector('.product-card')).toBeNull();
  });

  it('offers retry on failure and recovers after a successful response', () => {
    http.expectOne('/api/products').flush(null, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Não foi possível');
    page.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    expect(page.querySelector('[role="status"]')?.textContent).toContain('Carregando');
    http.expectOne('/api/products').flush([regular]);
    fixture.detectChanges();
    expect(page.querySelector('[role="alert"]')).toBeNull();
    expect(page.querySelectorAll('.product-card').length).toBe(1);
  });

  it('keeps product information visible when its image fails', () => {
    http.expectOne('/api/products').flush([regular]);
    fixture.detectChanges();
    page.querySelector('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(page.querySelector('img')).toBeNull();
    expect(page.querySelector('.image-fallback')?.getAttribute('aria-label')).toContain(regular.name);
    expect(page.querySelector('h2')?.textContent).toBe(regular.name);
    expect(page.querySelector('.price')?.textContent).toContain('12,50');
  });

  it('cancels the pending request when navigating away', () => {
    const request = http.expectOne('/api/products');
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });
});
