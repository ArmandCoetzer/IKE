import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ClientsService } from './clients.service';

const API = 'http://localhost:5020/api/clients';

describe('ClientsService', () => {
  let service: ClientsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [ClientsService] });
    service = TestBed.inject(ClientsService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('list(isActive) GETs with param', () => { service.list(true).subscribe(); httpMock.expectOne(`${API}?isActive=true`).flush([]); });
  it('get(id) GETs single client', () => { service.get('c1').subscribe(); httpMock.expectOne(`${API}/c1`).flush({}); });
  it('create(body) POSTs', () => { service.create({ companyName: 'Co' }).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
  it('update(id, body) PUTs', () => { service.update('c1', {}).subscribe(); const r = httpMock.expectOne(`${API}/c1`); expect(r.request.method).toBe('PUT'); r.flush({}); });
});
