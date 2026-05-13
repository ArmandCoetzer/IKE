import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/job_cards_service.dart';
import '../services/job_permits_service.dart';
import '../widgets/signature_capture_dialog.dart';
import '../widgets/tradion_buttons.dart';

class WorkAuthorizationPermitScreen extends StatefulWidget {
  final String permitId;
  /// When opened from a job, empty WA fields are prefilled from site, description, and assignment count.
  final String? jobCardId;

  const WorkAuthorizationPermitScreen({super.key, required this.permitId, this.jobCardId});

  @override
  State<WorkAuthorizationPermitScreen> createState() => _WorkAuthorizationPermitScreenState();
}

class _WorkAuthorizationPermitScreenState extends State<WorkAuthorizationPermitScreen> {
  final _service = JobPermitsService();
  final _formKey = GlobalKey<FormState>();
  bool _loading = true;
  bool _saving = false;
  int _step = 0;
  String? _error;
  Map<String, dynamic> _payload = {};
  /// [permitLabel] strings from API rules (same as PDF / persisted JSON).
  List<String> _derivedPermitLabels = [];
  bool _derivedLabelsRefreshing = false;
  Timer? _deriveDebounce;

  final _waNumber = TextEditingController();
  final _siteName = TextEditingController();
  final _scope = TextEditingController();
  final _contractor = TextEditingController();
  final _location = TextEditingController();
  final _task = TextEditingController();
  final _notes = TextEditingController();
  final _issuingName = TextEditingController();
  final _performingName = TextEditingController();
  final _clientName = TextEditingController();
  final _atexZone = TextEditingController();
  final _workers = TextEditingController();
  final _fuelDeliveryAt = TextEditingController();
  final _interferenceNotes = TextEditingController();
  final _preventionNotes = TextEditingController();
  final _otherRisksNotes = TextEditingController();
  final _withdrawalNotes = TextEditingController();
  final _issueDate = TextEditingController();
  final _validFromDate = TextEditingController();
  final _validToDate = TextEditingController();
  final _validFromTime = TextEditingController();
  final _validToTime = TextEditingController();
  final _preventionPlanRef = TextEditingController();
  final _otherWorkDayDetails = TextEditingController();
  final _gasCylinderDetails = TextEditingController();
  final _otherWorkDayRef = TextEditingController();
  final _nearbyWorkRef = TextEditingController();
  final _activitiesHaltedSpecify = TextEditingController();
  final _manualHandlingTypesAndWeight = TextEditingController();

  bool _hotWork = false;
  bool _excavation = false;
  bool _heights = false;
  bool _lifting = false;
  bool _confined = false;
  bool _degas = false;
  bool _electrical = false;
  bool _electricalHv = false;
  bool _project = false;
  bool _maintenance = true;
  bool _safetyCondition = false;
  bool _otherWorkPlanned = false;
  bool _gasCylinders = false;
  bool _nearbyWork = false;
  bool _personnelInformed = false;
  bool _phonesOff = false;
  bool _closureStation = false;
  bool _ppeRequired = false;
  bool _hardHat = false;
  bool _goggles = false;
  bool _vest = false;
  bool _boots = false;
  bool _gloves = false;
  bool _hotWorkRestrictedClassified = false;
  bool _distStopPartial = false;
  bool _distStopTotal = false;
  bool _hearingProtRequired = false;
  bool _hiVisCoverall = false;
  bool _dustMask = false;
  bool _actHaltedComplete = false;
  bool _actHaltedPartial = false;
  bool _riskFireExplosion = false;
  bool _riskElectricalShock = false;
  bool _riskFallingObjects = false;
  bool _riskHearing = false;
  bool _controlGasTesting = false;
  bool _controlEnergyIsolation = false;
  bool _controlLiftingPermit = false;
  bool _controlHeightsPermit = false;
  bool _controlDegasPermit = false;
  bool _controlHearingProtection = false;
  bool _withdrawn = false;
  bool _withdrawScopeChange = false;
  bool _withdrawRulesViolation = false;
  bool _withdrawAccident = false;
  bool _withdrawAuthorityAbsent = false;

  String? _issuingSigBase64;
  String? _performingSigBase64;
  String? _clientSigBase64;

  final List<Map<String, dynamic>> _revalidations = [];
  final List<Map<String, dynamic>> _handbacks = [];

  static const _natureWorksCommentKeys = <String>[
    'workInExplosionRiskZone',
    'equipmentTools',
    'dangerousMachineryWork',
    'hazardousChemicalProducts',
    'extremeTemperatureWork',
    'pressurisedEquipmentUse',
    'manualHandling',
    'drainingRinsingWork',
    'radiographicTestingWork',
    'noisyWork',
    'otherWork',
  ];

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _waNumber.dispose();
    _siteName.dispose();
    _scope.dispose();
    _contractor.dispose();
    _location.dispose();
    _task.dispose();
    _notes.dispose();
    _issuingName.dispose();
    _performingName.dispose();
    _clientName.dispose();
    _atexZone.dispose();
    _workers.dispose();
    _fuelDeliveryAt.dispose();
    _interferenceNotes.dispose();
    _preventionNotes.dispose();
    _otherRisksNotes.dispose();
    _withdrawalNotes.dispose();
    _issueDate.dispose();
    _validFromDate.dispose();
    _validToDate.dispose();
    _validFromTime.dispose();
    _validToTime.dispose();
    _preventionPlanRef.dispose();
    _otherWorkDayDetails.dispose();
    _gasCylinderDetails.dispose();
    _otherWorkDayRef.dispose();
    _nearbyWorkRef.dispose();
    _activitiesHaltedSpecify.dispose();
    _manualHandlingTypesAndWeight.dispose();
    _deriveDebounce?.cancel();
    super.dispose();
  }

  static String? _dateToApi(String raw) {
    final t = raw.trim();
    if (t.isEmpty) return null;
    if (RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(t)) return '${t}T00:00:00.000Z';
    return t;
  }

  static String? _fmtDate(dynamic v) {
    if (v == null) return null;
    final s = v.toString();
    if (s.length >= 10) return s.substring(0, 10);
    return s;
  }

  static String? _fmtTime(dynamic v) {
    if (v == null) return null;
    final s = v.toString();
    if (s.contains('.')) return s.split('.').first;
    return s;
  }

  static String? _timeToApi(String raw) {
    final t = raw.trim();
    if (t.isEmpty) return null;
    final m = RegExp(r'^(\d{1,2}):(\d{2})(?::(\d{2}))?$').firstMatch(t);
    if (m != null) {
      final h = int.parse(m.group(1)!);
      final min = int.parse(m.group(2)!);
      final sec = m.group(3) != null ? int.parse(m.group(3)!) : 0;
      return '${h.toString().padLeft(2, '0')}:${min.toString().padLeft(2, '0')}:${sec.toString().padLeft(2, '0')}';
    }
    return t;
  }

  List<String> _labelsFromDerivedPayload(Map<String, dynamic> p) {
    final raw = p['derivedRequiredPermits'];
    if (raw is! List) return [];
    final labels = <String>[];
    for (final e in raw) {
      if (e is! Map) continue;
      final m = Map<String, dynamic>.from(e);
      if (m['isRequired'] != true) continue;
      final label = m['permitLabel']?.toString().trim();
      if (label != null && label.isNotEmpty) labels.add(label);
    }
    labels.sort();
    return labels;
  }

  void _scheduleDerivedLabelsRefresh() {
    _deriveDebounce?.cancel();
    _deriveDebounce = Timer(const Duration(milliseconds: 400), () {
      if (!mounted) return;
      _refreshDerivedLabelsFromApi();
    });
  }

  Future<void> _refreshDerivedLabelsFromApi() async {
    if (!mounted) return;
    setState(() => _derivedLabelsRefreshing = true);
    try {
      final labels = await _service.deriveRequiredPermitLabels(_buildPayload());
      if (!mounted) return;
      setState(() {
        _derivedPermitLabels = labels;
        _derivedLabelsRefreshing = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() => _derivedLabelsRefreshing = false);
    }
  }

  void _derivedAwareSetState(VoidCallback fn) {
    setState(fn);
    _scheduleDerivedLabelsRefresh();
  }

  void _maybeScheduleDerivedRefreshForSection(String sectionKey) {
    if (sectionKey == 'natureOfWorks' || sectionKey == 'preventionMeasures') {
      _scheduleDerivedLabelsRefresh();
    }
  }

  bool _anyNatureOfWorkSelected() {
    if (_hotWork ||
        _excavation ||
        _heights ||
        _lifting ||
        _confined ||
        _degas ||
        _electrical ||
        _electricalHv) {
      return true;
    }
    final now = (_payload['natureOfWorks'] as Map?)?.cast<String, dynamic>();
    if (now == null) return false;
    if (now['electricalWorkHv'] == true) return true;
    for (final key in ['movementCirculationOptions', 'hotWorkOptions', 'liftingOptions', 'workAtHeightsOptions']) {
      final list = now[key];
      if (list is List && list.any((e) => e is Map && e['isSelected'] == true)) return true;
    }
    for (final key in _natureWorksCommentKeys) {
      final o = now[key];
      if (o is Map && o['isSelected'] == true) return true;
    }
    return false;
  }

  String _prettyListLabel(String key) {
    const labels = {
      'trafficAndMovementRisks': 'Traffic & movement',
      'fireExplosionRisks': 'Fire / explosion',
      'mechanicalRisks': 'Mechanical',
      'chemicalThermalRisks': 'Chemical / thermal',
      'manualHandlingRisks': 'Manual handling',
      'excavationRisks': 'Excavation',
      'electricalRisks': 'Electrical',
      'confinedSpaceRisks': 'Confined space',
      'overheadAndDroppedObjectRisks': 'Overhead / dropped objects',
      'heightRisks': 'Work at height',
      'radiographicRisks': 'Radiographic',
      'noiseRisks': 'Noise',
      'movementCirculationOptions': 'Movement / circulation',
      'hotWorkOptions': 'Hot work (detail)',
      'liftingOptions': 'Lifting (detail)',
      'workAtHeightsOptions': 'Work at heights (detail)',
      'trafficAndOperationalControls': 'Traffic & operations',
      'explosionAndAtexControls': 'Explosion / ATEX',
      'mechanicalControls': 'Mechanical controls',
      'chemicalAndPpeControls': 'Chemical & PPE',
      'pressureAndManualHandlingControls': 'Pressure & manual handling',
      'excavationControls': 'Excavation controls',
      'electricalControls': 'Electrical controls',
      'confinedSpaceControls': 'Confined space controls',
      'liftingControls': 'Lifting controls',
      'workingAtHeightsControls': 'Working at heights controls',
      'cleaningDegassingControls': 'Cleaning / degassing',
      'radiographicControls': 'Radiographic controls',
      'noiseControls': 'Noise controls',
    };
    return labels[key] ?? key.replaceAllMapped(RegExp(r'([A-Z])'), (m) => ' ${m[1]}').trim();
  }

  Widget _buildToggleListEditor(String sectionKey, String listKey, String? title) {
    final section = _payload[sectionKey];
    if (section is! Map<String, dynamic>) return const SizedBox.shrink();
    final raw = section[listKey];
    if (raw is! List) return const SizedBox.shrink();
    final children = <Widget>[];
    if (title != null && title.isNotEmpty) {
      children.add(Padding(
        padding: const EdgeInsets.only(bottom: 4, top: 4),
        child: Text(title, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
      ));
    }
    for (var i = 0; i < raw.length; i++) {
      final item = raw[i];
      if (item is! Map) continue;
      final m = Map<String, dynamic>.from(item);
      final label = m['label']?.toString() ?? m['key']?.toString() ?? '';
      final idx = i;
      children.add(CheckboxListTile(
        dense: true,
        value: m['isSelected'] == true,
        onChanged: (v) {
          setState(() {
            m['isSelected'] = v ?? false;
            raw[idx] = m;
            section[listKey] = raw;
          });
          _maybeScheduleDerivedRefreshForSection(sectionKey);
        },
        title: Text(label, style: const TextStyle(fontSize: 13)),
        controlAffinity: ListTileControlAffinity.leading,
      ));
    }
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: children);
  }

  Widget _buildToggleWithCommentTile(String sectionKey, String fieldKey) {
    final section = _payload[sectionKey];
    if (section is! Map<String, dynamic>) return const SizedBox.shrink();
    final raw = section[fieldKey];
    if (raw is! Map) return const SizedBox.shrink();
    final m = Map<String, dynamic>.from(raw);
    final label = m['label']?.toString() ?? fieldKey;
    final comment = m['comment']?.toString() ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SwitchListTile(
          dense: true,
          value: m['isSelected'] == true,
          onChanged: (v) {
            setState(() {
              m['isSelected'] = v;
              section[fieldKey] = m;
            });
            _maybeScheduleDerivedRefreshForSection(sectionKey);
          },
          title: Text(label, style: const TextStyle(fontSize: 13)),
        ),
        Padding(
          padding: const EdgeInsets.only(left: 16, right: 16, bottom: 8),
          child: TextFormField(
            key: ValueKey('$fieldKey-$comment'),
            initialValue: comment,
            decoration: const InputDecoration(
              labelText: 'Comment (optional)',
              border: OutlineInputBorder(),
              isDense: true,
            ),
            maxLines: 2,
            onChanged: (v) {
              m['comment'] = v;
              section[fieldKey] = m;
            },
          ),
        ),
      ],
    );
  }

  Widget _buildDynamicListSections(String sectionKey) {
    final sec = _payload[sectionKey];
    if (sec is! Map<String, dynamic>) return const SizedBox.shrink();
    final keys = sec.keys.where((k) {
      if (k == 'otherRisksNotes' ||
          k == 'otherPreventionMeasuresNotes' ||
          k == 'activitiesHaltedSpecify' ||
          k == 'manualHandlingTypesAndWeight') {
        return false;
      }
      return sec[k] is List;
    }).toList()
      ..sort();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: keys.map((k) {
        return ExpansionTile(
          title: Text(_prettyListLabel(k), style: const TextStyle(fontSize: 14)),
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: _buildToggleListEditor(sectionKey, k, null),
            ),
          ],
        );
      }).toList(),
    );
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final p = await _service.getMasterPermit(widget.permitId);
      _payload = p;
      _waNumber.text = p['workAuthorizationNumber']?.toString() ?? '';
      final header = (p['header'] as Map?)?.cast<String, dynamic>() ?? {};
      final locationTask = (p['locationTask'] as Map?)?.cast<String, dynamic>() ?? {};
      final natureOfWorks = (p['natureOfWorks'] as Map?)?.cast<String, dynamic>() ?? {};
      final declaration = (p['declaration'] as Map?)?.cast<String, dynamic>() ?? {};

      _siteName.text = header['siteName']?.toString() ?? '';
      _scope.text = header['scopeOfWorks']?.toString() ?? '';
      _atexZone.text = header['atexZone']?.toString() ?? '';
      _workers.text = header['numberOfWorkers']?.toString() ?? '';
      _issueDate.text = _fmtDate(header['issueDate']) ?? '';
      _validFromDate.text = _fmtDate(header['validFromDate']) ?? '';
      _validToDate.text = _fmtDate(header['validToDate']) ?? '';
      _validFromTime.text = _fmtTime(header['validFromTime']) ?? '';
      _validToTime.text = _fmtTime(header['validToTime']) ?? '';
      _contractor.text = locationTask['contractorCompany']?.toString() ?? '';
      _location.text = locationTask['locationOfOperation']?.toString() ?? '';
      _task.text = locationTask['taskDescription']?.toString() ?? '';
      _notes.text = p['notes']?.toString() ?? '';
      final associated = (p['associatedPermits'] as Map?)?.cast<String, dynamic>() ?? {};
      _project = associated['isProject'] == true;
      _maintenance = associated['isMaintenance'] == true;
      _safetyCondition = associated['safetyConditionCompleted'] == true;
      _preventionPlanRef.text = associated['preventionPlanReferenceNumber']?.toString() ?? '';
      final interference = (p['interference'] as Map?)?.cast<String, dynamic>() ?? {};
      _fuelDeliveryAt.text = interference['fuelDeliveryReceiptScheduledAt']?.toString() ?? '';
      _otherWorkPlanned = interference['hasOtherWorkPlannedForDay'] == true;
      _gasCylinders = interference['hasPresenceOfGasCylindersOrBarrels'] == true;
      _nearbyWork = interference['hasOtherNearbyWorkPlanned'] == true;
      _interferenceNotes.text = interference['otherNearbyWorkPlannedDetails']?.toString() ?? '';
      _otherWorkDayDetails.text = interference['otherWorkPlannedForDayDetails']?.toString() ?? '';
      _gasCylinderDetails.text = interference['presenceOfGasCylindersOrBarrelsDetails']?.toString() ?? '';
      _otherWorkDayRef.text = interference['otherWorkPlannedForDayReferenceNumber']?.toString() ?? '';
      _nearbyWorkRef.text = interference['otherNearbyWorkReferenceNumber']?.toString() ?? '';
      final safety = (p['compulsorySafetyMeasures'] as Map?)?.cast<String, dynamic>() ?? {};
      _personnelInformed = safety['personnelInformed'] == true;
      _phonesOff = safety['mobilePhonesCamerasEtcSwitchedOff'] == true;
      _closureStation = safety['closureOfStation'] == true;
      _ppeRequired = safety['protectiveClothingRequired'] == true;
      _hardHat = safety['hardHatRequired'] == true;
      _goggles = safety['gogglesOrFaceShieldRequired'] == true;
      _vest = safety['visibilityVestRequired'] == true;
      _boots = safety['steelToeCapShoesRequired'] == true;
      _gloves = safety['glovesRequired'] == true;
      _hotWorkRestrictedClassified = safety['hotWorkRestrictedInClassifiedAreas'] == true;
      _distStopPartial = safety['distributionStoppagePartial'] == true;
      _distStopTotal = safety['distributionStoppageTotal'] == true;
      _hearingProtRequired = safety['hearingProtectionRequired'] == true;
      _hiVisCoverall = safety['highVisibilityCoverallRequired'] == true;
      _dustMask = safety['dustMaskRequired'] == true;

      _hotWork = ((natureOfWorks['hotWorkOptions'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _excavation = (natureOfWorks['excavationWork'] as Map?)?['isSelected'] == true;
      _heights = ((natureOfWorks['workAtHeightsOptions'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _lifting = ((natureOfWorks['liftingOptions'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _confined = (natureOfWorks['confinedAtmosphereWork'] as Map?)?['isSelected'] == true;
      _degas = (natureOfWorks['cleaningDegassingWork'] as Map?)?['isSelected'] == true;
      _electrical = (natureOfWorks['electricalWork'] as Map?)?['isSelected'] == true;
      _electricalHv = natureOfWorks['electricalWorkHv'] == true;
      _manualHandlingTypesAndWeight.text = natureOfWorks['manualHandlingTypesAndWeight']?.toString() ?? '';
      final risks = (p['natureOfRisks'] as Map?)?.cast<String, dynamic>() ?? {};
      _riskFireExplosion = ((risks['fireExplosionRisks'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _riskElectricalShock = ((risks['electricalRisks'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _riskFallingObjects = ((risks['overheadAndDroppedObjectRisks'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _riskHearing = ((risks['noiseRisks'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _otherRisksNotes.text = risks['otherRisksNotes']?.toString() ?? '';
      final prevention = (p['preventionMeasures'] as Map?)?.cast<String, dynamic>() ?? {};
      _controlGasTesting = ((prevention['explosionAndAtexControls'] as List?) ?? []).any((e) {
        final m = (e as Map);
        final k = m['key']?.toString() ?? '';
        return (k == 'gas_testing_lel_o2' || k == 'gas_testing') && m['isSelected'] == true;
      });
      _controlEnergyIsolation = ((prevention['electricalControls'] as List?) ?? []).any((e) {
        final m = (e as Map);
        return m['key'].toString().contains('energy') && m['isSelected'] == true;
      });
      _controlLiftingPermit = ((prevention['liftingControls'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _controlHeightsPermit = ((prevention['workingAtHeightsControls'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _controlDegasPermit = ((prevention['cleaningDegassingControls'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _controlHearingProtection = ((prevention['noiseControls'] as List?) ?? []).any((e) => (e as Map)['isSelected'] == true);
      _actHaltedComplete = prevention['activitiesHaltedCompletely'] == true;
      _actHaltedPartial = prevention['activitiesHaltedPartially'] == true;
      _activitiesHaltedSpecify.text = prevention['activitiesHaltedSpecify']?.toString() ?? '';
      _preventionNotes.text = prevention['otherPreventionMeasuresNotes']?.toString() ?? '';
      final withdrawal = (p['withdrawal'] as Map?)?.cast<String, dynamic>() ?? {};
      _withdrawn = withdrawal['isWithdrawn'] == true;
      _withdrawScopeChange = withdrawal['scopeOfWorkChanges'] == true;
      _withdrawRulesViolation = withdrawal['permitRulesViolation'] == true;
      _withdrawAccident = withdrawal['accidentOccurrence'] == true;
      _withdrawAuthorityAbsent = withdrawal['issuingOrPerformingAuthorityNotOnSite'] == true;
      _withdrawalNotes.text = withdrawal['notes']?.toString() ?? '';
      _revalidations
        ..clear()
        ..addAll(((p['revalidations'] as List?) ?? []).cast<Map>().map((e) => Map<String, dynamic>.from(e)));
      _handbacks
        ..clear()
        ..addAll(((p['handBackEntries'] as List?) ?? []).cast<Map>().map((e) => Map<String, dynamic>.from(e)));

      final issuing = (declaration['issuingAuthority'] as Map?)?.cast<String, dynamic>() ?? {};
      final performing = (declaration['performingAuthority'] as Map?)?.cast<String, dynamic>() ?? {};
      final client = (declaration['siteAcknowledgement'] as Map?)?.cast<String, dynamic>() ?? {};
      _issuingName.text = issuing['name']?.toString() ?? '';
      _performingName.text = performing['name']?.toString() ?? '';
      _clientName.text = client['name']?.toString() ?? '';
      _issuingSigBase64 = issuing['signatureImageBase64']?.toString();
      _performingSigBase64 = performing['signatureImageBase64']?.toString();
      _clientSigBase64 = client['signatureImageBase64']?.toString();
      _derivedPermitLabels = _labelsFromDerivedPayload(p);
      await _prefillFromJobIfNeeded();
    } catch (e) {
      _error = e.toString();
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _prefillFromJobIfNeeded() async {
    final id = widget.jobCardId;
    if (id == null || id.isEmpty) return;
    try {
      final job = await JobCardsService().getJobDetail(id);
      if (!mounted) return;
      setState(() {
        if (_siteName.text.trim().isEmpty) {
          final site = job.siteName?.trim() ?? '';
          final addr = job.siteAddress?.trim() ?? '';
          if (site.isNotEmpty && addr.isNotEmpty) {
            _siteName.text = '$site — $addr';
          } else if (site.isNotEmpty) {
            _siteName.text = site;
          } else if (addr.isNotEmpty) {
            _siteName.text = addr;
          }
        }
        if (_location.text.trim().isEmpty && job.siteAddress != null && job.siteAddress!.trim().isNotEmpty) {
          _location.text = job.siteAddress!.trim();
        }
        if (_scope.text.trim().isEmpty) {
          final d = job.description?.trim();
          final sr = job.serviceRequestDescription?.trim();
          final parts = <String>[];
          if (d != null && d.isNotEmpty) parts.add(d);
          if (sr != null && sr.isNotEmpty && sr != d) parts.add(sr);
          if (parts.isNotEmpty) _scope.text = parts.join('\n\n');
        }
        if (_task.text.trim().isEmpty) {
          final d = job.description?.trim();
          if (d != null && d.isNotEmpty) _task.text = d;
        }
        if (_workers.text.trim().isEmpty && job.assignments.isNotEmpty) {
          _workers.text = '${job.assignments.length}';
        }
      });
    } catch (_) {}
  }

  Future<void> _pickDate(TextEditingController c) async {
    DateTime initial = DateTime.now();
    final raw = c.text.trim();
    if (raw.length >= 10) {
      initial = DateTime.tryParse(raw.substring(0, 10)) ?? initial;
    }
    final d = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(initial.year - 1),
      lastDate: DateTime(initial.year + 2),
    );
    if (d != null && mounted) {
      c.text =
          '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
      setState(() {});
    }
  }

  Future<void> _pickTime(TextEditingController c) async {
    var initial = TimeOfDay.now();
    final raw = c.text.trim();
    final m = RegExp(r'^(\d{1,2}):(\d{2})').firstMatch(raw);
    if (m != null) {
      final h = int.tryParse(m.group(1)!) ?? 0;
      final min = int.tryParse(m.group(2)!) ?? 0;
      initial = TimeOfDay(hour: h.clamp(0, 23), minute: min.clamp(0, 59));
    }
    final t = await showTimePicker(context: context, initialTime: initial);
    if (t != null && mounted) {
      c.text =
          '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
      setState(() {});
    }
  }

  String? _validateRequiredDate(String label, String? v) {
    if (v == null || v.trim().isEmpty) return '$label is required';
    if (!RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(v.trim())) return 'Pick a valid date';
    return null;
  }

  String? _validateRequiredTime(String label, String? v) {
    if (v == null || v.trim().isEmpty) return '$label is required';
    if (!RegExp(r'^\d{1,2}:\d{2}').hasMatch(v.trim())) return 'Pick a time (HH:mm)';
    return null;
  }

  String? _validateScope(String? v) => (v == null || v.trim().isEmpty) ? 'Scope of works is required' : null;

  String? _validateWorkers(String? v) {
    if (v == null || v.trim().isEmpty) return 'Number of workers is required';
    final n = int.tryParse(v.trim());
    if (n == null || n < 1) return 'Enter a positive whole number';
    return null;
  }

  /// Deep clone JSON-like maps/lists so we can patch toggle rows without mutating [_payload].
  dynamic _deepCloneJson(dynamic value) {
    if (value == null) return null;
    if (value is Map) {
      return value.map((k, v) => MapEntry(k.toString(), _deepCloneJson(v)));
    }
    if (value is List) {
      return value.map(_deepCloneJson).toList();
    }
    return value;
  }

  bool _anySelectedInList(Map<String, dynamic> section, String listKey) {
    final raw = section[listKey];
    if (raw is! List) return false;
    return raw.any((e) => e is Map && e['isSelected'] == true);
  }

  /// Applies [coarseOn] to known keys; if [coarseOn] is false but the list already has selections (from detailed UI), leaves the list unchanged.
  void _applyCoarseToggleList(
    Map<String, dynamic> section,
    String listKey,
    List<String> keys,
    bool coarseOn,
  ) {
    if (coarseOn) {
      _applyBoolToToggleList(section, listKey, keys, true);
    } else if (!_anySelectedInList(section, listKey)) {
      _applyBoolToToggleList(section, listKey, keys, false);
    }
  }

  void _applyBoolToToggleList(Map<String, dynamic> section, String listKey, List<String> keys, bool value) {
    final raw = section[listKey];
    if (raw is! List) return;
    final keySet = keys.map((k) => k.toLowerCase()).toSet();
    for (var i = 0; i < raw.length; i++) {
      final item = raw[i];
      if (item is! Map) continue;
      final m = Map<String, dynamic>.from(item.cast<String, dynamic>());
      final k = (m['key'] ?? '').toString().toLowerCase();
      if (keySet.contains(k)) m['isSelected'] = value;
      raw[i] = m;
    }
  }

  void _patchToggleWithComment(Map<String, dynamic> section, String fieldKey, bool selected) {
    final raw = section[fieldKey];
    if (raw is Map) {
      final m = Map<String, dynamic>.from(raw.cast<String, dynamic>());
      m['isSelected'] = selected;
      section[fieldKey] = m;
    }
  }

  Map<String, dynamic> _buildNatureOfWorksForSave() {
    final base = _deepCloneJson(_payload['natureOfWorks']) as Map<String, dynamic>? ?? <String, dynamic>{};
    _applyCoarseToggleList(base, 'hotWorkOptions', const ['grinding', 'drilling', 'welding', 'brazing', 'cutting'], _hotWork);
    _applyCoarseToggleList(base, 'liftingOptions',
        const ['lifting_beam_elevator', 'crane_truck', 'bridge_crane', 'warehousing_carrier', 'winches'], _lifting);
    _applyCoarseToggleList(base, 'workAtHeightsOptions',
        const ['ladder_stepladder', 'scaffolding', 'rope_access', 'mewp', 'canopies_roofs'], _heights);
    _patchToggleWithComment(base, 'excavationWork', _excavation);
    _patchToggleWithComment(base, 'confinedAtmosphereWork', _confined);
    _patchToggleWithComment(base, 'cleaningDegassingWork', _degas);
    _patchToggleWithComment(base, 'electricalWork', _electrical);
    base['electricalWorkLv'] = _electrical && !_electricalHv;
    base['electricalWorkHv'] = _electrical && _electricalHv;
    base['manualHandlingTypesAndWeight'] = _manualHandlingTypesAndWeight.text.trim();
    return base;
  }

  Map<String, dynamic> _buildNatureOfRisksForSave() {
    final base = _deepCloneJson(_payload['natureOfRisks']) as Map<String, dynamic>? ?? <String, dynamic>{};
    _applyCoarseToggleList(base, 'fireExplosionRisks',
        const ['start_fire_explosion', 'flammable_materials', 'fire_explosion'], _riskFireExplosion);
    _applyCoarseToggleList(base, 'electricalRisks',
        const ['electrical_shock', 'electrocution', 'arcing', 'burn', 'flash_fire'], _riskElectricalShock);
    _applyCoarseToggleList(base, 'overheadAndDroppedObjectRisks',
        const ['damage_facilities', 'overhead_cables', 'falling_material', 'falling_objects'], _riskFallingObjects);
    _applyCoarseToggleList(base, 'noiseRisks', const ['noise_hearing_loss', 'hearing_loss'], _riskHearing);
    base['otherRisksNotes'] = _otherRisksNotes.text.trim();
    return base;
  }

  Map<String, dynamic> _buildPreventionMeasuresForSave() {
    final base = _deepCloneJson(_payload['preventionMeasures']) as Map<String, dynamic>? ?? <String, dynamic>{};
    _applyCoarseToggleList(base, 'explosionAndAtexControls', const ['gas_testing_lel_o2', 'gas_testing'], _controlGasTesting);
    _applyCoarseToggleList(base, 'electricalControls', const ['energy_isolation_permit', 'energy_isolation_certificate'], _controlEnergyIsolation);
    _applyCoarseToggleList(base, 'liftingControls', const ['lifting_permit'], _controlLiftingPermit);
    _applyCoarseToggleList(base, 'workingAtHeightsControls', const ['heights_permit'], _controlHeightsPermit);
    _applyCoarseToggleList(
        base, 'cleaningDegassingControls', const ['cleaning_degassing_permit', 'degas_permit'], _controlDegasPermit);
    _applyCoarseToggleList(base, 'noiseControls', const ['hearing_protection'], _controlHearingProtection);
    base['activitiesHaltedCompletely'] = _actHaltedComplete;
    base['activitiesHaltedPartially'] = _actHaltedPartial;
    base['activitiesHaltedSpecify'] = _activitiesHaltedSpecify.text.trim();
    base['otherPreventionMeasuresNotes'] = _preventionNotes.text.trim();
    return base;
  }

  Map<String, dynamic> _buildPayload() {
    final nowIso = DateTime.now().toUtc().toIso8601String();
    final declaration = _deepCloneJson(_payload['declaration']) as Map<String, dynamic>? ?? <String, dynamic>{};
    final issuing = Map<String, dynamic>.from((declaration['issuingAuthority'] as Map?)?.cast<String, dynamic>() ?? {});
    final performing = Map<String, dynamic>.from((declaration['performingAuthority'] as Map?)?.cast<String, dynamic>() ?? {});
    final client = Map<String, dynamic>.from((declaration['siteAcknowledgement'] as Map?)?.cast<String, dynamic>() ?? {});

    return {
      ..._payload,
      'permitGuid': _payload['permitGuid'] ?? widget.permitId,
      'workAuthorizationNumber': _waNumber.text.trim(),
      'header': {
        ...((_payload['header'] as Map?)?.cast<String, dynamic>() ?? {}),
        'issueDate': _dateToApi(_issueDate.text),
        'validFromDate': _dateToApi(_validFromDate.text),
        'validToDate': _dateToApi(_validToDate.text),
        'validFromTime': _timeToApi(_validFromTime.text),
        'validToTime': _timeToApi(_validToTime.text),
        'siteName': _siteName.text.trim(),
        'scopeOfWorks': _scope.text.trim(),
        'atexZone': _atexZone.text.trim(),
        'numberOfWorkers': int.tryParse(_workers.text.trim()),
      },
      'locationTask': {
        ...((_payload['locationTask'] as Map?)?.cast<String, dynamic>() ?? {}),
        'contractorCompany': _contractor.text.trim(),
        'locationOfOperation': _location.text.trim(),
        'taskDescription': _task.text.trim(),
      },
      'natureOfWorks': _buildNatureOfWorksForSave(),
      'associatedPermits': {
        ...((_payload['associatedPermits'] as Map?)?.cast<String, dynamic>() ?? {}),
        'isProject': _project,
        'isMaintenance': _maintenance,
        'preventionPlanReferenceNumber': _preventionPlanRef.text.trim(),
        'safetyConditionCompleted': _safetyCondition
      },
      'interference': {
        ...((_payload['interference'] as Map?)?.cast<String, dynamic>() ?? {}),
        'fuelDeliveryReceiptScheduledAt': _fuelDeliveryAt.text.trim(),
        'hasOtherWorkPlannedForDay': _otherWorkPlanned,
        'otherWorkPlannedForDayDetails': _otherWorkDayDetails.text.trim(),
        'otherWorkPlannedForDayReferenceNumber': _otherWorkDayRef.text.trim(),
        'hasPresenceOfGasCylindersOrBarrels': _gasCylinders,
        'presenceOfGasCylindersOrBarrelsDetails': _gasCylinderDetails.text.trim(),
        'hasOtherNearbyWorkPlanned': _nearbyWork,
        'otherNearbyWorkPlannedDetails': _interferenceNotes.text.trim(),
        'otherNearbyWorkReferenceNumber': _nearbyWorkRef.text.trim()
      },
      'compulsorySafetyMeasures': {
        ...((_payload['compulsorySafetyMeasures'] as Map?)?.cast<String, dynamic>() ?? {}),
        'personnelInformed': _personnelInformed,
        'mobilePhonesCamerasEtcSwitchedOff': _phonesOff,
        'closureOfStation': _closureStation,
        'hotWorkRestrictedInClassifiedAreas': _hotWorkRestrictedClassified,
        'distributionStoppagePartial': _distStopPartial,
        'distributionStoppageTotal': _distStopTotal,
        'protectiveClothingRequired': _ppeRequired,
        'hearingProtectionRequired': _hearingProtRequired,
        'hardHatRequired': _hardHat,
        'gogglesOrFaceShieldRequired': _goggles,
        'visibilityVestRequired': _vest,
        'steelToeCapShoesRequired': _boots,
        'highVisibilityCoverallRequired': _hiVisCoverall,
        'dustMaskRequired': _dustMask,
        'glovesRequired': _gloves
      },
      'natureOfRisks': _buildNatureOfRisksForSave(),
      'preventionMeasures': _buildPreventionMeasuresForSave(),
      'declaration': {
        ...declaration,
        'issuingAuthority': {
          ...issuing,
          'name': _issuingName.text.trim(),
          'signedDateTime': _issuingSigBase64 != null ? nowIso : issuing['signedDateTime'],
          'signatureImageBase64': _issuingSigBase64 ?? issuing['signatureImageBase64'] ?? '',
          'signatureImageUrl': issuing['signatureImageUrl'] ?? ''
        },
        'performingAuthority': {
          ...performing,
          'name': _performingName.text.trim(),
          'signedDateTime': _performingSigBase64 != null ? nowIso : performing['signedDateTime'],
          'signatureImageBase64': _performingSigBase64 ?? performing['signatureImageBase64'] ?? '',
          'signatureImageUrl': performing['signatureImageUrl'] ?? ''
        },
        'siteAcknowledgement': {
          ...client,
          'name': _clientName.text.trim(),
          'signedDateTime': _clientSigBase64 != null ? nowIso : client['signedDateTime'],
          'signatureImageBase64': _clientSigBase64 ?? client['signatureImageBase64'] ?? '',
          'signatureImageUrl': client['signatureImageUrl'] ?? ''
        }
      },
      'revalidations': _revalidations,
      'handBackEntries': _handbacks,
      'withdrawal': {
        ...((_payload['withdrawal'] as Map?)?.cast<String, dynamic>() ?? {}),
        'isWithdrawn': _withdrawn,
        'scopeOfWorkChanges': _withdrawScopeChange,
        'permitRulesViolation': _withdrawRulesViolation,
        'accidentOccurrence': _withdrawAccident,
        'issuingOrPerformingAuthorityNotOnSite': _withdrawAuthorityAbsent,
        'notes': _withdrawalNotes.text.trim()
      },
      'notes': _notes.text.trim(),
      'modifiedDateUtc': nowIso
    };
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _saving = true);
    try {
      final payload = _buildPayload();
      await _service.saveMasterPermit(widget.permitId, payload);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Master permit saved')));
      Navigator.of(context).pop(true);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _captureSig(String who) async {
    final bytes = await showSignatureCaptureDialog(context);
    if (bytes == null || bytes.isEmpty) return;
    final b64 = base64Encode(bytes);
    setState(() {
      if (who == 'issuing') _issuingSigBase64 = b64;
      if (who == 'performing') _performingSigBase64 = b64;
      if (who == 'client') _clientSigBase64 = b64;
    });
  }

  bool _validateCurrentStep() {
    String? msg;
    switch (_step) {
      case 0:
        if (_siteName.text.trim().isEmpty) {
          msg = 'Site name is required.';
        } else if (_scope.text.trim().isEmpty) {
          msg = 'Scope of works is required.';
        } else if (_issueDate.text.trim().isEmpty) {
          msg = 'Issue date is required.';
        } else if (_validFromDate.text.trim().isEmpty || _validToDate.text.trim().isEmpty) {
          msg = 'Valid from and valid to dates are required.';
        } else if (_validFromTime.text.trim().isEmpty || _validToTime.text.trim().isEmpty) {
          msg = 'Valid from and valid to times are required.';
        } else {
          final n = int.tryParse(_workers.text.trim());
          if (_workers.text.trim().isEmpty || n == null || n < 1) {
            msg = 'Enter a valid number of workers (at least 1).';
          }
        }
        break;
      case 1:
        if (_task.text.trim().isEmpty) msg = 'Task description is required.';
        break;
      case 5:
        if (!_anyNatureOfWorkSelected()) msg = 'Select at least one nature-of-work item.';
        break;
      case 8:
        if (_issuingName.text.trim().isEmpty || _performingName.text.trim().isEmpty) {
          msg = 'Issuing and performing authority names are required.';
        }
        break;
      case 11:
        if (_withdrawn && _withdrawalNotes.text.trim().isEmpty) {
          msg = 'Please add withdrawal notes when permit is withdrawn.';
        }
        break;
    }
    if (msg != null) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
      return false;
    }
    return true;
  }

  void _addRevalidation() {
    setState(() {
      _revalidations.add({
        'date': DateTime.now().toUtc().toIso8601String(),
        'timeFrom': '08:00:00',
        'timeTo': '17:00:00',
        'issuingAuthorityName': _issuingName.text.trim(),
        'performingAuthorityName': _performingName.text.trim()
      });
    });
  }

  void _addHandback() {
    setState(() {
      _handbacks.add({
        'date': DateTime.now().toUtc().toIso8601String(),
        'worksCompleted': true,
        'issuingAuthorityName': _issuingName.text.trim(),
        'performingAuthorityName': _performingName.text.trim()
      });
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    if (_error != null) return Scaffold(appBar: AppBar(title: const Text('Work Authorisation')), body: Center(child: Text(_error!)));
    return Scaffold(
      appBar: AppBar(
        title: const Text('Work Authorisation (Master Permit)'),
        actions: [TextButton(onPressed: _saving ? null : _save, child: Text(_saving ? 'Saving...' : 'Save', style: const TextStyle(color: Colors.white)))],
      ),
      body: Form(
        key: _formKey,
        child: Column(
          children: [
            Container(
              width: double.infinity,
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
              color: Colors.blueGrey.shade50,
              child: Wrap(
                spacing: 8,
                runSpacing: 6,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  if (_derivedLabelsRefreshing)
                    const Padding(
                      padding: EdgeInsets.only(right: 4),
                      child: SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    ),
                  if (!_derivedLabelsRefreshing && _derivedPermitLabels.isEmpty) const Text('No work permits indicated yet'),
                  ..._derivedPermitLabels.map(
                    (e) => Chip(
                      label: Text(e, style: const TextStyle(fontSize: 12)),
                      visualDensity: VisualDensity.compact,
                    ),
                  ),
                ],
              ),
            ),
            Expanded(
              child: Stepper(
                currentStep: _step,
                onStepContinue: () {
                  if (!_validateCurrentStep()) return;
                  if (_step < 11) {
                    setState(() => _step++);
                    _scheduleDerivedLabelsRefresh();
                  } else {
                    _save();
                  }
                },
                onStepCancel: () {
                  if (_step > 0) {
                    setState(() => _step--);
                  }
                },
                onStepTapped: (i) => setState(() => _step = i),
                controlsBuilder: (context, details) => Row(
                  children: [
                    TradionButtons.primary(
                      onPressed: details.onStepContinue,
                      child: Text(_step == 11 ? 'Save' : 'Next'),
                    ),
                    const SizedBox(width: 8),
                    TextButton(onPressed: details.onStepCancel, child: const Text('Back'))
                  ],
                ),
                steps: [
            Step(
              title: const Text('Header'),
              isActive: _step >= 0,
              content: Column(children: [
                TextFormField(
                  controller: _waNumber,
                  decoration: const InputDecoration(
                    labelText: 'Work authorisation number',
                    border: OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _siteName,
                  decoration: const InputDecoration(
                    labelText: 'Site name *',
                    border: OutlineInputBorder(),
                  ),
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Site name is required' : null,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _scope,
                  decoration: const InputDecoration(
                    labelText: 'Scope of works *',
                    border: OutlineInputBorder(),
                  ),
                  maxLines: 3,
                  validator: _validateScope,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _atexZone,
                  decoration: const InputDecoration(
                    labelText: 'ATEX zone',
                    border: OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _workers,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  decoration: const InputDecoration(
                    labelText: 'Number of workers *',
                    border: OutlineInputBorder(),
                  ),
                  validator: _validateWorkers,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _issueDate,
                  readOnly: true,
                  decoration: InputDecoration(
                    labelText: 'Issue date *',
                    border: const OutlineInputBorder(),
                    suffixIcon: IconButton(
                      icon: const Icon(Icons.calendar_today),
                      onPressed: () => _pickDate(_issueDate),
                    ),
                  ),
                  onTap: () => _pickDate(_issueDate),
                  validator: (v) => _validateRequiredDate('Issue date', v),
                ),
                const SizedBox(height: 8),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: TextFormField(
                        controller: _validFromDate,
                        readOnly: true,
                        decoration: InputDecoration(
                          labelText: 'Valid from date *',
                          border: const OutlineInputBorder(),
                          suffixIcon: IconButton(
                            icon: const Icon(Icons.calendar_today),
                            onPressed: () => _pickDate(_validFromDate),
                          ),
                        ),
                        onTap: () => _pickDate(_validFromDate),
                        validator: (v) => _validateRequiredDate('Valid from date', v),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextFormField(
                        controller: _validFromTime,
                        readOnly: true,
                        decoration: InputDecoration(
                          labelText: 'From time *',
                          border: const OutlineInputBorder(),
                          suffixIcon: IconButton(
                            icon: const Icon(Icons.access_time),
                            onPressed: () => _pickTime(_validFromTime),
                          ),
                        ),
                        onTap: () => _pickTime(_validFromTime),
                        validator: (v) => _validateRequiredTime('Valid from time', v),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: TextFormField(
                        controller: _validToDate,
                        readOnly: true,
                        decoration: InputDecoration(
                          labelText: 'Valid to date *',
                          border: const OutlineInputBorder(),
                          suffixIcon: IconButton(
                            icon: const Icon(Icons.calendar_today),
                            onPressed: () => _pickDate(_validToDate),
                          ),
                        ),
                        onTap: () => _pickDate(_validToDate),
                        validator: (v) => _validateRequiredDate('Valid to date', v),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextFormField(
                        controller: _validToTime,
                        readOnly: true,
                        decoration: InputDecoration(
                          labelText: 'To time *',
                          border: const OutlineInputBorder(),
                          suffixIcon: IconButton(
                            icon: const Icon(Icons.access_time),
                            onPressed: () => _pickTime(_validToTime),
                          ),
                        ),
                        onTap: () => _pickTime(_validToTime),
                        validator: (v) => _validateRequiredTime('Valid to time', v),
                      ),
                    ),
                  ],
                ),
              ]),
            ),
            Step(
              title: const Text('Location & Task'),
              isActive: _step >= 1,
              content: Column(children: [
                TextFormField(
                  controller: _contractor,
                  decoration: const InputDecoration(
                    labelText: 'Contractor company',
                    border: OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _location,
                  decoration: const InputDecoration(
                    labelText: 'Location of operation',
                    border: OutlineInputBorder(),
                  ),
                  maxLines: 2,
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _task,
                  decoration: const InputDecoration(
                    labelText: 'Task description *',
                    border: OutlineInputBorder(),
                  ),
                  maxLines: 3,
                  validator: (v) => (v == null || v.trim().isEmpty) ? 'Task description is required' : null,
                ),
              ]),
            ),
            Step(
              title: const Text('Associated Permits'),
              isActive: _step >= 2,
              content: Column(children: [
                SwitchListTile(value: _project, onChanged: (v) => setState(() => _project = v), title: const Text('Project')),
                SwitchListTile(value: _maintenance, onChanged: (v) => setState(() => _maintenance = v), title: const Text('Maintenance')),
                TextFormField(controller: _preventionPlanRef, decoration: const InputDecoration(labelText: 'Prevention plan reference')),
                SwitchListTile(value: _safetyCondition, onChanged: (v) => setState(() => _safetyCondition = v), title: const Text('Safety induction completed')),
              ]),
            ),
            Step(
              title: const Text('Interference'),
              isActive: _step >= 3,
              content: Column(children: [
                TextFormField(controller: _fuelDeliveryAt, decoration: const InputDecoration(labelText: 'Fuel delivery scheduled at')),
                SwitchListTile(value: _otherWorkPlanned, onChanged: (v) => setState(() => _otherWorkPlanned = v), title: const Text('Other work planned today')),
                if (_otherWorkPlanned) ...[
                  TextFormField(controller: _otherWorkDayDetails, maxLines: 2, decoration: const InputDecoration(labelText: 'Other work today — details')),
                  TextFormField(controller: _otherWorkDayRef, decoration: const InputDecoration(labelText: 'Other work today — reference')),
                ],
                SwitchListTile(value: _gasCylinders, onChanged: (v) => setState(() => _gasCylinders = v), title: const Text('Gas cylinders/barrels present')),
                if (_gasCylinders)
                  TextFormField(controller: _gasCylinderDetails, maxLines: 2, decoration: const InputDecoration(labelText: 'Gas cylinders / barrels — details')),
                SwitchListTile(value: _nearbyWork, onChanged: (v) => setState(() => _nearbyWork = v), title: const Text('Nearby work planned')),
                if (_nearbyWork) ...[
                  TextFormField(controller: _interferenceNotes, maxLines: 2, decoration: const InputDecoration(labelText: 'Nearby work — details')),
                  TextFormField(controller: _nearbyWorkRef, decoration: const InputDecoration(labelText: 'Nearby work — reference')),
                ],
              ]),
            ),
            Step(
              title: const Text('Safety Measures'),
              isActive: _step >= 4,
              content: Column(
                children: [
                  SwitchListTile(value: _personnelInformed, onChanged: (v) => setState(() => _personnelInformed = v), title: const Text('Personnel informed')),
                  SwitchListTile(value: _phonesOff, onChanged: (v) => setState(() => _phonesOff = v), title: const Text('Phones/cameras switched off')),
                  SwitchListTile(value: _closureStation, onChanged: (v) => setState(() => _closureStation = v), title: const Text('Closure of station')),
                  SwitchListTile(
                      value: _hotWorkRestrictedClassified,
                      onChanged: (v) => setState(() => _hotWorkRestrictedClassified = v),
                      title: const Text('Hot work restricted in classified areas')),
                  SwitchListTile(value: _distStopPartial, onChanged: (v) => setState(() => _distStopPartial = v), title: const Text('Distribution stoppage — partial')),
                  SwitchListTile(value: _distStopTotal, onChanged: (v) => setState(() => _distStopTotal = v), title: const Text('Distribution stoppage — total')),
                  SwitchListTile(value: _ppeRequired, onChanged: (v) => setState(() => _ppeRequired = v), title: const Text('Protective clothing required')),
                  SwitchListTile(value: _hearingProtRequired, onChanged: (v) => setState(() => _hearingProtRequired = v), title: const Text('Hearing protection required')),
                  SwitchListTile(value: _hiVisCoverall, onChanged: (v) => setState(() => _hiVisCoverall = v), title: const Text('High-visibility coverall required')),
                  SwitchListTile(value: _dustMask, onChanged: (v) => setState(() => _dustMask = v), title: const Text('Dust mask required')),
                  if (_ppeRequired)
                    Wrap(
                      spacing: 8,
                      children: [
                        FilterChip(label: const Text('Hard hat'), selected: _hardHat, onSelected: (v) => setState(() => _hardHat = v)),
                        FilterChip(label: const Text('Goggles/face shield'), selected: _goggles, onSelected: (v) => setState(() => _goggles = v)),
                        FilterChip(label: const Text('Visibility vest'), selected: _vest, onSelected: (v) => setState(() => _vest = v)),
                        FilterChip(label: const Text('Steel toe boots'), selected: _boots, onSelected: (v) => setState(() => _boots = v)),
                        FilterChip(label: const Text('Gloves'), selected: _gloves, onSelected: (v) => setState(() => _gloves = v)),
                      ],
                    )
                ],
              ),
            ),
            Step(
              title: const Text('Nature of Works'),
              isActive: _step >= 5,
              content: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SwitchListTile(value: _hotWork, onChanged: (v) => _derivedAwareSetState(() => _hotWork = v), title: const Text('Hot work (drilling / welding / brazing / cutting)')),
                  SwitchListTile(value: _excavation, onChanged: (v) => _derivedAwareSetState(() => _excavation = v), title: const Text('Excavation work')),
                  SwitchListTile(value: _heights, onChanged: (v) => _derivedAwareSetState(() => _heights = v), title: const Text('Work at heights (ladder / scaffold / rope / MEWP)')),
                  SwitchListTile(value: _lifting, onChanged: (v) => _derivedAwareSetState(() => _lifting = v), title: const Text('Lifting (beam/elevator/crane truck/bridge crane/winch)')),
                  SwitchListTile(value: _confined, onChanged: (v) => _derivedAwareSetState(() => _confined = v), title: const Text('Work in confined atmosphere')),
                  SwitchListTile(value: _degas, onChanged: (v) => _derivedAwareSetState(() => _degas = v), title: const Text('Cleaning-degassing work')),
                  SwitchListTile(value: _electrical, onChanged: (v) => _derivedAwareSetState(() => _electrical = v), title: const Text('Electrical work')),
                  if (_electrical)
                    SwitchListTile(
                      value: _electricalHv,
                      onChanged: (v) => _derivedAwareSetState(() => _electricalHv = v),
                      title: const Text('High-voltage (HV) electrical work'),
                    ),
                  ExpansionTile(
                    title: const Text('Detailed toggles (movement, tools, machinery, …)'),
                    children: [
                      for (final k in _natureWorksCommentKeys) _buildToggleWithCommentTile('natureOfWorks', k),
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                        child: TextFormField(
                          controller: _manualHandlingTypesAndWeight,
                          decoration: const InputDecoration(
                            labelText: 'Manual handling — types & weight',
                            border: OutlineInputBorder(),
                            isDense: true,
                          ),
                          maxLines: 2,
                        ),
                      ),
                    ],
                  ),
                  ExpansionTile(
                    title: const Text('Detailed option lists (hot work, lifting, heights, …)'),
                    children: [
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        child: _buildDynamicListSections('natureOfWorks'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Step(
              title: const Text('Nature of Risks'),
              isActive: _step >= 6,
              content: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SwitchListTile(value: _riskFireExplosion, onChanged: (v) => setState(() => _riskFireExplosion = v), title: const Text('Fire/explosion')),
                  SwitchListTile(value: _riskElectricalShock, onChanged: (v) => setState(() => _riskElectricalShock = v), title: const Text('Electrical shock')),
                  SwitchListTile(value: _riskFallingObjects, onChanged: (v) => setState(() => _riskFallingObjects = v), title: const Text('Falling objects')),
                  SwitchListTile(value: _riskHearing, onChanged: (v) => setState(() => _riskHearing = v), title: const Text('Noise induced hearing loss')),
                  TextFormField(controller: _otherRisksNotes, maxLines: 2, decoration: const InputDecoration(labelText: 'Other risk notes')),
                  ExpansionTile(
                    title: const Text('All risk categories (detailed)'),
                    children: [
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        child: _buildDynamicListSections('natureOfRisks'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Step(
              title: const Text('Prevention Measures'),
              isActive: _step >= 7,
              content: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SwitchListTile(value: _actHaltedComplete, onChanged: (v) => setState(() => _actHaltedComplete = v), title: const Text('Activities halted completely')),
                  SwitchListTile(value: _actHaltedPartial, onChanged: (v) => setState(() => _actHaltedPartial = v), title: const Text('Activities halted partially')),
                  TextFormField(controller: _activitiesHaltedSpecify, maxLines: 2, decoration: const InputDecoration(labelText: 'Activities halted — specify')),
                  SwitchListTile(value: _controlGasTesting, onChanged: (v) => setState(() => _controlGasTesting = v), title: const Text('Gas testing')),
                  SwitchListTile(
                      value: _controlEnergyIsolation,
                      onChanged: (v) => _derivedAwareSetState(() => _controlEnergyIsolation = v),
                      title: const Text('Energy isolation permit/certificate')),
                  SwitchListTile(value: _controlLiftingPermit, onChanged: (v) => setState(() => _controlLiftingPermit = v), title: const Text('Lifting permit')),
                  SwitchListTile(value: _controlHeightsPermit, onChanged: (v) => setState(() => _controlHeightsPermit = v), title: const Text('Working at heights permit')),
                  SwitchListTile(value: _controlDegasPermit, onChanged: (v) => setState(() => _controlDegasPermit = v), title: const Text('Cleaning-degassing permit')),
                  SwitchListTile(value: _controlHearingProtection, onChanged: (v) => setState(() => _controlHearingProtection = v), title: const Text('Hearing protection')),
                  TextFormField(controller: _preventionNotes, maxLines: 2, decoration: const InputDecoration(labelText: 'Other prevention notes')),
                  ExpansionTile(
                    title: const Text('All prevention controls (detailed)'),
                    children: [
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        child: _buildDynamicListSections('preventionMeasures'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Step(
              title: const Text('Declaration & signatures'),
              isActive: _step >= 8,
              content: Column(children: [
                TextFormField(controller: _issuingName, decoration: const InputDecoration(labelText: 'Issuing authority name')),
                Row(children: [Expanded(child: Text(_issuingSigBase64 == null ? 'No issuing signature' : 'Issuing signature captured')), TextButton(onPressed: () => _captureSig('issuing'), child: const Text('Sign'))]),
                TextFormField(controller: _performingName, decoration: const InputDecoration(labelText: 'Performing authority name')),
                Row(children: [Expanded(child: Text(_performingSigBase64 == null ? 'No performing signature' : 'Performing signature captured')), TextButton(onPressed: () => _captureSig('performing'), child: const Text('Sign'))]),
                TextFormField(controller: _clientName, decoration: const InputDecoration(labelText: 'Client acknowledgement name')),
                Row(children: [Expanded(child: Text(_clientSigBase64 == null ? 'No client signature' : 'Client signature captured')), TextButton(onPressed: () => _captureSig('client'), child: const Text('Sign'))]),
              ]),
            ),
            Step(
              title: const Text('Revalidations'),
              isActive: _step >= 9,
              content: Column(
                children: [
                  for (var i = 0; i < _revalidations.length; i++)
                    ListTile(
                      title: Text('Revalidation ${i + 1}'),
                      subtitle: Text((_revalidations[i]['date'] ?? '').toString()),
                      trailing: IconButton(icon: const Icon(Icons.delete_outline), onPressed: () => setState(() => _revalidations.removeAt(i))),
                    ),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: OutlinedButton.icon(onPressed: _addRevalidation, icon: const Icon(Icons.add), label: const Text('Add revalidation')),
                  )
                ],
              ),
            ),
            Step(
              title: const Text('Hand Back'),
              isActive: _step >= 10,
              content: Column(
                children: [
                  for (var i = 0; i < _handbacks.length; i++)
                    ListTile(
                      title: Text('Hand back ${i + 1}'),
                      subtitle: Text((_handbacks[i]['date'] ?? '').toString()),
                      trailing: IconButton(icon: const Icon(Icons.delete_outline), onPressed: () => setState(() => _handbacks.removeAt(i))),
                    ),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: OutlinedButton.icon(onPressed: _addHandback, icon: const Icon(Icons.add), label: const Text('Add hand back entry')),
                  )
                ],
              ),
            ),
            Step(
              title: const Text('Withdrawal & Summary'),
              isActive: _step >= 11,
              content: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SwitchListTile(value: _withdrawn, onChanged: (v) => setState(() => _withdrawn = v), title: const Text('Permit withdrawn')),
                  if (_withdrawn) ...[
                    SwitchListTile(value: _withdrawScopeChange, onChanged: (v) => setState(() => _withdrawScopeChange = v), title: const Text('Scope of work changes')),
                    SwitchListTile(value: _withdrawRulesViolation, onChanged: (v) => setState(() => _withdrawRulesViolation = v), title: const Text('Permit rules violation')),
                    SwitchListTile(value: _withdrawAccident, onChanged: (v) => setState(() => _withdrawAccident = v), title: const Text('Accident occurrence')),
                    SwitchListTile(value: _withdrawAuthorityAbsent, onChanged: (v) => setState(() => _withdrawAuthorityAbsent = v), title: const Text('Issuing/performing authority not on site')),
                    TextFormField(controller: _withdrawalNotes, maxLines: 2, decoration: const InputDecoration(labelText: 'Withdrawal notes')),
                  ],
                  const SizedBox(height: 12),
                  const Text('Work permits indicated from this form', style: TextStyle(fontWeight: FontWeight.w600)),
                  const SizedBox(height: 6),
                  if (_derivedPermitLabels.isEmpty)
                    const Text('No work permits currently derived.')
                  else
                    ..._derivedPermitLabels.map((e) => Text('• $e')),
                  const SizedBox(height: 12),
                  TextFormField(controller: _notes, decoration: const InputDecoration(labelText: 'General notes'), maxLines: 3),
                ],
              ),
            )
          ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
