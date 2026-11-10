import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly router = inject(Router);
  protected readonly logoutBusy = signal(false);
  protected readonly notice = signal('');
  protected readonly logoutError = signal('');

  ngOnInit(): void {
    void this.auth.restoreSession();
  }

  protected async logout(): Promise<void> {
    if (this.logoutBusy()) return;
    this.logoutBusy.set(true);
    this.logoutError.set('');
    try {
      await this.auth.logout();
      this.notice.set('Você saiu da sua conta.');
      await this.router.navigateByUrl('/');
    } catch {
      this.logoutError.set('Não foi possível sair. Tente novamente.');
    } finally {
      this.logoutBusy.set(false);
    }
  }
}
