# Technician Android App

Native Android app for field technicians.

## Features

- Login with desktop Username + Password
- Role-based access (`Tech` or `Admin` only)
- Load open job cards from desktop API
- Upload photos for selected job cards
- Optional Firebase photo submission bridge (hybrid mode)

## API expected

- `POST /api/tech/auth/login`
- `GET /api/tech/job-cards/open`
- `POST /api/tech/job-cards/{jobCardId}/photos`

Bearer auth after login:

- `Authorization: Bearer <session-token>`

## Build

1. Open this folder in Android Studio.
2. Sync Gradle.
3. Build APK from Android Studio.

## Build from terminal (Gradle wrapper)

From `mobile/TechnicianAndroidApp`:

- Windows: `./gradlew.bat assembleDebug`
- macOS/Linux: `./gradlew assembleDebug`

APK output:

- `app/build/outputs/apk/debug/app-debug.apk`

## Runtime/build config

Set non-secret defaults in `mobile/TechnicianAndroidApp/gradle.properties`.

Set real Firebase values in user-level Gradle properties (recommended):

- Windows: `%USERPROFILE%\.gradle\gradle.properties`
- macOS/Linux: `~/.gradle/gradle.properties`

or via environment variables with the same names.

Keys used by the app:

- `TECH_API_BASE_URL=http://<desktop-ip>:5075`
- `FIREBASE_ENABLED=false|true`
- `FIREBASE_API_KEY=...`
- `FIREBASE_APP_ID=...`
- `FIREBASE_PROJECT_ID=...`
- `FIREBASE_STORAGE_BUCKET=...`

If `FIREBASE_ENABLED=true`, photo uploads are submitted to Firebase (`photo_submissions`) for desktop import.
If `FIREBASE_ENABLED=false`, photos upload directly to desktop API.

When Firebase mode is enabled, desktop also publishes eligible users to Firestore collection `mobile_users`.

## Firestore/Storage rules

If login fails with `permission_denied`, deploy the repository rules from project root:

- `firebase use ca-sting-list-app`
- `firebase deploy --only firestore:rules,storage`
