# Technician Mobile Portal (Android)

This app now hosts a mobile-friendly technician portal from the desktop app itself.
It also includes a native Android project:

- `mobile/TechnicianAndroidApp`

## What technicians can do

- View **open job cards**
- Upload **job card photos** from phone camera/gallery
- Add photo notes

Uploads are saved as `JobPhoto` attachments in the same database and attachments folder used by the desktop app.

## Enable and configure

1. Open desktop app.
2. Go to `Settings`.
3. In **Field Technician Mobile Access**:
   - Enable the API.
   - Confirm port (default `5075`).
   - Set or generate API key.
4. Restart desktop app.

## Technician usage

1. Connect phone to same Wi-Fi/LAN as desktop.
2. Open one portal URL shown in Settings (for example `http://<desktop-ip>:5075/technician`).
3. Enter API key.
4. Tap **Refresh Open Job Cards**.
5. Select/take photo and upload.

## Native Android APK (phase 2)

Use project: `mobile/TechnicianAndroidApp`

1. Install Android Studio.
2. Open folder `mobile/TechnicianAndroidApp`.
3. Let Gradle sync/download dependencies.
4. Build APK:
   - Android Studio menu: `Build` -> `Build Bundle(s) / APK(s)` -> `Build APK(s)`.
5. Install on technician phone.
6. In app, enter:
   - API Base URL: `http://<desktop-ip>:5075`
   - Technician API Key: from desktop `Settings`.

Notes:
- Phone and desktop must be on same network.
- Ensure desktop firewall allows inbound TCP on the configured technician API port.

## Desktop verification

- Open `Job Cards` -> select card -> `Documents`.
- Uploaded photos appear under attachments as `Job Photo`.

## API endpoints

- `GET /api/tech/health`
- `GET /api/tech/job-cards/open`
- `GET /api/tech/job-cards/{jobCardId}/photos`
- `POST /api/tech/job-cards/{jobCardId}/photos` (multipart form-data)

Auth: `X-Tech-Key` header (or `apiKey` query parameter).
