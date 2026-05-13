## Run technician app on an Android phone (USB debug)

### 1) Prereqs on Windows
- **Flutter SDK installed** and `flutter` available in your PATH
- **Android SDK Platform-Tools** installed and `adb` available in your PATH
- Optional but common: **Android Studio** (installs SDKs + accepts licenses)

Quick checks:

```powershell
flutter doctor -v
adb version
```

### 2) Phone setup
- Enable **Developer options**
- Enable **USB debugging**
- Plug phone in via USB
- Accept the “Allow USB debugging?” prompt on the phone

Verify the device shows up:

```powershell
adb devices
```

### 3) Ensure your phone can reach the API
Your backend runs on your PC on port **5020**. Your phone must reach it via your PC’s **LAN IP**, e.g.:

- `http://192.168.10.161:5020`

Test from the phone browser (on same Wi‑Fi):
- Open: `http://<PC_LAN_IP>:5020/api/auth/me` (should return 401/unauthorized, that’s fine — it proves reachability)

### 4) Run the app to your phone
From `mobile/technician_app/`:

```powershell
flutter pub get
flutter run --dart-define=API_URL=http://192.168.10.161:5020/api
```

Notes:
- The app reads the base URL from `--dart-define=API_URL=...` (see `lib/config/api_config.dart`).
- If you change networks, update the IP in the command.

### 5) If you want an installable APK later
Once it’s running, we can generate an APK for easy install without USB.

