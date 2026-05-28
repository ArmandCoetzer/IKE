import 'dart:io';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:url_launcher/url_launcher.dart';
import '../app.dart';
import '../models/job_card.dart';
import '../models/job_status.dart';
import '../models/permit_status.dart';
import '../services/auth_service.dart';
import '../widgets/signature_capture_dialog.dart';
import '../services/job_cards_service.dart';
import '../services/job_permits_service.dart';
import '../services/job_work_service.dart';
import 'child_permit_form_screen.dart';
import 'work_authorization_permit_screen.dart';

bool _permitRejectedOrCancelled(JobPermitDto p) {
  return PermitStatusValue.isRejectedOrCancelled(p.status);
}

/// Expired by date/status, excluding completed and rejected rows.
bool _permitLiveExpired(JobPermitDto p) {
  if (p.isPermitDone) return false;
  if (_permitRejectedOrCancelled(p)) return false;
  return p.isExpired;
}

bool _permitHasSuccessorRequest(List<JobPermitDto> permits, JobPermitDto p) {
  final myNum = p.permitNumber;
  for (final q in permits) {
    if (q.id == p.id) continue;
    if (_permitRejectedOrCancelled(q)) continue;
    if (q.permitNumber <= myNum) continue;
    if (p.isWorkAuthorisation) {
      if (q.isWorkAuthorisation) return true;
    } else {
      final sameType = (p.permitTypeId != null &&
              p.permitTypeId!.isNotEmpty &&
              p.permitTypeId == q.permitTypeId) ||
          ((p.permitTypeId == null || p.permitTypeId!.isEmpty) &&
              (p.permitTemplateName ?? '').trim() == (q.permitTemplateName ?? '').trim());
      if (sameType) return true;
    }
  }
  return false;
}

enum _ExpiredPermitMenuMode { normal, replacementOnly, readonlySuperseded }

_ExpiredPermitMenuMode _expiredPermitMenuMode(List<JobPermitDto> permits, JobPermitDto p) {
  if (!_permitLiveExpired(p)) return _ExpiredPermitMenuMode.normal;
  if (_permitHasSuccessorRequest(permits, p)) return _ExpiredPermitMenuMode.readonlySuperseded;
  return _ExpiredPermitMenuMode.replacementOnly;
}

JobPermitDto? _effectiveWaMasterForChildRequest(List<JobPermitDto> permits) {
  final sorted = [...permits]..sort((a, b) => b.permitNumber.compareTo(a.permitNumber));
  for (final q in sorted) {
    if (!q.isWorkAuthorisation) continue;
    if (_permitRejectedOrCancelled(q)) continue;
    if (q.isPermitDone) continue;
    if (q.isExpired) continue;
    return q;
  }
  return null;
}

class JobDetailScreen extends StatefulWidget {
  final String jobId;
  final VoidCallback? onClosed;
  final bool viewOnly;

  const JobDetailScreen({super.key, required this.jobId, this.onClosed, this.viewOnly = false});

  @override
  State<JobDetailScreen> createState() => _JobDetailScreenState();
}

class _JobDetailScreenState extends State<JobDetailScreen> {
  static const String _finalClientSignOffDocType = 'FinalClientSignOff';

  JobCardWorkDto? _job;
  bool _loading = true;
  /// True while re-fetching job detail when a job was already loaded (pull-to-refresh).
  bool _jobRefreshing = false;
  bool _autoCreatingWaDraft = false;
  bool _addingWorkPermits = false;
  int _documentsAccordionKey = 0;
  bool _documentsAccordionExpanded = true;
  String? _error;
  String? _currentUserId;
  final _jobCardsService = JobCardsService();
  final _permitsService = JobPermitsService();
  final _workService = JobWorkService();
  final _auth = AuthService();

  @override
  void initState() {
    super.initState();
    _loadUserId();
    _load();
  }

  Future<void> _loadUserId() async {
    final user = await _auth.getUser();
    if (!mounted) return;
    setState(() => _currentUserId = user?.userId);
    // Re-evaluate permit-manager driven UI/actions once identity is known.
    await _load();
  }

  bool get _isPermitManager {
    if (_currentUserId == null || _job == null) return false;
    return _job!.assignments.any((a) => a.userId == _currentUserId && a.isPermitManager);
  }

  bool get _isAssignedToJob {
    if (_currentUserId == null || _job == null) return false;
    return _job!.assignments.any((a) => a.userId == _currentUserId);
  }

  bool get _hasBeforePhoto =>
      _job?.documents.any((d) => d.documentType == 'BeforeWork') ?? false;
  bool get _hasAfterPhoto =>
      _job?.documents.any((d) => d.documentType == 'AfterWork') ?? false;
  List<JobCardDocumentDto> get _midPhotos =>
      _job?.documents.where((d) => d.documentType == 'MidWork').toList() ?? [];

  bool get _isJobStarted =>
      _job != null && JobStatusValue.isInProgressLike(_job!.status);

  /// Draft / Captured / Pending = waiting on client sign-off or not ready — block starting work.
  static bool _awaitingClientSignOff(String status) {
    final s = PermitStatusValue.norm(status);
    return PermitStatusValue.isDraftLike(s) || PermitStatusValue.isCapturedLike(s) || s == 'pending';
  }

  /// Every permit on the job has client sign-off (Active/Approved), is Done (Closed), or is rejected/cancelled; no expired active permit.
  bool get _permitsClientSignedOffForWork {
    if (_job == null || !_job!.permitsRequired) return true;
    if (_job!.permits.isEmpty) return false;
    for (final p in _job!.permits) {
      if (_isRejectedOrCancelledPermit(p.status)) continue;
      final s = PermitStatusValue.norm(p.status);
      if (PermitStatusValue.isExpiredLike(s)) return false;
      if (_awaitingClientSignOff(p.status)) return false;
      if (PermitStatusValue.isActiveLike(s) && p.isExpired) return false;
    }
    return true;
  }

  /// Work Authorisation only: client sign-off before on-site work may start. Child work permits do not block Start job.
  bool get _workAuthorisationSignedOffForStart {
    if (_job == null || !_job!.permitsRequired) return true;
    final wa = _job!.permits.where((p) => p.isWorkAuthorisation && !_isRejectedOrCancelledPermit(p.status)).toList();
    if (wa.isEmpty) return false;
    for (final p in wa) {
      final s = p.status.toLowerCase();
      if (PermitStatusValue.isExpiredLike(s)) return false;
      if (_awaitingClientSignOff(p.status)) return false;
      if (PermitStatusValue.isActiveLike(s) && p.isExpired) return false;
    }
    return true;
  }

  /// Child/work permits must be Done (Closed). Work Authorisation is excluded — it often stays Active for the shift and may be closed with or after the job.
  bool get _allPermitsMarkedDone {
    if (_job == null || !_job!.permitsRequired) return true;
    if (_job!.permits.isEmpty) return false;
    for (final p in _job!.permits) {
      if (p.isWorkAuthorisation) continue;
      if (_isRejectedOrCancelledPermit(p.status)) continue;
      if (!PermitStatusValue.isClosedLike(p.status)) return false;
    }
    return true;
  }

  /// Child/work permits only (not WA) for tagging site photos — WA uses separate “General site photo”.
  List<JobPermitDto> get _childPermitsForPhotoTagging {
    if (_job == null) return [];
    return _job!.permits
        .where((p) => !p.isWorkAuthorisation && !_isRejectedOrCancelledPermit(p.status))
        .toList();
  }

  /// Completion gate: job started, every permit Done, before + after photos, no blocking expired active permit.
  bool get _canComplete {
    if (_job == null || !_job!.canOpen) return false;
    if (_waExpiredStandstill) return false;
    if (!_isJobStarted) return false;
    if (!_allPermitsMarkedDone) return false;
    final anyExpiredActive = _job!.permitsRequired &&
        _job!.permits.any((p) {
          return PermitStatusValue.isActiveLike(p.status) && p.isExpired;
        });
    if (anyExpiredActive) return false;
    return _hasBeforePhoto && _hasAfterPhoto;
  }

  bool get _hasFinalClientSignOff =>
      _job?.documents.any((d) =>
          d.documentType == _finalClientSignOffDocType &&
          (d.filePath != null && d.filePath!.trim().isNotEmpty)) ??
      false;

  List<JobCardDocumentDto> get _visibleUploadedDocuments =>
      _job?.documents.where((d) => d.documentType != _finalClientSignOffDocType).toList() ?? [];

  String _documentListTitle(JobCardDocumentDto d) {
    if (d.documentType == _finalClientSignOffDocType) return 'Final client sign-off';
    return d.documentType;
  }

  /// Before photos: first upload waits for all permit sign-offs; additional uploads allowed any time the job is in progress.
  bool get _canUploadBefore =>
      _isJobStarted && ((_job?.paperPermitMode ?? false) || _permitsClientSignedOffForWork || _hasBeforePhoto);
  bool get _canUploadMid => _isJobStarted && _hasBeforePhoto;
  bool get _canUploadAfter => _isJobStarted && _hasBeforePhoto;

  bool get _showSitePhotosSection =>
      _isJobStarted || _hasBeforePhoto || _hasAfterPhoto || _midPhotos.isNotEmpty;

  bool get _hasExpiredPermit =>
      (_job?.permitsRequired ?? false) &&
      (_job?.permits.any((p) => PermitStatusValue.isActiveLike(p.status) && p.isExpired) ?? false);

  /// When true, block all non-permit actions until permit expiry is resolved.
  bool get _blockedByExpiredPermit => _hasExpiredPermit;

  bool get _waAmendmentBlocksSitePhotos =>
      (_job?.permitsRequired ?? false) && (_job?.pendingWaAmendmentSignOff ?? false);
  bool get _waExpiredStandstill =>
      (_job?.permitsRequired ?? false) && (_job?.waExpiredStandstill ?? false);

  bool get _canStartWork =>
      _job != null && !_waExpiredStandstill && (!_job!.permitsRequired || _workAuthorisationSignedOffForStart);

  void _showExpiredPermitBlockedDialog() {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Permit expired'),
        content: const Text(
          'A permit has expired. You must resolve this before continuing.\n\n'
          '• Need more work? Request a new permit (same type available next day).\n'
          '• Done with this work? You can proceed after requesting any other permits needed.\n\n'
          'Go to the Permits section below to take action.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('OK'),
          ),
        ],
      ),
    );
  }

  void _showNeedMoreWorkDialog() {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Permit expired – need more work?'),
        content: const Text(
          'This permit has expired. What would you like to do?\n\n'
          '• Need more work of this type? You can request the same permit again, but only from the next day. Tap "Request permit" tomorrow.\n\n'
          '• Done with this work? No action needed. Complete any other permits, then upload before/after photos to mark the job complete.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('OK'),
          ),
        ],
      ),
    );
  }

  Future<void> _uploadSitePhotos(String documentType, {bool multi = false}) async {
    if (_waExpiredStandstill) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Work is paused because the Work Authorisation expired. Complete and sign off the new Work Authorisation first.')),
      );
      return;
    }
    if (!_isAssignedToJob) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('You must be assigned to this job to upload site photos.')));
      return;
    }
    if (_blockedByExpiredPermit) {
      _showExpiredPermitBlockedDialog();
      return;
    }
    if (_job == null) return;
    if (_job!.pendingWaAmendmentSignOff) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Site photos are paused until the client signs off the amended Work Authorisation again.',
          ),
        ),
      );
      return;
    }
    final choice = await showModalBottomSheet<String>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.camera_alt),
              title: const Text('Take photo'),
              onTap: () => Navigator.pop(ctx, 'camera'),
            ),
            ListTile(
              leading: const Icon(Icons.photo_library),
              title: const Text('Choose from camera roll'),
              onTap: () => Navigator.pop(ctx, 'gallery'),
            ),
          ],
        ),
      ),
    );
    if (choice == null || !mounted) return;
    final picker = ImagePicker();
    List<XFile> files = [];
    if (choice == 'camera') {
      final x = await picker.pickImage(source: ImageSource.camera);
      if (x != null) files = [x];
    } else {
      if (multi) {
        files = await picker.pickMultiImage();
      } else {
        final x = await picker.pickImage(source: ImageSource.gallery);
        if (x != null) files = [x];
      }
    }
    if (files.isEmpty || !mounted) return;
    final contextNote = await _promptPhotoPermitContext(documentType);
    if (!mounted) return;
    var uploaded = 0;
    for (final x in files) {
      final bytes = await x.readAsBytes();
      final ext = x.path.split('.').last;
      final fileName = '${documentType}_${DateTime.now().millisecondsSinceEpoch}.${ext == 'jpg' || ext == 'jpeg' ? 'jpg' : 'png'}';
      try {
        await _workService.uploadSitePhoto(_job!.id, documentType, bytes, fileName, notes: contextNote);
        uploaded++;
      } catch (e) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
        return;
      }
    }
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('$uploaded $documentType photo(s) uploaded')),
    );
    _load();
  }

  static const _generalPhotoContextId = '__general__';

  Future<String?> _promptPhotoPermitContext(String documentType) async {
    if (_job == null) return null;
    if (!_job!.permitsRequired) return 'Work: $documentType';

    final children = _childPermitsForPhotoTagging;
    var selectedId = _generalPhotoContextId;
    final workController = TextEditingController(text: documentType);
    final note = await showDialog<String>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setDialogState) => AlertDialog(
          title: const Text('Photo context'),
          content: SizedBox(
            width: double.maxFinite,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  value: selectedId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: 'Link to permit',
                    helperText: 'Use General for overall site photos not tied to a work permit.',
                  ),
                  items: [
                    const DropdownMenuItem<String>(
                      value: _generalPhotoContextId,
                      child: Text('General site photo', maxLines: 1, overflow: TextOverflow.ellipsis),
                    ),
                    ...children.map(
                      (p) => DropdownMenuItem<String>(
                        value: p.id,
                        child: Text(
                          '${p.permitTemplateName ?? 'Permit'} (${p.status})',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ),
                  ],
                  onChanged: (v) {
                    if (v != null) setDialogState(() => selectedId = v);
                  },
                ),
                const SizedBox(height: 8),
                TextField(
                  controller: workController,
                  decoration: const InputDecoration(labelText: 'Work being done'),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, null), child: const Text('Skip')),
            FilledButton(
              onPressed: () {
                final work = workController.text.trim();
                final String text;
                if (selectedId == _generalPhotoContextId) {
                  text = 'General site photo | Work: $work';
                } else {
                  final permit = children.firstWhere((p) => p.id == selectedId, orElse: () => children.first);
                  text = 'Permit: ${permit.permitTemplateName ?? 'Permit'} | Work: $work';
                }
                Navigator.pop(ctx, text);
              },
              child: const Text('Use context'),
            ),
          ],
        ),
      ),
    );
    return note ?? 'Work: $documentType';
  }

  Future<void> _load() async {
    final hadJob = _job != null;
    setState(() {
      _error = null;
      if (!hadJob) {
        _loading = true;
      } else {
        _jobRefreshing = true;
      }
    });
    try {
      final job = await _jobCardsService.getJobDetail(widget.jobId);
      if (!mounted) return;
      setState(() {
        _job = job;
        _documentsAccordionKey++;
        _documentsAccordionExpanded = _visibleUploadedDocuments.length <= 3;
        _loading = false;
        _jobRefreshing = false;
      });

      // Permit manager UX: WA draft should already be waiting instead of requiring a manual request tap.
      if (await _ensureWorkAuthorisationDraftIfNeeded(job)) {
        if (!mounted) return;
        final refreshed = await _jobCardsService.getJobDetail(widget.jobId);
        if (!mounted) return;
        setState(() {
          _job = refreshed;
          _documentsAccordionKey++;
          _documentsAccordionExpanded = _visibleUploadedDocuments.length <= 3;
        });
      }
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.toString();
        _loading = false;
        _jobRefreshing = false;
      });
    }
  }

  Future<bool> _ensureWorkAuthorisationDraftIfNeeded(JobCardWorkDto job) async {
    if (_autoCreatingWaDraft || !mounted) return false;
    if (widget.viewOnly || !job.canOpen) return false;
    if (!job.permitsRequired || job.paperPermitMode) return false;
    if (_currentUserId == null) return false;
    final isPermitManager = job.assignments.any((a) => a.userId == _currentUserId && a.isPermitManager);
    if (!isPermitManager) return false;
    if (job.pendingWaAmendmentSignOff || job.waExpiredStandstill) return false;

    final hasUsableWa = job.permits.any((p) => p.isWorkAuthorisation && !_isRejectedOrCancelledPermit(p.status));
    if (hasUsableWa) return false;
    if (job.permits.any(_workAuthorisationBlocksNewRequest)) return false;

    _autoCreatingWaDraft = true;
    try {
      await _permitsService.requestPermit(job.id);
      return true;
    } catch (_) {
      // Non-blocking UX enhancement only; keep screen usable if auto-create fails.
      return false;
    } finally {
      _autoCreatingWaDraft = false;
    }
  }

  Future<void> _openMaps(JobCardWorkDto job) async {
    final lat = job.siteLatitude;
    final lng = job.siteLongitude;
    final address = job.siteAddress ?? job.siteName ?? '';
    String url;
    // Prefer free-text address so directions match what was entered on the web; fall back to stored coordinates.
    if (address.isNotEmpty) {
      url = 'https://www.google.com/maps/dir/?api=1&destination=${Uri.encodeComponent(address)}';
    } else if (lat != null && lng != null) {
      url = 'https://www.google.com/maps/dir/?api=1&destination=$lat,$lng';
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('No site location available')),
      );
      return;
    }
    try {
      await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Could not open maps: $e')),
      );
    }
  }

  Future<void> _recordFinalClientSignOff() async {
    if (_job == null || !_canComplete) return;
    if (!_isAssignedToJob) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('You must be assigned to this job to record final client sign-off.')),
      );
      return;
    }
    final bytes = await showSignatureCaptureDialog(
      context,
      title: 'Final client sign-off',
    );
    if (bytes == null || bytes.isEmpty || !mounted) return;
    try {
      await _jobCardsService.finalClientSignOff(
        _job!.id,
        bytes,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Client signature saved.')));
      await _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _updateStatus(JobCardWorkDto job, String status, {bool popAfter = false}) async {
    try {
      if (status == 'Completed' &&
          !job.documents.any((d) =>
              d.documentType == _finalClientSignOffDocType &&
              (d.filePath != null && d.filePath!.trim().isNotEmpty))) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Capture the client signature for final sign-off before marking the job complete.')),
        );
        return;
      }
      await _jobCardsService.updateStatus(job.id, status);
      if (!mounted) return;
      if (popAfter) {
        widget.onClosed?.call();
        Navigator.of(context).pop();
      } else {
        _load();
      }
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  static bool _isRejectedOrCancelledPermit(String status) {
    return PermitStatusValue.isRejectedOrCancelled(status);
  }

  /// Same exception as API: signed-off active/approved child while job is In Progress may continue during WA amendment standstill.
  bool _childPermitMayContinueDuringWaAmendment(JobPermitDto p) {
    if (p.isWorkAuthorisation) return false;
    if (p.masterPermitId == null || p.masterPermitId!.isEmpty) return false;
    if (!p.hasClientSignOff) return false;
    if (!_isJobStarted) return false;
    return PermitStatusValue.isActiveLike(p.status);
  }

  bool _waStandstillBlocksChildFileActions(JobPermitDto p) {
    if (_job == null || !_job!.pendingWaAmendmentSignOff || p.isWorkAuthorisation) return false;
    return !_childPermitMayContinueDuringWaAmendment(p);
  }

  bool _showDeletePermitInMenu(JobPermitDto p) {
    if (!_isPermitManager || widget.viewOnly || _job == null) return false;
    if (_job!.paperPermitMode) {
      if (p.isWorkAuthorisation) return false;
      if (p.masterPermitId == null || p.masterPermitId!.isEmpty) return false;
      return !PermitStatusValue.isClosedLike(p.status) &&
          !PermitStatusValue.isRejectedOrCancelled(p.status) &&
          !PermitStatusValue.isExpiredLike(p.status);
    }
    if (_waExpiredStandstill && !p.isWorkAuthorisation) return false;
    if (p.isWorkAuthorisation) return false;
    if (p.stillRequiredByWorkAuthorisation != false) return false;
    if (PermitStatusValue.isClosedLike(p.status) ||
        PermitStatusValue.isExpiredLike(p.status) ||
        PermitStatusValue.isRejectedOrCancelled(p.status) ||
        PermitStatusValue.isDraftLike(p.status)) {
      return false;
    }
    if (PermitStatusValue.isCapturedLike(p.status)) return true;
    if (PermitStatusValue.isActiveLike(p.status) && p.hasClientSignOff) return true;
    return false;
  }

  /// Matches API: another master WA is blocked while any WA is draft or still within its validity calendar window (incl. Done/Closed).
  static bool _workAuthorisationBlocksNewRequest(JobPermitDto p) {
    if (!p.isWorkAuthorisation) return false;
    if (_isRejectedOrCancelledPermit(p.status)) return false;
    if (PermitStatusValue.isDraftLike(p.status)) return true;
    final vt = p.validTo;
    if (vt != null) {
      final v = vt.toUtc();
      final expiryDay = DateTime.utc(v.year, v.month, v.day);
      final now = DateTime.now().toUtc();
      final todayUtc = DateTime.utc(now.year, now.month, now.day);
      return todayUtc.compareTo(expiryDay) <= 0;
    }
    return true;
  }

  /// Valid master can be used to request linked work permits (matches API: draft allowed; not rejected/cancelled; not past validTo).
  static bool _masterAllowsWorkPermitRequests(JobPermitDto p) {
    if (!p.isWorkAuthorisation) return false;
    if (_isRejectedOrCancelledPermit(p.status)) return false;
    if (p.validTo != null && p.validTo!.toUtc().isBefore(DateTime.now().toUtc())) return false;
    return true;
  }

  /// Prefer highest permit number among valid masters (most recent).
  JobPermitDto? get _validMasterWorkAuthorisation {
    if (_job == null) return null;
    final candidates =
        _job!.permits.where((p) => p.isWorkAuthorisation && _masterAllowsWorkPermitRequests(p)).toList()
          ..sort((a, b) => b.permitNumber.compareTo(a.permitNumber));
    if (candidates.isEmpty) return null;
    return candidates.first;
  }

  Future<void> _pickAndRequestPaperChildPermit(JobPermitDto master) async {
    if (_job == null) return;
    final ids = master.triggersPermitTypeIds ?? [];
    if (ids.isEmpty) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('No linked permit types are configured for this Work Authorisation. Ask your coordinator.')),
      );
      return;
    }
    final names = master.triggersPermitTypeNames;
    final picked = <String>{};
    final selected = await showModalBottomSheet<List<String>>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setSheetState) {
          return SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text('Add work permits', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
                  const SizedBox(height: 8),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxHeight: 320),
                    child: ListView.builder(
                      shrinkWrap: true,
                      itemCount: ids.length,
                      itemBuilder: (_, i) {
                        final id = ids[i];
                        final label = i < names.length ? names[i] : 'Permit type';
                        final isChecked = picked.contains(id);
                        return CheckboxListTile(
                          value: isChecked,
                          title: Text(label),
                          contentPadding: EdgeInsets.zero,
                          controlAffinity: ListTileControlAffinity.leading,
                          onChanged: (v) {
                            setSheetState(() {
                              if (v == true) {
                                picked.add(id);
                              } else {
                                picked.remove(id);
                              }
                            });
                          },
                        );
                      },
                    ),
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      TextButton(onPressed: () => Navigator.pop(ctx, null), child: const Text('Cancel')),
                      const Spacer(),
                      FilledButton(
                        onPressed: picked.isEmpty ? null : () => Navigator.pop(ctx, picked.toList()),
                        child: Text(picked.isEmpty ? 'Select permits' : 'Add ${picked.length}'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
    if (selected == null || selected.isEmpty || !mounted) return;

    if (mounted) {
      setState(() => _addingWorkPermits = true);
    }
    try {
      var successCount = 0;
      final failures = <String>[];
      for (final typeId in selected) {
        try {
          await _permitsService.requestPermit(_job!.id, permitTypeId: typeId, masterPermitId: master.id);
          successCount++;
        } catch (e) {
          failures.add(e.toString());
        }
      }
      if (!mounted) return;
      if (successCount > 0 && failures.isEmpty) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(successCount == 1 ? 'Permit requested' : '$successCount permits requested')),
        );
      } else if (successCount > 0) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('$successCount requested, ${failures.length} failed')),
        );
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(failures.isNotEmpty ? failures.first : 'Failed to request permits')),
        );
      }
      await _load();
    } finally {
      if (mounted) {
        setState(() => _addingWorkPermits = false);
      }
    }
  }

  Future<void> _activatePaperPermitMode() async {
    if (_job == null) return;
    final existingPermitCount = _job!.permits.length;
    final resetMsg = existingPermitCount > 0
        ? 'This job has $existingPermitCount existing permit(s). Switching to paper mode will remove all existing permits (including captured permits) and create a fresh Work Authorisation in paper mode. Continue?'
        : 'Switching to paper mode will create a fresh Work Authorisation in paper mode. Continue?';
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Switch to paper permits?'),
        content: Text(resetMsg),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Switch')),
        ],
      ),
    );
    if (ok != true || !mounted) return;
    try {
      await _jobCardsService.activatePaperPermitMode(_job!.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Paper permit mode enabled')));
      await _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _editPaperPermitNumber(JobPermitDto permit) async {
    final ctrl = TextEditingController(text: permit.paperPermitNumber ?? '');
    final saved = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Paper permit number'),
        content: TextField(
          controller: ctrl,
          decoration: const InputDecoration(hintText: 'Reference on the physical permit'),
          maxLength: 50,
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Save')),
        ],
      ),
    );
    if (saved != true || !mounted) return;
    try {
      await _permitsService.setPaperPermitNumber(permit.id, ctrl.text.trim());
      if (!mounted) return;
      await _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _paperClientSignOff(JobPermitDto permit) async {
    try {
      await _permitsService.paperClientSignOff(permit.id);
      if (!mounted) return;
      await _jobCardsService.setActivePermit(_job!.id, permit.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Paper sign-off recorded')));
      await _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _viewDocument(JobCardDocumentDto doc) async {
    final bytes = await _workService.getDocumentFile(_job!.id, doc.id);
    if (!mounted || bytes == null || bytes.isEmpty) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Unable to load file')));
      return;
    }
    final lowerType = (doc.documentType).toLowerCase();
    final imageTypes = ['beforework', 'midwork', 'afterwork', 'finalclientsignoff'];
    if (imageTypes.contains(lowerType)) {
      await showDialog(
        context: context,
        builder: (ctx) => Dialog(
          child: InteractiveViewer(
            child: Image.memory(Uint8List.fromList(bytes), fit: BoxFit.contain),
          ),
        ),
      );
    } else {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('File loaded. Preview supported for site photos.')));
    }
  }

  Future<void> _uploadPermitSignature(JobPermitDto permit) async {
    final bytes = await showSignatureCaptureDialog(context);
    if (bytes == null || bytes.isEmpty || !mounted) return;
    try {
      await _permitsService.uploadAttachment(permit.id, bytes, 'client-signature.png');
      if (!mounted) return;
      await _jobCardsService.setActivePermit(_job!.id, permit.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Signature uploaded')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _requestReplacementPermit(JobPermitDto p) async {
    if (_job == null || !_isPermitManager) return;
    try {
      if (p.isWorkAuthorisation) {
        await _permitsService.requestPermit(_job!.id);
      } else {
        final master = _effectiveWaMasterForChildRequest(_job!.permits);
        final typeId = p.permitTypeId;
        if (master == null || typeId == null || typeId.isEmpty) {
          if (!mounted) return;
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Renew the Work Authorisation before requesting this work permit again.')),
          );
          return;
        }
        await _permitsService.requestPermit(_job!.id, permitTypeId: typeId, masterPermitId: master.id);
      }
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Replacement permit requested.')));
      await _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _showReportIncident() async {
    final descController = TextEditingController();
    var severity = 'Medium';
    var blockJobDueToIncident = false;
    final photoFiles = <File>[];
    final result = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => AlertDialog(
          title: const Text('Report incident'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextField(
                  controller: descController,
                  decoration: const InputDecoration(
                    labelText: 'Description',
                    hintText: 'What happened?',
                    border: OutlineInputBorder(),
                  ),
                  maxLines: 4,
                ),
                const SizedBox(height: 16),
                DropdownButtonFormField<String>(
                  value: severity,
                  decoration: const InputDecoration(labelText: 'Severity', border: OutlineInputBorder()),
                  items: const [
                    DropdownMenuItem(value: 'Low', child: Text('Low')),
                    DropdownMenuItem(value: 'Medium', child: Text('Medium')),
                    DropdownMenuItem(value: 'High', child: Text('High')),
                    DropdownMenuItem(value: 'Critical', child: Text('Critical')),
                  ],
                  onChanged: (v) => setState(() => severity = v ?? 'Medium'),
                ),
                const SizedBox(height: 12),
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Pause / block this job'),
                  subtitle: const Text(
                    'Same as Block job on the web: technicians cannot open this job until a coordinator clears the block.',
                    style: TextStyle(fontSize: 12),
                  ),
                  value: blockJobDueToIncident,
                  onChanged: (v) => setState(() => blockJobDueToIncident = v),
                ),
                const SizedBox(height: 12),
                Text('Photos (optional)', style: TextStyle(fontSize: 12, color: Colors.grey[600])),
                const SizedBox(height: 4),
                Row(
                  children: [
                    OutlinedButton.icon(
                      onPressed: () async {
                        final picker = ImagePicker();
                        final x = await picker.pickImage(source: ImageSource.camera);
                        if (x != null) setState(() => photoFiles.add(File(x.path)));
                      },
                      icon: const Icon(Icons.camera_alt, size: 18),
                      label: const Text('Camera'),
                    ),
                    const SizedBox(width: 8),
                    OutlinedButton.icon(
                      onPressed: () async {
                        final picker = ImagePicker();
                        final x = await picker.pickImage(source: ImageSource.gallery);
                        if (x != null) setState(() => photoFiles.add(File(x.path)));
                      },
                      icon: const Icon(Icons.photo_library, size: 18),
                      label: const Text('Gallery'),
                    ),
                  ],
                ),
                if (photoFiles.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text('${photoFiles.length} photo(s)', style: TextStyle(fontSize: 12, color: Colors.grey[600])),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
            FilledButton(
              onPressed: () {
                final desc = descController.text.trim();
                if (desc.isEmpty) {
                  ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Description is required')));
                  return;
                }
                if (severity.trim().isEmpty) {
                  ScaffoldMessenger.of(ctx).showSnackBar(const SnackBar(content: Text('Severity is required')));
                  return;
                }
                Navigator.pop(ctx, true);
              },
              child: const Text('Report'),
            ),
          ],
        ),
      ),
    );
    if (result != true || _job == null) return;
    if (!mounted) return;
    final desc = descController.text.trim();
    if (desc.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Description required')));
      return;
    }
    try {
      List<MapEntry<List<int>, String>>? photoBytesAndNames;
      if (photoFiles.isNotEmpty) {
        photoBytesAndNames = [];
        for (var i = 0; i < photoFiles.length; i++) {
          final bytes = await photoFiles[i].readAsBytes();
          final ext = photoFiles[i].path.toLowerCase().contains('.png') ? 'png' : 'jpg';
          photoBytesAndNames.add(MapEntry(bytes, 'incident_${DateTime.now().millisecondsSinceEpoch}_$i.$ext'));
        }
      }
      await _workService.createIncidentWithPhotos(
        _job!.id,
        desc,
        severity: severity,
        photoBytesAndNames: photoBytesAndNames,
        blockJobDueToIncident: blockJobDueToIncident,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Incident reported')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _markPermitDone(JobPermitDto permit) async {
    try {
      await _permitsService.updateStatus(permit.id, 'Closed');
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Permit marked done')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _deletePermit(JobPermitDto permit) async {
    if (!_isPermitManager || widget.viewOnly) return;
    final body = PermitStatusValue.isActiveLike(permit.status)
        ? 'This permit type is no longer required by the saved Work Authorisation. Remove it from the job? This cannot be undone.'
        : 'This permit type is no longer required by the saved Work Authorisation. Remove it and any unsigned form data? This cannot be undone.';
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Remove permit?'),
        content: Text(body),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Delete')),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await _permitsService.deletePermit(permit.id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Permit removed')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _emailPermitToClient(JobPermitDto permit) async {
    final ok = permit.hasClientSignOff || PermitStatusValue.isActiveLike(permit.status);
    if (!ok) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Client sign-off is required before emailing the permit.')),
      );
      return;
    }
    try {
      if (permit.isWorkAuthorisation) {
        await _permitsService.emailMasterPermitToClient(permit.id);
      } else {
        await _permitsService.emailChildPermitToClient(permit.id);
      }
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Permit documentation emailed to client')),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  Future<void> _openMasterPermitWizard(JobPermitDto permit) async {
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => WorkAuthorizationPermitScreen(permitId: permit.id, jobCardId: _job!.id),
      ),
    );
    if (changed == true) {
      _load();
    }
  }

  bool _childPermitFormEditable(JobPermitDto p) {
    if (!(PermitStatusValue.isDraftLike(p.status) || PermitStatusValue.isCapturedLike(p.status))) return false;
    if (_job == null || !_job!.pendingWaAmendmentSignOff) return true;
    return _childPermitMayContinueDuringWaAmendment(p);
  }

  Future<void> _openChildPermitForm(JobPermitDto permit) async {
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => ChildPermitFormScreen(permit: permit),
      ),
    );
    if (changed == true && mounted) {
      _load();
    }
  }

  Widget _workflowStep(int n, String title, String detail, {bool done = false, bool muted = false}) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        CircleAvatar(
          radius: 14,
          backgroundColor: done ? Colors.green : (muted ? Colors.grey.shade400 : Colors.blue.shade700),
          foregroundColor: Colors.white,
          child: done
              ? const Icon(Icons.check, size: 16)
              : Text('$n', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold)),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  color: muted ? Colors.grey[600] : Colors.black87,
                  fontSize: 13,
                ),
              ),
              Text(
                detail,
                style: TextStyle(fontSize: 12, color: muted ? Colors.grey[600] : Colors.black54),
              ),
            ],
          ),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leadingWidth: 96,
        leading: Row(
          children: [
            IconButton(
              icon: const Icon(Icons.arrow_back),
              onPressed: () {
                widget.onClosed?.call();
                Navigator.of(context).pop();
              },
            ),
            Image.asset('assets/logo/ike-icon.png', width: 24, height: 24, fit: BoxFit.contain),
          ],
        ),
        title: const Text('Job detail'),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(_error!),
                        const SizedBox(height: 16),
                        TextButton(
                          onPressed: () => Navigator.of(context).pop(),
                          child: const Text('Back'),
                        ),
                      ],
                    ),
                  ),
                )
              : _job == null
                  ? const Center(child: Text('Job not found'))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: SingleChildScrollView(
                        physics: const AlwaysScrollableScrollPhysics(),
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            if (!widget.viewOnly && (_job!.pendingWaAmendmentSignOff || _waExpiredStandstill) && !_job!.paperPermitMode) ...[
                              Card(
                                color: _waExpiredStandstill ? Colors.red.shade50 : Colors.orange.shade50,
                                margin: EdgeInsets.zero,
                                child: Padding(
                                  padding: const EdgeInsets.all(12),
                                  child: Row(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Icon(Icons.pause_circle_outline, color: _waExpiredStandstill ? Colors.red[900] : Colors.orange[900]),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Text(
                                              _waExpiredStandstill ? 'Work Authorisation expired' : 'Work Authorisation amended',
                                              style: TextStyle(fontWeight: FontWeight.bold, color: _waExpiredStandstill ? Colors.red[900] : Colors.orange[900]),
                                            ),
                                            const SizedBox(height: 4),
                                            Text(
                                              _waExpiredStandstill
                                                  ? 'Work is paused. Complete and sign off the new Work Authorisation draft to continue. Incidents can still be reported.'
                                                  : 'The client must sign off the Work Authorisation again before general site work (photos, parts) can continue. You may still work only under child permits that were already signed off and active while the job is in progress.',
                                              style: TextStyle(color: _waExpiredStandstill ? Colors.red[900] : Colors.orange[900], fontSize: 13),
                                            ),
                                          ],
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                              const SizedBox(height: 12),
                            ],
                            if (_job!.paperPermitMode) ...[
                              Card(
                                color: Colors.lightBlue.shade50,
                                margin: EdgeInsets.zero,
                                child: Padding(
                                  padding: const EdgeInsets.all(12),
                                  child: Row(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Icon(Icons.description_outlined, color: Colors.blue[800]),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Text(
                                          'Paper permit mode: open each permit’s menu to enter the paper reference, then tap when the client has signed the physical permit.',
                                          style: TextStyle(color: Colors.blue[900], fontSize: 13),
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                              const SizedBox(height: 12),
                            ],
                            if (_isPermitManager && _job!.canActivatePaperPermitMode) ...[
                              OutlinedButton.icon(
                                onPressed: _activatePaperPermitMode,
                                icon: const Icon(Icons.assignment_outlined),
                                label: const Text('Switch job to paper permits'),
                              ),
                              const SizedBox(height: 12),
                            ],
                            if (_job!.blockedReason != null && _job!.blockedReason!.trim().isNotEmpty) ...[
                              Card(
                                color: Colors.red.shade50,
                                margin: EdgeInsets.zero,
                                child: Padding(
                                  padding: const EdgeInsets.all(12),
                                  child: Row(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Icon(Icons.block, color: Colors.red[800]),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Text(
                                              'Job blocked',
                                              style: TextStyle(fontWeight: FontWeight.bold, color: Colors.red[900]),
                                            ),
                                            const SizedBox(height: 4),
                                            Text(
                                              _job!.blockedReason!,
                                              style: TextStyle(color: Colors.red[900], fontSize: 13),
                                            ),
                                            const SizedBox(height: 6),
                                            Text(
                                              'A coordinator can clear this with Unblock job on the web (same as the job card block control).',
                                              style: TextStyle(fontSize: 12, color: Colors.red[800]),
                                            ),
                                          ],
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                              const SizedBox(height: 16),
                            ],
                            _Section(
                              title: _job!.jobCardNumber,
                              children: [
                                _row('Status', _job!.status),
                                _row('Site', _job!.siteName ?? '—'),
                                if (_job!.siteAddress != null) _row('Address', _job!.siteAddress!),
                                _row('Priority', 'P${_job!.priority}'),
                                if (_job!.dueDate != null) _row('Due', _formatDate(_job!.dueDate!)),
                                if (_job!.permitsRequired) _row('Permits', 'Required'),
                              ],
                            ),
                            _Section(
                              title: 'Key timestamps',
                              children: [
                                if (_job!.createdAt != null) _row('Job created', _formatDateTime(_job!.createdAt!)),
                                if (_job!.startedAt != null) _row('Work started', _formatDateTime(_job!.startedAt!)),
                                if (_job!.permitsRequired && _job!.firstPermitRequestedAt != null) _row('1st permit requested', _formatDateTime(_job!.firstPermitRequestedAt!)),
                                if (_job!.permitsRequired && _job!.firstPermitApprovedAt != null) _row('1st permit approved', _formatDateTime(_job!.firstPermitApprovedAt!)),
                                if (_job!.firstSitePhotoAt != null) _row('1st site photo', _formatDateTime(_job!.firstSitePhotoAt!)),
                                if (_job!.completedAt != null) _row('Job completed', _formatDateTime(_job!.completedAt!)),
                              ],
                            ),
                            if (_job!.description != null && _job!.description!.isNotEmpty)
                              _Section(
                                title: 'Description',
                                children: [Text(_job!.description!)],
                              ),
                            if (_job!.siteAddress != null || _job!.siteLatitude != null) ...[
                              const SizedBox(height: 16),
                              FilledButton.icon(
                                onPressed: widget.viewOnly ? null : () => _openMaps(_job!),
                                icon: const Icon(Icons.directions),
                                label: const Text('Navigate to site'),
                              ),
                            ],
                            if (!widget.viewOnly && _job!.canOpen && !_isJobStarted && _job!.permitsRequired) ...[
                              const SizedBox(height: 16),
                              Card(
                                color: Colors.blue.shade50,
                                margin: EdgeInsets.zero,
                                child: Padding(
                                  padding: const EdgeInsets.all(12),
                                  child: Column(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        'On-site work order',
                                        style: TextStyle(
                                          fontWeight: FontWeight.bold,
                                          color: Colors.blueGrey[900],
                                          fontSize: 14,
                                        ),
                                      ),
                                      const SizedBox(height: 8),
                                      _workflowStep(
                                        1,
                                        'Work Authorisation first',
                                        _workAuthorisationSignedOffForStart
                                            ? 'Signed off — you can start the job below.'
                                            : 'Complete the master permit, then client sign-off (upload / signature). Hot Work and other permits appear in the list after you start.',
                                        done: _workAuthorisationSignedOffForStart,
                                      ),
                                      const SizedBox(height: 6),
                                      _workflowStep(
                                        2,
                                        'Start job',
                                        'Begins the on-site work phase (after the WA is signed off).',
                                        done: false,
                                        muted: !_workAuthorisationSignedOffForStart,
                                      ),
                                      const SizedBox(height: 6),
                                      _workflowStep(
                                        3,
                                        'Other work permits',
                                        'After you start the job, required types from the Work Authorisation appear in the Permits list automatically.',
                                        done: false,
                                        muted: true,
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                            ],
                            if (!widget.viewOnly && _job!.permitsRequired) ...[
                              const SizedBox(height: 24),
                              if (_hasExpiredPermit)
                                GestureDetector(
                                  onTap: _showNeedMoreWorkDialog,
                                  child: Card(
                                    color: Colors.orange.shade50,
                                    margin: const EdgeInsets.only(bottom: 12),
                                    child: Padding(
                                      padding: const EdgeInsets.all(12),
                                      child: Row(
                                        children: [
                                          Icon(Icons.warning_amber, color: Colors.orange[700]),
                                          const SizedBox(width: 8),
                                          Expanded(
                                            child: Column(
                                              crossAxisAlignment: CrossAxisAlignment.start,
                                              children: [
                                                Text(
                                                  'Permit expired. Resolve before other actions.',
                                                  style: TextStyle(color: Colors.orange[900], fontWeight: FontWeight.w500),
                                                ),
                                                Text(
                                                  'Open the expired permit’s menu (⋮) and tap Request new…',
                                                  style: TextStyle(color: Colors.orange[800], fontSize: 12),
                                                ),
                                              ],
                                            ),
                                          ),
                                          Icon(Icons.info_outline, color: Colors.orange[700], size: 20),
                                        ],
                                      ),
                                    ),
                                  ),
                                ),
                              _Section(
                                title: 'Permits',
                                children: [
                                  if (_jobRefreshing)
                                    const Padding(
                                      padding: EdgeInsets.only(bottom: 8),
                                      child: LinearProgressIndicator(),
                                    ),
                                  ..._job!.permits.map((p) => _PermitTile(
                                        permit: p,
                                        jobId: _job!.id,
                                        jobPermits: _job!.permits,
                                        paperPermitMode: _job!.paperPermitMode,
                                        jobPendingWaAmendmentSignOff: _job!.pendingWaAmendmentSignOff,
                                        suppressPermitMenu:
                                            _job!.pendingWaAmendmentSignOff && !p.isWorkAuthorisation,
                                        onRequestReplacementPermit: _isPermitManager && !widget.viewOnly
                                            ? () => _requestReplacementPermit(p)
                                            : null,
                                        onCaptureSignature: _isPermitManager &&
                                                !_job!.paperPermitMode &&
                                                !_waStandstillBlocksChildFileActions(p) &&
                                                (!_waExpiredStandstill || p.isWorkAuthorisation)
                                            ? () => _uploadPermitSignature(p)
                                            : null,
                                        onSetPaperNumber:
                                            _isPermitManager && _job!.paperPermitMode ? () => _editPaperPermitNumber(p) : null,
                                        onPaperClientSignOff:
                                            _isPermitManager && _job!.paperPermitMode ? () => _paperClientSignOff(p) : null,
                                        onMarkDone: _isPermitManager &&
                                                !p.isWorkAuthorisation &&
                                                (!_waExpiredStandstill || _job!.paperPermitMode)
                                            ? () => _markPermitDone(p)
                                            : null,
                                        onEmailClient: _isPermitManager &&
                                                !_job!.paperPermitMode &&
                                                !(_job!.pendingWaAmendmentSignOff &&
                                                    !p.isWorkAuthorisation &&
                                                    !_childPermitMayContinueDuringWaAmendment(p))
                                                && (!_waExpiredStandstill || p.isWorkAuthorisation)
                                            ? () => _emailPermitToClient(p)
                                            : null,
                                        onOpenMasterPermit: _isPermitManager && p.isWorkAuthorisation && !_job!.paperPermitMode
                                            ? () => _openMasterPermitWizard(p)
                                            : null,
                                        onFillChildPermit: _isPermitManager &&
                                                !_job!.paperPermitMode &&
                                                !p.isWorkAuthorisation &&
                                                p.hasChildFormContent &&
                                                !_waExpiredStandstill &&
                                                _childPermitFormEditable(p)
                                            ? () => _openChildPermitForm(p)
                                            : null,
                                        onDeletePermit: _showDeletePermitInMenu(p) ? () => _deletePermit(p) : null,
                                      )),
                                  const SizedBox(height: 8),
                                  if (!widget.viewOnly &&
                                      _job!.canOpen &&
                                      !_isJobStarted &&
                                      (!_job!.permitsRequired || _workAuthorisationSignedOffForStart)) ...[
                                    const SizedBox(height: 20),
                                    FilledButton.icon(
                                      onPressed: _blockedByExpiredPermit
                                          ? () => _showExpiredPermitBlockedDialog()
                                          : (_canStartWork
                                              ? () => _updateStatus(_job!, 'In Progress')
                                              : () => ScaffoldMessenger.of(context).showSnackBar(
                                                    SnackBar(
                                                      content: Text(
                                                        _job!.permitsRequired
                                                            ? 'Complete the Work Authorisation and get client sign-off first. Then you can start the job.'
                                                            : 'You cannot start this job yet.',
                                                      ),
                                                    ),
                                                  )),
                                      icon: const Icon(Icons.play_arrow),
                                      label: const Text('Start job'),
                                      style: FilledButton.styleFrom(
                                        backgroundColor: AppColors.brandRed,
                                        foregroundColor: AppColors.white,
                                      ),
                                    ),
                                    const SizedBox(height: 8),
                                    Text(
                                      _job!.permitsRequired
                                          ? 'Start job only after the Work Authorisation is signed off. Required child permits appear after you start.'
                                          : 'Start when ready.',
                                      style: TextStyle(color: Colors.grey[600], fontSize: 12),
                                    ),
                                  ],
                                  if (_isPermitManager &&
                                      _job!.paperPermitMode &&
                                      _isJobStarted &&
                                      _workAuthorisationSignedOffForStart &&
                                      _validMasterWorkAuthorisation != null)
                                    Padding(
                                      padding: const EdgeInsets.only(top: 6, bottom: 8),
                                      child: OutlinedButton.icon(
                                        onPressed: _addingWorkPermits
                                            ? null
                                            : () => _pickAndRequestPaperChildPermit(_validMasterWorkAuthorisation!),
                                        icon: const Icon(Icons.add),
                                        label: Text(_addingWorkPermits ? 'Adding permits…' : 'Add work permit'),
                                      ),
                                    ),
                                  if (!_isPermitManager)
                                    Padding(
                                      padding: const EdgeInsets.only(top: 4),
                                      child: Text(
                                        'Only the permit manager can manage permits.',
                                        style: TextStyle(color: Colors.grey[600], fontSize: 13),
                                      ),
                                    ),
                                  if (_isPermitManager &&
                                      _validMasterWorkAuthorisation != null &&
                                      _isJobStarted &&
                                      _job!.pendingWaAmendmentSignOff)
                                    Padding(
                                      padding: const EdgeInsets.only(top: 6),
                                      child: Text(
                                        'New or changed work permits from the amended Work Authorisation are applied after the client signs off again.',
                                        style: TextStyle(color: Colors.orange[800], fontSize: 12),
                                      ),
                                    ),
                                  if (_isPermitManager && !_isJobStarted && _job!.permitsRequired)
                                    Padding(
                                      padding: const EdgeInsets.only(top: 6),
                                      child: Text(
                                        _validMasterWorkAuthorisation != null
                                            ? 'Start the job after the Work Authorisation is signed off; required permits then appear in the list below.'
                                            : 'A Work Authorisation draft will appear automatically. Complete and sign it off, then start the job.',
                                        style: TextStyle(color: Colors.orange[800], fontSize: 12),
                                      ),
                                    ),
                                ],
                              ),
                            ],
                            if (!widget.viewOnly && _job!.canOpen && !_isJobStarted && !_job!.permitsRequired) ...[
                              const SizedBox(height: 24),
                              FilledButton.icon(
                                onPressed: _blockedByExpiredPermit
                                    ? () => _showExpiredPermitBlockedDialog()
                                    : (_canStartWork
                                        ? () => _updateStatus(_job!, 'In Progress')
                                        : () => ScaffoldMessenger.of(context).showSnackBar(
                                              SnackBar(
                                                content: Text(
                                                  _job!.permitsRequired
                                                      ? 'Complete the Work Authorisation and get client sign-off first. Then you can start the job.'
                                                      : 'You cannot start this job yet.',
                                                ),
                                              ),
                                            )),
                                icon: const Icon(Icons.play_arrow),
                                label: const Text('Start job'),
                                style: FilledButton.styleFrom(
                                  backgroundColor: AppColors.brandRed,
                                  foregroundColor: AppColors.white,
                                ),
                              ),
                              const SizedBox(height: 8),
                              Text(
                                'Start when ready.',
                                style: TextStyle(color: Colors.grey[600], fontSize: 12),
                              ),
                            ],
                            if (_showSitePhotosSection) ...[
                              const SizedBox(height: 24),
                              _Section(
                                title: 'Site photos',
                                children: [
                                  _SitePhotoRow(
                                    label: 'Before work (required)',
                                    hasPhoto: _hasBeforePhoto,
                                    count: _job!.documents.where((d) => d.documentType == 'BeforeWork').length,
                                    canUpload: !widget.viewOnly &&
                                        _isAssignedToJob &&
                                        _canUploadBefore &&
                                        !_waAmendmentBlocksSitePhotos,
                                    onUpload: () => _uploadSitePhotos('BeforeWork', multi: true),
                                    blocked: _blockedByExpiredPermit,
                                    onBlocked: _showExpiredPermitBlockedDialog,
                                    lockedReason: widget.viewOnly
                                        ? 'Job completed'
                                        : (_waAmendmentBlocksSitePhotos
                                            ? 'Work Authorisation amended — get client sign-off again before uploading site photos'
                                            : (!_isJobStarted
                                                ? 'Start job first'
                                                : (!(_job?.paperPermitMode ?? false) && !_permitsClientSignedOffForWork && !_hasBeforePhoto)
                                                    ? 'Client sign-off required on all permits first'
                                                    : null)),
                                  ),
                                  _SitePhotoRow(
                                    label: 'Progress (optional)',
                                    hasPhoto: _midPhotos.isNotEmpty,
                                    count: _midPhotos.length,
                                    canUpload: !widget.viewOnly &&
                                        _isAssignedToJob &&
                                        _canUploadMid &&
                                        !_waAmendmentBlocksSitePhotos,
                                    onUpload: () => _uploadSitePhotos('MidWork', multi: true),
                                    blocked: _blockedByExpiredPermit,
                                    onBlocked: _showExpiredPermitBlockedDialog,
                                    lockedReason: widget.viewOnly
                                        ? 'Job completed'
                                        : (_waAmendmentBlocksSitePhotos
                                            ? 'Work Authorisation amended — get client sign-off again before uploading site photos'
                                            : (!_hasBeforePhoto ? 'Upload before photos first' : null)),
                                  ),
                                  _SitePhotoRow(
                                    label: 'After work (required)',
                                    hasPhoto: _hasAfterPhoto,
                                    count: _job!.documents.where((d) => d.documentType == 'AfterWork').length,
                                    canUpload: !widget.viewOnly &&
                                        _isAssignedToJob &&
                                        _canUploadAfter &&
                                        !_waAmendmentBlocksSitePhotos,
                                    onUpload: () => _uploadSitePhotos('AfterWork', multi: true),
                                    blocked: _blockedByExpiredPermit,
                                    onBlocked: _showExpiredPermitBlockedDialog,
                                    lockedReason: widget.viewOnly
                                        ? 'Job completed'
                                        : (_waAmendmentBlocksSitePhotos
                                            ? 'Work Authorisation amended — get client sign-off again before uploading site photos'
                                            : (!_hasBeforePhoto ? 'Upload before photos first' : null)),
                                  ),
                                ],
                              ),
                            ],
                            if (_visibleUploadedDocuments.isNotEmpty) ...[
                              const SizedBox(height: 24),
                              Theme(
                                data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
                                child: ExpansionTile(
                                  key: ValueKey<int>(_documentsAccordionKey),
                                  initiallyExpanded: _documentsAccordionExpanded,
                                  onExpansionChanged: (expanded) => setState(() => _documentsAccordionExpanded = expanded),
                                  title: Text(
                                    'Uploaded documents',
                                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                                          fontWeight: FontWeight.bold,
                                          color: AppColors.charcoal,
                                        ),
                                  ),
                                  subtitle: Text(
                                    '${_visibleUploadedDocuments.length} file(s)',
                                    style: TextStyle(fontSize: 13, color: Colors.grey[600]),
                                  ),
                                  children: _visibleUploadedDocuments
                                      .map((d) => ListTile(
                                            dense: true,
                                            title: Text(
                                                '${_documentListTitle(d)}${d.signedByUserName != null ? ' • ${d.signedByUserName}' : ''}'),
                                            subtitle: d.signedAt != null ? Text(_formatDate(d.signedAt!)) : null,
                                            trailing: IconButton(
                                              icon: const Icon(Icons.visibility_outlined),
                                              onPressed: () => _viewDocument(d),
                                            ),
                                          ))
                                      .toList(),
                                ),
                              ),
                            ],
                            if (_job!.incidentReports.isNotEmpty) ...[
                              const SizedBox(height: 24),
                              _Section(
                                title: 'Incidents',
                                children: _job!.incidentReports
                                    .map((ir) => ListTile(
                                          dense: true,
                                          title: Text(ir.description),
                                          subtitle: Text('${ir.severity} • ${_formatDate(ir.createdAt)}'),
                                        ))
                                    .toList(),
                              ),
                            ],
                            if (!widget.viewOnly && _job!.canOpen) ...[
                              const SizedBox(height: 24),
                              OutlinedButton.icon(
                                onPressed: _blockedByExpiredPermit ? () => _showExpiredPermitBlockedDialog() : _showReportIncident,
                                icon: const Icon(Icons.warning_amber),
                                label: const Text('Report incident'),
                              ),
                            ],
                            const SizedBox(height: 32),
                            if (!widget.viewOnly && _job!.canOpen) ...[
                              const Divider(),
                              const SizedBox(height: 8),
                              if (_isJobStarted)
                                ...[
                                  if (!_canComplete) ...[
                                    Text(
                                      (_canUploadBefore || _canUploadMid || _canUploadAfter || _hasBeforePhoto || _hasAfterPhoto || _midPhotos.isNotEmpty)
                                              && (!_hasBeforePhoto || !_hasAfterPhoto)
                                          ? 'Upload before and after site photos to complete.'
                                          : _job!.permitsRequired && _job!.permits.isNotEmpty && !_allPermitsMarkedDone
                                              ? 'Mark each work permit (Hot Work, etc.) as done when finished. The Work Authorisation does not need to be marked done before you complete the job.'
                                              : _hasExpiredPermit
                                                  ? 'A permit has expired. Request again (next day if same type) and get client sign-off before continuing.'
                                                  : 'Complete requirements above to enable.',
                                      style: TextStyle(color: Colors.orange[800], fontSize: 13),
                                    ),
                                    const SizedBox(height: 8),
                                  ],
                                  if (!_hasFinalClientSignOff)
                                    OutlinedButton.icon(
                                      onPressed: _canComplete ? _recordFinalClientSignOff : null,
                                      icon: const Icon(Icons.draw_outlined),
                                      label: const Text('Final client sign off'),
                                    )
                                  else
                                    OutlinedButton.icon(
                                      onPressed: _canComplete
                                          ? () => _updateStatus(_job!, 'Completed', popAfter: true)
                                          : null,
                                      icon: const Icon(Icons.check_circle),
                                      label: const Text('Mark completed'),
                                    ),
                                ],
                            ],
                          ],
                        ),
                      ),
                    ),
    );
  }

  String _formatDate(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  String _formatDateTime(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')} ${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';

  Widget _row(String label, String value) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 100,
              child: Text(label, style: const TextStyle(color: Colors.grey, fontSize: 14)),
            ),
            Expanded(child: Text(value)),
          ],
        ),
      );
}

class _Section extends StatelessWidget {
  final String title;
  final List<Widget> children;

  const _Section({required this.title, required this.children});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: Theme.of(context).textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.bold,
                color: AppColors.charcoal,
              ),
        ),
        const SizedBox(height: 8),
        ...children,
      ],
    );
  }
}

class _SitePhotoRow extends StatelessWidget {
  final String label;
  final bool hasPhoto;
  final int count;
  final bool canUpload;
  final VoidCallback onUpload;
  final bool blocked;
  final VoidCallback? onBlocked;
  final String? lockedReason;

  const _SitePhotoRow({
    required this.label,
    required this.hasPhoto,
    this.count = 0,
    this.canUpload = true,
    required this.onUpload,
    this.blocked = false,
    this.onBlocked,
    this.lockedReason,
  });

  @override
  Widget build(BuildContext context) {
    final isLocked = !canUpload || (lockedReason != null && lockedReason!.isNotEmpty);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  hasPhoto ? '$label ✓${count > 1 ? ' ($count)' : ''}' : label,
                  style: TextStyle(
                    fontWeight: hasPhoto ? FontWeight.w600 : FontWeight.normal,
                  ),
                ),
                if (isLocked && lockedReason != null)
                  Text(lockedReason!, style: TextStyle(fontSize: 12, color: Colors.grey[600])),
              ],
            ),
          ),
          OutlinedButton.icon(
            onPressed: isLocked ? null : (blocked && onBlocked != null ? onBlocked : onUpload),
            icon: Icon(hasPhoto ? Icons.add_photo_alternate : Icons.camera_alt, size: 18),
            label: Text(hasPhoto ? 'Add more' : 'Upload'),
          ),
        ],
      ),
    );
  }
}

class _PermitTile extends StatelessWidget {
  final JobPermitDto permit;
  final String jobId;
  final List<JobPermitDto> jobPermits;
  final bool paperPermitMode;
  final bool jobPendingWaAmendmentSignOff;
  final bool suppressPermitMenu;
  final VoidCallback? onCaptureSignature;
  final VoidCallback? onSetPaperNumber;
  final VoidCallback? onPaperClientSignOff;
  final VoidCallback? onMarkDone;
  final VoidCallback? onEmailClient;
  final VoidCallback? onOpenMasterPermit;
  final VoidCallback? onFillChildPermit;
  final VoidCallback? onDeletePermit;
  final VoidCallback? onRequestReplacementPermit;

  const _PermitTile({
    required this.permit,
    required this.jobId,
    required this.jobPermits,
    this.paperPermitMode = false,
    this.jobPendingWaAmendmentSignOff = false,
    this.suppressPermitMenu = false,
    this.onCaptureSignature,
    this.onSetPaperNumber,
    this.onPaperClientSignOff,
    this.onMarkDone,
    this.onEmailClient,
    this.onOpenMasterPermit,
    this.onFillChildPermit,
    this.onDeletePermit,
    this.onRequestReplacementPermit,
  });

  String _formatDate(DateTime dt) =>
      '${dt.year}-${dt.month.toString().padLeft(2, '0')}-${dt.day.toString().padLeft(2, '0')} ${dt.hour.toString().padLeft(2, '0')}:${dt.minute.toString().padLeft(2, '0')}';

  String _timeLeftLabel(DateTime? validTo) {
    if (validTo == null) return '';
    final now = DateTime.now().toUtc();
    final diff = validTo.difference(now);
    if (diff.isNegative) return 'Expired';
    final days = diff.inDays;
    final hours = diff.inHours % 24;
    final mins = diff.inMinutes % 60;
    if (days > 0) return '$days d $hours h left';
    if (diff.inHours > 0) return '${diff.inHours} h $mins m left';
    return '${diff.inMinutes} m left';
  }

  String _displayStatus() {
    if (PermitStatusValue.isClosedLike(permit.status)) return 'Done';
    if (PermitStatusValue.isCapturedLike(permit.status)) return 'Form captured';
    return permit.status;
  }

  Widget _leadingIcon() {
    if (permit.isWorkAuthorisation) {
      return const Tooltip(
        message: 'Work Authorisation (master)',
        child: Icon(Icons.admin_panel_settings, color: Colors.blue),
      );
    }
    final n = (permit.permitTemplateName ?? '').toLowerCase();
    IconData icon = Icons.assignment_outlined;
    Color color = Colors.blueGrey;
    if (n.contains('hot')) {
      icon = Icons.local_fire_department;
      color = Colors.deepOrange;
    } else if (n.contains('height')) {
      icon = Icons.arrow_upward;
      color = Colors.indigo;
    } else if (n.contains('lift')) {
      icon = Icons.precision_manufacturing_outlined;
      color = Colors.indigo;
    } else if (n.contains('confined')) {
      icon = Icons.door_front_door_outlined;
      color = Colors.teal;
    } else if (n.contains('excavat')) {
      icon = Icons.construction;
      color = Colors.brown;
    } else if (n.contains('energy') || n.contains('isol') || n.contains('lockout') || n.contains('power')) {
      icon = Icons.power;
      color = Colors.amber.shade800;
    } else if (n.contains('degas') || (n.contains('clean') && n.contains('gas'))) {
      icon = Icons.cleaning_services_outlined;
      color = Colors.cyan;
    } else if (n.contains('radiograph')) {
      icon = Icons.medical_information_outlined;
      color = Colors.purple;
    } else if (n.contains('electric')) {
      icon = Icons.electrical_services;
      color = Colors.amber.shade700;
    } else if (n.contains('weld')) {
      icon = Icons.whatshot;
      color = Colors.deepOrange;
    } else if (n.contains('scaffold')) {
      icon = Icons.view_week_outlined;
      color = Colors.blueGrey;
    } else if (n.contains('crane') || n.contains('hoist')) {
      icon = Icons.precision_manufacturing;
      color = Colors.indigo;
    } else if (n.contains('rope') || n.contains('access')) {
      icon = Icons.cable_outlined;
      color = Colors.teal;
    } else if (n.contains('chemical') || n.contains('hazard')) {
      icon = Icons.science_outlined;
      color = Colors.deepPurple;
    } else if (n.contains('vehicle') || n.contains('traffic') || n.contains('mobile plant')) {
      icon = Icons.local_shipping_outlined;
      color = Colors.brown;
    } else if (n.contains('breathing') || n.contains('respir')) {
      icon = Icons.masks_outlined;
      color = Colors.cyan;
    } else if (n.contains('pressure') || n.contains('hydraulic')) {
      icon = Icons.compress;
      color = Colors.blue;
    } else if (n.contains('grind') || n.contains('drill') || n.contains('mechanical')) {
      icon = Icons.hardware;
      color = Colors.grey.shade700;
    }
    return Tooltip(
      message: permit.permitTemplateName ?? 'Permit',
      child: Icon(icon, color: color),
    );
  }

  bool _statusAllowsMoreFiles() {
    return !PermitStatusValue.isClosedLike(permit.status) && !PermitStatusValue.isExpiredLike(permit.status);
  }

  /// WA: saved past Draft (checklist persisted). Child: form/checklist content present from API.
  bool _permitFormReadyForClientSignature() {
    if (permit.isWorkAuthorisation) return !PermitStatusValue.isDraftLike(permit.status);
    if (PermitStatusValue.isDraftLike(permit.status)) return false;
    return permit.hasChildFormContent;
  }

  /// After WA amendment, client must sign again — show even if legacy attachments still satisfy [hasClientSignOff].
  bool _showCaptureClientSignatureMenu() {
    if (!_permitFormReadyForClientSignature()) return false;
    if (permit.isWorkAuthorisation && jobPendingWaAmendmentSignOff) return true;
    if (permit.hasClientSignOff) return false;
    if (PermitStatusValue.isClosedLike(permit.status) || PermitStatusValue.isExpiredLike(permit.status)) return false;
    if (permit.isPermitActive) return false;
    return true;
  }

  bool _canEmailFromMenu() {
    return permit.hasClientSignOff || PermitStatusValue.isActiveLike(permit.status);
  }

  @override
  Widget build(BuildContext context) {
    final lines = <String>[_displayStatus()];
    if (permit.requestedAt != null) lines.add('Requested ${_formatDate(permit.requestedAt!)}');
    final paperRef = permit.paperPermitNumber?.trim();
    if (paperRef != null && paperRef.isNotEmpty) lines.add('Paper ref: $paperRef');
    if (permit.paperClientSignedOffAt != null) lines.add('Paper signed ${_formatDate(permit.paperClientSignedOffAt!)}');
    if (permit.attachments.isNotEmpty) lines.add('${permit.attachments.length} file(s)');
    if (permit.hasClientSignOff && permit.validTo != null) {
      lines.add('Valid to ${_formatDate(permit.validTo!)}');
    }
    if (permit.hasClientSignOff && permit.isPermitActive && !permit.isPermitDone) {
      final left = _timeLeftLabel(permit.validTo);
      if (left.isNotEmpty) lines.add(left);
    }
    Color? cardColor;
    if (permit.isPermitDone) {
      cardColor = Colors.green.shade50;
    } else if (permit.isExpired && permit.isPermitActive) {
      cardColor = Colors.red.shade50;
    }
    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      color: cardColor,
      child: ListTile(
        leading: _leadingIcon(),
        title: Row(
          children: [
            Expanded(child: Text(permit.permitTemplateName ?? 'Permit')),
            if (paperPermitMode) ...[
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: Colors.blueGrey.shade100,
                  borderRadius: BorderRadius.circular(4),
                ),
                child: const Text('Paper', style: TextStyle(color: Colors.black87, fontSize: 11, fontWeight: FontWeight.w600)),
              ),
              const SizedBox(width: 6),
            ],
            if (permit.isExpired && permit.isPermitActive)
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: Colors.red,
                  borderRadius: BorderRadius.circular(4),
                ),
                child: const Text('Expired', style: TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.bold)),
              )
            else if (permit.isExpiringSoon)
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color: Colors.orange,
                  borderRadius: BorderRadius.circular(4),
                ),
                child: const Text('Expiring soon', style: TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.bold)),
              ),
          ],
        ),
        subtitle: Text(lines.join(' • ')),
        trailing: _buildPermitMenu(context),
      ),
    );
  }

  Widget? _buildPermitMenu(BuildContext context) {
    if (suppressPermitMenu) return null;

    final expMode = _expiredPermitMenuMode(jobPermits, permit);
    if (expMode == _ExpiredPermitMenuMode.readonlySuperseded) {
      return null;
    }
    if (expMode == _ExpiredPermitMenuMode.replacementOnly) {
      if (onRequestReplacementPermit == null) return null;
      final label = permit.isWorkAuthorisation
          ? 'Request new Work Authorisation'
          : 'Request new ${permit.permitTemplateName ?? 'permit'}';
      return PopupMenuButton<String>(
        icon: const Icon(Icons.more_vert),
        tooltip: 'Permit actions',
        onSelected: (v) {
          if (v == 'request_replacement') onRequestReplacementPermit!();
        },
        itemBuilder: (_) => [
          PopupMenuItem(
            value: 'request_replacement',
            child: Row(
              children: [
                const Icon(Icons.add_circle_outline),
                const SizedBox(width: 8),
                Expanded(child: Text(label)),
              ],
            ),
          ),
        ],
      );
    }

    if (paperPermitMode) {
      final items = <PopupMenuEntry<String>>[];
      if (onSetPaperNumber != null && permit.paperClientSignedOffAt == null) {
        items.add(
          PopupMenuItem(
            value: 'paper_num',
            child: Row(
              children: [
                const Icon(Icons.tag),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    (permit.paperPermitNumber == null || permit.paperPermitNumber!.trim().isEmpty)
                        ? 'Set paper permit number'
                        : 'Edit paper permit number',
                  ),
                ),
              ],
            ),
          ),
        );
      }
      if (onPaperClientSignOff != null) {
        final numOk = (permit.paperPermitNumber ?? '').trim().isNotEmpty;
        final notYet = permit.paperClientSignedOffAt == null;
        final label = !notYet
            ? 'Paper sign-off recorded'
            : (numOk ? 'Client signed paper permit' : 'Client signed paper permit (set number first)');
        items.add(
          PopupMenuItem(
            value: 'paper_sign',
            enabled: numOk && notYet,
            child: Row(
              children: [
                const Icon(Icons.check_circle_outline),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    label,
                  ),
                ),
              ],
            ),
          ),
        );
      }
      if (_statusAllowsMoreFiles() && onMarkDone != null && permit.hasClientSignOff && !permit.isWorkAuthorisation) {
        items.add(
          const PopupMenuItem(
            value: 'done',
            child: Row(children: [Icon(Icons.check), SizedBox(width: 8), Text('Mark done')]),
          ),
        );
      }
      if (onDeletePermit != null) {
        items.add(
          const PopupMenuItem(
            value: 'delete_permit',
            child: Row(children: [Icon(Icons.delete_outline, color: Colors.red), SizedBox(width: 8), Text('Remove permit')]),
          ),
        );
      }
      if (items.isEmpty) return null;
      return PopupMenuButton<String>(
        icon: const Icon(Icons.more_vert),
        tooltip: 'Permit actions',
        onSelected: (v) {
          if (v == 'paper_num') {
            onSetPaperNumber?.call();
          } else if (v == 'paper_sign') {
            onPaperClientSignOff?.call();
          } else if (v == 'done') {
            onMarkDone?.call();
          } else if (v == 'delete_permit') {
            onDeletePermit?.call();
          }
        },
        itemBuilder: (_) => items,
      );
    }

    if (jobPendingWaAmendmentSignOff && permit.isWorkAuthorisation) {
      if (!_showCaptureClientSignatureMenu() || onCaptureSignature == null) return null;
      return PopupMenuButton<String>(
        icon: const Icon(Icons.more_vert),
        tooltip: 'Client sign-off',
        onSelected: (v) {
          if (v == 'signature') onCaptureSignature?.call();
        },
        itemBuilder: (_) => [
          const PopupMenuItem(
            value: 'signature',
            child: Row(
              children: [
                Icon(Icons.draw),
                SizedBox(width: 8),
                Text('Capture client signature'),
              ],
            ),
          ),
        ],
      );
    }

    final items = <PopupMenuEntry<String>>[];
    if (permit.isWorkAuthorisation && onOpenMasterPermit != null) {
      items.add(
        PopupMenuItem(
          value: 'fill_master',
          child: Row(
            children: [
              const Icon(Icons.fact_check_outlined),
              const SizedBox(width: 8),
              Text(
                permit.hasClientSignOff
                    ? 'View master permit summary'
                    : ((PermitStatusValue.isDraftLike(permit.status) || PermitStatusValue.isCapturedLike(permit.status))
                        ? 'Continue master permit'
                        : 'Fill master permit'),
              ),
            ],
          ),
        ),
      );
    }
    if (!permit.isWorkAuthorisation && onFillChildPermit != null) {
      items.add(
        PopupMenuItem(
          value: 'fill_child',
          child: Row(
            children: [
              const Icon(Icons.edit_note),
              const SizedBox(width: 8),
              Text(
                PermitStatusValue.isCapturedLike(permit.status)
                    ? 'Update work permit form'
                    : 'Fill work permit & commitments',
              ),
            ],
          ),
        ),
      );
    }
    if (_showCaptureClientSignatureMenu() && onCaptureSignature != null) {
      items.add(
        const PopupMenuItem(
          value: 'signature',
          child: Row(
            children: [
              Icon(Icons.draw),
              SizedBox(width: 8),
              Text('Capture client signature'),
            ],
          ),
        ),
      );
    }
    if (_statusAllowsMoreFiles() && onMarkDone != null && permit.hasClientSignOff) {
      items.add(
        const PopupMenuItem(
          value: 'done',
          child: Row(children: [Icon(Icons.check), SizedBox(width: 8), Text('Mark done')]),
        ),
      );
    }
    if (onEmailClient != null) {
      items.add(
        PopupMenuItem(
          value: 'email_client',
          enabled: _canEmailFromMenu(),
          child: Row(
            children: [
              const Icon(Icons.email_outlined),
              const SizedBox(width: 8),
              Text(_canEmailFromMenu() ? 'Email client copy' : 'Email client (needs sign-off / active)'),
            ],
          ),
        ),
      );
    }
    if (onDeletePermit != null) {
      items.add(
        const PopupMenuItem(
          value: 'delete_permit',
          child: Row(children: [Icon(Icons.delete_outline, color: Colors.red), SizedBox(width: 8), Text('Delete permit')]),
        ),
      );
    }

    if (items.isEmpty) return null;

    return PopupMenuButton<String>(
      icon: const Icon(Icons.more_vert),
      tooltip: 'Permit actions',
      onSelected: (v) {
        if (v == 'fill_master') {
          onOpenMasterPermit?.call();
        } else if (v == 'fill_child') {
          onFillChildPermit?.call();
        } else if (v == 'signature') {
          onCaptureSignature?.call();
        } else if (v == 'done') {
          onMarkDone?.call();
        } else if (v == 'email_client') {
          onEmailClient?.call();
        } else if (v == 'delete_permit') {
          onDeletePermit?.call();
        }
      },
      itemBuilder: (_) => items,
    );
  }
}

