class PermitStatusValue {
  static const String draft = 'draft';
  static const String captured = 'captured';
  static const String signed = 'signed';
  static const String active = 'active';
  static const String approved = 'approved';
  static const String expired = 'expired';
  static const String closed = 'closed';
  static const String done = 'done';
  static const String rejected = 'rejected';
  static const String cancelled = 'cancelled';

  static String norm(String? status) => (status ?? '').trim().toLowerCase();

  static bool isDraftLike(String? status) => norm(status) == draft;
  static bool isCapturedLike(String? status) {
    final s = norm(status);
    return s == captured || s == signed;
  }

  static bool isActiveLike(String? status) {
    final s = norm(status);
    return s == active || s == approved;
  }

  static bool isExpiredLike(String? status) => norm(status) == expired;
  static bool isClosedLike(String? status) {
    final s = norm(status);
    return s == closed || s == done;
  }

  static bool isRejectedOrCancelled(String? status) {
    final s = norm(status);
    return s == rejected || s == cancelled;
  }
}
