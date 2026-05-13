using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tradion.Api.Data;
using Tradion.Api.Helpers;
using Tradion.Api.Models;

namespace Tradion.Api.Permits;

/// <summary>
/// Ensures global (CompanyId null) DVCP permit types and default templates exist; refreshes WA trigger IDs for legacy fallback.
/// </summary>
public static class PermitCatalogSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static async Task EnsureAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var definitions = DvcpPermitTemplateCatalog.All;
        foreach (var def in definitions)
        {
            var type = await db.PermitTypes
                .Include(t => t.Templates)
                .FirstOrDefaultAsync(
                    t => t.CompanyId == null
                         && t.Name == def.Name
                         && t.IsWorkAuthorisation == def.IsWorkAuthorisation,
                    ct);

            if (type == null)
            {
                type = new PermitType
                {
                    Id = Guid.NewGuid(),
                    Name = def.Name,
                    Description = def.Description,
                    IsActive = true,
                    CompanyId = null,
                    IsWorkAuthorisation = def.IsWorkAuthorisation,
                    CreatedAt = DateTime.UtcNow
                };
                db.PermitTypes.Add(type);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                if (type.Description != def.Description)
                {
                    type.Description = def.Description;
                    await db.SaveChangesAsync(ct);
                }
            }

            var defaultTemplate = type.Templates
                .Where(t => t.IsActive && t.SiteId == null && t.CompanyId == null)
                .OrderBy(t => t.CreatedAt)
                .FirstOrDefault();

            var checklistJson = ToChecklistJson(def);
            var formSchemaJson = PermitFormJsonHelper.SerializeSchema(def.FormFields);

            if (defaultTemplate == null)
            {
                defaultTemplate = new PermitTemplate
                {
                    Id = Guid.NewGuid(),
                    PermitTypeId = type.Id,
                    Name = def.Name,
                    SiteId = null,
                    CompanyId = null,
                    ChecklistJson = checklistJson,
                    FormSchemaJson = formSchemaJson,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.PermitTemplates.Add(defaultTemplate);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                if (string.Equals(defaultTemplate.Name, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    defaultTemplate.Name = def.Name;
                    await db.SaveChangesAsync(ct);
                }

                if (!def.UsesStructuredWorkAuthorisationForm
                    && def.ChecklistLines.Count > 0
                    && string.IsNullOrWhiteSpace(defaultTemplate.ChecklistJson))
                {
                    defaultTemplate.ChecklistJson = checklistJson;
                    await db.SaveChangesAsync(ct);
                }

                if (def.FormFields.Count > 0 && string.IsNullOrWhiteSpace(defaultTemplate.FormSchemaJson))
                {
                    defaultTemplate.FormSchemaJson = formSchemaJson;
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        var childNameList = definitions
            .Where(d => !d.IsWorkAuthorisation)
            .Select(d => d.Name)
            .ToList();
        var childTypeIds = await db.PermitTypes.AsNoTracking()
            .Where(t => t.CompanyId == null && !t.IsWorkAuthorisation && childNameList.Contains(t.Name))
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (childTypeIds.Count == 0)
            return;

        var triggersJson = JsonSerializer.Serialize(childTypeIds.Select(id => id.ToString()).ToList(), JsonOptions);
        // All WA rows (global or company-scoped) get the same canonical child IDs so legacy trigger fallback matches the catalog.
        var waTypes = await db.PermitTypes
            .Where(t => t.IsWorkAuthorisation)
            .ToListAsync(ct);
        foreach (var wa in waTypes)
        {
            if (wa.TriggersPermitTypeIdsJson != triggersJson)
            {
                wa.TriggersPermitTypeIdsJson = triggersJson;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? ToChecklistJson(IPermitCatalogDefinition def)
    {
        if (def.UsesStructuredWorkAuthorisationForm || def.ChecklistLines.Count == 0)
            return null;
        return JsonSerializer.Serialize(
            def.ChecklistLines.Select(l => new { id = l.Id, label = l.Label }).ToList(),
            JsonOptions);
    }
}
