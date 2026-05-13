import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { UsersService } from './users.service';

const API = 'http://localhost:5020/api/users';

describe('UsersService', () => {
  let service: UsersService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [UsersService] });
    service = TestBed.inject(UsersService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('get(id) GETs single user', () => { service.get('u1').subscribe(); httpMock.expectOne(`${API}/u1`).flush({}); });
  it('getRoles() GETs roles', () => { service.getRoles().subscribe(); httpMock.expectOne(`${API}/roles`).flush([]); });
  it('getRoles(true) GETs roles with excludeClient', () => { service.getRoles(true).subscribe(); httpMock.expectOne(`${API}/roles?excludeClient=true`).flush([]); });
  it('create(request) POSTs', () => {
    service.create({ email: 'a@b.com', firstName: 'A', lastName: 'B', role: 'Admin' }).subscribe();
    const r = httpMock.expectOne(API);
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ email: 'a@b.com', firstName: 'A', lastName: 'B', role: 'Admin' });
    r.flush({});
  });
});
