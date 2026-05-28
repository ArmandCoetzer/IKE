import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ike_technician/app.dart';

void main() {
  testWidgets('App boots', (WidgetTester tester) async {
    await tester.pumpWidget(const IkeTechnicianApp());
    await tester.pump();
    expect(find.byType(MaterialApp), findsOneWidget);
  });
}
