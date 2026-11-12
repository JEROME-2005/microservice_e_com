import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { NgModule, provideBrowserGlobalErrorListeners } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BrowserModule } from '@angular/platform-browser';
import { NotificationPreferences } from './account/notification-preferences';
import { AppRoutingModule } from './app-routing-module';
import { App } from './app';
import { SignIn } from './auth/sign-in';
import { authInterceptor } from './core/auth.interceptor';
import { HomePage } from './home/home-page';
import { NotificationBell } from './notifications/notification-bell';
import { OrderPage } from './orders/order-page';

@NgModule({
  declarations: [App, HomePage, SignIn, NotificationBell, NotificationPreferences, OrderPage],
  imports: [BrowserModule, FormsModule, AppRoutingModule],
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([authInterceptor])),
  ],
  bootstrap: [App],
})
export class AppModule {}
