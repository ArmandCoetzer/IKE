import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import {
  PurchaseOrdersService,
  PurchaseOrderDto,
  CreatePurchaseOrderRequest,
  UpdatePurchaseOrderRequest
} from './purchase-orders.service';

describe('PurchaseOrdersService', () => {
  let service: PurchaseOrdersService;
  let httpMock: HttpTestingController;
  const apiBase = 'http://localhost:5020/api';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [PurchaseOrdersService]
    });
    service = TestBed.inject(PurchaseOrdersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('list() should GET without query when no params', () => {
    service.list().subscribe();
    const req = httpMock.expectOne(`${apiBase}/purchaseorders`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.toString()).toBe('');
    req.flush([]);
  });

  it('list() should GET with clientId, siteId, status when provided', () => {
    service.list('c1', 's1', 'Draft').subscribe();
    const req = httpMock.expectOne(
      (r) => r.url.startsWith(`${apiBase}/purchaseorders`) && r.urlWithParams.includes('clientId=c1') && r.urlWithParams.includes('siteId=s1') && r.urlWithParams.includes('status=Draft')
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('get(id) should GET single PO', () => {
    const dto: PurchaseOrderDto = {
      id: 'po1',
      poNumber: 'PO-001',
      hasClientPOFile: false,
      clientId: 'c1',
      siteId: 's1',
      amount: 100,
      currency: 'ZAR',
      status: 'Draft',
      createdAt: '2025-01-01T00:00:00Z'
    };
    service.get('po1').subscribe((p) => expect(p).toEqual(dto));
    const req = httpMock.expectOne(`${apiBase}/purchaseorders/po1`);
    expect(req.request.method).toBe('GET');
    req.flush(dto);
  });

  it('updateStatus(id, status) should PATCH status', () => {
    service.updateStatus('po1', 'Approved').subscribe();
    const req = httpMock.expectOne(`${apiBase}/purchaseorders/po1/status`);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Approved' });
    req.flush({});
  });

  it('create(body) should POST and return created PO', () => {
    const body: CreatePurchaseOrderRequest = {
      clientId: 'c1',
      siteId: 's1',
      amount: 200,
      currency: 'ZAR'
    };
    const created: PurchaseOrderDto = {
      id: 'po-new',
      poNumber: 'PO-002',
      hasClientPOFile: false,
      clientId: 'c1',
      siteId: 's1',
      amount: 200,
      currency: 'ZAR',
      status: 'Draft',
      createdAt: new Date().toISOString()
    };
    service.create(body).subscribe((p) => expect(p.id).toBe('po-new'));
    const req = httpMock.expectOne(`${apiBase}/purchaseorders`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(created);
  });

  it('update(id, body) should PUT', () => {
    const body: UpdatePurchaseOrderRequest = {
      clientPONumber: 'CPO-1',
      amount: 150,
      currency: 'ZAR'
    };
    service.update('po1', body).subscribe();
    const req = httpMock.expectOne(`${apiBase}/purchaseorders/po1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('uploadClientPO(id, file) should POST FormData with file', () => {
    const file = new File(['content'], 'client-po.pdf', { type: 'application/pdf' });
    service.uploadClientPO('po1', file).subscribe();

    const req = httpMock.expectOne(`${apiBase}/purchaseorders/po1/client-po-upload`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    const form = req.request.body as FormData;
    expect(form.get('file')).toBe(file);
    req.flush(null);
  });
});
