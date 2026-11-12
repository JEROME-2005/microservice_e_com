import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { NotificationPreferences } from './account/notification-preferences';
import { SignIn } from './auth/sign-in';
import { HomePage } from './home/home-page';
import { OrderPage } from './orders/order-page';

const routes: Routes = [
  { path: '', component: HomePage, title: 'LankaMart' },
  { path: 'sign-in', component: SignIn, title: 'Sign in · LankaMart' },
  { path: 'account/notifications', component: NotificationPreferences, title: 'Notification settings · LankaMart' },
  { path: 'orders/:id', component: OrderPage, title: 'Order · LankaMart' },
  { path: '**', redirectTo: '' },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}
