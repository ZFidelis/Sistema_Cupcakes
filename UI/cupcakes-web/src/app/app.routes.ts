import { Routes } from '@angular/router';
import { AuthPage } from './features/auth/auth-page';
import { Home } from './features/home/home';
import { CartPage } from './features/cart/cart-page';
import { CheckoutPage } from './features/checkout/checkout-page';
import { OrdersPage } from './features/orders/orders-page';
import { ProductCreatePage } from './features/admin/product-create-page';
import { ProductsPage } from './features/admin/products-page';
import { SalesReportPage } from './features/admin/sales-report-page';

export const routes: Routes = [
  { path: '', component: Home, pathMatch: 'full' },
  { path: 'carrinho', component: CartPage },
  { path: 'checkout', component: CheckoutPage },
  { path: 'admin/produtos/novo', component: ProductCreatePage },
  { path: 'admin/produtos/:productId/editar', component: ProductCreatePage },
  { path: 'admin/produtos', component: ProductsPage },
  { path: 'admin/relatorios/vendas', component: SalesReportPage },
  { path: 'admin/pedidos', component: OrdersPage, data: { administrative: true } },
  { path: 'admin/pedidos/:orderId', component: OrdersPage, data: { administrative: true } },
  { path: 'pedidos', component: OrdersPage },
  { path: 'pedidos/:orderId', component: OrdersPage },
  { path: 'entrar', component: AuthPage, data: { mode: 'login' } },
  { path: 'cadastro', component: AuthPage, data: { mode: 'register' } },
  { path: '**', redirectTo: '' },
];
