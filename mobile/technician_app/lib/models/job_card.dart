import 'permit_status.dart';
import 'job_status.dart';

/// Matches JobCardListDto from API
class JobCardListDto {
  final String id;
  final String jobCardNumber;
  final String? serviceRequestNumber;
  final String siteId;
  final String? siteName;
  final String status;
  final int priority;
  final DateTime? dueDate;
  final DateTime createdAt;
  final String? assignedTechnicianNames;
  final String? blockedReason;

  JobCardListDto({
    required this.id,
    required this.jobCardNumber,
    this.serviceRequestNumber,
    required this.siteId,
    this.siteName,
    required this.status,
    this.priority = 3,
    this.dueDate,
    required this.createdAt,
    this.assignedTechnicianNames,
    this.blockedReason,
  });

  factory JobCardListDto.fromJson(Map<String, dynamic> json) => JobCardListDto(
        id: json['id'] as String,
        jobCardNumber: json['jobCardNumber'] as String? ?? '',
        serviceRequestNumber: json['serviceRequestNumber'] as String?,
        siteId: json['siteId'] as String,
        siteName: json['siteName'] as String?,
        status: json['status'] as String? ?? 'Open',
        priority: (json['priority'] as num?)?.toInt() ?? 3,
        dueDate: json['dueDate'] != null ? DateTime.tryParse(json['dueDate'] as String) : null,
        createdAt: DateTime.parse(json['createdAt'] as String),
        assignedTechnicianNames: json['assignedTechnicianNames'] as String?,
        blockedReason: json['blockedReason'] as String?,
      );

  /// Priority 5 = most urgent, 1 = least. Sort descending for "highest first"
  static int compareByPriority(JobCardListDto a, JobCardListDto b) =>
      b.priority.compareTo(a.priority);
}

/// Matches JobCardWorkDto from API (detail view, no prices shown to technician)
class JobCardWorkDto {
  final String id;
  final String jobCardNumber;
  final DateTime? createdAt;
  final DateTime? startedAt;
  final DateTime? completedAt;
  final DateTime? firstPermitRequestedAt;
  final DateTime? firstPermitApprovedAt;
  final DateTime? firstSitePhotoAt;
  final String? description;
  final String? serviceRequestNumber;
  final String? serviceRequestDescription;
  final String? quoteNumber;
  final String siteId;
  final String? siteName;
  final String? siteAddress;
  final double? siteLatitude;
  final double? siteLongitude;
  final String status;
  final int priority;
  final DateTime? dueDate;
  final String? notes;
  final List<PlannedPartDto> plannedParts;
  final List<JobPartDto> parts;
  final List<JobPermitDto> permits;
  final List<IncidentReportDto> incidentReports;
  final List<JobCardAssignmentDto> assignments;
  final List<JobCardDocumentDto> documents;
  final bool permitsRequired;
  final String? requiredPermitTypeId;
  final List<String> requiredPermitTypeIdsFromEquipment;
  /// When set, job is blocked (same as web Block job); technicians may be unable to open from the list.
  final String? blockedReason;
  /// Work Authorisation amended after client sign-off; must be signed again (see permits).
  final bool pendingWaAmendmentSignOff;
  /// Work is paused because no valid signed WA is in force.
  final bool waExpiredStandstill;

  final bool paperPermitMode;

  /// Server-side: user may call activate paper mode (before visible WA client sign-off).
  final bool canActivatePaperPermitMode;

  final DateTime? finalClientSignOffAt;
  final String? finalClientSignOffByName;
  final String? finalClientSignerName;

  JobCardWorkDto({
    required this.id,
    required this.jobCardNumber,
    this.createdAt,
    this.startedAt,
    this.completedAt,
    this.firstPermitRequestedAt,
    this.firstPermitApprovedAt,
    this.firstSitePhotoAt,
    this.description,
    this.serviceRequestNumber,
    this.serviceRequestDescription,
    this.quoteNumber,
    required this.siteId,
    this.siteName,
    this.siteAddress,
    this.siteLatitude,
    this.siteLongitude,
    required this.status,
    this.priority = 3,
    this.dueDate,
    this.notes,
    this.plannedParts = const [],
    this.parts = const [],
    this.permits = const [],
    this.incidentReports = const [],
    this.assignments = const [],
    this.documents = const [],
    this.permitsRequired = false,
    this.requiredPermitTypeId,
    this.requiredPermitTypeIdsFromEquipment = const [],
    this.blockedReason,
    this.pendingWaAmendmentSignOff = false,
    this.waExpiredStandstill = false,
    this.paperPermitMode = false,
    this.canActivatePaperPermitMode = false,
    this.finalClientSignOffAt,
    this.finalClientSignOffByName,
    this.finalClientSignerName,
  });

  factory JobCardWorkDto.fromJson(Map<String, dynamic> json) {
    return JobCardWorkDto(
      id: json['id'] as String,
      jobCardNumber: json['jobCardNumber'] as String? ?? '',
      createdAt: json['createdAt'] != null ? DateTime.tryParse(json['createdAt'] as String) : null,
      startedAt: json['startedAt'] != null ? DateTime.tryParse(json['startedAt'] as String) : null,
      completedAt: json['completedAt'] != null ? DateTime.tryParse(json['completedAt'] as String) : null,
      firstPermitRequestedAt: json['firstPermitRequestedAt'] != null ? DateTime.tryParse(json['firstPermitRequestedAt'] as String) : null,
      firstPermitApprovedAt: json['firstPermitApprovedAt'] != null ? DateTime.tryParse(json['firstPermitApprovedAt'] as String) : null,
      firstSitePhotoAt: json['firstSitePhotoAt'] != null ? DateTime.tryParse(json['firstSitePhotoAt'] as String) : null,
      description: json['description'] as String?,
      serviceRequestNumber: json['serviceRequestNumber'] as String?,
      serviceRequestDescription: json['serviceRequestDescription'] as String?,
      quoteNumber: json['quoteNumber'] as String?,
      siteId: json['siteId'] as String,
      siteName: json['siteName'] as String?,
      siteAddress: json['siteAddress'] as String?,
      siteLatitude: (json['siteLatitude'] as num?)?.toDouble(),
      siteLongitude: (json['siteLongitude'] as num?)?.toDouble(),
      status: json['status'] as String? ?? 'Open',
      priority: (json['priority'] as num?)?.toInt() ?? 3,
      dueDate: json['dueDate'] != null ? DateTime.tryParse(json['dueDate'] as String) : null,
      notes: json['notes'] as String?,
      plannedParts: (json['plannedParts'] as List?)
              ?.map((e) => PlannedPartDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      parts: (json['parts'] as List?)
              ?.map((e) => JobPartDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      permits: (json['permits'] as List?)
              ?.map((e) => JobPermitDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      incidentReports: (json['incidentReports'] as List?)
              ?.map((e) => IncidentReportDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      assignments: (json['assignments'] as List?)
              ?.map((e) => JobCardAssignmentDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      documents: (json['documents'] as List?)
              ?.map((e) => JobCardDocumentDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      permitsRequired: json['permitsRequired'] as bool? ?? false,
      requiredPermitTypeId: json['requiredPermitTypeId'] as String?,
      requiredPermitTypeIdsFromEquipment: (json['requiredPermitTypeIdsFromEquipment'] as List?)?.map((e) => e.toString()).toList() ?? [],
      blockedReason: json['blockedReason'] as String?,
      pendingWaAmendmentSignOff: json['pendingWaAmendmentSignOff'] as bool? ?? false,
      waExpiredStandstill: json['waExpiredStandstill'] as bool? ?? false,
      paperPermitMode: json['paperPermitMode'] as bool? ?? false,
      canActivatePaperPermitMode: json['canActivatePaperPermitMode'] as bool? ?? false,
      finalClientSignOffAt:
          json['finalClientSignOffAt'] != null ? DateTime.tryParse(json['finalClientSignOffAt'] as String) : null,
      finalClientSignOffByName: json['finalClientSignOffByName'] as String?,
      finalClientSignerName: json['finalClientSignerName'] as String?,
    );
  }

  bool get canOpen => !JobStatusValue.isCompletedLike(status);
}

class PlannedPartDto {
  final String id;
  final String partName;
  final int quantity;

  PlannedPartDto({required this.id, required this.partName, required this.quantity});

  factory PlannedPartDto.fromJson(Map<String, dynamic> json) => PlannedPartDto(
        id: json['id'] as String,
        partName: json['partName'] as String? ?? '',
        quantity: (json['quantity'] as num?)?.toInt() ?? 1,
      );
}

class ChecklistItemDto {
  final String id;
  final String label;
  final bool checked;

  ChecklistItemDto({required this.id, required this.label, this.checked = false});

  factory ChecklistItemDto.fromJson(Map<String, dynamic> json) => ChecklistItemDto(
        id: json['id'] as String? ?? '',
        label: json['label'] as String? ?? '',
        checked: json['checked'] as bool? ?? false,
      );
}

class PermitFormFieldSchemaDto {
  final String id;
  final String label;
  final String type;
  final String? group;
  final bool required;

  PermitFormFieldSchemaDto({
    required this.id,
    required this.label,
    this.type = 'text',
    this.group,
    this.required = false,
  });

  factory PermitFormFieldSchemaDto.fromJson(Map<String, dynamic> json) => PermitFormFieldSchemaDto(
        id: json['id'] as String? ?? '',
        label: json['label'] as String? ?? '',
        type: json['type'] as String? ?? 'text',
        group: json['group'] as String?,
        required: json['required'] as bool? ?? false,
      );
}

class JobPermitDto {
  final String id;
  final int permitNumber;
  final String status;
  /// API Guid as string; used to compare with master requestable types.
  final String? permitTypeId;
  final String? permitTemplateName;
  final DateTime? requestedAt;
  final DateTime? approvedAt;
  final DateTime? validFrom;
  final DateTime? validTo;
  final String? masterPermitId;
  final bool isWorkAuthorisation;
  final bool hasClientSignOff;
  final List<String>? triggersPermitTypeIds;
  final List<String> triggersPermitTypeNames;
  /// From API: work permits still requestable from saved Work Authorisation checklist (preferred over [triggersPermitTypeIds]).
  final List<String>? requestableWorkPermitTypeIds;
  final List<String> requestableWorkPermitTypeNames;
  final List<JobPermitAttachmentDto> attachments;
  final List<ChecklistItemDto> checklistItems;
  final List<PermitFormFieldSchemaDto>? formFields;
  final Map<String, String>? formValues;
  final bool pendingWaAmendmentSignOff;
  /// Null for WA; false = no longer on saved master checklist.
  final bool? stillRequiredByWorkAuthorisation;

  final String? paperPermitNumber;

  final DateTime? paperClientSignedOffAt;

  JobPermitDto({
    required this.id,
    this.permitNumber = 0,
    required this.status,
    this.permitTypeId,
    this.permitTemplateName,
    this.requestedAt,
    this.approvedAt,
    this.validFrom,
    this.validTo,
    this.masterPermitId,
    this.isWorkAuthorisation = false,
    this.hasClientSignOff = false,
    this.triggersPermitTypeIds,
    this.triggersPermitTypeNames = const [],
    this.requestableWorkPermitTypeIds,
    this.requestableWorkPermitTypeNames = const [],
    this.attachments = const [],
    this.checklistItems = const [],
    this.formFields,
    this.formValues,
    this.pendingWaAmendmentSignOff = false,
    this.stillRequiredByWorkAuthorisation,
    this.paperPermitNumber,
    this.paperClientSignedOffAt,
  });

  bool get hasChildFormContent => checklistItems.isNotEmpty || (formFields != null && formFields!.isNotEmpty);

  bool get isExpired {
    if (PermitStatusValue.isExpiredLike(status)) return true;
    return validTo != null && validTo!.isBefore(DateTime.now().toUtc());
  }

  /// True when permit is live on site (client has signed off / clock running).
  bool get isPermitActive {
    return PermitStatusValue.isActiveLike(status);
  }

  bool get isPermitDone {
    return PermitStatusValue.isClosedLike(status);
  }

  /// Last 15% of validity window (validFrom→validTo), only while active and client has signed off.
  bool get isExpiringSoon {
    if (!isPermitActive || !hasClientSignOff || validTo == null) return false;
    final now = DateTime.now().toUtc();
    if (!now.isBefore(validTo!)) return false;
    final start = (validFrom ?? approvedAt ?? requestedAt ?? now).toUtc();
    if (!start.isBefore(validTo!)) return false;
    final totalMs = validTo!.difference(start).inMilliseconds;
    if (totalMs <= 0) return false;
    final remainingMs = validTo!.difference(now).inMilliseconds;
    return remainingMs > 0 && remainingMs * 100 <= totalMs * 15;
  }

  factory JobPermitDto.fromJson(Map<String, dynamic> json) {
    Map<String, String>? fv;
    final rawFv = json['formValues'];
    if (rawFv is Map) {
      fv = rawFv.map((k, v) => MapEntry(k.toString(), v?.toString() ?? ''));
    }
    return JobPermitDto(
      id: json['id'] as String,
      permitNumber: (json['permitNumber'] as num?)?.toInt() ?? 0,
      status: json['status'] as String? ?? '',
      permitTypeId: json['permitTypeId'] as String?,
      permitTemplateName: json['permitTemplateName'] as String?,
      requestedAt: json['requestedAt'] != null ? DateTime.tryParse(json['requestedAt'] as String) : null,
      approvedAt: json['approvedAt'] != null ? DateTime.tryParse(json['approvedAt'] as String) : null,
      validFrom: json['validFrom'] != null ? DateTime.tryParse(json['validFrom'] as String) : null,
      validTo: json['validTo'] != null ? DateTime.tryParse(json['validTo'] as String) : null,
      masterPermitId: json['masterPermitId'] as String?,
      isWorkAuthorisation: json['isWorkAuthorisation'] as bool? ?? false,
      hasClientSignOff: json['hasClientSignOff'] as bool? ?? false,
      triggersPermitTypeIds: (json['triggersPermitTypeIds'] as List?)?.map((e) => e.toString()).toList(),
      triggersPermitTypeNames: (json['triggersPermitTypeNames'] as List?)?.map((e) => e.toString()).toList() ?? [],
      requestableWorkPermitTypeIds: (json['requestableWorkPermitTypeIds'] as List?)?.map((e) => e.toString()).toList(),
      requestableWorkPermitTypeNames:
          (json['requestableWorkPermitTypeNames'] as List?)?.map((e) => e.toString()).toList() ?? [],
      attachments: (json['attachments'] as List?)
              ?.map((e) => JobPermitAttachmentDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      checklistItems: (json['checklistItems'] as List?)
              ?.map((e) => ChecklistItemDto.fromJson(e as Map<String, dynamic>))
              .toList() ??
          [],
      formFields: (json['formFields'] as List?)
          ?.map((e) => PermitFormFieldSchemaDto.fromJson(e as Map<String, dynamic>))
          .toList(),
      formValues: fv,
      pendingWaAmendmentSignOff: json['pendingWaAmendmentSignOff'] as bool? ?? false,
      stillRequiredByWorkAuthorisation: json['stillRequiredByWorkAuthorisation'] as bool?,
      paperPermitNumber: json['paperPermitNumber'] as String?,
      paperClientSignedOffAt:
          json['paperClientSignedOffAt'] != null ? DateTime.tryParse(json['paperClientSignedOffAt'] as String) : null,
    );
  }
}

class JobPermitAttachmentDto {
  final String id;
  final String fileName;
  final DateTime uploadedAt;

  JobPermitAttachmentDto({required this.id, required this.fileName, required this.uploadedAt});

  factory JobPermitAttachmentDto.fromJson(Map<String, dynamic> json) => JobPermitAttachmentDto(
        id: json['id'] as String,
        fileName: json['fileName'] as String? ?? '',
        uploadedAt: DateTime.tryParse(json['uploadedAt'] as String? ?? '') ?? DateTime.now(),
      );
}

class JobPartDto {
  final String id;
  final String brand;
  final String? serialNumber;
  final String? description;
  final String? oldPartPhotoPath;
  final String? newPartPhotoPath;

  JobPartDto({
    required this.id,
    required this.brand,
    this.serialNumber,
    this.description,
    this.oldPartPhotoPath,
    this.newPartPhotoPath,
  });

  factory JobPartDto.fromJson(Map<String, dynamic> json) => JobPartDto(
        id: json['id'] as String,
        brand: json['brand'] as String? ?? '',
        serialNumber: json['serialNumber'] as String?,
        description: json['description'] as String?,
        oldPartPhotoPath: json['oldPartPhotoPath'] as String?,
        newPartPhotoPath: json['newPartPhotoPath'] as String?,
      );
}

class JobCardDocumentDto {
  final String id;
  final String documentType;
  final DateTime? signedAt;
  final String? signedByUserName;
  final String? filePath;

  JobCardDocumentDto({
    required this.id,
    required this.documentType,
    this.signedAt,
    this.signedByUserName,
    this.filePath,
  });

  factory JobCardDocumentDto.fromJson(Map<String, dynamic> json) => JobCardDocumentDto(
        id: json['id'] as String,
        documentType: json['documentType'] as String? ?? '',
        signedAt: json['signedAt'] != null ? DateTime.tryParse(json['signedAt'] as String) : null,
        signedByUserName: json['signedByUserName'] as String?,
        filePath: json['filePath'] as String?,
      );
}

class JobCardAssignmentDto {
  final String userId;
  final String? userName;
  final bool isPermitManager;

  JobCardAssignmentDto({
    required this.userId,
    this.userName,
    this.isPermitManager = false,
  });

  factory JobCardAssignmentDto.fromJson(Map<String, dynamic> json) => JobCardAssignmentDto(
        userId: json['userId'] as String,
        userName: json['userName'] as String?,
        isPermitManager: json['isPermitManager'] as bool? ?? false,
      );
}

class IncidentReportDto {
  final String id;
  final String description;
  final String severity;
  final DateTime createdAt;

  IncidentReportDto({
    required this.id,
    required this.description,
    required this.severity,
    required this.createdAt,
  });

  factory IncidentReportDto.fromJson(Map<String, dynamic> json) => IncidentReportDto(
        id: json['id'] as String,
        description: json['description'] as String? ?? '',
        severity: json['severity'] as String? ?? 'Medium',
        createdAt: DateTime.tryParse(json['createdAt'] as String? ?? '') ?? DateTime.now(),
      );
}
