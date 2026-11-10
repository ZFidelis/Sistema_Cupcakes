import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { CheckoutPreview, CheckoutService, ConfirmationRequest, ConfirmationResponse } from '../../core/checkout/checkout.service';
import { CheckoutPage } from './checkout-page';

describe('CheckoutPage', () => {
  let fixture: ComponentFixture<CheckoutPage>;
  let page: HTMLElement;
  const user = signal<UserSession | null>(null);
  const sessionState = signal('ready');
  const api = { preview: vi.fn(), confirm: vi.fn(), remember: vi.fn(), pending: vi.fn(), forget: vi.fn() };
  const auth = { user, sessionState, restoreSession: vi.fn() };
  const address = { recipient: 'Pessoa', street: 'Rua', number: 's/n', neighborhood: 'Bairro', city: 'Cidade', state: 'SP', postalCode: '01234567', complement: null };
  const preview: CheckoutPreview = { address, canProceed: true, issues: [], notice: 'Simulação', cart: {
    items: [{ productId: 'p1', name: 'Morango', imagePath: '/cupcake.svg', quantity: 2, price: 14,
      promotionalPrice: 11.9, effectivePrice: 11.9, subtotal: 28, discount: 4.2, total: 23.8,
      stock: 5, isAvailable: true, availabilityMessage: null }],
    itemCount: 2, subtotal: 28, discount: 4.2, total: 23.8, hasUnavailableItems: false, pricingNotice: 'Frete não calculado.'
  } };
  const approved: ConfirmationResponse = { code: 'approved', isSimulation: true, message: 'Simulado', preview: null,
    order: { id: 'order-1', confirmationId: 'confirmation', createdAt: '2026-11-10T12:00:00Z', status: 'Recebido',
      paymentMethod: 'Pix', paymentResult: 'Aprovado', address, subtotal: 28, discount: 4.2, total: 23.8,
      items: [{ productId: 'p1', productName: 'Morango', quantity: 2, originalUnitPrice: 14, appliedUnitPrice: 11.9,
        subtotal: 28, discount: 4.2, total: 23.8 }] } };

  beforeEach(() => {
    vi.resetAllMocks(); user.set({ id: 'owner', email: 'owner@example.invalid', roles: ['Cliente'] }); sessionState.set('ready');
    api.pending.mockReturnValue(null); api.preview.mockResolvedValue(preview); api.confirm.mockResolvedValue(approved);
    TestBed.configureTestingModule({ imports: [CheckoutPage], providers: [provideRouter([]),
      { provide: AuthService, useValue: auth }, { provide: CheckoutService, useValue: api }] });
    fixture = TestBed.createComponent(CheckoutPage); page = fixture.nativeElement;
  });

  async function render() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  function fill() {
    for (const [key, value] of Object.entries(address)) {
      const input = page.querySelector<HTMLInputElement>('#checkout-' + key);
      if (input) { input.value = value ?? ''; input.dispatchEvent(new Event('input', { bubbles: true })); }
    }
  }
  async function review() {
    await render(); fill();
    page.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await render();
  }
  async function confirm() { page.querySelector<HTMLButtonElement>('#confirm-checkout')!.click(); await render(); }

  it('asks anonymous users to log in without requesting a preview', async () => {
    user.set(null); await render();
    expect(page.textContent).toContain('Entre na sua conta'); expect(api.preview).not.toHaveBeenCalled();
  });

  it('blocks empty and malformed addresses before requesting preview', async () => {
    await render(); page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true })); await render();
    expect(api.preview).not.toHaveBeenCalled(); expect(page.textContent).toContain('Confira os campos');
    fill(); const cep = page.querySelector<HTMLInputElement>('#checkout-postalCode')!;
    cep.value = '123'; cep.dispatchEvent(new Event('input')); page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true })); await render();
    expect(api.preview).not.toHaveBeenCalled();
  });

  it('renders server totals and requires a fresh review after editing the address', async () => {
    await review();
    expect(page.querySelector('[data-testid=checkout-total]')?.textContent).toContain('23,80');
    expect(page.textContent).toContain('4,20');
    expect(page.querySelector<HTMLButtonElement>('#confirm-checkout')?.disabled).toBe(false);
    const number = page.querySelector<HTMLInputElement>('#checkout-number')!;
    number.value = '2'; number.dispatchEvent(new Event('input')); await render();
    expect(page.querySelector('#confirm-checkout')).toBeNull(); expect(api.confirm).not.toHaveBeenCalled();
  });

  it('sends the reviewed snapshot once, blocks duplicate clicks and displays the receipt', async () => {
    await review();
    let resolve!: (result: ConfirmationResponse) => void;
    api.confirm.mockReturnValue(new Promise<ConfirmationResponse>(done => resolve = done));
    const button = page.querySelector<HTMLButtonElement>('#confirm-checkout')!;
    button.click(); button.click(); fixture.detectChanges();
    expect(api.confirm).toHaveBeenCalledTimes(1); expect(button.disabled).toBe(true);
    const sent: ConfirmationRequest = api.confirm.mock.calls[0][0];
    expect(sent.confirmationId).toBeTruthy(); expect(sent.expectedItems).toEqual([{ productId: 'p1', quantity: 2, price: 14, effectivePrice: 11.9 }]);
    expect(api.remember).toHaveBeenCalledWith('owner', sent);
    resolve(approved); await render();
    expect(page.textContent).toContain('Pedido confirmado'); expect(page.textContent).toContain('order-1');
    expect(page.querySelector('[data-testid=confirmed-total]')?.textContent).toContain('23,80');
    expect(api.forget).toHaveBeenCalledWith('owner'); expect(page.querySelector('form')).toBeNull();
  });

  it('shows refusal without a receipt and requires review before another attempt', async () => {
    api.confirm.mockResolvedValue({ ...approved, code: 'declined', order: null });
    await review(); await confirm();
    expect(page.textContent).toContain('recusado na simulação'); expect(page.querySelector('#confirmed-title')).toBeNull();
    expect(page.querySelector<HTMLButtonElement>('#confirm-checkout')?.disabled).toBe(true);
    expect(api.forget).toHaveBeenCalledWith('owner');
  });

  it('blocks unavailable or empty cart returned by preview', async () => {
    const blocked = { ...preview, canProceed: false, issues: [{ code: 'unavailable_product', message: 'Estoque insuficiente.', productId: 'p1' }] };
    api.preview.mockRejectedValue(new HttpErrorResponse({ status: 409, error: blocked }));
    await review();
    expect(page.textContent).toContain('Estoque insuficiente'); expect(page.querySelector<HTMLButtonElement>('#confirm-checkout')?.disabled).toBe(true);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it('shows updated prices after conflict and demands explicit review again', async () => {
    api.confirm.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'checkout_changed',
      preview: { ...preview, cart: { ...preview.cart, total: 30 } } } }));
    await review(); await confirm();
    expect(page.querySelector('[data-testid=checkout-total]')?.textContent).toContain('30,00');
    expect(page.querySelector<HTMLButtonElement>('#confirm-checkout')?.disabled).toBe(true);
    expect(api.forget).toHaveBeenCalledWith('owner'); expect(page.textContent).toContain('exige revisão');
  });

  it.each([0, 500])('recovers an uncertain status %s with the same identifier and body', async status => {
    api.confirm.mockRejectedValueOnce(new HttpErrorResponse({ status })).mockResolvedValueOnce(approved);
    await review(); await confirm();
    expect(page.textContent).toContain('tentativa de confirmação pendente');
    expect(api.confirm).toHaveBeenCalledTimes(1);
    const sent = api.confirm.mock.calls[0][0];
    page.querySelector<HTMLButtonElement>('.feedback button')!.click(); await render();
    expect(api.confirm).toHaveBeenCalledTimes(2); expect(api.confirm.mock.calls[1][0]).toBe(sent);
    expect(api.remember).toHaveBeenCalledTimes(1); expect(page.textContent).toContain('Pedido confirmado');
  });

  it('recovers a stored pending request instead of creating a new preview or identifier', async () => {
    const pending: ConfirmationRequest = { confirmationId: 'same-id', address, paymentMethod: 'Cartao', simulation: 'Aprovar', expectedItems: [] };
    api.pending.mockReturnValue(pending); await render();
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
    page.querySelector<HTMLButtonElement>('.feedback button')!.click(); await render();
    expect(api.confirm).toHaveBeenCalledWith(pending); expect(api.preview).not.toHaveBeenCalled();
  });

  it('preserves the pending request and hides review data after session expiration', async () => {
    api.confirm.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    await review(); await confirm();
    expect(page.textContent).toContain('sessão expirou'); expect(page.querySelector('#confirm-checkout')).toBeNull();
    expect(auth.restoreSession).toHaveBeenCalledTimes(1); expect(api.forget).not.toHaveBeenCalled();
  });

  it('ignores late approval after account exit', async () => {
    await review(); let resolve!: (result: ConfirmationResponse) => void;
    api.confirm.mockReturnValue(new Promise<ConfirmationResponse>(done => resolve = done));
    page.querySelector<HTMLButtonElement>('#confirm-checkout')!.click();
    user.set(null); fixture.detectChanges(); resolve(approved); await render();
    expect(page.textContent).not.toContain('order-1'); expect(page.textContent).toContain('Entre na sua conta');
  });
});
