import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TrainingService, QuizDto, QuizResultDto } from '../../../core/services/training.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-training-quiz',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, PageHeaderComponent],
  templateUrl: './training-quiz.component.html',
  styleUrl: './training-quiz.component.scss'
})
export class TrainingQuizComponent implements OnInit {
  quiz: QuizDto | null = null;
  answers: Record<string, number> = {};
  loading = true;
  submitting = false;
  error: string | null = null;
  result: QuizResultDto | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private trainingService: TrainingService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.trainingService.getQuiz(id).subscribe({
      next: (q) => {
        this.quiz = q;
        this.answers = {};
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load quiz.';
        this.loading = false;
      }
    });
  }

  submit(): void {
    if (!this.quiz) return;
    const answersList = this.quiz.questions.map(q => ({
      questionId: q.id,
      selectedIndex: this.answers[q.id] ?? -1
    }));
    this.submitting = true;
    this.trainingService.submitQuiz(this.quiz.id, answersList).subscribe({
      next: (res) => {
        this.result = res;
        this.submitting = false;
      },
      error: () => {
        this.error = 'Failed to submit quiz.';
        this.submitting = false;
      }
    });
  }

  backToModule(): void {
    if (this.quiz?.moduleId) {
      this.router.navigate(['/training', 'module', this.quiz.moduleId]);
    } else {
      this.router.navigate(['/training']);
    }
  }
}
