import { TestBed } from '@angular/core/testing';
import { HttpRequest } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { jwtInterceptor } from './jwt.interceptor';

describe('jwtInterceptor', () => {
  let authMock: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authMock = jasmine.createSpyObj('AuthService', ['getToken']);
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: authMock }]
    });
  });

  it('adds Authorization header when token exists', (done) => {
    authMock.getToken.and.returnValue('fake-token');
    const req = new HttpRequest('GET', '/api/test');
    const next = jasmine.createSpy('next').and.callFake((r: HttpRequest<unknown>) => {
      expect(r.headers.get('Authorization')).toBe('Bearer fake-token');
      done();
      return of({});
    });
    TestBed.runInInjectionContext(() => jwtInterceptor(req, next));
    expect(next).toHaveBeenCalled();
  });

  it('does not add Authorization when no token', (done) => {
    authMock.getToken.and.returnValue(null);
    const req = new HttpRequest('GET', '/api/test');
    const next = jasmine.createSpy('next').and.callFake((r: HttpRequest<unknown>) => {
      expect(r.headers.has('Authorization')).toBe(false);
      done();
      return of({});
    });
    TestBed.runInInjectionContext(() => jwtInterceptor(req, next));
    expect(next).toHaveBeenCalledWith(req);
  });
});
