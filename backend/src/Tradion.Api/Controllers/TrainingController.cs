using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.DTOs.Training;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TrainingController : ControllerBase
{
    private const string TrainingMediaFolder = "uploads/training";
    private const int MaxMediaFileSizeBytes = 50 * 1024 * 1024; // 50 MB for video
    private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
    private static readonly string[] AllowedVideoExtensions = { ".mp4", ".webm" };

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public TrainingController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet("courses")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(List<CourseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CourseDto>>> ListCourses()
    {
        var list = await _db.Courses.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CourseDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive,
                ModuleCount = c.Modules.Count(m => m.IsActive)
            }).ToListAsync();
        return Ok(list);
    }

    [HttpPost("courses")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(CourseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CourseDto>> CreateCourse([FromBody] CreateCourseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponseBodies.Message("Name is required."));
        var course = new Course
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true
        };
        _db.Courses.Add(course);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetCourse), new { id = course.Id }, new CourseDto
        {
            Id = course.Id,
            Name = course.Name,
            Description = course.Description,
            SortOrder = course.SortOrder,
            IsActive = course.IsActive,
            ModuleCount = 0
        });
    }

    [HttpGet("setup/courses")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(List<CourseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CourseDto>>> ListSetupCourses(CancellationToken ct)
    {
        var list = await _db.Courses.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CourseDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive,
                ModuleCount = c.Modules.Count
            }).ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("setup/courses/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(CourseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseDetailDto>> GetSetupCourse(Guid id, CancellationToken ct)
    {
        var course = await _db.Courses.AsNoTracking()
            .Include(c => c.Modules)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null) return NotFound();
        var moduleIds = course.Modules.Select(m => m.Id).ToList();
        var quizByModule = await _db.TrainingQuizzes.AsNoTracking()
            .Where(q => moduleIds.Contains(q.ModuleId))
            .Select(q => new { q.ModuleId, q.Id })
            .ToListAsync(ct);
        var modules = course.Modules.OrderBy(m => m.SortOrder).ThenBy(m => m.Title)
            .Select(m => new ModuleSummaryDto
            {
                Id = m.Id,
                CourseId = course.Id,
                Title = m.Title,
                SortOrder = m.SortOrder,
                HasQuiz = quizByModule.Any(q => q.ModuleId == m.Id),
                QuizId = quizByModule.FirstOrDefault(q => q.ModuleId == m.Id)?.Id
            }).ToList();
        return Ok(new CourseDetailDto
        {
            Id = course.Id,
            Name = course.Name,
            Description = course.Description,
            SortOrder = course.SortOrder,
            Modules = modules
        });
    }

    [HttpDelete("setup/courses/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCourse(Guid id, CancellationToken ct)
    {
        var course = await _db.Courses.Include(c => c.Badges).Include(c => c.Modules).ThenInclude(m => m.Quiz).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null) return NotFound();
        var moduleIds = course.Modules.Select(m => m.Id).ToList();
        foreach (var module in course.Modules)
        {
            if (module.Quiz != null)
            {
                _db.UserQuizAttempts.RemoveRange(await _db.UserQuizAttempts.Where(a => a.QuizId == module.Quiz.Id).ToListAsync(ct));
                _db.QuizQuestions.RemoveRange(await _db.QuizQuestions.Where(q => q.QuizId == module.Quiz.Id).ToListAsync(ct));
                _db.TrainingQuizzes.Remove(module.Quiz);
            }
        }
        _db.UserModuleProgress.RemoveRange(await _db.UserModuleProgress.Where(p => moduleIds.Contains(p.ModuleId)).ToListAsync(ct));
        _db.TrainingModules.RemoveRange(course.Modules);
        _db.Badges.RemoveRange(course.Badges);
        _db.Courses.Remove(course);
        await _db.SaveChangesAsync(ct);
        foreach (var mid in moduleIds)
        {
            var mediaDir = Path.Combine(_env.ContentRootPath, TrainingMediaFolder, mid.ToString("N"));
            if (Directory.Exists(mediaDir))
            {
                try { Directory.Delete(mediaDir, true); } catch { /* best effort */ }
            }
        }
        return NoContent();
    }

    [HttpPut("courses/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(CourseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseDto>> UpdateCourse(Guid id, [FromBody] UpdateCourseRequest request, CancellationToken ct)
    {
        var course = await _db.Courses.FindAsync([id], ct);
        if (course == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponseBodies.Message("Name is required."));
        course.Name = request.Name.Trim();
        course.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        course.SortOrder = request.SortOrder;
        course.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        var moduleCount = await _db.TrainingModules.CountAsync(m => m.CourseId == id && m.IsActive, ct);
        return Ok(new CourseDto
        {
            Id = course.Id,
            Name = course.Name,
            Description = course.Description,
            SortOrder = course.SortOrder,
            IsActive = course.IsActive,
            ModuleCount = moduleCount
        });
    }

    [HttpPost("modules/{id:guid}/media")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(TrainingMediaUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrainingMediaUploadResponse>> UploadModuleMedia(Guid id, [FromForm] IFormFile file, CancellationToken ct)
    {
        var module = await _db.TrainingModules.FindAsync([id], ct);
        if (module == null) return NotFound();
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > MaxMediaFileSizeBytes)
            return BadRequest(new { message = "File size exceeds 50 MB limit." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var isImage = AllowedImageExtensions.Contains(ext);
        var isVideo = AllowedVideoExtensions.Contains(ext);
        if (!isImage && !isVideo)
            return BadRequest(new { message = "Allowed: images (PNG, JPG, JPEG, GIF, WEBP) or video (MP4, WEBM)." });
        var sigErr = await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(file, ext, ct);
        if (sigErr != null)
            return BadRequest(new { message = sigErr });
        var dir = Path.Combine(_env.ContentRootPath, TrainingMediaFolder, id.ToString("N"));
        Directory.CreateDirectory(dir);
        var fileName = Guid.NewGuid().ToString("N") + ext;
        var fullPath = Path.Combine(dir, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await file.CopyToAsync(stream, ct);
        var url = $"/api/training/media/{id}/{fileName}";
        return Ok(new TrainingMediaUploadResponse { Url = url });
    }

    [HttpGet("media/{moduleId:guid}/{fileName}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModuleMedia(Guid moduleId, string fileName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Contains(".."))
            return NotFound();
        var module = await _db.TrainingModules.FindAsync([moduleId], ct);
        if (module == null) return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, TrainingMediaFolder, moduleId.ToString("N"), fileName);
        if (!System.IO.File.Exists(fullPath))
            return NotFound();
        var contentType = fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4"
            : fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm"
            : "application/octet-stream";
        return PhysicalFile(fullPath, contentType, fileName);
    }

    [HttpDelete("modules/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteModule(Guid id, CancellationToken ct)
    {
        var module = await _db.TrainingModules.Include(m => m.Quiz).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module == null) return NotFound();
        if (module.Quiz != null)
        {
            var hasAttempts = await _db.UserQuizAttempts.AnyAsync(a => a.QuizId == module.Quiz.Id, ct);
            if (hasAttempts)
                return BadRequest(new { message = "Cannot delete module: its quiz has attempts. Remove the quiz first." });
            var questions = await _db.QuizQuestions.Where(q => q.QuizId == module.Quiz.Id).ToListAsync(ct);
            _db.QuizQuestions.RemoveRange(questions);
            _db.TrainingQuizzes.Remove(module.Quiz);
        }
        _db.UserModuleProgress.RemoveRange(await _db.UserModuleProgress.Where(p => p.ModuleId == id).ToListAsync(ct));
        _db.TrainingModules.Remove(module);
        await _db.SaveChangesAsync(ct);
        var mediaDir = Path.Combine(_env.ContentRootPath, TrainingMediaFolder, id.ToString("N"));
        if (Directory.Exists(mediaDir))
        {
            try { Directory.Delete(mediaDir, true); } catch { /* best effort */ }
        }
        return NoContent();
    }

    [HttpPut("modules/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(ModuleSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModuleSummaryDto>> UpdateModule(Guid id, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        var module = await _db.TrainingModules.FindAsync([id], ct);
        if (module == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest(ApiResponseBodies.Message("Title is required."));
        module.Title = request.Title.Trim();
        module.ContentHtml = string.IsNullOrWhiteSpace(request.ContentHtml) ? null : request.ContentHtml.Trim();
        module.VideoUrl = string.IsNullOrWhiteSpace(request.VideoUrl) ? null : request.VideoUrl.Trim();
        module.SortOrder = request.SortOrder;
        await _db.SaveChangesAsync(ct);
        var hasQuiz = await _db.TrainingQuizzes.AnyAsync(q => q.ModuleId == id, ct);
        return Ok(new ModuleSummaryDto
        {
            Id = module.Id,
            CourseId = module.CourseId,
            Title = module.Title,
            SortOrder = module.SortOrder,
            HasQuiz = hasQuiz
        });
    }

    [HttpPost("modules/{id:guid}/quiz")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizDto>> CreateQuiz(Guid id, [FromBody] CreateQuizRequest request, CancellationToken ct)
    {
        var module = await _db.TrainingModules.FindAsync([id], ct);
        if (module == null) return NotFound();
        if (await _db.TrainingQuizzes.AnyAsync(q => q.ModuleId == id, ct))
            return BadRequest(ApiResponseBodies.Message("Module already has a quiz."));
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponseBodies.Message("Name is required."));
        var quiz = new TrainingQuiz
        {
            Id = Guid.NewGuid(),
            ModuleId = id,
            Name = request.Name.Trim(),
            PassScore = Math.Clamp(request.PassScore, 0, 100),
            CreatedAt = DateTime.UtcNow
        };
        _db.TrainingQuizzes.Add(quiz);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetQuiz), new { id = quiz.Id }, new QuizDto
        {
            Id = quiz.Id,
            ModuleId = quiz.ModuleId,
            ModuleTitle = module.Title,
            Name = quiz.Name,
            PassScore = quiz.PassScore,
            Questions = new List<QuizQuestionDto>()
        });
    }

    [HttpDelete("quizzes/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuiz(Guid id, CancellationToken ct)
    {
        var quiz = await _db.TrainingQuizzes.FindAsync([id], ct);
        if (quiz == null) return NotFound();
        var hasAttempts = await _db.UserQuizAttempts.AnyAsync(a => a.QuizId == id, ct);
        if (hasAttempts)
            return BadRequest(new { message = "Cannot remove quiz: it has attempts. Delete is not allowed." });
        var questions = await _db.QuizQuestions.Where(q => q.QuizId == id).ToListAsync(ct);
        _db.QuizQuestions.RemoveRange(questions);
        _db.TrainingQuizzes.Remove(quiz);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("quizzes/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizDto>> UpdateQuiz(Guid id, [FromBody] UpdateQuizRequest request, CancellationToken ct)
    {
        var quiz = await _db.TrainingQuizzes.Include(q => q.Module).FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponseBodies.Message("Name is required."));
        quiz.Name = request.Name.Trim();
        quiz.PassScore = Math.Clamp(request.PassScore, 0, 100);
        await _db.SaveChangesAsync(ct);
        return await GetQuiz(id, ct);
    }

    [HttpPost("quizzes/{id:guid}/questions")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(QuizQuestionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizQuestionDto>> AddQuizQuestion(Guid id, [FromBody] CreateQuizQuestionRequest request, CancellationToken ct)
    {
        var quiz = await _db.TrainingQuizzes.FindAsync([id], ct);
        if (quiz == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.QuestionText)) return BadRequest(ApiResponseBodies.Message("Question text is required."));
        if (request.Options == null || request.Options.Count < 2) return BadRequest(ApiResponseBodies.Message("At least 2 options are required."));
        if (request.CorrectIndex < 0 || request.CorrectIndex >= request.Options.Count)
            return BadRequest(ApiResponseBodies.Message("CorrectIndex is out of range."));
        var optionsJson = JsonSerializer.Serialize(request.Options);
        var maxOrder = await _db.QuizQuestions.Where(q => q.QuizId == id).MaxAsync(q => (int?)q.SortOrder, ct) ?? -1;
        var question = new QuizQuestion
        {
            Id = Guid.NewGuid(),
            QuizId = id,
            QuestionText = request.QuestionText.Trim(),
            OptionsJson = optionsJson,
            CorrectIndex = request.CorrectIndex,
            SortOrder = request.SortOrder >= 0 ? request.SortOrder : maxOrder + 1,
            CreatedAt = DateTime.UtcNow
        };
        _db.QuizQuestions.Add(question);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetQuiz), new { id }, new QuizQuestionDto
        {
            Id = question.Id,
            QuestionText = question.QuestionText,
            Options = request.Options,
            SortOrder = question.SortOrder
        });
    }

    [HttpPut("quizzes/{quizId:guid}/questions/{questionId:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(QuizQuestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizQuestionDto>> UpdateQuizQuestion(Guid quizId, Guid questionId, [FromBody] UpdateQuizQuestionRequest request, CancellationToken ct)
    {
        var question = await _db.QuizQuestions.FirstOrDefaultAsync(q => q.Id == questionId && q.QuizId == quizId, ct);
        if (question == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.QuestionText)) return BadRequest(ApiResponseBodies.Message("Question text is required."));
        if (request.Options == null || request.Options.Count < 2) return BadRequest(ApiResponseBodies.Message("At least 2 options are required."));
        if (request.CorrectIndex < 0 || request.CorrectIndex >= request.Options.Count)
            return BadRequest(ApiResponseBodies.Message("CorrectIndex is out of range."));
        question.QuestionText = request.QuestionText.Trim();
        question.OptionsJson = JsonSerializer.Serialize(request.Options);
        question.CorrectIndex = request.CorrectIndex;
        question.SortOrder = request.SortOrder;
        await _db.SaveChangesAsync(ct);
        return Ok(new QuizQuestionDto
        {
            Id = question.Id,
            QuestionText = question.QuestionText,
            Options = request.Options,
            SortOrder = question.SortOrder
        });
    }

    [HttpDelete("quizzes/{quizId:guid}/questions/{questionId:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuizQuestion(Guid quizId, Guid questionId, CancellationToken ct)
    {
        var question = await _db.QuizQuestions.FirstOrDefaultAsync(q => q.Id == questionId && q.QuizId == quizId, ct);
        if (question == null) return NotFound();
        _db.QuizQuestions.Remove(question);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("quizzes/{quizId:guid}/questions/reorder")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderQuizQuestions(Guid quizId, [FromBody] List<Guid> questionIds, CancellationToken ct)
    {
        if (questionIds == null || questionIds.Count == 0) return NoContent();
        var questions = await _db.QuizQuestions.Where(q => q.QuizId == quizId).ToListAsync(ct);
        if (questions.Count == 0) return NotFound();
        for (var i = 0; i < questionIds.Count; i++)
        {
            var q = questions.FirstOrDefault(x => x.Id == questionIds[i]);
            if (q != null) q.SortOrder = i;
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("courses/{id:guid}/modules")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(ModuleSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModuleSummaryDto>> CreateModule(Guid id, [FromBody] CreateModuleRequest request, CancellationToken ct)
    {
        var course = await _db.Courses.FindAsync([id], ct);
        if (course == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponseBodies.Message("Title is required."));
        var module = new TrainingModule
        {
            Id = Guid.NewGuid(),
            CourseId = id,
            Title = request.Title.Trim(),
            ContentHtml = string.IsNullOrWhiteSpace(request.ContentHtml) ? null : request.ContentHtml.Trim(),
            VideoUrl = string.IsNullOrWhiteSpace(request.VideoUrl) ? null : request.VideoUrl.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true
        };
        _db.TrainingModules.Add(module);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetModule), new { id = module.Id }, new ModuleSummaryDto
        {
            Id = module.Id,
            CourseId = id,
            Title = module.Title,
            SortOrder = module.SortOrder,
            HasQuiz = false
        });
    }

    [HttpGet("courses/{id:guid}")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(CourseDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseDetailDto>> GetCourse(Guid id, CancellationToken ct)
    {
        var course = await _db.Courses.AsNoTracking()
            .Include(c => c.Modules.Where(m => m.IsActive)).ThenInclude(m => m.Quiz)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive, ct);
        if (course == null) return NotFound();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var progress = userId != null
            ? await _db.UserModuleProgress.AsNoTracking()
                .Where(p => p.UserId == userId && course.Modules.Select(m => m.Id).Contains(p.ModuleId))
                .ToDictionaryAsync(p => p.ModuleId, p => p.CompletedAt != null, ct)
            : new Dictionary<Guid, bool>();
        var dto = new CourseDetailDto
        {
            Id = course.Id,
            Name = course.Name,
            Description = course.Description,
            SortOrder = course.SortOrder,
            Modules = course.Modules.OrderBy(m => m.SortOrder).ThenBy(m => m.Title)
                .Select(m => new ModuleSummaryDto
                {
                    Id = m.Id,
                    CourseId = course.Id,
                    Title = m.Title,
                    SortOrder = m.SortOrder,
                    HasQuiz = m.Quiz != null,
                    IsCompleted = progress.TryGetValue(m.Id, out var done) ? done : null
                }).ToList()
        };
        return Ok(dto);
    }

    [HttpGet("modules/{id:guid}")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(ModuleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModuleDetailDto>> GetModule(Guid id, CancellationToken ct)
    {
        var module = await _db.TrainingModules.AsNoTracking()
            .Include(m => m.Course)
            .Include(m => m.Quiz)
            .FirstOrDefaultAsync(m => m.Id == id && m.IsActive, ct);
        if (module == null) return NotFound();
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool? completed = null;
        if (userId != null)
        {
            var prog = await _db.UserModuleProgress.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.ModuleId == id, ct);
            completed = prog?.CompletedAt != null;
        }
        return Ok(new ModuleDetailDto
        {
            Id = module.Id,
            CourseId = module.CourseId,
            CourseName = module.Course?.Name,
            Title = module.Title,
            ContentHtml = module.ContentHtml,
            VideoUrl = module.VideoUrl,
            SortOrder = module.SortOrder,
            QuizId = module.Quiz?.Id,
            QuizName = module.Quiz?.Name,
            IsCompleted = completed
        });
    }

    [HttpPost("modules/{id:guid}/complete")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteModule(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var module = await _db.TrainingModules.FindAsync([id], ct);
        if (module == null) return NotFound();
        var existing = await _db.UserModuleProgress.FirstOrDefaultAsync(
            p => p.UserId == userId && p.ModuleId == id, ct);
        if (existing != null)
        {
            existing.VideoProgressPercent = 100;
            existing.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            _db.UserModuleProgress.Add(new UserModuleProgress
            {
                UserId = userId,
                ModuleId = id,
                VideoProgressPercent = 100,
                CompletedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync(ct);
        await TryAwardBadgesForCourseAsync(userId, module.CourseId, ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("quizzes/{id:guid}")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizDto>> GetQuiz(Guid id, CancellationToken ct)
    {
        var quiz = await _db.TrainingQuizzes.AsNoTracking()
            .Include(q => q.Module)
            .Include(q => q.Questions.OrderBy(x => x.SortOrder))
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz == null) return NotFound();
        var questions = quiz.Questions.Select(q => new QuizQuestionDto
        {
            Id = q.Id,
            QuestionText = q.QuestionText,
            Options = ParseOptions(q.OptionsJson),
            CorrectIndex = 0,
            SortOrder = q.SortOrder
        }).ToList();
        return Ok(new QuizDto
        {
            Id = quiz.Id,
            ModuleId = quiz.ModuleId,
            ModuleTitle = quiz.Module?.Title,
            Name = quiz.Name,
            PassScore = quiz.PassScore,
            Questions = questions
        });
    }

    [HttpGet("setup/quizzes/{id:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(QuizDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizDto>> GetSetupQuiz(Guid id, CancellationToken ct)
    {
        var quiz = await _db.TrainingQuizzes.AsNoTracking()
            .Include(q => q.Module)
            .Include(q => q.Questions.OrderBy(x => x.SortOrder))
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz == null) return NotFound();
        var questions = quiz.Questions.Select(q => new QuizQuestionDto
        {
            Id = q.Id,
            QuestionText = q.QuestionText,
            Options = ParseOptions(q.OptionsJson),
            CorrectIndex = q.CorrectIndex,
            SortOrder = q.SortOrder
        }).ToList();
        return Ok(new QuizDto
        {
            Id = quiz.Id,
            ModuleId = quiz.ModuleId,
            ModuleTitle = quiz.Module?.Title,
            Name = quiz.Name,
            PassScore = quiz.PassScore,
            Questions = questions
        });
    }

    [HttpPost("quizzes/{id:guid}/submit")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(QuizResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuizResultDto>> SubmitQuiz(Guid id, [FromBody] SubmitQuizRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var quiz = await _db.TrainingQuizzes
            .Include(q => q.Questions)
            .Include(q => q.Module).ThenInclude(m => m!.Course)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz == null) return NotFound();
        var correctByQuestion = quiz.Questions.ToDictionary(q => q.Id, q => q.CorrectIndex);
        int correct = 0;
        foreach (var a in request.Answers)
        {
            if (correctByQuestion.TryGetValue(a.QuestionId, out var correctIndex) && a.SelectedIndex == correctIndex)
                correct++;
        }
        int total = quiz.Questions.Count;
        bool passed = total > 0 && (correct * 100 / total) >= quiz.PassScore;
        var attempt = new UserQuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuizId = id,
            Score = total > 0 ? (correct * 100 / total) : 0,
            Passed = passed,
            AnswersJson = JsonSerializer.Serialize(request.Answers),
            CompletedAt = DateTime.UtcNow
        };
        _db.UserQuizAttempts.Add(attempt);

        if (passed && quiz.Module != null)
        {
            var module = quiz.Module;
            var existing = await _db.UserModuleProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.ModuleId == module.Id, ct);
            if (existing != null)
            {
                existing.VideoProgressPercent = 100;
                existing.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                _db.UserModuleProgress.Add(new UserModuleProgress
                {
                    UserId = userId,
                    ModuleId = module.Id,
                    VideoProgressPercent = 100,
                    CompletedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        if (passed && quiz.Module != null)
        {
            await TryAwardBadgesForCourseAsync(userId, quiz.Module.CourseId, ct);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new QuizResultDto
        {
            Score = attempt.Score,
            Total = total,
            Passed = passed,
            PassScore = quiz.PassScore
        });
    }

    private async Task TryAwardBadgesForCourseAsync(string userId, Guid courseId, CancellationToken ct)
    {
        var course = await _db.Courses.AsNoTracking()
            .Include(c => c.Modules.Where(m => m.IsActive))
            .Include(c => c.Badges.Where(b => b.IsActive))
            .FirstOrDefaultAsync(c => c.Id == courseId, ct);
        if (course == null) return;
        var moduleIds = course.Modules.Select(m => m.Id).ToList();
        var completedCount = await _db.UserModuleProgress
            .CountAsync(p => p.UserId == userId && moduleIds.Contains(p.ModuleId) && p.CompletedAt != null, ct);
        if (completedCount != moduleIds.Count) return;
        foreach (var badge in course.Badges)
        {
            var existing = await _db.UserBadges
                .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BadgeId == badge.Id && ub.IsActive && ub.ExpiresAt > DateTime.UtcNow, ct);
            if (existing != null) continue;
            var issuedAt = DateTime.UtcNow;
            var expiresAt = issuedAt.AddMonths(badge.ValidityMonths);
            _db.UserBadges.Add(new UserBadge
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BadgeId = badge.Id,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                IsActive = true
            });
        }
    }

    [HttpGet("my-badges")]
    [Authorize(Policy = "RequireViewTraining")]
    [ProducesResponseType(typeof(List<UserBadgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserBadgeDto>>> GetMyBadges(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var list = await _db.UserBadges.AsNoTracking()
            .Where(ub => ub.UserId == userId && ub.IsActive)
            .Include(ub => ub.Badge)
            .OrderByDescending(ub => ub.ExpiresAt)
            .Select(ub => new UserBadgeDto
            {
                Id = ub.Id,
                BadgeId = ub.BadgeId,
                BadgeName = ub.Badge.Name,
                BadgeDescription = ub.Badge.Description,
                IssuedAt = ub.IssuedAt,
                ExpiresAt = ub.ExpiresAt
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("badges/expiring")]
    [Authorize(Policy = "RequireViewReports")]
    [ProducesResponseType(typeof(List<ExpiringBadgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ExpiringBadgeDto>>> GetExpiringBadges([FromQuery] int withinDays = 30, [FromQuery] bool includeExpired = false, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Date;
        var expiryEnd = cutoff.AddDays(Math.Max(0, withinDays));
        var query = _db.UserBadges.AsNoTracking()
            .Where(ub => ub.IsActive && ub.ExpiresAt <= expiryEnd);
        if (!includeExpired)
            query = query.Where(ub => ub.ExpiresAt >= cutoff);
        var list = await query
            .Include(ub => ub.User)
            .Include(ub => ub.Badge)
            .OrderBy(ub => ub.ExpiresAt)
            .Select(ub => new ExpiringBadgeDto
            {
                UserBadgeId = ub.Id,
                UserId = ub.UserId,
                UserName = ub.User.FullName ?? ub.User.Email,
                BadgeName = ub.Badge.Name,
                ExpiresAt = ub.ExpiresAt,
                IsExpired = ub.ExpiresAt < cutoff
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("badges")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(typeof(List<BadgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BadgeDto>>> ListBadges(CancellationToken ct = default)
    {
        var list = await _db.Badges.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BadgeDto { Id = b.Id, CourseId = b.CourseId, Name = b.Name, Description = b.Description, ValidityMonths = b.ValidityMonths })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("setup/badges")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(List<BadgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<BadgeDto>>> ListAllBadges(CancellationToken ct = default)
    {
        var list = await _db.Badges.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.CourseId).ThenBy(b => b.Name)
            .Select(b => new BadgeDto { Id = b.Id, CourseId = b.CourseId, Name = b.Name, Description = b.Description, ValidityMonths = b.ValidityMonths })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("setup/courses/{id:guid}/badges")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(List<BadgeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<BadgeDto>>> ListCourseBadges(Guid id, CancellationToken ct = default)
    {
        if (await _db.Courses.FindAsync([id], ct) == null)
            return NotFound();
        var list = await _db.Badges.AsNoTracking()
            .Where(b => b.CourseId == id)
            .OrderBy(b => b.Name)
            .Select(b => new BadgeDto { Id = b.Id, CourseId = b.CourseId, Name = b.Name, Description = b.Description, ValidityMonths = b.ValidityMonths })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("setup/courses/{id:guid}/badges")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(BadgeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BadgeDto>> CreateBadge(Guid id, [FromBody] CreateBadgeRequest request, CancellationToken ct = default)
    {
        var course = await _db.Courses.FindAsync([id], ct);
        if (course == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponseBodies.Message("Name is required."));
        var validityMonths = Math.Clamp(request.ValidityMonths, 1, 120);
        var badge = new Badge
        {
            Id = Guid.NewGuid(),
            CourseId = id,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ValidityMonths = validityMonths,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Badges.Add(badge);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListCourseBadges), new { id }, new BadgeDto
        {
            Id = badge.Id,
            CourseId = badge.CourseId,
            Name = badge.Name,
            Description = badge.Description,
            ValidityMonths = badge.ValidityMonths
        });
    }

    [HttpPut("setup/badges/{badgeId:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(typeof(BadgeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BadgeDto>> UpdateBadge(Guid badgeId, [FromBody] UpdateBadgeRequest request, CancellationToken ct = default)
    {
        var badge = await _db.Badges.FindAsync([badgeId], ct);
        if (badge == null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ApiResponseBodies.Message("Name is required."));
        badge.Name = request.Name.Trim();
        badge.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        badge.ValidityMonths = Math.Clamp(request.ValidityMonths, 1, 120);
        await _db.SaveChangesAsync(ct);
        return Ok(new BadgeDto
        {
            Id = badge.Id,
            CourseId = badge.CourseId,
            Name = badge.Name,
            Description = badge.Description,
            ValidityMonths = badge.ValidityMonths
        });
    }

    [HttpDelete("setup/badges/{badgeId:guid}")]
    [Authorize(Policy = "RequireManageTraining")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBadge(Guid badgeId, CancellationToken ct = default)
    {
        var badge = await _db.Badges.FindAsync([badgeId], ct);
        if (badge == null) return NotFound();
        _db.Badges.Remove(badge);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static List<string> ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return new List<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(optionsJson);
            return list ?? new List<string>();
        }
        catch { return new List<string>(); }
    }
}
