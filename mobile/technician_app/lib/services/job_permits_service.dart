import 'dart:convert';
import 'package:http/http.dart' as http;
import 'auth_service.dart';
import '../config/api_config.dart';
import '../helpers/api_error_message.dart';

class JobPermitsService {
  final AuthService _auth = AuthService();

  /// Request a permit for a job card. Returns the permit id.
  Future<String> requestPermit(String jobCardId, {String? permitTypeId, String? masterPermitId}) async {
    final headers = await _auth.authHeaders();
    final body = <String, dynamic>{'jobCardId': jobCardId};
    if (permitTypeId != null && permitTypeId.isNotEmpty) {
      body['permitTypeId'] = permitTypeId;
    }
    if (masterPermitId != null && masterPermitId.isNotEmpty) {
      body['masterPermitId'] = masterPermitId;
    }
    final response = await http.post(
      Uri.parse(jobPermitsUrl),
      headers: headers,
      body: jsonEncode(body),
    );
    if (response.statusCode == 201) {
      final data = jsonDecode(response.body) as Map<String, dynamic>;
      return data['id'] as String;
    }
    throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to request permit');
  }

  /// Upload a permit document (PDF or image).
  Future<void> uploadAttachment(String permitId, List<int> bytes, String fileName) async {
    final token = await _auth.getToken();
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('$jobPermitsUrl/$permitId/upload'),
    );
    request.headers['Authorization'] = 'Bearer $token';
    request.files.add(http.MultipartFile.fromBytes(
      'file',
      bytes,
      filename: fileName,
    ));
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Upload failed');
    }
  }

  Future<void> setPaperPermitNumber(String permitId, String paperPermitNumber) async {
    final headers = await _auth.authHeaders();
    final response = await http.patch(
      Uri.parse('$jobPermitsUrl/$permitId/paper-number'),
      headers: headers,
      body: jsonEncode({'paperPermitNumber': paperPermitNumber}),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to set paper permit number');
    }
  }

  Future<void> paperClientSignOff(String permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.patch(
      Uri.parse('$jobPermitsUrl/$permitId/paper-client-sign-off'),
      headers: headers,
      body: jsonEncode({}),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to record paper sign-off');
    }
  }

  Future<void> updateStatus(String permitId, String status) async {
    final headers = await _auth.authHeaders();
    final response = await http.patch(
      Uri.parse('$jobPermitsUrl/$permitId'),
      headers: headers,
      body: jsonEncode({'status': status}),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to update permit status');
    }
  }

  /// Removes a permit: draft WA (and draft children), draft/captured/signed child, or active child no longer required by WA.
  Future<void> deletePermit(String permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.delete(
      Uri.parse('$jobPermitsUrl/$permitId'),
      headers: headers,
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to delete permit');
    }
  }

  /// Get permits expiring within the given hours for a job card. For "need more work?" flow.
  Future<List<ExpiringPermitDto>> getExpiringPermits(String jobCardId, {int hours = 24}) async {
    final headers = await _auth.authHeaders();
    final uri = Uri.parse('$jobPermitsUrl/expiring?jobCardId=$jobCardId&hours=$hours');
    final response = await http.get(uri, headers: headers);
    if (response.statusCode != 200) return [];
    final list = jsonDecode(response.body) as List;
    return list.map((e) => ExpiringPermitDto.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<void> emailMasterPermitToClient(String permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.post(
      Uri.parse('$workAuthMasterPermitUrl/$permitId/email-client'),
      headers: headers,
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to email permit to client');
    }
  }

  /// Child / work permit: sends uploaded files to client (API requires sign-off).
  Future<void> emailChildPermitToClient(String permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.post(
      Uri.parse('$jobPermitsUrl/$permitId/email-client'),
      headers: headers,
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to email permit to client');
    }
  }

  /// Same [permitLabel] strings as the API rules engine / PDF (unsaved draft OK).
  Future<List<String>> deriveRequiredPermitLabels(Map<String, dynamic> workAuthorizationPayload) async {
    final headers = await _auth.authHeaders();
    final response = await http.post(
      Uri.parse('$workAuthMasterPermitUrl/derive-required-permits'),
      headers: headers,
      body: jsonEncode(workAuthorizationPayload),
    );
    if (response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to derive required permits');
    }
    final list = jsonDecode(response.body) as List<dynamic>;
    final labels = <String>[];
    for (final e in list) {
      if (e is! Map) continue;
      final m = Map<String, dynamic>.from(e);
      if (m['isRequired'] != true) continue;
      final label = m['permitLabel']?.toString().trim();
      if (label != null && label.isNotEmpty) labels.add(label);
    }
    labels.sort();
    return labels;
  }

  Future<Map<String, dynamic>> getMasterPermit(String permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.get(
      Uri.parse('$workAuthMasterPermitUrl/$permitId'),
      headers: headers,
    );
    if (response.statusCode == 200) {
      return jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to load master permit');
  }

  Future<void> saveMasterPermit(String permitId, Map<String, dynamic> payload) async {
    final headers = await _auth.authHeaders();
    final response = await http.put(
      Uri.parse('$workAuthMasterPermitUrl/$permitId'),
      headers: headers,
      body: jsonEncode(payload),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to save master permit');
    }
  }

  /// Child work permit: safety commitments + structured form. Sets status to Captured when valid.
  Future<void> submitPermitChecklist(
    String permitId, {
    required List<Map<String, dynamic>> items,
    Map<String, String>? form,
  }) async {
    final headers = await _auth.authHeaders();
    headers['Content-Type'] = 'application/json';
    final body = <String, dynamic>{'items': items};
    if (form != null && form.isNotEmpty) {
      body['form'] = form;
    }
    final response = await http.patch(
      Uri.parse('$jobPermitsUrl/$permitId/checklist'),
      headers: headers,
      body: jsonEncode(body),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to submit permit');
    }
  }
}

class ExpiringPermitDto {
  final String id;
  final String permitTemplateName;
  final DateTime validTo;

  ExpiringPermitDto({required this.id, required this.permitTemplateName, required this.validTo});

  factory ExpiringPermitDto.fromJson(Map<String, dynamic> json) => ExpiringPermitDto(
        id: json['id'] as String,
        permitTemplateName: json['permitTemplateName'] as String? ?? '',
        validTo: DateTime.parse(json['validTo'] as String),
      );
}
