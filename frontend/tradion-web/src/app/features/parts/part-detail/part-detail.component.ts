import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PartsService, PartDto } from '../../../core/services/parts.service';
import { AuthService } from '../../../core/services/auth.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-part-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './part-detail.component.html',
  styleUrl: './part-detail.component.scss'
})
export class PartDetailComponent implements OnInit {
  item: PartDto | null = null;
  loading = true;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private partsService: PartsService,
    public auth: AuthService
  ) {}

  stockRequestQuantity(): number | undefined {
    if (!this.item) return undefined;
    const needed = (this.item.reorderLevel ?? 0) - (this.item.quantity ?? 0);
    return needed > 0 ? needed : undefined;
  }

  canRequestStock(): boolean {
    const p = this.item;
    return !!p?.supplierId && !!p?.hasSupplierEmail;
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.partsService.get(id).subscribe({
      next: (p) => {
        this.item = p;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load part.';
        this.loading = false;
      }
    });
  }
}
