# Tradion Technician App

Mobile app for field technicians to view assigned jobs, navigate to sites, and report location for live tracking.

## Setup

1. **Install Flutter** and ensure it's in your PATH.

2. **Generate platform folders** (android, ios):
   ```bash
   cd mobile/technician_app
   flutter create . --platforms=android,ios
   ```

3. **Add location permissions** (required for tracking):

   **Android** (`android/app/src/main/AndroidManifest.xml`): Add inside `<manifest>`:
   ```xml
   <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
   <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
   <uses-permission android:name="android.permission.CAMERA" />
   ```

   **iOS** (`ios/Runner/Info.plist`): Add inside `<dict>`:
   ```xml
   <key>NSLocationWhenInUseUsageDescription</key>
   <string>Your location is used to show your position to managers on the tracking map.</string>
   <key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
   <string>Your location is used for live technician tracking when you are on a job.</string>
   <key>NSCameraUsageDescription</key>
   <string>Take photos of parts and permit documents.</string>
   <key>NSPhotoLibraryUsageDescription</key>
   <string>Select photos for part documentation and permit uploads.</string>
   ```

4. **Run on a physical device** (API must be reachable from the phone):
   ```powershell
   .\run-dev.ps1
   ```
   This uses your machine's LAN IP (same as the frontend when accessed from another device). For emulator, use `flutter run` (defaults to localhost).

## Features

- **Login** – Sign in with technician credentials
- **Job list** – View assigned jobs sorted by priority (5 = highest)
- **Job detail** – Description, site, parts, permits, incidents
- **Navigate** – Open Google Maps to site address or coordinates
- **Status updates** – Start job, mark completed
- **Location reporting** – Automatically reports GPS position for admin tracking map
- **Permits** – Request permit, upload signed document (PDF or image)
- **Parts** – Add parts with old/new photos (camera)
- **Incidents** – Report incidents with description and severity

## Theme

DVCP colors: Yellow #FDCB00, Charcoal #2C2E33, White, Black.
