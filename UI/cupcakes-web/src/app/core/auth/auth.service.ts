import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface UserSession {
  id: string;
  email: string;
  roles: string[];
}

interface RegistrationResponse {
  id: string;
  email: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly currentUser = signal<UserSession | null>(null);
  private readonly state = signal<'loading' | 'ready' | 'unavailable'>('loading');
  readonly user = this.currentUser.asReadonly();
  readonly sessionState = this.state.asReadonly();

  async restoreSession(): Promise<void> {
    this.state.set('loading');
    try {
      this.currentUser.set(await firstValueFrom(this.http.get<UserSession>('/api/auth/me')));
      this.state.set('ready');
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.currentUser.set(null);
        this.state.set('ready');
      } else {
        this.state.set('unavailable');
      }
    }
  }

  async register(email: string, password: string): Promise<RegistrationResponse> {
    const result = await this.post<RegistrationResponse>('/api/auth/register', { email: email.trim(), password });
    this.state.set('ready');
    return result;
  }

  async login(email: string, password: string): Promise<void> {
    const user = await this.post<UserSession>('/api/auth/login', { email: email.trim(), password });
    this.currentUser.set(user);
    this.state.set('ready');
  }

  async logout(): Promise<void> {
    try {
      await this.post<void>('/api/auth/logout', null);
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401)) throw error;
    }
    this.currentUser.set(null);
    this.state.set('ready');
  }

  private async post<T>(url: string, body: unknown): Promise<T> {
    const token = await firstValueFrom(
      this.http.get<{ requestToken: string }>('/api/auth/antiforgery'),
    );
    return firstValueFrom(this.http.post<T>(url, body, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }
}
