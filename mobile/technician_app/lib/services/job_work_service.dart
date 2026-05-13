import 'dart:convert';
import 'package:http/http.dart' as http;
import 'auth_service.dart';
import '../config/api_config.dart';
import '../models/job_card.dart';

class JobWorkService {
  final AuthService _auth = AuthService();

  /// Create an incident report for a job (text only).
  Future<IncidentReportDto> createIncident(String jobId, String description, {String severity = 'Medium'}) async {
    final headers = await _auth.authHeaders();
    final response = await http.post(
      Uri.parse('$jobCardWorkUrl/$jobId/incidents'),
      headers: headers,
      body: jsonEncode({'description': description, 'severity': severity}),
    );
    if (response.statusCode == 201) {
      return IncidentReportDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw Exception('Failed to create incident');
  }

  /// Create an incident report with optional photos.
  Future<IncidentReportDto> createIncidentWithPhotos(
    String jobId,
    String description, {
    String severity = 'Medium',
    List<MapEntry<List<int>, String>>? photoBytesAndNames,
    bool blockJobDueToIncident = false,
  }) async {
    final token = await _auth.getToken();
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('$jobCardWorkUrl/$jobId/incidents/with-photos'),
    );
    request.headers['Authorization'] = 'Bearer $token';
    request.fields['description'] = description;
    request.fields['severity'] = severity;
    if (blockJobDueToIncident) {
      request.fields['blockJob'] = 'true';
    }
    if (photoBytesAndNames != null) {
      for (final entry in photoBytesAndNames) {
        request.files.add(http.MultipartFile.fromBytes(
          'photos',
          entry.key,
          filename: entry.value,
        ));
      }
    }
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 201) {
      return IncidentReportDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw Exception('Failed to create incident');
  }

  /// Add a part with optional photos (old part, new part).
  Future<JobPartDto> addPart(
    String jobId, {
    required String brand,
    String? serialNumber,
    String? description,
    List<int>? oldPartPhotoBytes,
    String? oldPartPhotoFileName,
    List<int>? newPartPhotoBytes,
    String? newPartPhotoFileName,
  }) async {
    final token = await _auth.getToken();
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('$jobCardWorkUrl/$jobId/parts'),
    );
    request.headers['Authorization'] = 'Bearer $token';
    request.fields['brand'] = brand;
    if (serialNumber != null && serialNumber.isNotEmpty) request.fields['serialNumber'] = serialNumber;
    if (description != null && description.isNotEmpty) request.fields['description'] = description;
    if (oldPartPhotoBytes != null && oldPartPhotoFileName != null) {
      request.files.add(http.MultipartFile.fromBytes(
        'oldPartPhoto',
        oldPartPhotoBytes,
        filename: oldPartPhotoFileName,
      ));
    }
    if (newPartPhotoBytes != null && newPartPhotoFileName != null) {
      request.files.add(http.MultipartFile.fromBytes(
        'newPartPhoto',
        newPartPhotoBytes,
        filename: newPartPhotoFileName,
      ));
    }
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode == 201) {
      return JobPartDto.fromJson(jsonDecode(response.body) as Map<String, dynamic>);
    }
    throw Exception('Failed to add part');
  }

  /// Fetch part photo bytes (auth required).
  Future<List<int>?> getPartPhoto(String jobId, String partId, {String kind = 'old'}) async {
    final headers = await _auth.authHeaders();
    final uri = Uri.parse('$jobCardWorkUrl/$jobId/parts/$partId/photo').replace(queryParameters: {'kind': kind});
    final response = await http.get(uri, headers: headers);
    if (response.statusCode == 200) return response.bodyBytes;
    return null;
  }

  /// Upload site photo (BeforeWork, MidWork, or AfterWork).
  Future<void> uploadSitePhoto(String jobId, String documentType, List<int> bytes, String fileName, {String? notes}) async {
    final token = await _auth.getToken();
    final request = http.MultipartRequest(
      'POST',
      Uri.parse('$jobCardWorkUrl/$jobId/documents/upload'),
    );
    request.headers['Authorization'] = 'Bearer $token';
    request.fields['documentType'] = documentType;
    if (notes != null && notes.isNotEmpty) {
      request.fields['notes'] = notes;
    }
    request.files.add(http.MultipartFile.fromBytes('file', bytes, filename: fileName));
    final streamed = await request.send();
    final response = await http.Response.fromStream(streamed);
    if (response.statusCode != 200) throw Exception('Failed to upload photo');
  }

  /// Fetch document/file bytes (auth required).
  Future<List<int>?> getDocumentFile(String jobId, String docId) async {
    final headers = await _auth.authHeaders();
    final response = await http.get(
      Uri.parse('$jobCardWorkUrl/$jobId/documents/$docId/file'),
      headers: headers,
    );
    if (response.statusCode == 200) return response.bodyBytes;
    return null;
  }
}
