import 'dart:convert';

/// Parses API error bodies from the IKE API (e.g. `{ "message": "..." }`) and common ASP.NET shapes.
String? apiErrorMessageFromBody(String body) {
  final t = body.trim();
  if (t.isEmpty) return null;
  if (t.startsWith('"')) {
    try {
      final s = jsonDecode(t);
      if (s is String) {
        final m = s.trim();
        if (m.isNotEmpty) return m;
      }
    } catch (_) {}
  }
  if (!t.startsWith('{')) return null;
  try {
    final decoded = jsonDecode(t);
    if (decoded is Map<String, dynamic>) {
      final m = decoded['message'];
      if (m is String && m.trim().isNotEmpty) return m.trim();
      final title = decoded['title'];
      if (title is String && title.trim().isNotEmpty) return title.trim();
    }
  } catch (_) {}
  return null;
}
