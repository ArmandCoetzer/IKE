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
  it('requestPermit(jobCardId) POSTs', () => {
    service.requestPermit('jc1').subscribe();
    const r = httpMock.expectOne(API);
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ jobCardId: 'jc1' });
    r.flush(null);
  });
  it('paperClientSignOff(permitId) PATCHes', () => {
    service.paperClientSignOff('p1').subscribe();
    const r = httpMock.expectOne(`${API}/p1/paper-client-sign-off`);
    expect(r.request.method).toBe('PATCH');
    expect(r.request.body).toEqual({});
    r.flush(null);
  });
});
