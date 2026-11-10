import { Routes } from '@angular/router';
import { AuthPage } from './features/auth/auth-page';
import { Home } from './features/home/home';

export const routes: Routes = [
  { path: '', component: Home, pathMatch: 'full' },
  { path: 'entrar', component: AuthPage, data: { mode: 'login' } },
  { path: 'cadastro', component: AuthPage, data: { mode: 'register' } },
  { path: '**', redirectTo: '' },
];
