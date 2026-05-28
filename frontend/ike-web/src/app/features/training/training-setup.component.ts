import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TrainingService, CourseDto, CourseDetailDto, ModuleSummaryDto, ModuleDetailDto, QuizDto, QuizQuestionDto, BadgeDto } from '../../core/services/training.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { API_BASE } from '../../core/api-url';
import { Editor, NgxEditorModule } from 'ngx-editor';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-training-setup',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, NgxEditorModule, PageHeaderComponent],
  templateUrl: './training-setup.component.html',
  styleUrl: './training-setup.component.scss'
})
export class TrainingSetupComponent implements OnInit, OnDestroy {
  courses: CourseDto[] = [];
  loading = true;
  error: string | null = null;
  createCourseName = '';
  createCourseDescription = '';
  creatingCourse = false;
  addModuleCourseId: string | null = null;
  addModuleTitle = '';
  addModuleSortOrder = 0;
  addingModule = false;
  canManage = false;
  editingCourseId: string | null = null;
  editingCourseName = '';
  editingCourseDescription = '';
  editingCourseSortOrder = 0;
  editingCourseIsActive = true;
  savingCourse = false;
  expandedCourseId: string | null = null;
  courseDetail: CourseDetailDto | null = null;
  loadingDetail = false;
  editingModuleId: string | null = null;
  moduleEdit: Partial<ModuleDetailDto> = {};
  savingModule = false;
  addingQuizModuleId: string | null = null;
  quizName = '';
  quizPassScore = 70;
  creatingQuiz = false;
  addingQuestionQuizId: string | null = null;
  questionText = '';
  questionOptionsStr = '';
  questionCorrectIndex = 0;
  addingQuestion = false;
  moduleEditor: Editor | null = null;
  badges: BadgeDto[] = [];
  loadingBadges = false;
  addingBadgeCourseId: string | null = null;
  addBadgeName = '';
  addBadgeDescription = '';
  addBadgeValidityMonths = 12;
  addingBadge = false;
  editingBadgeId: string | null = null;
  editBadgeName = '';
  editBadgeDescription = '';
  editBadgeValidityMonths = 12;
  savingBadge = false;
  uploadingImage = false;
  uploadingVideo = false;
  quizQuestions: QuizQuestionDto[] = [];
  editingQuestionId: string | null = null;
  editQuestionText = '';
  editQuestionOptionsStr = '';
  editQuestionCorrectIndex = 0;
  editQuestionSortOrder = 0;
  savingQuestion = false;

  private trainingService = inject(TrainingService);
  private toast = inject(ToastService);
  private auth = inject(AuthService);

  ngOnDestroy(): void {
    if (this.moduleEditor) {
      this.moduleEditor.destroy();
      this.moduleEditor = null;
    }
  }

  ngOnInit(): void {
    this.canManage = this.auth.hasPermission('ManageTraining');
    this.loadCourses();
  }

  loadCourses(): void {
    this.loading = true;
    this.error = null;
    const list$ = this.canManage ? this.trainingService.listSetupCourses() : this.trainingService.listCourses();
    list$.subscribe({
      next: (list) => {
        this.courses = list;
        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load courses.';
        this.loading = false;
      }
    });
  }

  createCourse(): void {
    const name = this.createCourseName?.trim();
    if (!name) return;
    this.creatingCourse = true;
    this.error = null;
    this.trainingService.createCourse({
      name,
      description: this.createCourseDescription?.trim() || undefined,
      sortOrder: this.courses.length
    }).subscribe({
      next: () => {
        this.createCourseName = '';
        this.createCourseDescription = '';
        this.creatingCourse = false;
        this.toast.success('Course created.');
        this.loadCourses();
      },
      error: () => {
        this.error = 'Failed to create course.';
        this.creatingCourse = false;
        this.toast.error('Failed to create course.');
      }
    });
  }

  openAddModule(course: CourseDto): void {
    this.addModuleCourseId = course.id;
    this.addModuleTitle = '';
    this.addModuleSortOrder = course.moduleCount;
  }

  cancelAddModule(): void {
    this.addModuleCourseId = null;
    this.addModuleTitle = '';
  }

  addModule(): void {
    if (!this.addModuleCourseId) return;
    const title = this.addModuleTitle?.trim();
    if (!title) return;
    this.addingModule = true;
    this.error = null;
    this.trainingService.createModule(this.addModuleCourseId, {
      title,
      sortOrder: this.addModuleSortOrder
    }).subscribe({
      next: () => {
        this.addModuleCourseId = null;
        this.addModuleTitle = '';
        this.addingModule = false;
        this.loadCourses();
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
      },
      error: () => {
        this.error = 'Failed to add module.';
        this.addingModule = false;
      }
    });
  }

  startEditCourse(c: CourseDto): void {
    this.editingCourseId = c.id;
    this.editingCourseName = c.name;
    this.editingCourseDescription = c.description ?? '';
    this.editingCourseSortOrder = c.sortOrder;
    this.editingCourseIsActive = c.isActive;
  }

  cancelEditCourse(): void {
    this.editingCourseId = null;
  }

  saveCourse(): void {
    if (!this.editingCourseId) return;
    const name = this.editingCourseName?.trim();
    if (!name) return;
    this.savingCourse = true;
    this.error = null;
    this.trainingService.updateCourse(this.editingCourseId, {
      name,
      description: this.editingCourseDescription?.trim() || undefined,
      sortOrder: this.editingCourseSortOrder,
      isActive: this.editingCourseIsActive
    }).subscribe({
      next: () => {
        this.editingCourseId = null;
        this.savingCourse = false;
        this.toast.success('Course saved.');
        this.loadCourses();
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
      },
      error: () => {
        this.error = 'Failed to update course.';
        this.savingCourse = false;
        this.toast.error('Failed to update course.');
      }
    });
  }

  toggleCourseDetail(courseId: string): void {
    if (this.expandedCourseId === courseId) {
      this.expandedCourseId = null;
      this.courseDetail = null;
      this.editingModuleId = null;
      this.addingQuizModuleId = null;
      this.addingQuestionQuizId = null;
      this.badges = [];
      return;
    }
    this.expandedCourseId = courseId;
    this.loadCourseDetail(courseId);
    this.loadBadges(courseId);
  }

  loadBadges(courseId: string): void {
    if (!this.canManage) return;
    this.loadingBadges = true;
    this.trainingService.listCourseBadges(courseId).subscribe({
      next: (list) => { this.badges = list; this.loadingBadges = false; },
      error: () => { this.loadingBadges = false; }
    });
  }

  openAddBadge(courseId: string): void {
    this.addingBadgeCourseId = courseId;
    this.addBadgeName = '';
    this.addBadgeDescription = '';
    this.addBadgeValidityMonths = 12;
  }

  cancelAddBadge(): void { this.addingBadgeCourseId = null; }

  addBadge(): void {
    if (!this.addingBadgeCourseId || !this.addBadgeName?.trim()) return;
    this.addingBadge = true;
    this.trainingService.createBadge(this.addingBadgeCourseId, {
      name: this.addBadgeName.trim(),
      description: this.addBadgeDescription?.trim() || undefined,
      validityMonths: this.addBadgeValidityMonths
    }).subscribe({
      next: () => {
        this.addingBadgeCourseId = null;
        this.addingBadge = false;
        this.loadBadges(this.expandedCourseId!);
        this.toast.success('Badge created.');
      },
      error: () => { this.addingBadge = false; this.toast.error('Failed to create badge.'); }
    });
  }

  startEditBadge(b: BadgeDto): void {
    this.editingBadgeId = b.id;
    this.editBadgeName = b.name;
    this.editBadgeDescription = b.description ?? '';
    this.editBadgeValidityMonths = b.validityMonths;
  }

  cancelEditBadge(): void { this.editingBadgeId = null; }

  saveBadge(): void {
    if (!this.editingBadgeId) return;
    this.savingBadge = true;
    this.trainingService.updateBadge(this.editingBadgeId, {
      name: this.editBadgeName.trim(),
      description: this.editBadgeDescription?.trim() || undefined,
      validityMonths: this.editBadgeValidityMonths
    }).subscribe({
      next: () => {
        this.editingBadgeId = null;
        this.savingBadge = false;
        this.loadBadges(this.expandedCourseId!);
        this.toast.success('Badge saved.');
      },
      error: () => { this.savingBadge = false; this.toast.error('Failed to save badge.'); }
    });
  }

  deleteBadge(badgeId: string): void {
    if (!confirm('Delete this badge? Users who earned it will keep their records.')) return;
    this.trainingService.deleteBadge(badgeId).subscribe({
      next: () => {
        this.loadBadges(this.expandedCourseId!);
        this.toast.success('Badge deleted.');
      },
      error: () => this.toast.error('Failed to delete badge.')
    });
  }

  loadCourseDetail(courseId: string): void {
    if (!this.canManage) return;
    this.loadingDetail = true;
    this.trainingService.getSetupCourse(courseId).subscribe({
      next: (detail) => {
        this.courseDetail = detail;
        this.loadingDetail = false;
      },
      error: () => {
        this.error = 'Failed to load course detail.';
        this.loadingDetail = false;
      }
    });
  }

  startEditModule(moduleId: string): void {
    if (this.moduleEditor) {
      this.moduleEditor.destroy();
      this.moduleEditor = null;
    }
    this.editingModuleId = moduleId;
    this.moduleEdit = {};
    this.trainingService.getModule(moduleId).subscribe({
      next: (m) => {
        this.moduleEdit = { id: m.id, courseId: m.courseId, title: m.title, contentHtml: m.contentHtml, videoUrl: m.videoUrl, sortOrder: m.sortOrder };
        this.moduleEditor = new Editor({ content: (m.contentHtml as string) || '' });
      },
      error: () => (this.error = 'Failed to load module.')
    });
  }

  cancelEditModule(): void {
    if (this.moduleEditor) {
      this.moduleEditor.destroy();
      this.moduleEditor = null;
    }
    this.editingModuleId = null;
    this.moduleEdit = {};
  }

  saveModule(): void {
    const id = this.editingModuleId;
    if (!id || !this.moduleEdit.title?.trim()) return;
    this.savingModule = true;
    this.error = null;
    this.trainingService.updateModule(id, {
      title: this.moduleEdit.title.trim(),
      contentHtml: this.moduleEdit.contentHtml?.trim() || undefined,
      videoUrl: this.moduleEdit.videoUrl?.trim() || undefined,
      sortOrder: this.moduleEdit.sortOrder ?? 0
    }).subscribe({
      next: () => {
        if (this.moduleEditor) {
          this.moduleEditor.destroy();
          this.moduleEditor = null;
        }
        this.editingModuleId = null;
        this.moduleEdit = {};
        this.savingModule = false;
        this.toast.success('Module saved.');
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
        this.loadCourses();
      },
      error: () => {
        this.error = 'Failed to update module.';
        this.savingModule = false;
        this.toast.error('Failed to update module.');
      }
    });
  }

  openAddQuiz(moduleId: string): void {
    this.addingQuizModuleId = moduleId;
    this.quizName = '';
    this.quizPassScore = 70;
  }

  cancelAddQuiz(): void {
    this.addingQuizModuleId = null;
  }

  createQuiz(): void {
    if (!this.addingQuizModuleId) return;
    const name = this.quizName?.trim();
    if (!name) return;
    this.creatingQuiz = true;
    this.error = null;
    this.trainingService.createQuiz(this.addingQuizModuleId, { name, passScore: this.quizPassScore }).subscribe({
      next: () => {
        this.addingQuizModuleId = null;
        this.creatingQuiz = false;
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
        this.loadCourses();
      },
      error: () => {
        this.error = 'Failed to create quiz.';
        this.creatingQuiz = false;
      }
    });
  }

  openAddQuestion(quizId: string): void {
    this.addingQuestionQuizId = quizId;
    this.questionText = '';
    this.questionOptionsStr = '';
    this.questionCorrectIndex = 0;
    this.editingQuestionId = null;
    this.trainingService.getSetupQuiz(quizId).subscribe({
      next: (q) => { this.quizQuestions = q.questions ?? []; },
      error: () => { this.quizQuestions = []; }
    });
  }

  cancelAddQuestion(): void {
    this.addingQuestionQuizId = null;
    this.quizQuestions = [];
    this.editingQuestionId = null;
  }

  startEditQuestion(q: QuizQuestionDto): void {
    this.editingQuestionId = q.id;
    this.editQuestionText = q.questionText;
    this.editQuestionOptionsStr = q.options?.join('\n') ?? '';
    this.editQuestionCorrectIndex = q.correctIndex ?? 0;
    this.editQuestionSortOrder = q.sortOrder ?? 0;
  }

  cancelEditQuestion(): void {
    this.editingQuestionId = null;
  }

  saveQuestion(): void {
    const quizId = this.addingQuestionQuizId;
    const questionId = this.editingQuestionId;
    if (!quizId || !questionId) return;
    const options = this.editQuestionOptionsStr.split(/[,;\n]/).map(s => s.trim()).filter(Boolean);
    if (options.length < 2) {
      this.toast.error('At least 2 options required.');
      return;
    }
    if (this.editQuestionCorrectIndex < 0 || this.editQuestionCorrectIndex >= options.length) {
      this.toast.error('Correct index out of range.');
      return;
    }
    this.savingQuestion = true;
    this.trainingService.updateQuizQuestion(quizId, questionId, {
      questionText: this.editQuestionText.trim(),
      options,
      correctIndex: this.editQuestionCorrectIndex,
      sortOrder: this.editQuestionSortOrder
    }).subscribe({
      next: () => {
        this.editingQuestionId = null;
        this.savingQuestion = false;
        this.trainingService.getSetupQuiz(quizId).subscribe({ next: (q) => { this.quizQuestions = q.questions ?? []; } });
      },
      error: () => { this.savingQuestion = false; this.toast.error('Failed to update question.'); }
    });
  }

  deleteQuestion(questionId: string): void {
    const quizId = this.addingQuestionQuizId;
    if (!quizId || !confirm('Delete this question?')) return;
    this.trainingService.deleteQuizQuestion(quizId, questionId).subscribe({
      next: () => {
        this.quizQuestions = this.quizQuestions.filter(q => q.id !== questionId);
        this.toast.success('Question deleted.');
      },
      error: () => this.toast.error('Failed to delete question.')
    });
  }

  removeQuiz(quizId: string): void {
    if (!confirm('Remove this quiz? This cannot be undone and is only allowed if there are no attempts.')) return;
    this.trainingService.deleteQuiz(quizId).subscribe({
      next: () => {
        this.addingQuestionQuizId = null;
        this.quizQuestions = [];
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
        this.loadCourses();
        this.toast.success('Quiz removed.');
      },
      error: (err) => this.toast.error(err.error?.message ?? 'Failed to remove quiz.')
    });
  }

  deleteModule(moduleId: string): void {
    if (!confirm('Delete this module? Modules with quiz attempts cannot be deleted.')) return;
    this.trainingService.deleteModule(moduleId).subscribe({
      next: () => {
        if (this.expandedCourseId) this.loadCourseDetail(this.expandedCourseId);
        this.loadCourses();
        this.toast.success('Module deleted.');
      },
      error: (err) => this.toast.error(err.error?.message ?? 'Failed to delete module.')
    });
  }

  deleteCourse(courseId: string): void {
    if (!confirm('Delete this course permanently? Badges required by job types must be removed first.')) return;
    this.trainingService.deleteCourse(courseId).subscribe({
      next: () => {
        this.expandedCourseId = null;
        this.loadCourses();
        this.toast.success('Course deleted.');
      },
      error: (err) => this.toast.error(err.error?.message ?? 'Failed to delete course.')
    });
  }

  onImageFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.editingModuleId) return;
    this.uploadingImage = true;
    this.trainingService.uploadModuleMedia(this.editingModuleId, file).subscribe({
      next: (res) => {
        const src = this.toApiMediaUrl(res.url);
        const img = `<p><img src="${src}" alt="Uploaded image" style="max-width: 100%;"></p>`;
        this.moduleEdit = { ...this.moduleEdit, contentHtml: (this.moduleEdit.contentHtml || '') + img };
        this.uploadingImage = false;
        this.toast.success('Image inserted.');
      },
      error: (err) => {
        this.uploadingImage = false;
        this.toast.error(err.error?.message || 'Failed to upload image.');
      }
    });
    input.value = '';
  }

  onVideoFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || !this.editingModuleId) return;
    this.uploadingVideo = true;
    this.trainingService.uploadModuleMedia(this.editingModuleId, file).subscribe({
      next: (res) => {
        const videoUrl = this.toApiMediaUrl(res.url);
        this.moduleEdit = { ...this.moduleEdit, videoUrl };
        this.uploadingVideo = false;
        this.toast.success('Video uploaded. Save the module to keep changes.');
      },
      error: (err) => {
        this.uploadingVideo = false;
        this.toast.error(err.error?.message || 'Failed to upload video.');
      }
    });
    input.value = '';
  }

  private toApiMediaUrl(url: string): string {
    if (/^https?:\/\//i.test(url)) return url;
    const origin = API_BASE.replace(/\/api$/, '');
    return `${origin}${url.startsWith('/') ? url : `/${url}`}`;
  }

  addQuestion(): void {
    if (!this.addingQuestionQuizId) return;
    const questionText = this.questionText?.trim();
    if (!questionText) return;
    const options = this.questionOptionsStr.split(/[,;\n]/).map(s => s.trim()).filter(Boolean);
    if (options.length < 2) {
      this.error = 'At least 2 options required (comma- or newline-separated).';
      return;
    }
    if (this.questionCorrectIndex < 0 || this.questionCorrectIndex >= options.length) {
      this.error = 'Correct option index must be between 0 and ' + (options.length - 1);
      return;
    }
    this.addingQuestion = true;
    this.error = null;
    this.trainingService.addQuizQuestion(this.addingQuestionQuizId, {
      questionText,
      options,
      correctIndex: this.questionCorrectIndex
    }).subscribe({
      next: () => {
        this.questionText = '';
        this.questionOptionsStr = '';
        this.addingQuestion = false;
        this.trainingService.getSetupQuiz(this.addingQuestionQuizId!).subscribe({ next: (q) => { this.quizQuestions = q.questions ?? []; } });
      },
      error: () => {
        this.error = 'Failed to add question.';
        this.addingQuestion = false;
      }
    });
  }
}
