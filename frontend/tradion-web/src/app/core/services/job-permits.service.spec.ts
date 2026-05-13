import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { JobPermitsService } from './job-permits.service';

const API = 'http://localhost:5020/api/jobpermits';

describe('JobPermitsService', () => {
  let service: JobPermitsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [JobPermitsService] });
    service = TestBed.inject(JobPermitsService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('setStatus(permitId, Approved) PATCHes', () => {
    service.setStatus('p1', 'Approved').subscribe();
    const r = httpMock.expectOne(`${API}/p1`);
    expect(r.request.method).toBe('PATCH');
    expect(r.request.body).toEqual({ status: 'Approved' });
    r.flush(null);
  });
  it('setStatus(permitId, Rejected) PATCHes', () => {
    service.setStatus('p1', 'Rejected').subscribe();
    const r = httpMock.expectOne(`${API}/p1`);
    expect(r.request.body).toEqual({ status: 'Rejected' });
    r.flush(null);
  });
});
