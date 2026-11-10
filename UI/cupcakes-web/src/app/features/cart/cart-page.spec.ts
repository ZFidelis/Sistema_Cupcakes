import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { Cart, CartService } from '../../core/cart/cart.service';
import { CartPage } from './cart-page';

describe('CartPage', () => {
  let fixture: ComponentFixture<CartPage>;
  let page: HTMLElement;
  const user = signal<UserSession | null>(null);
  const sessionState = signal('ready');
  const api = { get: vi.fn(), update: vi.fn(), remove: vi.fn() };
  const auth = { user, sessionState, restoreSession: vi.fn() };
  const empty: Cart = { items: [], itemCount: 0, subtotal: 0, discount: 0, total: 0,
    hasUnavailableItems: false, pricingNotice: 'Frete e impostos não calculados.' };
  const populated: Cart = { ...empty, itemCount: 2, subtotal: 28, discount: 4.2, total: 23.8,
    items: [{ productId: 'p1', name: 'Morango', imagePath: '/cupcake.svg', quantity: 2,
      price: 14, promotionalPrice: 11.9, effectivePrice: 11.9, subtotal: 28, discount: 4.2,
      total: 23.8, stock: 5, isAvailable: true, availabilityMessage: null }] };

  beforeEach(() => {
    vi.resetAllMocks();
    user.set({ id: 'owner', email: 'owner@example.invalid', roles: ['Cliente'] });
    sessionState.set('ready');
    api.get.mockResolvedValue(populated);
    TestBed.configureTestingModule({ imports: [CartPage], providers: [provideRouter([]),
      { provide: AuthService, useValue: auth }, { provide: CartService, useValue: api }] });
    fixture = TestBed.createComponent(CartPage);
    page = fixture.nativeElement;
  });

  async function render() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  function submit(value: string) {
    page.querySelector<HTMLInputElement>('input')!.value = value;
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  }

  it('asks anonymous visitors to sign in without loading private data', async () => {
    user.set(null);
    await render();
    expect(page.textContent).toContain('Entre na sua conta');
    expect(api.get).not.toHaveBeenCalled();
  });

  it('shows loading before the response, then server prices, discounts and totals', async () => {
    let resolve!: (cart: Cart) => void;
    api.get.mockReturnValue(new Promise<Cart>(done => resolve = done));
    fixture.detectChanges();
    expect(page.textContent).toContain('Carregando');
    resolve(populated);
    await render();
    expect(page.querySelector('[data-testid=cart-total]')?.textContent?.replace(/\s/g, '')).toBe('R$23,80');
    expect(page.textContent).toContain('Morango');
    expect(page.querySelector('s')?.textContent).toContain('14,00');
    expect(page.textContent).toContain('4,20');
  });

  it('shows the empty cart and zero summary', async () => {
    api.get.mockResolvedValue(empty);
    await render();
    expect(page.textContent).toContain('carrinho está vazio');
    expect(page.querySelector('[data-testid=cart-total]')?.textContent).toContain('0,00');
  });

  it('updates with absolute quantity and blocks duplicate submits while pending', async () => {
    await render();
    let resolve!: (cart: Cart) => void;
    api.update.mockReturnValue(new Promise<Cart>(done => resolve = done));
    submit('3'); submit('3'); fixture.detectChanges();
    expect(api.update).toHaveBeenCalledExactlyOnceWith('p1', 3);
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
    resolve({ ...populated, total: 35.7 });
    await render();
    expect(page.querySelector('[data-testid=cart-total]')?.textContent).toContain('35,70');
    expect(page.textContent).toContain('Quantidade atualizada');
  });

  it.each(['0', '-1', '1.5', ''])('rejects invalid quantity %s without writing', async quantity => {
    await render(); submit(quantity); await render();
    expect(api.update).not.toHaveBeenCalled();
    expect(page.querySelector('[role=alert]')?.textContent).toContain('inteira maior que zero');
  });

  it('removes a product and renders the empty response', async () => {
    await render(); api.remove.mockResolvedValue(empty);
    page.querySelector<HTMLButtonElement>('.item-actions button[type=button]')!.click();
    await render();
    expect(api.remove).toHaveBeenCalledWith('p1');
    expect(page.textContent).toContain('carrinho está vazio');
  });

  it('refreshes availability after conflict without reporting success', async () => {
    await render();
    api.update.mockRejectedValue(new HttpErrorResponse({ status: 409 }));
    api.get.mockResolvedValue({ ...populated, hasUnavailableItems: true,
      items: [{ ...populated.items[0], isAvailable: false, stock: 1, availabilityMessage: 'Estoque insuficiente.' }] });
    submit('4'); await render();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Há itens indisponíveis');
    });
    expect(page.textContent).toContain('Estoque insuficiente');
    expect(page.textContent).not.toContain('Quantidade atualizada');
  });

  it('offers retry when reading fails', async () => {
    api.get.mockRejectedValueOnce(new HttpErrorResponse({ status: 503 }));
    await render();
    expect(page.textContent).toContain('Não foi possível carregar');
    page.querySelector<HTMLButtonElement>('button')!.click(); await render();
    expect(page.querySelectorAll('.cart-item').length).toBe(1);
  });

  it('does not report a failed removal as success or resend it', async () => {
    await render(); api.remove.mockRejectedValue(new HttpErrorResponse({ status: 0 }));
    page.querySelector<HTMLButtonElement>('.item-actions button[type=button]')!.click(); await render();
    expect(page.textContent).toContain('Não foi possível confirmar');
    expect(page.querySelectorAll('.cart-item').length).toBe(1);
    expect(api.remove).toHaveBeenCalledTimes(1);
  });

  it('hides private data when the session expires', async () => {
    await render(); api.remove.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    page.querySelector<HTMLButtonElement>('.item-actions button[type=button]')!.click(); await render();
    expect(page.querySelector('.cart-item')).toBeNull();
    expect(page.textContent).toContain('sessão expirou');
    expect(auth.restoreSession).toHaveBeenCalledTimes(1);
  });

  it('ignores a delayed response after the account has left', async () => {
    let resolve!: (cart: Cart) => void;
    api.get.mockReturnValue(new Promise<Cart>(done => resolve = done));
    fixture.detectChanges(); user.set(null); fixture.detectChanges();
    resolve(populated); await render();
    expect(page.textContent).toContain('Entre na sua conta');
    expect(page.textContent).not.toContain('Morango');
  });
});
