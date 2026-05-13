import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { InvoicesService } from './invoices.service';

const API = 'http://localhost:5020/api/invoices';

describe('InvoicesService', () => {
  let service: InvoicesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [InvoicesService] });
    service = TestBed.inject(InvoicesService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('get(id) GETs single invoice', () => { service.get('i1').subscribe(); httpMock.expectOne(`${API}/i1`).flush({}); });
  it('create(body) POSTs', () => { service.create({} as any).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
  it('sendReminder(id) POSTs to send-reminder', () => { service.sendReminder('i1').subscribe(); const r = httpMock.expectOne((req) => req.url.startsWith(`${API}/i1/send-reminder`)); expect(r.request.method).toBe('POST'); r.flush(null); });
});
