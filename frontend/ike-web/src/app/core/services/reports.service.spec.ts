import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ReportsService } from './reports.service';

const API = 'http://localhost:5020/api/reports';

describe('ReportsService', () => {
  let service: ReportsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [ReportsService] });
    service = TestBed.inject(ReportsService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('invoicesByPeriod() GETs invoices-by-period', () => { service.invoicesByPeriod().subscribe(); httpMock.expectOne(`${API}/invoices-by-period`).flush([]); });
  it('invoicesByPeriod(from, to) GETs with params', () => { service.invoicesByPeriod('2024-01-01', '2024-12-31').subscribe(); const r = httpMock.expectOne((req) => req.url.startsWith(`${API}/invoices-by-period`) && req.urlWithParams.includes('from=2024-01-01') && req.urlWithParams.includes('to=2024-12-31')); r.flush([]); });
});
