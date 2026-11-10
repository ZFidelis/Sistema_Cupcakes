import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService, UserSession } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  const user: UserSession = { id: 'cliente-1', email: 'cliente@example.invalid', roles: ['Cliente'] };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('restores the current account after a reload', async () => {
    const pending = auth.restoreSession();
    http.expectOne('/api/auth/me').flush(user);
    await pending;
    expect(auth.user()).toEqual(user);
    expect(auth.sessionState()).toBe('ready');
  });

  it('treats 401 as a normal anonymous visit', async () => {
    const pending = auth.restoreSession();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await pending;
    expect(auth.user()).toBeNull();
    expect(auth.sessionState()).toBe('ready');
  });

  it('logs in using relative API paths and the token supplied by the API', async () => {
    const pending = auth.login('  cliente@example.invalid  ', 'SenhaTeste123');
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'test-token' });
    await Promise.resolve();
    const request = http.expectOne('/api/auth/login');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('test-token');
    expect(request.request.body).toEqual({ email: user.email, password: 'SenhaTeste123' });
    request.flush(user);
    await pending;
    expect(auth.user()).toEqual(user);
  });

  it('does not create a session automatically when registration succeeds', async () => {
    const pending = auth.register(user.email, 'SenhaTeste123');
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'test-token' });
    await Promise.resolve();
    http.expectOne('/api/auth/register').flush({ id: user.id, email: user.email, message: 'Conta criada.' },
      { status: 201, statusText: 'Created' });
    await pending;
    expect(auth.user()).toBeNull();
    expect(auth.sessionState()).toBe('ready');
  });

  it('clears the displayed account after logout succeeds', async () => {
    const restore = auth.restoreSession();
    http.expectOne('/api/auth/me').flush(user);
    await restore;
    const pending = auth.logout();
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'test-token' });
    await Promise.resolve();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    expect(auth.user()).toBeNull();
  });

  it('keeps the displayed session when logout fails', async () => {
    const restore = auth.restoreSession();
    http.expectOne('/api/auth/me').flush(user);
    await restore;
    const pending = auth.logout();
    const rejected = expect(pending).rejects.toBeTruthy();
    http.expectOne('/api/auth/antiforgery').flush({ requestToken: 'test-token' });
    await Promise.resolve();
    http.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Server Error' });
    await rejected;
    expect(auth.user()).toEqual(user);
  });
});
