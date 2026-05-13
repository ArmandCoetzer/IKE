import 'dart:convert';
import 'package:http/http.dart' as http;
import 'auth_service.dart';
import '../config/api_config.dart';
import '../helpers/api_error_message.dart';
import '../models/job_card.dart';

class JobCardsService {
  final AuthService _auth = AuthService();

  Future<List<JobCardListDto>> getMyJobs() async {
    final headers = await _auth.authHeaders();
    final response = await http.get(
      Uri.parse('$jobCardsUrl?assignedToMe=true'),
      headers: headers,
    );
    if (response.statusCode != 200) {
      throw Exception('Failed to load jobs');
    }
    final list = jsonDecode(response.body) as List;
    return list.map((e) => JobCardListDto.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<JobCardWorkDto> getJobDetail(String id) async {
    final headers = await _auth.authHeaders();
    final response = await http.get(
      Uri.parse('$jobCardWorkUrl/$id'),
      headers: headers,
    );
    if (response.statusCode == 404) throw Exception('Job not found');
    if (response.statusCode != 200) {
      final msg = apiErrorMessageFromBody(response.body) ?? 'Failed to load job';
      throw Exception(msg);
    }
    return JobCardWorkDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  /// Records final client sign-off with a captured signature image (PNG bytes). Optional [signerPrintName] is stored with the document.
  Future<void> finalClientSignOff(String id, List<int> signaturePngBytes, {String? signerPrintName}) async {
    final uri = Uri.parse('$jobCardWorkUrl/$id/final-client-sign-off');
    final req = http.MultipartRequest('POST', uri);
    req.headers.addAll(await _auth.authHeaders());
    req.files.add(
      http.MultipartFile.fromBytes(
        'file',
        signaturePngBytes,
        filename: 'final-client-signoff.png',
      ),
    );
    final trimmed = signerPrintName?.trim();
    if (trimmed != null && trimmed.isNotEmpty) {
      req.fields['signerName'] = trimmed;
    }
    final streamed = await req.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 200) {
      final msg = apiErrorMessageFromBody(response.body) ?? 'Failed to record final client sign-off';
      throw Exception(msg);
    }
  }

  Future<JobCardListDto> updateStatus(String id, String status) async {
    final headers = await _auth.authHeaders();
    final response = await http.patch(
      Uri.parse('$jobCardsUrl/$id/status'),
      headers: headers,
      body: jsonEncode({'status': status}),
    );
    if (response.statusCode != 200) {
      final msg = apiErrorMessageFromBody(response.body) ?? 'Failed to update status';
      throw Exception(msg);
    }
    return JobCardListDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
  }

  /// Hides existing permits in the app (kept in DB); job switches to paper permit workflow.
  Future<void> activatePaperPermitMode(String jobCardId) async {
    final headers = await _auth.authHeaders();
    final response = await http.post(
      Uri.parse('$jobCardWorkUrl/$jobCardId/paper-permit-mode'),
      headers: headers,
      body: jsonEncode({'enable': true}),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      throw Exception(apiErrorMessageFromBody(response.body) ?? 'Failed to activate paper permit mode');
    }
  }

  /// Set the active permit technicians are working on (updates web view).
  Future<void> setActivePermit(String jobCardId, String? permitId) async {
    final headers = await _auth.authHeaders();
    final response = await http.patch(
      Uri.parse('$jobCardsUrl/$jobCardId'),
      headers: headers,
      body: jsonEncode({'activeJobPermitId': permitId}),
    );
    if (response.statusCode != 200) {
      final msg = apiErrorMessageFromBody(response.body) ?? 'Failed to set active permit';
      throw Exception(msg);
    }
  }

}
