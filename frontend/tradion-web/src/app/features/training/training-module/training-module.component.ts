import { Component, OnInit, SecurityContext } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml, SafeResourceUrl } from '@angular/platform-browser';
import { TrainingService, ModuleDetailDto } from '../../../core/services/training.service';
import { PageHeaderComponent } from '../../../shared/page-header/page-header.component';

@Component({
  selector: 'app-training-module',
  standalone: true,
  imports: [CommonModule, RouterLink, PageHeaderComponent],
  templateUrl: './training-module.component.html',
  styleUrl: './training-module.component.scss'
})
export class TrainingModuleComponent implements OnInit {
  module: ModuleDetailDto | null = null;
  loading = true;
  error: string | null = null;
  markingComplete = false;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private trainingService: TrainingService,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading = false;
      return;
    }
    this.trainingService.getModule(id).subscribe({
      next: (m) => {
        this.module = m;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load module.';
        this.loading = false;
      }
    });
  }

  safeContent(html: string | undefined): SafeHtml {
    if (!html) return '';
    const cleaned = this.sanitizer.sanitize(SecurityContext.HTML, html) ?? '';
    return this.sanitizer.bypassSecurityTrustHtml(cleaned);
  }

  safeVideoUrl(url: string | undefined): SafeResourceUrl | null {
    if (!url) return null;
    return this.sanitizer.bypassSecurityTrustResourceUrl(url);
  }

  isEmbeddedVideo(url: string): boolean {
    const u = url.toLowerCase();
    return u.includes('youtube') || u.includes('youtu.be') || u.includes('vimeo');
  }

  markComplete(): void {
    if (!this.module?.id) return;
    this.markingComplete = true;
    this.trainingService.completeModule(this.module.id).subscribe({
      next: () => {
        this.module = this.module ? { ...this.module, isCompleted: true } : null;
        this.markingComplete = false;
      },
      error: () => (this.markingComplete = false)
    });
  }

  goToQuiz(): void {
    if (this.module?.quizId) {
      this.router.navigate(['/training', 'quiz', this.module.quizId]);
    }
  }
}
