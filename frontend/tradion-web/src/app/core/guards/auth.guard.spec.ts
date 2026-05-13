import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let authMock: jasmine.SpyObj<AuthService>;
  let routerMock: jasmine.SpyObj<Router>;

  beforeEach(() => {
    authMock = jasmine.createSpyObj('AuthService', ['isLoggedIn']);
    routerMock = jasmine.createSpyObj('Router', ['navigate']);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authMock },
        { provide: Router, useValue: routerMock }
      ]
    });
  });

  it('returns true when user is logged in', () => {
    authMock.isLoggedIn.and.returnValue(true);
    const result = TestBed.runInInjectionContext(() => authGuard(null!, null!));
    expect(result).toBe(true);
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('navigates to /login and returns false when not logged in', () => {
    authMock.isLoggedIn.and.returnValue(false);
    const result = TestBed.runInInjectionContext(() => authGuard(null!, null!));
    expect(result).toBe(false);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/login']);
  });
});
