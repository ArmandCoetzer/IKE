import 'package:flutter/material.dart';

/// Primary and secondary actions should use the same control types app-wide
/// ([ThemeData.filledButtonTheme] / [outlinedButtonTheme] in [app.dart]).
/// Prefer these over mixing [ElevatedButton] so flows match the rest of the technician app.
abstract final class IkeButtons {
  IkeButtons._();

  static Widget primary({required VoidCallback? onPressed, required Widget child}) =>
      FilledButton(onPressed: onPressed, child: child);

  static Widget secondaryOutlined({required VoidCallback? onPressed, required Widget child}) =>
      OutlinedButton(onPressed: onPressed, child: child);
}
