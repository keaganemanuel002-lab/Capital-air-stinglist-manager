# Technician Android App

Native Android app for field technicians.

## Features

- Load open job cards from desktop technician API
- Pick image from phone
- Upload photo to selected job card

## API expected

- `GET /api/tech/job-cards/open`
- `POST /api/tech/job-cards/{jobCardId}/photos`

Header auth:

- `X-Tech-Key: <technician-api-key>`

## Build

1. Open this folder in Android Studio.
2. Sync Gradle.
3. Build APK from Android Studio.

## Runtime config

Inside app:

- API Base URL: `http://<desktop-ip>:5075`
- Technician API Key: from desktop app Settings.
