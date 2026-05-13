import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TrainingService, CourseDetailDto } from '../../../core/services/training.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-training-course',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './training-course.component.html',
  styleUrl: './training-course.component.scss'
})
export class TrainingCourseComponent implements OnInit {
  course: CourseDetailDto | null = null;
  loading = true;
  error: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private trainingService: TrainingService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.trainingService.getCourse(id).subscribe({
      next: (c) => {
        this.course = c;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load course.';
        this.loading = false;
      }
    });
  }
}
