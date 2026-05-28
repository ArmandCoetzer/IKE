import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ServiceRequestsService, ServiceRequestDto } from './service-requests.service';

describe('ServiceRequestsService', () => {
  let service: ServiceRequestsService;
  let httpMock: HttpTestingController;
  const apiBase = 'http://localhost:5020/api';
  const API = `${apiBase}/servicerequests`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ServiceRequestsService]
    });
    service = TestBed.inject(ServiceRequestsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('list() should GET without params', () => {
    service.list().subscribe();
    const req = httpMock.expectOne(API);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('list(siteId, status) should GET with query params', () => {
    service.list('site1', 'New').subscribe();
    const req = httpMock.expectOne((r) => r.url.startsWith(API) && r.urlWithParams.includes('siteId=site1') && r.urlWithParams.includes('status=New'));
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('get(id) should GET single request', () => {
    const dto: ServiceRequestDto = {
      id: 'sr1',
      requestNumber: 'SR-001',
      siteId: 's1',
      description: 'Desc',
      priority: 1,
      status: 'New',
      createdAt: '2025-01-01T00:00:00Z'
    };
    service.get('sr1').subscribe((r) => expect(r.id).toBe('sr1'));
    const req = httpMock.expectOne(`${API}/sr1`);
    expect(req.request.method).toBe('GET');
    req.flush(dto);
  });

  it('create(body) should POST', () => {
    const body = { siteId: 's1', description: 'D', priority: 1 };
    service.create(body).subscribe();
    const req = httpMock.expectOne(API);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('updateStatus(id, status) should PATCH', () => {
    service.updateStatus('sr1', 'Open').subscribe();
    const req = httpMock.expectOne(`${API}/sr1/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Open' });
    req.flush({});
  });
});
