# Technician Mobile + Firebase Hybrid Sync

This app supports a hybrid technician workflow:

- Technician APK logs in with **Username + Password** (desktop user account).
- Only active users with role **Tech** or **Admin** can sign in.
- Technician can upload job card photos.
- Desktop app imports uploaded photos into local Job Card attachments.

## What technicians can do

- View open job cards.
- Upload job card photos from camera/gallery.
- Add notes per photo.
- Log in with Username + Password.

## Desktop setup

1. Open desktop app.
2. Go to `Settings`.
3. In **Field Technician Mobile Access**:
   - Enable Technician API.
   - Confirm port (default `5075`).
4. In **Users** page:
   - Create technician user accounts.
   - Assign role `Tech` (or `Admin`).
   - Ensure user is active.
5. In **Firebase Hybrid Sync (Option 1)**:
   - Enable Firebase Sync.
   - Set Firebase Project ID.
   - Set Firebase Storage Bucket.
   - Select Firebase service account JSON file.
   - Set sync interval.
6. Restart desktop app.

## Android APK setup

Project: `mobile/TechnicianAndroidApp`

1. Open `mobile/TechnicianAndroidApp/gradle.properties` and set non-secret defaults only.
2. Set real Firebase values in user-level Gradle properties:
   - Windows: `%USERPROFILE%\.gradle\gradle.properties`
   - macOS/Linux: `~/.gradle/gradle.properties`
3. Add:
   - `TECH_API_BASE_URL=http://<desktop-ip>:5075`
   - `FIREBASE_ENABLED=true`
   - `FIREBASE_API_KEY=...`
   - `FIREBASE_APP_ID=...`
   - `FIREBASE_PROJECT_ID=...`
   - `FIREBASE_STORAGE_BUCKET=...`
4. Build APK (`.\gradlew.bat assembleDebug`).
5. Install APK on technician phone.
6. Technician logs in with Username + Password (Tech/Admin user).

## Firebase Rules (required)

If mobile login shows `permission_denied`, deploy the repo rules:

1. Open terminal in repo root.
2. Select project:
   - `firebase use ca-sting-list-app`
3. Deploy rules:
   - `firebase deploy --only firestore:rules,storage`

Rules files used:

- `firestore.rules`
- `storage.rules`

These rules allow authenticated technician app access to:

- `mobile_users` (read)
- `job_cards_open` / `job_cards_completed` (read)
- `photo_submissions` (create/read)
- storage path `job-cards/*` (read/write)

## Firebase collections used

- `job_cards_open/{jobCardId}`
  - Open job card snapshot published by desktop.
- `mobile_users/{usernameNorm}`
  - Active `Tech`/`Admin` users published by desktop for mobile auth.
- `photo_submissions/{autoId}`
  - Written by APK after photo upload.
  - Desktop imports and updates status.

Expected submission fields:

- `jobCardId`
- `jobCardReference`
- `technicianName`
- `notes`
- `storagePath` (`gs://...`)
- `importStatus` (`pending` -> `imported`/`failed`)
- `createdAtUtc`

Import result fields written by desktop:

- `importedAtUtc`
- `importMessage`
- `localAttachmentId`

## API endpoints (desktop)

- `POST /api/tech/auth/login`
- `GET /api/tech/job-cards/open`
- `POST /api/tech/job-cards/{jobCardId}/photos`

Auth:

- Login endpoint uses Username + Password payload.
- Other endpoints use `Authorization: Bearer <session-token>`.

## Notes

- If `FIREBASE_ENABLED=true`, mobile auth/job list/photo upload work from internet (desktop does not need to be on same LAN).
- If `FIREBASE_ENABLED=false`, phone and desktop must be on same LAN for login/job list/photo upload.
- Firebase photo sync requires valid Firebase config and service account permissions.
- Install/Transfer completion still requires at least one `JobPhoto` attachment.
