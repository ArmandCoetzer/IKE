import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NotificationsService, NotificationDto, UnreadCountDto } from './notifications.service';

describe('NotificationsService', () => {
  let service: NotificationsService;
  let httpMock: HttpTestingController;
  const apiBase = 'http://localhost:5020/api';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [NotificationsService]
    });
    service = TestBed.inject(NotificationsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getUnreadCount should GET unread-count and return count', () => {
    const expected: UnreadCountDto = { count: 3 };
    service.getUnreadCount().subscribe((d) => expect(d).toEqual(expected));

    const req = httpMock.expectOne(`${apiBase}/notifications/unread-count`);
    expect(req.request.method).toBe('GET');
    req.flush(expected);
  });

  it('getUnreadCount should handle zero count', () => {
    service.getUnreadCount().subscribe((d) => expect(d.count).toBe(0));
    const req = httpMock.expectOne(`${apiBase}/notifications/unread-count`);
    req.flush({ count: 0 });
  });

  it('list should GET notifications and return array', () => {
    const expected: NotificationDto[] = [
      {
        id: 'n1',
        title: 'New request',
        body: 'Request created',
        type: 'ServiceRequest',
        relatedEntityId: 'sr-1',
        createdAt: '2025-01-01T00:00:00Z',
        readAt: undefined
      }
    ];
    service.list().subscribe((list) => expect(list).toEqual(expected));

    const req = httpMock.expectOne(`${apiBase}/notifications`);
    expect(req.request.method).toBe('GET');
    req.flush(expected);
  });

  it('list should handle empty array', () => {
    service.list().subscribe((list) => expect(list).toEqual([]));
    const req = httpMock.expectOne(`${apiBase}/notifications`);
    req.flush([]);
  });

  it('markRead should PATCH notification id with read', () => {
    const id = 'notif-123';
    service.markRead(id).subscribe();

    const req = httpMock.expectOne(`${apiBase}/notifications/${id}/read`);
    expect(req.request.method).toBe('PATCH');
    req.flush(null);
  });

  it('markAllRead should POST mark-all-read', () => {
    service.markAllRead().subscribe();

    const req = httpMock.expectOne(`${apiBase}/notifications/mark-all-read`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('getLinkFor returns correct routes', () => {
    expect(service.getLinkFor({ type: 'ServiceRequest', relatedEntityId: 'sr-1' } as NotificationDto)).toEqual(['/service-requests', 'sr-1']);
    expect(service.getLinkFor({ type: 'OverdueInvoice', relatedEntityId: 'inv-1' } as NotificationDto)).toEqual(['/invoices', 'inv-1']);
    expect(service.getLinkFor({ type: 'JobCard', relatedEntityId: 'jc-1' } as NotificationDto)).toEqual(['/job-cards', 'jc-1']);
    expect(service.getLinkFor({ type: 'JobCompleted', relatedEntityId: 'jc-2' } as NotificationDto)).toEqual(['/job-cards', 'jc-2']);
    expect(service.getLinkFor({ type: 'Other' } as NotificationDto)).toEqual(['/notifications']);
  });
});
