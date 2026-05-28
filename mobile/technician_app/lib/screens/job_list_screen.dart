import 'dart:async';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import '../models/job_card.dart';
import '../models/job_status.dart';
import '../services/auth_service.dart';
import '../services/job_cards_service.dart';
import '../services/tracking_service.dart';
import 'job_detail_screen.dart';
import 'login_screen.dart';
import 'profile_screen.dart';

class JobListScreen extends StatefulWidget {
  const JobListScreen({super.key});

  @override
  State<JobListScreen> createState() => _JobListScreenState();
}

class _JobListScreenState extends State<JobListScreen> {
  final _jobService = JobCardsService();
  final _auth = AuthService();
  final _tracking = TrackingService();
  List<JobCardListDto> _jobs = [];
  bool _loading = true;
  String? _error;
  StreamSubscription<Position>? _locationSub;
  String? _activeJobId;

  @override
  void initState() {
    super.initState();
    _load();
    _startLocationReporting();
  }

  @override
  void dispose() {
    _locationSub?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final list = await _jobService.getMyJobs();
      final open = list.where((j) => !_isCompletedStatus(j.status)).toList()
        ..sort((a, b) {
          final byPriority = b.priority.compareTo(a.priority); // highest first
          if (byPriority != 0) return byPriority;
          final aDue = a.dueDate ?? DateTime(9999);
          final bDue = b.dueDate ?? DateTime(9999);
          final byDue = aDue.compareTo(bDue); // earlier due first
          if (byDue != 0) return byDue;
          return b.createdAt.compareTo(a.createdAt); // newest tie-break
        });
      final completed = list.where((j) => _isCompletedStatus(j.status)).toList()
        ..sort((a, b) => b.createdAt.compareTo(a.createdAt)); // latest completed first
      final ordered = [...open, ...completed.take(5)];
      if (!mounted) return;
      setState(() {
        _jobs = ordered;
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.toString();
        _loading = false;
      });
    }
  }

  static bool _isCompletedStatus(String? status) {
    return JobStatusValue.isCompletedLike(status);
  }

  void _startLocationReporting() async {
    final permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      await Geolocator.requestPermission();
    }
    if (await Geolocator.isLocationServiceEnabled() == false) return;
    _locationSub = Geolocator.getPositionStream(
      locationSettings: const LocationSettings(accuracy: LocationAccuracy.medium),
    ).listen((position) async {
      await _tracking.reportLocation(
        latitude: position.latitude,
        longitude: position.longitude,
        jobCardId: _activeJobId,
        accuracyMeters: position.accuracy,
      );
    });
  }

  void _setActiveJob(String? jobId) {
    setState(() => _activeJobId = jobId);
  }

  Future<void> _logout() async {
    await _auth.logout();
    if (!mounted) return;
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(builder: (_) => const LoginScreen()),
      (_) => false,
    );
  }

  /// Highest priority among open (non-completed) jobs (for ordering and optional warning).
  int? get _highestOpenPriority {
    final open = _jobs.where((j) => !_isCompletedStatus(j.status)).toList();
    if (open.isEmpty) return null;
    return open.map((j) => j.priority).reduce((a, b) => a > b ? a : b);
  }

  String _formatDate(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  Future<void> _onJobTileTap(JobCardListDto job) async {
    final isCompleted = _isCompletedStatus(job.status);
    final blockedByReason = job.blockedReason != null && job.blockedReason!.trim().isNotEmpty;
    if (blockedByReason) return;

    final highest = _highestOpenPriority;
    if (!isCompleted && highest != null && job.priority < highest) {
      final ok = await showDialog<bool>(
            context: context,
            builder: (ctx) => AlertDialog(
              title: const Text('Lower priority job'),
              content: Text(
                'There are open jobs with higher priority (P$highest). '
                'This job is P${job.priority}. Are you sure you want to open it?',
              ),
              actions: [
                TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
                FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Continue')),
              ],
            ),
          ) ??
          false;
      if (!ok || !mounted) return;
    }

    _setActiveJob(isCompleted ? null : job.id);
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => JobDetailScreen(
          jobId: job.id,
          onClosed: () => _setActiveJob(null),
          viewOnly: isCompleted,
        ),
      ),
    );
    if (mounted) _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leading: Padding(
          padding: const EdgeInsets.all(8),
          child: Image.asset('assets/logo/ike-icon.png', fit: BoxFit.contain),
        ),
        title: const Text('My jobs'),
        actions: [
          IconButton(icon: const Icon(Icons.refresh), onPressed: _loading ? null : _load),
          PopupMenuButton<String>(
            icon: const Icon(Icons.person_outline),
            onSelected: (v) async {
              if (v == 'profile') {
                await Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const ProfileScreen()),
                );
              } else if (v == 'logout') {
                _logout();
              }
            },
            itemBuilder: (_) => [
              const PopupMenuItem(value: 'profile', child: Text('Profile')),
              const PopupMenuItem(value: 'logout', child: Text('Sign out')),
            ],
          ),
        ],
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
                        Text(_error!, textAlign: TextAlign.center),
                        const SizedBox(height: 16),
                        FilledButton(onPressed: _load, child: const Text('Retry')),
                      ],
                    ),
                  ),
                )
              : _jobs.isEmpty
                  ? Center(
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(Icons.work_outline, size: 64, color: Colors.grey[400]),
                          const SizedBox(height: 16),
                          Text('No jobs assigned', style: Theme.of(context).textTheme.titleMedium),
                          const SizedBox(height: 8),
                          Text(
                            'Jobs assigned to you will appear here',
                            style: TextStyle(color: Colors.grey[600]),
                          ),
                        ],
                      ),
                    )
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.builder(
                        padding: const EdgeInsets.all(16),
                        itemCount: _jobs.length,
                        itemBuilder: (context, i) {
                          final job = _jobs[i];
                          final isCompleted = _isCompletedStatus(job.status);
                          final highest = _highestOpenPriority;
                          final blockedByReason = job.blockedReason != null && job.blockedReason!.trim().isNotEmpty;
                          final isLowerOpenPriority =
                              !isCompleted && !blockedByReason && highest != null && job.priority < highest;
                          final canTap = isCompleted || !blockedByReason;

                          return Card(
                            margin: const EdgeInsets.only(bottom: 12),
                            child: ListTile(
                              title: Text(
                                job.jobCardNumber,
                                style: const TextStyle(fontWeight: FontWeight.w600),
                              ),
                              subtitle: Text(
                                '${[job.siteName].whereType<String>().join(' • ')}\n'
                                '${job.status} • P${job.priority}'
                                '${job.dueDate != null ? ' • Due ${_formatDate(job.dueDate!)}' : ''}'
                                '${blockedByReason ? ' • Blocked: ${job.blockedReason}' : ''}'
                                '${isLowerOpenPriority ? ' • Lower priority than other open jobs' : ''}',
                              ),
                              trailing: isCompleted
                                  ? Icon(Icons.check_circle, color: Colors.green[700], size: 20)
                                  : blockedByReason
                                      ? Icon(Icons.lock_outline, color: Colors.grey[500], size: 20)
                                      : const Icon(Icons.chevron_right),
                              onTap: canTap ? () => _onJobTileTap(job) : null,
                            ),
                          );
                        },
                      ),
                    ),
    );
  }
}
