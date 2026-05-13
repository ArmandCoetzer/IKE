import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { JobCardWorkService } from './job-card-work.service';

const API = 'http://localhost:5020/api/jobcardwork';

describe('JobCardWorkService', () => {
  let service: JobCardWorkService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [JobCardWorkService] });
    service = TestBed.inject(JobCardWorkService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('get(id) GETs job card work', () => { service.get('jc1').subscribe(); httpMock.expectOne(`${API}/jc1`).flush({}); });
});
