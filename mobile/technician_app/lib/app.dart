import 'dart:async';
import 'package:flutter/material.dart';
import 'screens/login_screen.dart';
import 'screens/job_list_screen.dart';
import 'services/auth_service.dart';

/// Ian Kleyn Electrical technician app — IKE palette (red, black, white).
class AppColors {
  static const Color brandRed = Color(0xFFE31837);
  static const Color charcoal = Color(0xFF0A0A0A);
  static const Color white = Color(0xFFFFFFFF);
  static const Color black = Color(0xFF000000);
}

class IkeTechnicianApp extends StatefulWidget {
  const IkeTechnicianApp({super.key});

  @override
  State<IkeTechnicianApp> createState() => _IkeTechnicianAppState();
}

class _IkeTechnicianAppState extends State<IkeTechnicianApp>
    with WidgetsBindingObserver {
  final _auth = AuthService();
  final _navigatorKey = GlobalKey<NavigatorState>();
  static const Duration _sessionIdleTimeout = Duration(minutes: 30);
  Timer? _idleTimer;
  Timer? _authHeartbeat;
  DateTime? _pausedAt;
  bool _loggingOut = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _syncAuthAndTimer();
    _authHeartbeat = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _syncAuthAndTimer(),
    );
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _idleTimer?.cancel();
    _authHeartbeat?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.inactive ||
        state == AppLifecycleState.detached) {
      _pausedAt = DateTime.now();
      return;
    }

    if (state == AppLifecycleState.resumed) {
      _handleResume();
    }
  }

  void _recordActivity() {
    _syncAuthAndTimer();
  }

  Future<void> _handleResume() async {
    final pausedAt = _pausedAt;
    _pausedAt = null;
    if (pausedAt != null && DateTime.now().difference(pausedAt) >= _sessionIdleTimeout) {
      await _logoutAndReturnToLogin();
      return;
    }
    _syncAuthAndTimer();
  }

  Future<void> _syncAuthAndTimer() async {
    if (!mounted) return;
    final hasToken = await _auth.hasValidToken();
    if (!hasToken) {
      _idleTimer?.cancel();
      _idleTimer = null;
      return;
    }
    _idleTimer?.cancel();
    _idleTimer = Timer(_sessionIdleTimeout, () async {
      await _logoutAndReturnToLogin();
    });
  }

  Future<void> _logoutAndReturnToLogin() async {
    if (_loggingOut) return;
    _loggingOut = true;
    try {
      final hasToken = await _auth.hasValidToken();
      if (!hasToken) return;
      await _auth.logout();
      _idleTimer?.cancel();
      _idleTimer = null;
      _navigatorKey.currentState?.pushAndRemoveUntil(
        MaterialPageRoute(builder: (_) => const LoginScreen()),
        (route) => false,
      );
    } finally {
      _loggingOut = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      navigatorKey: _navigatorKey,
      title: 'IKE Technician',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: AppColors.brandRed,
          primary: AppColors.charcoal,
          secondary: AppColors.brandRed,
          surface: AppColors.white,
          onPrimary: AppColors.white,
          onSecondary: AppColors.white,
          onSurface: AppColors.charcoal,
        ),
        useMaterial3: true,
        appBarTheme: const AppBarTheme(
          backgroundColor: AppColors.charcoal,
          foregroundColor: AppColors.white,
          elevation: 0,
        ),
        elevatedButtonTheme: ElevatedButtonThemeData(
          style: ElevatedButton.styleFrom(
            backgroundColor: AppColors.brandRed,
            foregroundColor: AppColors.white,
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            backgroundColor: AppColors.brandRed,
            foregroundColor: AppColors.white,
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            foregroundColor: AppColors.charcoal,
            side: const BorderSide(color: AppColors.brandRed),
          ),
        ),
      ),
      builder: (context, child) {
        return Listener(
          behavior: HitTestBehavior.translucent,
          onPointerDown: (_) => _recordActivity(),
          onPointerMove: (_) => _recordActivity(),
          onPointerUp: (_) => _recordActivity(),
          child: child ?? const SizedBox.shrink(),
        );
      },
      home: const AuthGate(),
    );
  }
}

class AuthGate extends StatelessWidget {
  const AuthGate({super.key});

  Future<bool> _initAuth() async {
    final auth = AuthService();
    if (!await auth.hasValidToken()) return false;
    await auth.refreshUser(); // Ensures userId etc. are loaded (e.g. after app update)
    return true;
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<bool>(
      future: _initAuth(),
      builder: (context, snapshot) {
        if (snapshot.connectionState == ConnectionState.waiting) {
          return const Scaffold(
            body: Center(child: CircularProgressIndicator()),
          );
        }
        if (snapshot.data == true) {
          return const JobListScreen();
        }
        return const LoginScreen();
      },
    );
  }
}
