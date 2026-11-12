import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Adds the bearer token to API calls; a 401 on an authenticated call means the session ended → forget it. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const isApi = req.url.startsWith('/api/');
  const authed = isApi && token !== null && !req.url.startsWith('/api/v1/auth/login');
  const request = authed ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((err: unknown) => {
      if (authed && err instanceof HttpErrorResponse && err.status === 401) {
        auth.clear();
      }
      return throwError(() => err);
    }),
  );
};
