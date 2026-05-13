import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TrainingService } from './training.service';

const API = 'http://localhost:5020/api/training';

describe('TrainingService', () => {
  let service: TrainingService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [TrainingService] });
    service = TestBed.inject(TrainingService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('listCourses() GETs courses', () => { service.listCourses().subscribe(); httpMock.expectOne(`${API}/courses`).flush([]); });
  it('getCourse(id) GETs course', () => { service.getCourse('c1').subscribe(); httpMock.expectOne(`${API}/courses/c1`).flush({}); });
  it('listSetupCourses() GETs setup/courses', () => { service.listSetupCourses().subscribe(); httpMock.expectOne(`${API}/setup/courses`).flush([]); });
});
