import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { QuotesService } from './quotes.service';

const API = 'http://localhost:5020/api/quotes';

describe('QuotesService', () => {
  let service: QuotesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [QuotesService] });
    service = TestBed.inject(QuotesService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('get(id) GETs single quote', () => { service.get('q1').subscribe(); httpMock.expectOne(`${API}/q1`).flush({}); });
  it('create(body) POSTs', () => { service.create({} as any).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
});
