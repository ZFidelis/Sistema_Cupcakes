import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { AdminProduct, AdminProductsService, CreateProduct } from '../../core/admin/admin-products.service';
import { AuthService } from '../../core/auth/auth.service';
import { Product } from '../../core/catalog/catalog.service';

registerLocaleData(localePt);

const money = (optional = false): ValidatorFn => control => {
  const value = (control.value as string).trim();
  if (optional && !value) return null;
  const number = Number(value.replace(',', '.'));
  return /^\d+([.,]\d{1,2})?$/.test(value) && number > 0 && number <= 9999999999.99 ? null : { money: true };
};
const text = (max: number): ValidatorFn => control => {
  const value = (control.value as string).trim();
  return value.length > 0 && value.length <= max ? null : { text: true };
};
const stock: ValidatorFn = control => /^\d+$/.test(control.value.trim()) && Number(control.value) <= 2147483647 ? null : { stock: true };
const promotion: ValidatorFn = group => {
  const price = Number(group.get('price')!.value.trim().replace(',', '.'));
  const promo = group.get('promotionalPrice')!.value.trim();
  return promo && Number(promo.replace(',', '.')) >= price ? { promotion: true } : null;
};

@Component({
  selector: 'app-product-create-page', imports: [ReactiveFormsModule, RouterLink, CurrencyPipe],
  templateUrl: './product-create-page.html', styleUrl: './product-create-page.css',
})
export class ProductCreatePage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly products = inject(AdminProductsService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly editingId = computed(() => this.params().get('productId'));
  protected readonly original = signal<AdminProduct | null>(null);
  protected readonly loadState = signal<'ready' | 'loading' | 'error' | 'missing'>('ready');
  protected readonly isAdmin = computed(() => this.auth.user()?.roles.includes('Administrador') === true);
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly blocked = signal(false);
  protected readonly error = signal('');
  protected readonly created = signal<Product | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', text(100)], flavor: ['', text(60)], description: ['', text(1000)],
    imagePath: ['/images/products/cupcake.svg', Validators.required],
    price: ['', money()], promotionalPrice: ['', money(true)], stock: ['', stock],
  }, { validators: promotion });
  private version = 0;

  constructor() {
    effect(() => {
      this.auth.user();
      const id = this.editingId();
      this.version++;
      this.reset();
      this.blocked.set(false);
      this.busy.set(false);
      this.original.set(null);
      this.loadState.set('ready');
      if (id && this.isAdmin()) void this.loadExisting();
    });
  }

  ngOnDestroy() { this.version++; }

  protected async loadExisting() {
    const id = this.editingId();
    if (!id || !this.isAdmin()) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.editingId() === id;
    this.reset(); this.original.set(null); this.loadState.set('loading');
    try {
      const product = (await this.products.list()).find(product => product.id.toLowerCase() === id.toLowerCase());
      if (!current()) return;
      if (!product) { this.loadState.set('missing'); return; }
      this.original.set(product);
      this.form.setValue({ name: product.name, flavor: product.flavor, description: product.description,
        imagePath: product.imagePath, price: product.price.toFixed(2).replace('.', ','),
        promotionalPrice: product.promotionalPrice === null ? '' : product.promotionalPrice.toFixed(2).replace('.', ','),
        stock: String(product.stock) });
      this.loadState.set('ready');
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.blocked.set(true); this.error.set('Acesso indisponível. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else this.loadState.set('error');
    }
  }

  protected reset() {
    this.form.reset(); this.submitted.set(false); this.error.set(''); this.created.set(null);
  }

  protected invalid(field: keyof typeof this.form.controls) {
    return this.submitted() && this.form.controls[field].invalid;
  }

  protected async submit() {
    if (!this.isAdmin() || this.blocked() || this.busy() || this.created() || this.loadState() !== 'ready') return;
    this.submitted.set(true); this.error.set(''); this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const value = this.form.getRawValue();
    const id = this.editingId();
    this.busy.set(true);
    const current = () => this.version === version && this.auth.user()?.id === owner && this.isAdmin();
    try {
      const payload: CreateProduct = {
        name: value.name.trim(), flavor: value.flavor.trim(), description: value.description.trim(),
        imagePath: value.imagePath, price: Number(value.price.trim().replace(',', '.')),
        promotionalPrice: value.promotionalPrice.trim() ? Number(value.promotionalPrice.trim().replace(',', '.')) : null,
        stock: Number(value.stock.trim()),
      };
      const result = await (id ? this.products.update(id, payload) : this.products.create(payload));
      if (current()) { this.created.set(result); this.form.reset(); }
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.blocked.set(true);
        this.error.set(error.status === 401 ? 'Sua sessão expirou. Entre novamente.' : 'Acesso administrativo negado. Entre com uma conta de administrador.');
        void this.auth.restoreSession();
      } else if (id && error instanceof HttpErrorResponse && error.status === 404) {
        this.loadState.set('missing'); this.original.set(null); this.form.reset();
      } else if (error instanceof HttpErrorResponse && error.status === 400) {
        this.error.set((id ? 'Edição rejeitada.' : 'Cadastro rejeitado.') + ' Confira os campos e as regras abaixo antes de tentar novamente.');
      } else {
        this.error.set(id ? 'Não foi possível confirmar a edição. Confira a lista de produtos antes de enviar novamente, pois os dados podem ter sido atualizados.'
          : 'Não foi possível confirmar o cadastro. Confira a vitrine antes de enviar novamente, pois o produto pode ter sido gravado.');
      }
    } finally {
      if (current()) this.busy.set(false);
    }
  }
}
