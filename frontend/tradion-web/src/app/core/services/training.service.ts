import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE } from '../api-url';

const API = `${API_BASE}/training`;

export interface CourseDto {
  id: string;
  name: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
  moduleCount: number;
}

export interface CourseDetailDto {
  id: string;
  name: string;
  description?: string;
  sortOrder: number;
  modules: ModuleSummaryDto[];
}

export interface ModuleSummaryDto {
  id: string;
  courseId: string;
  title: string;
  sortOrder: number;
  hasQuiz: boolean;
  quizId?: string;
  isCompleted?: boolean;
}

export interface ModuleDetailDto {
  id: string;
  courseId: string;
  courseName?: string;
  title: string;
  contentHtml?: string;
  videoUrl?: string;
  sortOrder: number;
  quizId?: string;
  quizName?: string;
  isCompleted?: boolean;
}

export interface QuizDto {
  id: string;
  moduleId: string;
  moduleTitle?: string;
  name: string;
  passScore: number;
  questions: QuizQuestionDto[];
}

export interface QuizQuestionDto {
  id: string;
  questionText: string;
  options: string[];
  correctIndex?: number;
  sortOrder: number;
}

export interface QuizResultDto {
  score: number;
  total: number;
  passed: boolean;
  passScore: number;
}

@Injectable({ providedIn: 'root' })
export class TrainingService {
  constructor(private http: HttpClient) {}

  listCourses(): Observable<CourseDto[]> {
    return this.http.get<CourseDto[]>(`${API}/courses`);
  }

  createCourse(body: { name: string; description?: string; sortOrder?: number }): Observable<CourseDto> {
    return this.http.post<CourseDto>(`${API}/courses`, {
      name: body.name,
      description: body.description ?? null,
      sortOrder: body.sortOrder ?? 0
    });
  }

  createModule(courseId: string, body: { title: string; contentHtml?: string; videoUrl?: string; sortOrder?: number }): Observable<ModuleSummaryDto> {
    return this.http.post<ModuleSummaryDto>(`${API}/courses/${courseId}/modules`, {
      title: body.title,
      contentHtml: body.contentHtml ?? null,
      videoUrl: body.videoUrl ?? null,
      sortOrder: body.sortOrder ?? 0
    });
  }

  getCourse(id: string): Observable<CourseDetailDto> {
    return this.http.get<CourseDetailDto>(`${API}/courses/${id}`);
  }

  getModule(id: string): Observable<ModuleDetailDto> {
    return this.http.get<ModuleDetailDto>(`${API}/modules/${id}`);
  }

  completeModule(id: string): Observable<void> {
    return this.http.post<void>(`${API}/modules/${id}/complete`, {});
  }

  getQuiz(id: string): Observable<QuizDto> {
    return this.http.get<QuizDto>(`${API}/quizzes/${id}`);
  }

  getSetupQuiz(id: string): Observable<QuizDto> {
    return this.http.get<QuizDto>(`${API}/setup/quizzes/${id}`);
  }

  submitQuiz(id: string, answers: { questionId: string; selectedIndex: number }[]): Observable<QuizResultDto> {
    return this.http.post<QuizResultDto>(`${API}/quizzes/${id}/submit`, { answers });
  }

  listSetupCourses(): Observable<CourseDto[]> {
    return this.http.get<CourseDto[]>(`${API}/setup/courses`);
  }

  getSetupCourse(id: string): Observable<CourseDetailDto> {
    return this.http.get<CourseDetailDto>(`${API}/setup/courses/${id}`);
  }

  updateCourse(id: string, body: { name: string; description?: string; sortOrder: number; isActive?: boolean }): Observable<CourseDto> {
    return this.http.put<CourseDto>(`${API}/courses/${id}`, {
      name: body.name,
      description: body.description ?? null,
      sortOrder: body.sortOrder ?? 0,
      isActive: body.isActive ?? true
    });
  }

  updateModule(id: string, body: { title: string; contentHtml?: string; videoUrl?: string; sortOrder: number }): Observable<ModuleSummaryDto> {
    return this.http.put<ModuleSummaryDto>(`${API}/modules/${id}`, {
      title: body.title,
      contentHtml: body.contentHtml ?? null,
      videoUrl: body.videoUrl ?? null,
      sortOrder: body.sortOrder ?? 0
    });
  }

  createQuiz(moduleId: string, body: { name: string; passScore?: number }): Observable<QuizDto> {
    return this.http.post<QuizDto>(`${API}/modules/${moduleId}/quiz`, {
      name: body.name,
      passScore: body.passScore ?? 70
    });
  }

  updateQuiz(id: string, body: { name: string; passScore: number }): Observable<QuizDto> {
    return this.http.put<QuizDto>(`${API}/quizzes/${id}`, body);
  }

  addQuizQuestion(quizId: string, body: { questionText: string; options: string[]; correctIndex: number; sortOrder?: number }): Observable<QuizQuestionDto> {
    return this.http.post<QuizQuestionDto>(`${API}/quizzes/${quizId}/questions`, {
      questionText: body.questionText,
      options: body.options,
      correctIndex: body.correctIndex,
      sortOrder: body.sortOrder ?? 0
    });
  }

  getMyBadges(): Observable<UserBadgeDto[]> {
    return this.http.get<UserBadgeDto[]>(`${API}/my-badges`);
  }

  getExpiringBadges(params?: { withinDays?: number; includeExpired?: boolean }): Observable<ExpiringBadgeDto[]> {
    const q = new URLSearchParams();
    if (params?.withinDays != null) q.set('withinDays', String(params.withinDays));
    if (params?.includeExpired != null) q.set('includeExpired', String(params.includeExpired));
    return this.http.get<ExpiringBadgeDto[]>(`${API}/badges/expiring?${q}`);
  }

  listBadges(): Observable<BadgeDto[]> {
    return this.http.get<BadgeDto[]>(`${API}/badges`);
  }

  listAllBadges(): Observable<BadgeDto[]> {
    return this.http.get<BadgeDto[]>(`${API}/setup/badges`);
  }

  listCourseBadges(courseId: string): Observable<BadgeDto[]> {
    return this.http.get<BadgeDto[]>(`${API}/setup/courses/${courseId}/badges`);
  }

  createBadge(courseId: string, body: { name: string; description?: string; validityMonths?: number }): Observable<BadgeDto> {
    return this.http.post<BadgeDto>(`${API}/setup/courses/${courseId}/badges`, {
      name: body.name,
      description: body.description ?? null,
      validityMonths: body.validityMonths ?? 12
    });
  }

  updateBadge(badgeId: string, body: { name: string; description?: string; validityMonths: number }): Observable<BadgeDto> {
    return this.http.put<BadgeDto>(`${API}/setup/badges/${badgeId}`, body);
  }

  deleteBadge(badgeId: string): Observable<void> {
    return this.http.delete<void>(`${API}/setup/badges/${badgeId}`);
  }

  uploadModuleMedia(moduleId: string, file: File): Observable<{ url: string }> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ url: string }>(`${API}/modules/${moduleId}/media`, form);
  }

  deleteModule(moduleId: string): Observable<void> {
    return this.http.delete<void>(`${API}/modules/${moduleId}`);
  }

  deleteCourse(courseId: string): Observable<void> {
    return this.http.delete<void>(`${API}/setup/courses/${courseId}`);
  }

  deleteQuiz(quizId: string): Observable<void> {
    return this.http.delete<void>(`${API}/quizzes/${quizId}`);
  }

  updateQuizQuestion(quizId: string, questionId: string, body: { questionText: string; options: string[]; correctIndex: number; sortOrder: number }): Observable<QuizQuestionDto> {
    return this.http.put<QuizQuestionDto>(`${API}/quizzes/${quizId}/questions/${questionId}`, body);
  }

  deleteQuizQuestion(quizId: string, questionId: string): Observable<void> {
    return this.http.delete<void>(`${API}/quizzes/${quizId}/questions/${questionId}`);
  }

  reorderQuizQuestions(quizId: string, questionIds: string[]): Observable<void> {
    return this.http.put<void>(`${API}/quizzes/${quizId}/questions/reorder`, questionIds);
  }
}

export interface BadgeDto {
  id: string;
  courseId: string;
  name: string;
  description?: string;
  validityMonths: number;
}

export interface UserBadgeDto {
  id: string;
  badgeId: string;
  badgeName: string;
  badgeDescription?: string;
  issuedAt: string;
  expiresAt: string;
}

export interface ExpiringBadgeDto {
  userBadgeId: string;
  userId: string;
  userName?: string;
  badgeName: string;
  expiresAt: string;
  isExpired: boolean;
}
