import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AuthService, UserSession } from '../../core/auth/auth.service';
import { SalesReport } from '../../core/admin/sales-reports.service';
import { SalesReportPage } from './sales-report-page';

describe('Administrative sales report', () => {
  let fixture: ComponentFixture<SalesReportPage>;
  let page: HTMLElement;
  let http: HttpTestingController;
  const user = signal<UserSession | null>(null);
  const restoreSession = vi.fn();
  const report: SalesReport = { period: 'week', startDate: '2026-11-09', endDate: '2026-11-15', timeZone: 'America/Sao_Paulo',
    currency: 'BRL', orderCount: 2, itemCount: 3, subtotal: 42, discount: 4.2, total: 37.8, isSimulation: true, isEmpty: false,
    days: Array.from({ length: 7 }, (_, index) => ({ date: '2026-11-' + String(9 + index).padStart(2, '0'),
      orderCount: index === 2 ? 2 : 0, itemCount: index === 2 ? 3 : 0, subtotal: index === 2 ? 42 : 0,
      discount: index === 2 ? 4.2 : 0, total: index === 2 ? 37.8 : 0 })) };

  beforeEach(() => {
    user.set({ id: 'admin', email: 'admin@example.invalid', roles: ['Administrador'] }); restoreSession.mockReset();
    TestBed.configureTestingModule({ imports: [SalesReportPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: AuthService, useValue: { user, sessionState: signal('ready'), restoreSession } }] });
    fixture = TestBed.createComponent(SalesReportPage); page = fixture.nativeElement; http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function start() { fixture.detectChanges(); return http.expectOne(req => req.url === '/api/admin/reports/sales'); }
  async function render() { await fixture.whenStable(); fixture.detectChanges(); }
  function fill(date: string, period = 'week') {
    const input = page.querySelector<HTMLInputElement>('#sales-date')!; input.value = date; input.dispatchEvent(new Event('input'));
    const select = page.querySelector<HTMLSelectElement>('#sales-period')!; select.value = period; select.dispatchEvent(new Event('change'));
  }
  function submit() { page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true })); fixture.detectChanges(); }

  it.each([null, { id: 'customer', email: 'customer@example.invalid', roles: ['Cliente'] }])('denies report without requesting for %s', account => {
    user.set(account); fixture.detectChanges();
    http.expectNone(req => req.url === '/api/admin/reports/sales');
    expect(page.querySelector('form')).toBeNull();
  });

  it('loads a default daily query as GET with ISO date and no write token', () => {
    const request = start();
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('period')).toBe('day');
    expect(request.request.params.get('date')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    expect(page.textContent).toContain('Carregando relatório'); request.flush(report);
  });

  it('renders API totals, complete local dates and seven daily rows including zeros', async () => {
    start().flush(report); await render();
    expect(page.querySelector('h2')?.textContent).toContain('09/11/2026 a 15/11/2026');
    expect(page.querySelector('[data-testid=sales-total]')?.textContent).toContain('37,80');
    expect(page.querySelector('tbody')?.textContent).toContain('4,20');
    expect(page.querySelectorAll('tbody tr')).toHaveLength(7);
    expect(page.querySelector('tbody tr')?.textContent).toContain('0,00');
    expect(page.querySelector('.table-scroll')?.getAttribute('tabindex')).toBe('0');
  });

  it('applies selected date/period only after submission', async () => {
    start().flush(report); await render(); fill('2026-11-11', 'day');
    http.expectNone(req => req.url === '/api/admin/reports/sales'); submit();
    const request = http.expectOne('/api/admin/reports/sales?date=2026-11-11&period=day');
    request.flush({ ...report, period: 'day', startDate: '2026-11-11', endDate: '2026-11-11', days: [report.days[2]] }); await render();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it.each(['', '0001-01-01', '9999-12-31', '2026-02-29'])('rejects invalid date %s without sending', async date => {
    start().flush(report); await render(); fill(date); submit();
    http.expectNone(req => req.url === '/api/admin/reports/sales');
    expect(page.querySelector('#sales-errors [role=alert]')).not.toBeNull();
  });

  it('shows zero totals with empty-period feedback and keeps daily rows', async () => {
    start().flush({ ...report, orderCount: 0, itemCount: 0, subtotal: 0, discount: 0, total: 0, isEmpty: true,
      days: report.days.map(day => ({ ...day, orderCount: 0, itemCount: 0, subtotal: 0, discount: 0, total: 0 })) }); await render();
    expect(page.textContent).toContain('Nenhuma venda simulada aprovada');
    expect(page.querySelector('[data-testid=sales-total]')?.textContent).toContain('0,00');
  });

  it('retries applied parameters even with unsubmitted form edits', async () => {
    const first = start(); const date = first.request.params.get('date');
    first.flush(null, { status: 503, statusText: 'Unavailable' }); await render(); fill('2026-01-01');
    page.querySelector<HTMLButtonElement>('#retry-sales')!.click();
    const retry = http.expectOne(req => req.url === '/api/admin/reports/sales');
    expect(retry.request.params.get('date')).toBe(date); expect(retry.request.params.get('period')).toBe('day');
    retry.flush(report); await render(); expect(page.querySelector('#retry-sales')).toBeNull();
  });

  it.each([401, 403])('hides report after denied response %s', async status => {
    start().flush(null, { status, statusText: 'Denied' }); await render();
    expect(page.querySelector('table')).toBeNull(); expect(page.querySelector('form')).toBeNull();
    expect(restoreSession).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
  });

  it('ignores an older response after applying new parameters', async () => {
    const old = start(); fill('2026-11-11'); submit();
    http.expectOne('/api/admin/reports/sales?date=2026-11-11&period=week').flush(report); await render();
    old.flush({ ...report, total: 999 }); await render();
    expect(page.querySelector('[data-testid=sales-total]')?.textContent).toContain('37,80');
  });

  it('ignores late response after logout', async () => {
    const request = start(); user.set(null); fixture.detectChanges(); request.flush(report); await render();
    expect(page.querySelector('table')).toBeNull(); expect(page.querySelector('[data-testid=sales-total]')).toBeNull();
  });
});
