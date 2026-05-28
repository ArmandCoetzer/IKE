import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TrainingService, CourseDto, UserBadgeDto } from '../../core/services/training.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-training',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './training.component.html',
  styleUrl: './training.component.scss'
})
export class TrainingComponent implements OnInit {
  courses: CourseDto[] = [];
  myBadges: UserBadgeDto[] = [];
  loading = true;
  loadingBadges = true;
  error: string | null = null;

  constructor(private trainingService: TrainingService) {}

  ngOnInit(): void {
    this.trainingService.listCourses().subscribe({
      next: (list) => {
        this.courses = list;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load courses.';
        this.loading = false;
      }
    });
    this.trainingService.getMyBadges().subscribe({
      next: (list) => { this.myBadges = list; this.loadingBadges = false; },
      error: () => { this.loadingBadges = false; }
    });
  }

  formatExpiry(d: string): string {
    const date = new Date(d);
    const now = new Date();
    const diff = date.getTime() - now.getTime();
    const days = Math.ceil(diff / (1000 * 60 * 60 * 24));
    if (days < 0) return `Expired ${Math.abs(days)} days ago`;
    if (days === 0) return 'Expires today';
    if (days <= 30) return `Expires in ${days} days`;
    return date.toLocaleDateString();
  }

  isExpiringSoon(d: string): boolean {
    const date = new Date(d);
    const in30Days = new Date();
    in30Days.setDate(in30Days.getDate() + 30);
    return date <= in30Days;
  }
}
