import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PermitTypesService } from './permit-types.service';

const API = 'http://localhost:5020/api/permittypes';

describe('PermitTypesService', () => {
  let service: PermitTypesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [PermitTypesService] });
    service = TestBed.inject(PermitTypesService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('should be created', () => expect(service).toBeTruthy());
  it('list() GETs API', () => { service.list().subscribe(); httpMock.expectOne(API).flush([]); });
  it('get(id) GETs single permit type', () => { service.get('pt1').subscribe(); httpMock.expectOne(`${API}/pt1`).flush({}); });
  it('create(body) POSTs', () => { service.create({ name: 'Permit1' }).subscribe(); const r = httpMock.expectOne(API); expect(r.request.method).toBe('POST'); r.flush({}); });
});
