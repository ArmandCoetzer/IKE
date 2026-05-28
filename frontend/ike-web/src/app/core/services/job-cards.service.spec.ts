import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { JobCardsService, JobCardListDto, CreateJobCardRequest } from './job-cards.service';

describe('JobCardsService', () => {
  let service: JobCardsService;
  let httpMock: HttpTestingController;
  const apiBase = 'http://localhost:5020/api';
  const API = `${apiBase}/jobcards`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [JobCardsService]
    });
    service = TestBed.inject(JobCardsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('list() should GET without params and return items + total', () => {
    service.list().subscribe((r) => {
      expect(r.items).toEqual([]);
      expect(r.total).toBe(0);
    });
    const req = httpMock.expectOne(API);
    expect(req.request.method).toBe('GET');
    req.flush([], { headers: { 'X-Total-Count': '0' } });
  });

  it('list(params) should GET with query params', () => {
    service.list({ siteId: 'site1', status: 'Open' }).subscribe((r) => {
      expect(r.items).toEqual([]);
      expect(r.total).toBe(5);
    });
    const req = httpMock.expectOne((r) => r.url.startsWith(API) && r.urlWithParams.includes('siteId=site1') && r.urlWithParams.includes('status=Open'));
    expect(req.request.method).toBe('GET');
    req.flush([], { headers: { 'X-Total-Count': '5' } });
  });

  it('get(id) should GET single job card', () => {
    const dto: JobCardListDto = {
      id: 'jc1',
      jobCardNumber: 'JC-001',
      siteId: 's1',
      status: 'Open',
      priority: 0,
      createdAt: '2025-01-01T00:00:00Z'
    };
    service.get('jc1').subscribe((r) => expect(r.id).toBe('jc1'));
    const req = httpMock.expectOne(`${API}/jc1`);
    expect(req.request.method).toBe('GET');
    req.flush(dto);
  });

  it('create(body) should POST', () => {
    const body: CreateJobCardRequest = { siteId: 's1', serviceRequestId: 'sr1' };
    service.create(body).subscribe();
    const req = httpMock.expectOne(API);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('updateStatus(id, status) should PATCH', () => {
    service.updateStatus('jc1', 'Completed').subscribe();
    const req = httpMock.expectOne(`${API}/jc1/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Completed' });
    req.flush({});
  });
});
