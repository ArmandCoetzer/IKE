import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';
import '../config/api_config.dart';

const _tokenKey = 'tradion_token';
const _userKey = 'tradion_user';
const _rememberMeEnabledKey = 'remember_me_enabled';
const _rememberedEmailKey = 'remembered_email_secure';
const _rememberedPasswordKey = 'remembered_password_secure';
const _legacyRememberEnabledKey = 'remember_email_enabled';
const _legacyRememberedEmailKey = 'remembered_email';

class AuthResponse {
  final String? userId;
  final String token;
  final String email;
  final String? fullName;
  final String? role;

  AuthResponse({
    this.userId,
    required this.token,
    required this.email,
    this.fullName,
    this.role,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) => AuthResponse(
        userId: json['userId'] as String?,
        token: json['token'] as String,
        email: json['email'] as String,
        fullName: json['fullName'] as String?,
        role: json['role'] as String?,
      );
}

class AuthService {
  bool _isTokenExpired(String token) {
    try {
      final parts = token.split('.');
      if (parts.length != 3) return true;
      final payload = utf8.decode(base64Url.decode(base64Url.normalize(parts[1])));
      final json = jsonDecode(payload) as Map<String, dynamic>;
      final exp = json['exp'];
      if (exp is! num) return true;
      final expiresAt = DateTime.fromMillisecondsSinceEpoch(exp.toInt() * 1000, isUtc: true);
      return DateTime.now().toUtc().isAfter(expiresAt);
    } catch (_) {
      return true;
    }
  }

  String? _token;
  AuthResponse? _user;
  static const _secureStorage = FlutterSecureStorage();

  Future<({bool enabled, String? email, String? password})> getRememberedCredentials() async {
    final secureEnabledRaw = await _secureStorage.read(key: _rememberMeEnabledKey);
    var enabled = secureEnabledRaw == 'true';
    var email = await _secureStorage.read(key: _rememberedEmailKey);
    var password = await _secureStorage.read(key: _rememberedPasswordKey);

    // One-time migration from legacy SharedPreferences email-only remember state.
    if (email == null) {
      final prefs = await SharedPreferences.getInstance();
      final legacyEnabled = prefs.getBool(_legacyRememberEnabledKey);
      final legacyEmail = prefs.getString(_legacyRememberedEmailKey);
      if (legacyEmail != null && legacyEmail.isNotEmpty) {
        email = legacyEmail;
        enabled = legacyEnabled ?? true;
        await _secureStorage.write(
          key: _rememberMeEnabledKey,
          value: enabled.toString(),
        );
        await _secureStorage.write(key: _rememberedEmailKey, value: legacyEmail);
      }
      await prefs.remove(_legacyRememberedEmailKey);
      await prefs.remove(_legacyRememberEnabledKey);
    }

    if (!enabled) {
      password = null;
    }
    return (enabled: enabled, email: email, password: password);
  }

  Future<void> setRememberedCredentials({
    required bool rememberMe,
    required String email,
    required String password,
  }) async {
    if (!rememberMe) {
      await clearRememberedCredentials();
      return;
    }

    await _secureStorage.write(key: _rememberMeEnabledKey, value: 'true');
    await _secureStorage.write(key: _rememberedEmailKey, value: email);
    await _secureStorage.write(key: _rememberedPasswordKey, value: password);
  }

  Future<void> clearRememberedCredentials() async {
    await _secureStorage.write(key: _rememberMeEnabledKey, value: 'false');
    await _secureStorage.delete(key: _rememberedEmailKey);
    await _secureStorage.delete(key: _rememberedPasswordKey);
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_legacyRememberedEmailKey);
    await prefs.remove(_legacyRememberEnabledKey);
  }

  Future<String?> getToken() async {
    _token ??= await _secureStorage.read(key: _tokenKey);
    if (_token == null) {
      final prefs = await SharedPreferences.getInstance();
      final legacy = prefs.getString(_tokenKey);
      if (legacy != null && legacy.isNotEmpty) {
        _token = legacy;
        await _secureStorage.write(key: _tokenKey, value: legacy);
        await prefs.remove(_tokenKey);
      }
    }
    return _token;
  }

  Future<bool> hasValidToken() async {
    final t = await getToken();
    if (t == null || t.isEmpty) return false;
    if (_isTokenExpired(t)) {
      await logout();
      return false;
    }
    return true;
  }

  Future<AuthResponse?> getUser() async {
    if (_user != null) return _user;
    var json = await _secureStorage.read(key: _userKey);
    if (json == null) {
      final prefs = await SharedPreferences.getInstance();
      final legacy = prefs.getString(_userKey);
      if (legacy != null && legacy.isNotEmpty) {
        json = legacy;
        await _secureStorage.write(key: _userKey, value: legacy);
        await prefs.remove(_userKey);
      }
    }
    if (json == null) return null;
    try {
      _user = AuthResponse.fromJson(
        jsonDecode(json) as Map<String, dynamic>,
      );
      return _user;
    } catch (_) {
      return null;
    }
  }

  Future<void> refreshUser() async {
    final t = await getToken();
    if (t == null || t.isEmpty) return;
    try {
      final response = await http.get(
        Uri.parse('$authUrl/me'),
        headers: {'Authorization': 'Bearer $t'},
      );
      if (response.statusCode == 200) {
        final data = jsonDecode(response.body) as Map<String, dynamic>;
        _user = AuthResponse.fromJson(data);
        await _secureStorage.write(key: _userKey, value: jsonEncode(data));
      }
    } catch (_) {}
  }

  Future<AuthResponse> login(String email, String password) async {
    final uri = Uri.parse('$authUrl/login-mobile');
    try {
      final response = await http.post(
        uri,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'email': email, 'password': password}),
      );
      if (response.statusCode != 200) {
        dynamic body;
        try {
          body = jsonDecode(response.body);
        } catch (_) {
          body = <String, dynamic>{};
        }
        final msg = body is Map ? (body['message'] ?? 'Login failed') : 'Login failed';
        debugPrint('[Auth] Login failed: $response.statusCode $uri - $msg');
        throw Exception(msg);
      }
      final data = jsonDecode(response.body) as Map<String, dynamic>;
      final auth = AuthResponse.fromJson(data);
      await _secureStorage.write(key: _tokenKey, value: auth.token);
      await _secureStorage.write(key: _userKey, value: jsonEncode(data));
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(_tokenKey);
      await prefs.remove(_userKey);
      _token = auth.token;
      _user = auth;
      return auth;
    } catch (e, stack) {
      debugPrint('[Auth] Login error: $uri');
      debugPrint('[Auth] $e');
      debugPrint('[Auth] $stack');
      rethrow;
    }
  }

  Future<void> logout() async {
    _token = null;
    _user = null;
    await _secureStorage.delete(key: _tokenKey);
    await _secureStorage.delete(key: _userKey);
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_tokenKey);
    await prefs.remove(_userKey);
  }

  Future<Map<String, String>> authHeaders() async {
    final t = await getToken();
    if (t != null && _isTokenExpired(t)) {
      await logout();
      return {'Content-Type': 'application/json'};
    }
    return {
      'Content-Type': 'application/json',
      if (t != null) 'Authorization': 'Bearer $t',
    };
  }

  /// Profile for current user (firstName, lastName, phone, email).
  Future<ProfileDto> getProfile() async {
    final h = await authHeaders();
    final response = await http.get(Uri.parse('$authUrl/profile'), headers: h);
    if (response.statusCode != 200) throw Exception('Failed to load profile');
    return ProfileDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<ProfileDto> updateProfile({String? firstName, String? lastName, String? phone}) async {
    final h = await authHeaders();
    final body = <String, dynamic>{};
    if (firstName != null) body['firstName'] = firstName;
    if (lastName != null) body['lastName'] = lastName;
    if (phone != null) body['phone'] = phone;
    final response = await http.put(
      Uri.parse('$authUrl/profile'),
      headers: h,
      body: jsonEncode(body),
    );
    if (response.statusCode != 200) {
      final err = response.body;
      try {
        final m = jsonDecode(err) as Map<String, dynamic>;
        throw Exception(m['message'] ?? err);
      } catch (_) {
        throw Exception(err);
      }
    }
    return ProfileDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  Future<void> changePassword({required String currentPassword, required String newPassword}) async {
    final h = await authHeaders();
    final response = await http.post(
      Uri.parse('$authUrl/change-password'),
      headers: h,
      body: jsonEncode({
        'currentPassword': currentPassword,
        'newPassword': newPassword,
      }),
    );
    if (response.statusCode != 204) {
      final err = response.body;
      try {
        final m = jsonDecode(err) as Map<String, dynamic>;
        throw Exception(m['message'] ?? err);
      } catch (_) {
        throw Exception(err);
      }
    }
  }
}

class ProfileDto {
  final String userId;
  final String email;
  final String? fullName;
  final String? firstName;
  final String? lastName;
  final String? phone;

  ProfileDto({
    required this.userId,
    required this.email,
    this.fullName,
    this.firstName,
    this.lastName,
    this.phone,
  });

  factory ProfileDto.fromJson(Map<String, dynamic> json) => ProfileDto(
        userId: json['userId'] as String? ?? '',
        email: json['email'] as String? ?? '',
        fullName: json['fullName'] as String?,
        firstName: json['firstName'] as String?,
        lastName: json['lastName'] as String?,
        phone: json['phone'] as String?,
      );
}
