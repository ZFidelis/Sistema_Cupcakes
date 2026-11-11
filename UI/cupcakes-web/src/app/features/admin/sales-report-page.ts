import { CurrencyPipe, DecimalPipe, registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, OnDestroy, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SalesQuery, SalesReport, SalesReportsService } from '../../core/admin/sales-reports.service';

registerLocaleData(localePt);

function todayInSaoPaulo() {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  const part = (type: string) => parts.find(value => value.type === type)!.value;
  return `${part('year')}-${part('month')}-${part('day')}`;
}
const calendarDate: ValidatorFn = control => {
  const value = control.value as string;
  const date = new Date(value + 'T00:00:00Z');
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(date.getTime())
    && date.toISOString().slice(0, 10) === value && Number(value.slice(0, 4)) >= 2 && Number(value.slice(0, 4)) <= 9998
    ? null : { date: true };
};

@Component({
  selector: 'app-sales-report-page', imports: [ReactiveFormsModule, CurrencyPipe, DecimalPipe, RouterLink],
  templateUrl: './sales-report-page.html', styleUrl: './sales-report-page.css',
})
export class SalesReportPage implements OnDestroy {
  protected readonly auth = inject(AuthService);
  private readonly reports = inject(SalesReportsService);
  protected readonly isAdmin = computed(() => this.auth.user()?.roles.includes('Administrador') === true);
  protected readonly report = signal<SalesReport | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'error' | 'denied'>('denied');
  protected readonly message = signal('');
  protected readonly submitted = signal(false);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    date: [todayInSaoPaulo(), [Validators.required, calendarDate]],
    period: ['day' as 'day' | 'week', [Validators.required, Validators.pattern(/^(day|week)$/)]],
  });
  private applied: SalesQuery = this.form.getRawValue();
  private version = 0;

  constructor() {
    effect(() => {
      this.auth.user(); this.version++;
      this.report.set(null); this.message.set(''); this.submitted.set(false);
      this.form.reset(); this.applied = this.form.getRawValue();
      if (this.isAdmin()) untracked(() => void this.load());
      else this.state.set('denied');
    });
  }
  ngOnDestroy() { this.version++; }

  protected dateLabel(date: string) { return date.split('-').reverse().join('/'); }

  protected apply() {
    if (!this.isAdmin()) return;
    this.submitted.set(true); this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.applied = this.form.getRawValue();
    void this.load();
  }

  protected async load() {
    if (!this.isAdmin()) return;
    const owner = this.auth.user()!.id;
    const version = ++this.version;
    const current = () => this.version === version && this.auth.user()?.id === owner && this.isAdmin();
    this.report.set(null); this.message.set(''); this.state.set('loading');
    try {
      const result = await this.reports.get(this.applied);
      if (current()) { this.report.set(result); this.state.set('ready'); }
    } catch (error) {
      if (!current()) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.state.set('denied'); this.message.set('Acesso indisponível. Entre com uma conta de administrador.');
        if (error.status === 401) void this.auth.restoreSession();
      } else {
        this.state.set('error');
        if (error instanceof HttpErrorResponse && error.status === 400) this.message.set('Consulta rejeitada. Confira a data e o período e gere novamente.');
      }
    }
  }
}
