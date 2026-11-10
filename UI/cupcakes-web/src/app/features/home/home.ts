import { CurrencyPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CatalogService, Product } from '../../core/catalog/catalog.service';

registerLocaleData(localePt);

@Component({
  selector: 'app-home',
  imports: [CurrencyPipe],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home implements OnInit {
  private readonly catalog = inject(CatalogService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly products = signal<Product[]>([]);
  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly imageErrors = signal<Set<string>>(new Set());

  ngOnInit() {
    this.load();
  }

  protected load() {
    this.state.set('loading');
    this.catalog.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: products => {
        this.products.set(products);
        this.imageErrors.set(new Set());
        this.state.set('ready');
      },
      error: () => this.state.set('error'),
    });
  }

  protected imageFailed(id: string) {
    this.imageErrors.update(ids => new Set([...ids, id]));
  }
}
