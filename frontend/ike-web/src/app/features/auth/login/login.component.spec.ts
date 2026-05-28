import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/services/auth.service';

describe('LoginComponent', () => {
  let authMock: jasmine.SpyObj<AuthService>;
  let router: Router;

  beforeEach(async () => {
    authMock = jasmine.createSpyObj('AuthService', ['login', 'consumeReturnUrl']);
    await TestBed.configureTestingModule({
      imports: [
        HttpClientTestingModule,
        RouterTestingModule.withRoutes([{ path: 'login', component: LoginComponent }]),
        LoginComponent
      ],
      providers: [{ provide: AuthService, useValue: authMock }]
    }).compileComponents();
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.stub();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(LoginComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('submit calls auth.login and navigates on success', () => {
    authMock.login.and.returnValue({
      subscribe: (obs: { next?: () => void; error?: () => void; complete?: () => void }) => {
        obs.next?.();
        obs.complete?.();
      }
    } as any);
    authMock.consumeReturnUrl.and.returnValue(null);
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.componentInstance.email = 'u@test.com';
    fixture.componentInstance.password = 'pass';
    fixture.componentInstance.submit();
    expect(authMock.login).toHaveBeenCalledWith({ email: 'u@test.com', password: 'pass', rememberMe: true });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('submit sets error on login failure', () => {
    authMock.login.and.returnValue({
      subscribe: (obs: { next?: () => void; error?: (e: { error?: { message?: string } }) => void; complete?: () => void }) => {
        obs.error?.({ error: { message: 'Invalid credentials' } });
        obs.complete?.();
      }
    } as any);
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.componentInstance.submit();
    expect(fixture.componentInstance.error).toBe('Invalid credentials');
    expect(fixture.componentInstance.loading).toBe(false);
  });
});
