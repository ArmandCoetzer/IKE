import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { AuthService, AuthResponse, LoginRequest, RegisterRequest } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  const apiBase = 'http://localhost:5020/api';

  beforeEach(() => {
    router = jasmine.createSpyObj('Router', ['navigate']);
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService, { provide: Router, useValue: router }]
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    localStorage.clear();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('login should POST to login-web and return AuthResponse', () => {
    const req: LoginRequest = { email: 'u@t.com', password: 'Pass1!' };
    const res: AuthResponse = {
      token: 'jwt',
      expiresAt: new Date().toISOString(),
      email: 'u@t.com',
      role: 'Admin',
      permissions: ['ViewRequests'],
      fullName: 'User'
    };
    service.login(req).subscribe((r) => expect(r.token).toBe('jwt'));

    const httpReq = httpMock.expectOne(`${apiBase}/auth/login-web`);
    expect(httpReq.request.method).toBe('POST');
    expect(httpReq.request.body).toEqual(req);
    httpReq.flush(res);
  });

  it('register should POST to register and return AuthResponse', () => {
    const req: RegisterRequest = { email: 'u@t.com', password: 'Pass1!', fullName: 'User', companyName: 'Acme Ltd' };
    const res: AuthResponse = {
      token: 'jwt',
      expiresAt: new Date().toISOString(),
      email: 'u@t.com',
      role: 'Admin',
      permissions: [],
      fullName: 'User'
    };
    service.register(req).subscribe((r) => expect(r.email).toBe('u@t.com'));

    const httpReq = httpMock.expectOne(`${apiBase}/auth/register`);
    expect(httpReq.request.method).toBe('POST');
    httpReq.flush(res);
  });

  it('me should GET me and return AuthResponse', () => {
    const res: AuthResponse = {
      token: 'jwt',
      expiresAt: new Date().toISOString(),
      email: 'u@t.com',
      role: 'Admin',
      permissions: [],
      fullName: 'User'
    };
    service.me().subscribe((r) => expect(r.email).toBe('u@t.com'));

    const httpReq = httpMock.expectOne(`${apiBase}/auth/me`);
    expect(httpReq.request.method).toBe('GET');
    httpReq.flush(res);
  });

  it('logout should clear token and navigate to login', () => {
    service.logout();
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('hasPermission returns false when no user loaded', () => {
    expect(service.hasPermission('ViewRequests')).toBe(false);
  });
});
