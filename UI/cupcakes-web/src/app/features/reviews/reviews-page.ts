import { DatePipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, OnDestroy, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { CatalogService, Product } from '../../core/catalog/catalog.service';
import { ProductReview, ReviewsService } from '../../core/reviews/reviews.service';

registerLocaleData(localePt);
const commentText: ValidatorFn = control => {
  const length = control.value.trim().length;
  return length >= 1 && length <= 1000 ? null : { comment: true };
};

@Component({
  selector: 'app-reviews-page', imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './reviews-page.html', styleUrl: './reviews-page.css',
})
export class ReviewsPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ReviewsService);
  private readonly catalog = inject(CatalogService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly product = signal<Product | null>(null);
  protected readonly reviews = signal<ProductReview[]>([]);
  protected readonly mine = signal<ProductReview | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'missing'>('loading');
  protected readonly ownState = signal<'loading' | 'ready' | 'error' | 'anonymous'>('anonymous');
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    rating: ['5', [Validators.required, Validators.pattern(/^[1-5]$/)]], comment: ['', commentText],
  });
  private version = 0;

  constructor() {
    effect(() => { this.auth.user(); this.params(); this.version++; this.busy.set(false); untracked(() => void this.load()); });
  }
  ngOnDestroy() { this.version++; }

  protected async load() {
    if (this.busy()) return;
    const id = this.params().get('productId')!;
    const owner = this.auth.user()?.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.params().get('productId') === id;
    this.product.set(null); this.reviews.set([]); this.mine.set(null); this.form.reset();
    this.submitted.set(false); this.notice.set(''); this.error.set(''); this.state.set('loading');
    this.ownState.set(owner ? 'loading' : 'anonymous');
    try {
      const [products, reviews] = await Promise.all([firstValueFrom(this.catalog.list()), this.api.list(id)]);
      if (!current()) return;
      const product = products.find(value => value.id.toLowerCase() === id.toLowerCase());
      if (!product) { this.state.set('missing'); return; }
      this.product.set(product); this.reviews.set(reviews); this.state.set('ready');
    } catch (error) {
      if (!current()) return;
      this.state.set(error instanceof HttpErrorResponse && error.status === 404 ? 'missing' : 'error');
      return;
    }
    if (!owner) return;
    try {
      const mine = await this.api.mine(id);
      if (!current()) return;
      this.mine.set(mine); this.form.setValue({ rating: String(mine.rating), comment: mine.comment }); this.ownState.set('ready');
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && error.status === 404) this.ownState.set('ready');
      else if (error instanceof HttpErrorResponse && error.status === 401) {
        this.ownState.set('anonymous'); this.error.set('Sua sessão expirou. Entre novamente para avaliar.'); void this.auth.restoreSession();
      } else this.ownState.set('error');
    }
  }

  protected async save() {
    const owner = this.auth.user()?.id;
    const id = this.params().get('productId')!;
    if (!owner || this.busy() || this.state() !== 'ready' || this.ownState() !== 'ready') return;
    this.submitted.set(true); this.error.set(''); this.notice.set(''); this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.params().get('productId') === id;
    this.busy.set(true);
    const value = this.form.getRawValue();
    try {
      const saved = await this.api.save(id, Number(value.rating), value.comment.trim());
      if (!current()) return;
      this.mine.set(saved); this.form.setValue({ rating: String(saved.rating), comment: saved.comment });
      this.notice.set('Sua avaliação foi salva.'); this.submitted.set(false);
      try {
        const reviews = await this.api.list(id);
        if (current()) this.reviews.set(reviews);
      } catch {
        if (current()) this.error.set('A avaliação foi salva, mas a lista não pôde ser atualizada. Use Atualizar avaliações.');
      }
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.mine.set(null); this.form.reset(); this.ownState.set('anonymous');
        this.error.set('Sua sessão expirou. Entre novamente.'); void this.auth.restoreSession();
      } else if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 409)) {
        this.mine.set(null); this.form.reset(); this.product.set(null); this.reviews.set([]); this.state.set('missing');
      } else this.error.set('Não foi possível confirmar a avaliação. Confira os campos ou use Atualizar avaliações antes de tentar novamente.');
    } finally { if (current()) this.busy.set(false); }
  }
}
