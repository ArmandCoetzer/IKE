import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DocumentsService } from './documents.service';

const API = 'http://localhost:5020/api/documents';

describe('DocumentsService', () => {
  let service: DocumentsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [DocumentsService] });
    service = TestBed.inject(DocumentsService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('getQuotePdf GETs quote pdf', () => { service.getQuotePdf('q1').subscribe(); const r = httpMock.expectOne(`${API}/quote/q1/pdf`); expect(r.request.responseType).toBe('blob'); r.flush(new Blob()); });
  it('getInvoicePdf GETs invoice pdf', () => { service.getInvoicePdf('i1').subscribe(); httpMock.expectOne(`${API}/invoice/i1/pdf`).flush(new Blob()); });
  it('getPurchaseOrderPdf GETs PO pdf', () => { service.getPurchaseOrderPdf('po1').subscribe(); httpMock.expectOne(`${API}/purchase-order/po1/pdf`).flush(new Blob()); });
  it('getPurchaseOrderClientPO GETs client PO file', () => { service.getPurchaseOrderClientPO('po1').subscribe(); httpMock.expectOne(`${API}/purchase-order/po1/client-po`).flush(new Blob()); });
});
