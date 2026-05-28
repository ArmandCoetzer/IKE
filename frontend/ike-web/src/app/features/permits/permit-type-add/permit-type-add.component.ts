import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PermitTypesService } from '../../../core/services/permit-types.service';
import { sanitizeInternalReturnTo } from '../../../core/services/navigation.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-permit-type-add',
  standalone: true,
  imports: [CommonModule, FormsModule, PageHeaderComponent],
  templateUrl: './permit-type-add.component.html',
  styleUrl: './permit-type-add.component.scss'
})
export class PermitTypeAddComponent implements OnInit {
  name = '';
  description = '';
  submitting = false;
  error: string | null = null;
  returnTo: string | null = null;

  constructor(
    private permitTypesService: PermitTypesService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.returnTo = sanitizeInternalReturnTo(this.route.snapshot.queryParamMap.get('returnTo'));
  }

  goBack(): void {
    if (this.returnTo) this.router.navigateByUrl(this.returnTo);
    else this.router.navigate(['/permits']);
  }

  save(): void {
    this.error = null;
    if (!this.name.trim()) {
      this.error = 'Name is required.';
      return;
    }
    this.submitting = true;
    this.permitTypesService.create({
      name: this.name.trim(),
      description: this.description.trim() || undefined
    }).subscribe({
      next: (permitType) => {
        this.submitting = false;
        const back = sanitizeInternalReturnTo(this.returnTo);
        if (back) {
          this.router.navigateByUrl(back);
        } else {
          this.router.navigate(['/permits', permitType.id]);
        }
      },
      error: (err) => {
        this.submitting = false;
        this.error = err.error?.message || 'Failed to create permit type.';
      }
    });
  }
}
