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
    fixture.detectChanges(); expect(link()).toBeNull();
    user.set({ id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] });
    fixture.detectChanges(); expect(link()).toBeNull();
    user.set({ id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] });
    fixture.detectChanges(); expect(link()?.textContent).toContain('Produtos');
    user.set(null); fixture.detectChanges(); expect(link()).toBeNull();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Loja de cupcakes gourmet');
  });
});
