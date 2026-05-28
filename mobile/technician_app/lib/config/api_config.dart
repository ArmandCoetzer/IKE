import 'dart:io' show Platform;

import 'package:flutter/foundation.dart' show kIsWeb, kDebugMode, kProfileMode;

/// API base URL ending in `/api` (same idea as web `environment.apiUrl + '/api'`).
///
/// **Why not `localhost` on a phone?** On a device or emulator, `localhost` is the phone
/// itself, not your PC — you get "Connection refused".
///
/// - **Android emulator:** debug default is `http://10.0.2.2:5020/api` (cleartext allowed in debug only).
/// - **Release builds:** default to the hosted IKE API; set
///   `--dart-define=API_URL=https://your-host/api` only when deploying elsewhere.
/// - **Physical Android (USB):** debug: `adb reverse tcp:5020 tcp:5020` and HTTP to `127.0.0.1` as above.
/// - **Physical device (Wi‑Fi):** prefer HTTPS to your API host; for local HTTP during dev use a debug build.
///
/// Guards against empty/malformed `--dart-define` values.
const String _apiUrlFromDefine = String.fromEnvironment('API_URL', defaultValue: '');
const String _productionApiBaseUrl = 'https://ikeapi.accent-dev.co.za/api';
final String apiBaseUrl = _resolveApiBaseUrl();

String _resolveApiBaseUrl() {
  final raw = _apiUrlFromDefine.trim();
  if (raw.isNotEmpty) {
    final parsed = Uri.tryParse(raw);
    if (parsed != null && parsed.hasScheme && (parsed.host).isNotEmpty) {
      return raw;
    }
  }
  if (kDebugMode || kProfileMode) {
    if (kIsWeb) {
      return 'http://localhost:5020/api';
    }
    try {
      if (Platform.isAndroid) {
        return 'http://10.0.2.2:5020/api';
      }
    } catch (_) {
      // Platform unavailable in some test/embedder contexts
    }
    return 'http://localhost:5020/api';
  }
  // Release: no implicit cleartext. Default to the hosted API unless a build sets API_URL.
  return _productionApiBaseUrl;
}

String get authUrl => '$apiBaseUrl/auth';
String get jobCardsUrl => '$apiBaseUrl/jobcards';
String get jobCardWorkUrl => '$apiBaseUrl/jobcardwork';
String get jobPermitsUrl => '$apiBaseUrl/jobpermits';
String get workAuthMasterPermitUrl => '$apiBaseUrl/work-authorizations/master-permit';
String get trackingUrl => '$apiBaseUrl/tracking';
