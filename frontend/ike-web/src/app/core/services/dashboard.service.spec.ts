import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DashboardService, DashboardCountsDto } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;
  const apiBase = 'http://localhost:5020/api';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [DashboardService]
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getCounts should GET dashboard/counts and return DTO', () => {
    const dto: DashboardCountsDto = {
      unprocessedRequests: 2,
      ongoingJobCards: 3,
      overdueInvoices: 1,
      requestsWithoutJobCard: 1,
      completedJobsWithoutInvoice: 0,
      lowStockPartsCount: 0
    };
    service.getCounts().subscribe((r) => {
      expect(r.unprocessedRequests).toBe(2);
      expect(r.overdueInvoices).toBe(1);
    });

    const req = httpMock.expectOne(`${apiBase}/dashboard/counts`);
    expect(req.request.method).toBe('GET');
    req.flush(dto);
  });

  it('getCounts should handle all zeros', () => {
    service.getCounts().subscribe((r) => {
      expect(r.unprocessedRequests).toBe(0);
      expect(r.lowStockPartsCount).toBe(0);
    });
    httpMock.expectOne(`${apiBase}/dashboard/counts`).flush({
      unprocessedRequests: 0,
      ongoingJobCards: 0,
      overdueInvoices: 0,
      requestsWithoutJobCard: 0,
      completedJobsWithoutInvoice: 0,
      lowStockPartsCount: 0
    });
  });
});
