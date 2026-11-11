import { Routes } from '@angular/router';
import { AuthPage } from './features/auth/auth-page';
import { Home } from './features/home/home';
import { CartPage } from './features/cart/cart-page';
import { CheckoutPage } from './features/checkout/checkout-page';
import { OrdersPage } from './features/orders/orders-page';
import { ProductCreatePage } from './features/admin/product-create-page';

export const routes: Routes = [
  { path: '', component: Home, pathMatch: 'full' },
  { path: 'carrinho', component: CartPage },
  { path: 'checkout', component: CheckoutPage },
  { path: 'admin/produtos/novo', component: ProductCreatePage },
  { path: 'pedidos', component: OrdersPage },
  { path: 'pedidos/:orderId', component: OrdersPage },
  { path: 'entrar', component: AuthPage, data: { mode: 'login' } },
  { path: 'cadastro', component: AuthPage, data: { mode: 'register' } },
  { path: '**', redirectTo: '' },
];
