class JobStatusValue {
  static const String draft = 'draft';
  static const String open = 'open';
  static const String inProgressSpaced = 'in progress';
  static const String inProgressCompact = 'inprogress';
  static const String completed = 'completed';
  static const String done = 'done';
  static const String closed = 'closed';

  static String norm(String? status) => (status ?? '').trim().toLowerCase();

  static bool isInProgressLike(String? status) {
    final s = norm(status).replaceAll(' ', '');
    return s == inProgressCompact;
  }

  static bool isCompletedLike(String? status) {
    final s = norm(status);
    return s == completed || s == done || s == closed;
  }
}
