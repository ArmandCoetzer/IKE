import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PartsService } from './parts.service';

const API = 'http://localhost:5020/api/parts';

describe('PartsService', () => {
  let service: PartsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [PartsService] });
    service = TestBed.inject(PartsService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('list(true) GETs with lowStockOnly', () => { service.list(true).subscribe(); httpMock.expectOne(`${API}?lowStockOnly=true`).flush([]); });
  it('get(id) GETs single part', () => { service.get('pt1').subscribe(); httpMock.expectOne(`${API}/pt1`).flush({}); });
  it('create(body) POSTs', () => { service.create({ name: 'P', quantity: 0, reorderLevel: 0 }).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
  it('update(id, body) PUTs', () => { service.update('pt1', {}).subscribe(); const r = httpMock.expectOne(`${API}/pt1`); expect(r.request.method).toBe('PUT'); r.flush({}); });
});
