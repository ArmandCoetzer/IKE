import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { SitesService } from './sites.service';

const API = 'http://localhost:5020/api/sites';

describe('SitesService', () => {
  let service: SitesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [SitesService] });
    service = TestBed.inject(SitesService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('get(id) GETs single site', () => { service.get('s1').subscribe(); httpMock.expectOne(`${API}/s1`).flush({}); });
  it('create(request) POSTs', () => { service.create({ name: 'Site1' }).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
  it('update(id, request) PUTs', () => { service.update('s1', {}).subscribe(); const r = httpMock.expectOne(`${API}/s1`); expect(r.request.method).toBe('PUT'); r.flush({}); });
});
