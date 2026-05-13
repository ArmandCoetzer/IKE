import 'dart:convert';
import 'package:http/http.dart' as http;
import 'auth_service.dart';
import '../config/api_config.dart';

class TrackingService {
  final AuthService _auth = AuthService();

  Future<void> reportLocation({
    required double latitude,
    required double longitude,
    String? jobCardId,
    double? accuracyMeters,
  }) async {
    final headers = await _auth.authHeaders();
    final body = <String, dynamic>{
      'latitude': latitude,
      'longitude': longitude,
    };
    if (jobCardId != null && jobCardId.isNotEmpty) {
      body['jobCardId'] = jobCardId;
    }
    if (accuracyMeters != null) {
      body['accuracyMeters'] = accuracyMeters;
    }
    final response = await http.post(
      Uri.parse('$trackingUrl/location'),
      headers: headers,
      body: jsonEncode(body),
    );
    if (response.statusCode != 204 && response.statusCode != 200) {
      // Don't throw - tracking is best-effort; log and continue
      // ignore: avoid_print
      print('Tracking report failed: ${response.statusCode}');
    }
  }
}
