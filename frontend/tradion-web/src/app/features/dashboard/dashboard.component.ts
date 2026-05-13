import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from '../../core/services/dashboard.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, PageHeaderComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  unprocessedCount = 0;
  ongoingCount = 0;
  overdueInvoicesCount = 0;
  requestsWithoutJobCard = 0;
  completedJobsWithoutInvoice = 0;
  lowStockPartsCount = 0;
  completedJobsCount = 0;

  constructor(
    public auth: AuthService,
    private dashboardService: DashboardService
  ) {}

  ngOnInit(): void {
    this.dashboardService.getCounts().subscribe({
      next: (c) => {
        this.unprocessedCount = c.unprocessedRequests;
        this.ongoingCount = c.ongoingJobCards;
        this.overdueInvoicesCount = c.overdueInvoices ?? 0;
        this.requestsWithoutJobCard = c.requestsWithoutJobCard ?? 0;
        this.completedJobsWithoutInvoice = c.completedJobsWithoutInvoice ?? 0;
        this.lowStockPartsCount = c.lowStockPartsCount ?? 0;
        this.completedJobsCount = c.completedJobsCount ?? 0;
      }
    });
  }
}
