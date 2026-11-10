import { CurrencyPipe, DatePipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, OnDestroy, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CheckoutAddress, CheckoutPreview, CheckoutService, ConfirmationRequest, ConfirmationResponse, ConfirmedOrder } from '../../core/checkout/checkout.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-checkout-page', imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './checkout-page.html', styleUrl: './checkout-page.css',
})
export class CheckoutPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly checkout = inject(CheckoutService);
  protected readonly preview = signal<CheckoutPreview | null>(null);
  protected readonly order = signal<ConfirmedOrder | null>(null);
  protected readonly busy = signal(false);
  protected readonly uncertain = signal(false);
  protected readonly reviewed = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly expired = signal(false);
  private version = 0;
  private attempt: ConfirmationRequest | null = null;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    recipient: ['', [Validators.required, Validators.maxLength(100)]],
    street: ['', [Validators.required, Validators.maxLength(150)]],
    number: ['', [Validators.required, Validators.maxLength(20)]],
    neighborhood: ['', [Validators.required, Validators.maxLength(80)]],
    city: ['', [Validators.required, Validators.maxLength(80)]],
    state: ['', [Validators.required, Validators.pattern(/^[A-Za-z]{2}$/)]],
    postalCode: ['', [Validators.required, Validators.pattern(/^[0-9]{8}$/)]],
    complement: ['', Validators.maxLength(100)], paymentMethod: ['Pix', Validators.required], simulation: ['Aprovar', Validators.required],
  });
  private readonly changes = this.form.valueChanges.subscribe(() => {
    this.reviewed.set(false);
    this.preview.set(null);
    this.error.set('');
    this.notice.set('');
  });

  constructor() {
    effect(() => {
      const user = this.auth.user();
      this.version++;
      this.preview.set(null); this.order.set(null); this.reviewed.set(false); this.busy.set(false);
      this.uncertain.set(false); this.expired.set(false); this.submitted.set(false); this.error.set(''); this.notice.set('');
      this.form.reset({ paymentMethod: 'Pix', simulation: 'Aprovar' }, { emitEvent: false });
      this.attempt = user ? this.checkout.pending(user.id) : null;
      if (this.attempt) {
        this.form.patchValue({ ...this.attempt.address, complement: this.attempt.address.complement ?? '',
          paymentMethod: this.attempt.paymentMethod, simulation: this.attempt.simulation }, { emitEvent: false });
        this.uncertain.set(true);
      }
    });
  }

  ngOnDestroy() { this.version++; this.changes.unsubscribe(); }

  protected async review(): Promise<void> {
    if (this.busy() || this.uncertain() || !this.auth.user()) return;
    const raw = this.form.getRawValue();
    this.form.patchValue({ recipient: raw.recipient.trim(), street: raw.street.trim(), number: raw.number.trim(),
      neighborhood: raw.neighborhood.trim(), city: raw.city.trim(), state: raw.state.trim().toUpperCase(),
      postalCode: raw.postalCode.trim(), complement: raw.complement.trim() }, { emitEvent: false });
    this.submitted.set(true); this.form.markAllAsTouched();
    if (this.form.invalid) { this.error.set('Confira os campos do endereço antes de revisar.'); return; }
    const owner = this.auth.user()!.id, version = ++this.version;
    this.busy.set(true); this.error.set(''); this.notice.set(''); this.reviewed.set(false);
    try {
      const value = this.form.getRawValue();
      const address: CheckoutAddress = { recipient: value.recipient, street: value.street, number: value.number,
        neighborhood: value.neighborhood, city: value.city, state: value.state, postalCode: value.postalCode,
        complement: value.complement || null };
      const preview = await this.checkout.preview(address);
      if (!this.current(owner, version)) return;
      this.preview.set(preview); this.reviewed.set(preview.canProceed);
    } catch (error) {
      if (!this.current(owner, version)) return;
      if (error instanceof HttpErrorResponse && error.status === 401) this.sessionExpired();
      else if (error instanceof HttpErrorResponse && error.status === 409 && error.error?.cart) {
        this.preview.set(error.error); this.error.set('Revise os problemas do carrinho antes de continuar.');
      } else this.error.set('Não foi possível revisar. Confira o endereço e tente novamente.');
    } finally { if (this.current(owner, version)) this.busy.set(false); }
  }

  protected async confirm(): Promise<void> {
    if (this.busy() || !this.auth.user() || this.order()) return;
    const owner = this.auth.user()!.id;
    if (!this.uncertain()) {
      const preview = this.preview();
      if (!this.reviewed() || !preview?.canProceed) return;
      const value = this.form.getRawValue();
      this.attempt = { confirmationId: crypto.randomUUID(), address: preview.address,
        paymentMethod: value.paymentMethod as 'Pix' | 'Cartao', simulation: value.simulation as 'Aprovar' | 'Recusar',
        expectedItems: preview.cart.items.map(item => ({ productId: item.productId, quantity: item.quantity,
          price: item.price, effectivePrice: item.effectivePrice })) };
      this.checkout.remember(owner, this.attempt);
    }
    if (!this.attempt) return;
    const version = ++this.version;
    this.busy.set(true); this.error.set(''); this.notice.set('');
    try {
      const response = await this.checkout.confirm(this.attempt);
      if (!this.current(owner, version)) return;
      this.resolve(owner, response);
    } catch (error) {
      if (!this.current(owner, version)) return;
      if (error instanceof HttpErrorResponse && error.status === 401) this.sessionExpired();
      else if (error instanceof HttpErrorResponse && (error.status === 409 || error.status === 400)) {
        this.checkout.forget(owner); this.attempt = null; this.uncertain.set(false); this.reviewed.set(false);
        this.preview.set(error.error?.preview ?? null);
        this.error.set(error.status === 409
          ? 'A confirmação exige revisão. Confira os valores atualizados, ajuste o carrinho se necessário e use Revisar pedido novamente.'
          : 'Confira os dados e revise novamente antes de confirmar.');
      } else {
        this.uncertain.set(true);
        this.error.set('Não foi possível saber se a compra foi concluída. Use Conferir resultado para recuperar a mesma tentativa, sem criar outra compra.');
      }
    } finally { if (this.current(owner, version)) this.busy.set(false); }
  }

  private resolve(owner: string, response: ConfirmationResponse) {
    if (response.code === 'approved' && response.order) {
      this.order.set(response.order); this.notice.set('Pedido confirmado com pagamento simulado. Nenhuma cobrança real foi realizada.');
    } else if (response.code === 'declined') {
      this.notice.set('Pagamento recusado na simulação. Seu carrinho foi preservado e nenhuma cobrança foi realizada.');
      this.reviewed.set(false);
    } else {
      this.uncertain.set(true); this.error.set('Resultado não reconhecido. Confira o resultado da mesma tentativa.'); return;
    }
    this.checkout.forget(owner); this.attempt = null; this.uncertain.set(false);
  }

  private current(owner: string, version: number) { return this.version === version && this.auth.user()?.id === owner; }
  private sessionExpired() {
    this.preview.set(null); this.order.set(null); this.reviewed.set(false); this.expired.set(true);
    this.error.set('Sua sessão expirou. Entre novamente; uma tentativa pendente poderá ser recuperada após o login.');
    void this.auth.restoreSession();
  }
}
