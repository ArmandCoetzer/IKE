import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let authMock: jasmine.SpyObj<AuthService>;
  let routerMock: jasmine.SpyObj<Router>;

  beforeEach(() => {
    authMock = jasmine.createSpyObj('AuthService', ['isLoggedIn', 'isTokenExpired', 'getToken', 'clearSession', 'setReturnUrl']);
    routerMock = jasmine.createSpyObj('Router', ['createUrlTree'], { url: '/requested' });
    routerMock.createUrlTree.and.returnValue({} as ReturnType<Router['createUrlTree']>);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authMock },
        { provide: Router, useValue: routerMock }
      ]
    });
  });

  it('returns true when user is logged in', () => {
    authMock.isLoggedIn.and.returnValue(true);
    authMock.isTokenExpired.and.returnValue(false);
    const result = TestBed.runInInjectionContext(() => authGuard(null!, { url: '/requested' } as never));
    expect(result).toBe(true);
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });

  it('returns login UrlTree when not logged in', () => {
    authMock.isLoggedIn.and.returnValue(false);
    authMock.getToken.and.returnValue(null);
    const result = TestBed.runInInjectionContext(() => authGuard(null!, { url: '/requested' } as never));
    expect(result).toBeTruthy();
    expect(authMock.setReturnUrl).toHaveBeenCalledWith('/requested');
    expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/login']);
  });
});
