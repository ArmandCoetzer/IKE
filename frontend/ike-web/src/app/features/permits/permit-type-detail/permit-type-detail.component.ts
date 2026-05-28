import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { PermitTypesService, PermitTypeDto } from '../../../core/services/permit-types.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-permit-type-detail',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent],
  templateUrl: './permit-type-detail.component.html',
  styleUrl: './permit-type-detail.component.scss'
})
export class PermitTypeDetailComponent implements OnInit {
  item: PermitTypeDto | null = null;
  loading = true;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private permitTypesService: PermitTypesService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.permitTypesService.get(id).subscribe({
      next: (p) => {
        this.item = p;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load permit type.';
        this.loading = false;
      }
    });
  }
}
