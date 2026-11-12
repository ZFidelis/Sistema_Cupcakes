import { TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { AuthService, UserSession } from './core/auth/auth.service';
import { routes } from './app.routes';
import { of } from 'rxjs';
import { CatalogService } from './core/catalog/catalog.service';
import { CartService } from './core/cart/cart.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        { provide: CartService, useValue: {} },
        { provide: CatalogService, useValue: { list: () => of([]) } },
        { provide: AuthService, useValue: {
          user: signal(null), sessionState: signal('ready'),
          restoreSession: () => Promise.resolve(), logout: () => Promise.resolve(),
        } },
      ],
    })
      .compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('shows the administrative products link only for administrators', () => {
    const fixture = TestBed.createComponent(App);
    const user = TestBed.inject(AuthService).user as WritableSignal<UserSession | null>;
    const link = () => (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/produtos"]');
    const ordersLink = () => (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/pedidos"]');
    const reportLink = () => (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/relatorios/vendas"]');
    fixture.detectChanges(); expect(link()).toBeNull(); expect(ordersLink()).toBeNull();
    user.set({ id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] });
    fixture.detectChanges(); expect(link()).toBeNull(); expect(ordersLink()).toBeNull();
    expect(reportLink()).toBeNull();
    user.set({ id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] });
    fixture.detectChanges(); expect(link()?.textContent).toContain('Produtos');
    expect(ordersLink()?.textContent).toContain('Pedidos da loja');
    expect(reportLink()?.textContent).toContain('Relatório de vendas');
    user.set(null); fixture.detectChanges(); expect(link()).toBeNull(); expect(ordersLink()).toBeNull();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Loja de cupcakes gourmet');
  });

  it('skips the header without navigating away from the current route', async () => {
    const fixture = TestBed.createComponent(App); fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/'); await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    root.querySelector<HTMLAnchorElement>('.skip-link')!.click();
    expect(document.activeElement).toBe(root.querySelector('main'));
    expect(router.url).toBe('/');
    expect(root.querySelector('main')?.tabIndex).toBe(-1);
  });

  it('moves focus to the heading after navigation without adding it to the Tab order', async () => {
    const fixture = TestBed.createComponent(App); fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/'); await fixture.whenStable();
    await router.navigateByUrl('/carrinho'); await fixture.whenStable();
    const heading = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('main h1');
    expect(document.activeElement).toBe(heading);
    expect(heading?.tabIndex).toBe(-1);
    expect(heading?.textContent).toContain('Meu carrinho');
  });
});
