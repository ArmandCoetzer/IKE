import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';

describe('DashboardComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        HttpClientTestingModule,
        RouterTestingModule.withRoutes([{ path: '', component: DashboardComponent }]),
        DashboardComponent
      ],
      providers: [AuthService, DashboardService]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(DashboardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('calls getCounts on init and updates counts', () => {
    const dashboardService = TestBed.inject(DashboardService);
    spyOn(dashboardService, 'getCounts').and.returnValue(
      of({
        unprocessedRequests: 1,
        ongoingJobCards: 2,
        overdueInvoices: 3,
        requestsWithoutJobCard: 4,
        completedJobsWithoutInvoice: 5,
        lowStockPartsCount: 6
      })
    );
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    expect(dashboardService.getCounts).toHaveBeenCalled();
    expect(fixture.componentInstance.unprocessedCount).toBe(1);
    expect(fixture.componentInstance.ongoingCount).toBe(2);
    expect(fixture.componentInstance.overdueInvoicesCount).toBe(3);
    expect(fixture.componentInstance.requestsWithoutJobCard).toBe(4);
    expect(fixture.componentInstance.completedJobsWithoutInvoice).toBe(5);
    expect(fixture.componentInstance.lowStockPartsCount).toBe(6);
  });
});
