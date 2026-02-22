package za.co.capitalair.fieldtech.firebase

import android.content.Context
import android.net.Uri
import android.util.Base64
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.Timestamp
import com.google.firebase.auth.FirebaseAuth
import com.google.firebase.firestore.FirebaseFirestore
import com.google.firebase.firestore.FirebaseFirestoreException
import com.google.firebase.firestore.ListenerRegistration
import com.google.firebase.firestore.Query
import com.google.firebase.firestore.Source
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.storage.FirebaseStorage
import com.google.firebase.storage.StorageException
import kotlinx.coroutines.tasks.await
import za.co.capitalair.fieldtech.BuildConfig
import za.co.capitalair.fieldtech.api.JobCardDto
import java.security.MessageDigest
import java.util.UUID
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec

object FirebaseBridge {
    private const val TECHNICIAN_TOPIC = "technician-jobs"
    private const val MAX_JOB_CARD_ROWS = 300L
    private const val PASSWORD_HASH_ITERATIONS = 120_000
    private const val PASSWORD_HASH_BYTES = 32

    class JobCardsRealtimeHandle(
        private val registrations: List<ListenerRegistration>
    ) {
        fun close() {
            registrations.forEach { registration ->
                runCatching { registration.remove() }
            }
        }
    }

    fun isEnabled(): Boolean = BuildConfig.FIREBASE_ENABLED

    suspend fun login(context: Context, technicianName: String, pin: String): String {
        if (!isEnabled()) {
            throw IllegalStateException("Firebase bridge is disabled in this APK build.")
        }

        if (technicianName.isBlank()) {
            throw IllegalStateException("Username is required.")
        }

        if (pin.isBlank()) {
            throw IllegalStateException("Password is required.")
        }

        val normalizedUsername = normalizeUserName(technicianName)
        if (normalizedUsername.isBlank()) {
            throw IllegalStateException("Username is required.")
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val userDoc = try {
            firestore.collection("mobile_users")
                .document(normalizedUsername)
                .get(Source.SERVER)
                .await()
        } catch (ex: Exception) {
            throw IllegalStateException(
                mapFirestoreLoginError(ex),
                ex
            )
        }

        if (!userDoc.exists()) {
            throw IllegalStateException(
                "Invalid username or password. If this is first setup, start desktop app and allow Firebase Sync to publish mobile users."
            )
        }

        val role = userDoc.stringField("role").orEmpty()
        val isActive = userDoc.getBoolean("isActive") == true
        val passwordHash = userDoc.stringField("passwordHash").orEmpty()
        val passwordSalt = userDoc.stringField("passwordSalt").orEmpty()

        if (!isActive || !canAccessTechnicianApp(role)) {
            throw IllegalStateException("Access denied. Only Admin or Tech users can sign in.")
        }

        if (!verifyPassword(pin.trim(), passwordSalt, passwordHash)) {
            throw IllegalStateException("Invalid username or password.")
        }

        ensureTopicSubscription()

        return userDoc.stringField("username") ?: technicianName.trim()
    }

    suspend fun getOpenJobCards(
        context: Context,
        source: Source = Source.DEFAULT
    ): List<JobCardDto> {
        if (!isEnabled()) {
            throw IllegalStateException("Firebase bridge is disabled in this APK build.")
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val snapshot = try {
            firestore.collection("job_cards_open")
                .orderBy("createdAtUtc", Query.Direction.DESCENDING)
                .limit(MAX_JOB_CARD_ROWS)
                .get(source)
                .await()
        } catch (ex: Exception) {
            throw IllegalStateException(
                mapFirestoreCollectionError(ex, "job_cards_open"),
                ex
            )
        }
        return mapJobCards(snapshot.documents)
    }

    suspend fun getCompletedJobCards(
        context: Context,
        source: Source = Source.DEFAULT
    ): List<JobCardDto> {
        if (!isEnabled()) {
            throw IllegalStateException("Firebase bridge is disabled in this APK build.")
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val snapshot = try {
            firestore.collection("job_cards_completed")
                .orderBy("completedAtUtc", Query.Direction.DESCENDING)
                .limit(MAX_JOB_CARD_ROWS)
                .get(source)
                .await()
        } catch (ex: Exception) {
            throw IllegalStateException(
                mapFirestoreCollectionError(ex, "job_cards_completed"),
                ex
            )
        }
        return mapJobCards(snapshot.documents)
    }

    suspend fun subscribeJobCards(
        context: Context,
        includeCompleted: Boolean,
        onOpenChanged: (List<JobCardDto>) -> Unit,
        onCompletedChanged: (List<JobCardDto>) -> Unit,
        onError: (Throwable) -> Unit
    ): JobCardsRealtimeHandle {
        if (!isEnabled()) {
            throw IllegalStateException("Firebase bridge is disabled in this APK build.")
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val registrations = mutableListOf<ListenerRegistration>()

        val openRegistration = firestore.collection("job_cards_open")
            .orderBy("createdAtUtc", Query.Direction.DESCENDING)
            .limit(MAX_JOB_CARD_ROWS)
            .addSnapshotListener { snapshot, error ->
                if (error != null) {
                    onError(
                        IllegalStateException(
                            mapFirestoreCollectionError(error, "job_cards_open"),
                            error
                        )
                    )
                    return@addSnapshotListener
                }

                val rows = mapJobCards(snapshot?.documents.orEmpty())
                onOpenChanged(rows)
            }
        registrations.add(openRegistration)

        if (includeCompleted) {
            val completedRegistration = firestore.collection("job_cards_completed")
                .orderBy("completedAtUtc", Query.Direction.DESCENDING)
                .limit(MAX_JOB_CARD_ROWS)
                .addSnapshotListener { snapshot, error ->
                    if (error != null) {
                        onError(
                            IllegalStateException(
                                mapFirestoreCollectionError(error, "job_cards_completed"),
                                error
                            )
                        )
                        return@addSnapshotListener
                    }

                    val rows = mapJobCards(snapshot?.documents.orEmpty())
                    onCompletedChanged(rows)
                }
            registrations.add(completedRegistration)
        }

        return JobCardsRealtimeHandle(registrations)
    }

    private fun mapJobCards(documents: List<com.google.firebase.firestore.DocumentSnapshot>): List<JobCardDto> {
        val rows = documents.mapNotNull { doc ->
            val id = doc.longField("jobCardId") ?: doc.id.toIntOrNull()
            if (id == null) {
                null
            } else {
                JobCardDto(
                    id = id,
                    quoteId = doc.longField("quoteId"),
                    jobCardReference = doc.stringField("jobCardReference"),
                    quoteReference = doc.stringField("quoteReference")
                        ?: doc.longField("quoteId")?.let { "QUO${it.toString().padStart(4, '0')}" },
                    type = doc.stringField("type"),
                    status = doc.stringField("status"),
                    company = doc.stringField("company"),
                    registration = doc.stringField("registration"),
                    fleetNumber = doc.stringField("fleetNumber"),
                    make = doc.stringField("make"),
                    model = doc.stringField("model"),
                    colour = doc.stringField("colour"),
                    vinNumber = doc.stringField("vinNumber"),
                    gridLocation = doc.stringField("gridLocation"),
                    trackingUnitMake = doc.stringField("trackingUnitMake"),
                    imei = doc.stringField("imei"),
                    serialNumber = doc.stringField("serialNumber"),
                    iccid = doc.stringField("iccid"),
                    simNumber = doc.stringField("simNumber"),
                    createdAt = doc.timestampField("createdAtUtc")
                        ?: doc.timestampField("desktopSyncedAtUtc")
                        ?: doc.stringField("createdAt"),
                    completedAt = doc.timestampField("completedAtUtc")
                        ?: doc.stringField("completedAt")
                )
            }
        }

        return rows.sortedByDescending { it.completedAt ?: it.createdAt.orEmpty() }
    }

    suspend fun submitPhoto(
        context: Context,
        jobCard: JobCardDto,
        photoUri: Uri,
        notes: String?,
        technicianName: String,
        gridLocation: String?,
        isFinalInBatch: Boolean
    ): String {
        if (!isEnabled()) {
            throw IllegalStateException("Firebase bridge is disabled in this APK build.")
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val extension = context.contentResolver.getType(photoUri)
            ?.substringAfterLast('/')
            ?.ifBlank { "jpg" }
            ?: "jpg"

        val objectPath = "job-cards/${jobCard.id}/${System.currentTimeMillis()}_${UUID.randomUUID()}.$extension"
        val candidateBuckets = resolveStorageBuckets(
            BuildConfig.FIREBASE_STORAGE_BUCKET,
            BuildConfig.FIREBASE_PROJECT_ID
        )
        val (resolvedBucket, objectRef) = uploadToFirstWorkingBucket(
            app = app,
            photoUri = photoUri,
            objectPath = objectPath,
            candidateBuckets = candidateBuckets
        )

        val payload = hashMapOf<String, Any?>(
            "jobCardId" to jobCard.id,
            "jobCardReference" to (jobCard.jobCardReference ?: "-"),
            "quoteReference" to (jobCard.quoteReference ?: "-"),
            "company" to (jobCard.company ?: "-"),
            "registration" to (jobCard.registration ?: "-"),
            "imei" to (jobCard.imei ?: ""),
            "iccid" to (jobCard.iccid ?: ""),
            "gridLocation" to (gridLocation?.trim().orEmpty()),
            "notes" to (notes ?: ""),
            "technicianName" to technicianName,
            "isFinalInBatch" to isFinalInBatch,
            "fileName" to objectRef.name,
            "storagePath" to "gs://$resolvedBucket/$objectPath",
            "importStatus" to "pending",
            "createdAtUtc" to Timestamp.now()
        )

        firestore.collection("photo_submissions").add(payload).await()
        return "Photo submitted to Firebase. Desktop will import it shortly."
    }

    suspend fun getUploadedVerificationTypes(context: Context, jobCardId: Int): Set<String> {
        if (!isEnabled()) {
            return emptySet()
        }

        val app = ensureFirebaseApp(context)
        ensureSignedIn(app)

        val firestore = FirebaseFirestore.getInstance(app)
        val snapshot = firestore.collection("photo_submissions")
            .whereEqualTo("jobCardId", jobCardId)
            .get()
            .await()

        val tags = mutableSetOf<String>()
        for (doc in snapshot.documents) {
            val importStatus = doc.stringField("importStatus")
            if (importStatus.equals("failed", ignoreCase = true)) {
                continue
            }

            val noteText = doc.stringField("notes").orEmpty()
            extractVerificationTag(noteText)?.let { tags.add(it) }
        }

        return tags
    }

    private fun com.google.firebase.firestore.DocumentSnapshot.stringField(name: String): String? {
        return getString(name)?.trim()?.takeIf { it.isNotEmpty() }
    }

    private fun com.google.firebase.firestore.DocumentSnapshot.longField(name: String): Int? {
        return getLong(name)?.toInt()
    }

    private fun com.google.firebase.firestore.DocumentSnapshot.timestampField(name: String): String? {
        return getTimestamp(name)?.toDate()?.toInstant()?.toString()
    }

    private fun ensureFirebaseApp(context: Context): FirebaseApp {
        val existing = FirebaseApp.getApps(context).firstOrNull { it.name == FirebaseApp.DEFAULT_APP_NAME }
        if (existing != null) return existing

        if (BuildConfig.FIREBASE_API_KEY.isBlank()
            || BuildConfig.FIREBASE_APP_ID.isBlank()
            || BuildConfig.FIREBASE_PROJECT_ID.isBlank()
            || BuildConfig.FIREBASE_STORAGE_BUCKET.isBlank())
        {
            throw IllegalStateException("Firebase config is incomplete. Fill FIREBASE_* values in gradle.properties and rebuild APK.")
        }

        val storageBucket = resolveStorageBuckets(
            BuildConfig.FIREBASE_STORAGE_BUCKET,
            BuildConfig.FIREBASE_PROJECT_ID
        ).firstOrNull()
            ?: throw IllegalStateException("Firebase storage bucket is invalid. Use a bucket like '<project>.firebasestorage.app' or '<project>.appspot.com'.")

        val options = FirebaseOptions.Builder()
            .setApiKey(BuildConfig.FIREBASE_API_KEY)
            .setApplicationId(BuildConfig.FIREBASE_APP_ID)
            .setProjectId(BuildConfig.FIREBASE_PROJECT_ID)
            .setStorageBucket(storageBucket)
            .build()

        return FirebaseApp.initializeApp(context, options)
    }

    private suspend fun ensureSignedIn(app: FirebaseApp) {
        val auth = FirebaseAuth.getInstance(app)
        if (auth.currentUser != null) return

        try {
            auth.signInAnonymously().await()
        } catch (ex: Exception) {
            val message = ex.message.orEmpty()
            if (message.contains("CONFIGURATION_NOT_FOUND", ignoreCase = true)) {
                throw IllegalStateException(
                    "Firebase Authentication is not configured. In Firebase Console, open Authentication, click Get started, and enable Anonymous sign-in."
                )
            }
            throw ex
        }
    }

    private suspend fun ensureTopicSubscription() {
        FirebaseMessaging.getInstance().subscribeToTopic(TECHNICIAN_TOPIC).await()
    }

    private fun extractVerificationTag(notes: String): String? {
        val start = notes.indexOf("[Verification:", ignoreCase = true)
        if (start < 0) {
            return null
        }

        val tokenStart = start + "[Verification:".length
        val end = notes.indexOf(']', tokenStart)
        if (end <= tokenStart) {
            return null
        }

        val raw = notes.substring(tokenStart, end).trim()
        return raw.takeIf { it.isNotEmpty() }
    }

    private fun normalizeUserName(username: String): String {
        val sb = StringBuilder()
        username.trim().forEach { ch ->
            if (ch.isLetterOrDigit()) {
                sb.append(ch.lowercaseChar())
            }
        }
        return sb.toString()
    }

    private fun canAccessTechnicianApp(role: String): Boolean {
        return role.equals("Admin", ignoreCase = true)
            || role.equals("Tech", ignoreCase = true)
            || role.equals("Technician", ignoreCase = true)
    }

    private fun mapFirestoreLoginError(error: Exception): String {
        val permissionDenied = error is FirebaseFirestoreException
            && error.code == FirebaseFirestoreException.Code.PERMISSION_DENIED
        if (permissionDenied) {
            return "Firebase login is blocked by Firestore Rules. Allow authenticated read on 'mobile_users'."
        }

        return error.message
            ?.takeIf { it.isNotBlank() }
            ?: "Unable to read mobile user from Firestore."
    }

    private fun mapFirestoreCollectionError(error: Exception, collectionName: String): String {
        val permissionDenied = error is FirebaseFirestoreException
            && error.code == FirebaseFirestoreException.Code.PERMISSION_DENIED
        if (permissionDenied) {
            return "Firestore Rules blocked access to '$collectionName'. Allow authenticated read for technician app."
        }

        return error.message
            ?.takeIf { it.isNotBlank() }
            ?: "Firestore request failed for '$collectionName'."
    }

    private fun verifyPassword(password: String, saltBase64: String, hashBase64: String): Boolean {
        if (password.isBlank() || saltBase64.isBlank() || hashBase64.isBlank()) {
            return false
        }

        return try {
            val saltBytes = Base64.decode(saltBase64, Base64.DEFAULT)
            val expectedHash = Base64.decode(hashBase64, Base64.DEFAULT)
            val spec = PBEKeySpec(password.toCharArray(), saltBytes, PASSWORD_HASH_ITERATIONS, PASSWORD_HASH_BYTES * 8)
            val factory = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
            val actualHash = factory.generateSecret(spec).encoded
            MessageDigest.isEqual(actualHash, expectedHash)
        } catch (_: Exception) {
            false
        }
    }

    private suspend fun uploadToFirstWorkingBucket(
        app: FirebaseApp,
        photoUri: Uri,
        objectPath: String,
        candidateBuckets: List<String>
    ): Pair<String, com.google.firebase.storage.StorageReference> {
        if (candidateBuckets.isEmpty()) {
            throw IllegalStateException(
                "Firebase storage bucket is not configured. Set FIREBASE_STORAGE_BUCKET in gradle.properties and rebuild."
            )
        }

        var lastError: Exception? = null
        for (bucket in candidateBuckets) {
            try {
                val storage = FirebaseStorage.getInstance(app, "gs://$bucket")
                val objectRef = storage.reference.child(objectPath)
                objectRef.putFile(photoUri).await()
                return bucket to objectRef
            } catch (ex: Exception) {
                lastError = ex
            }
        }

        throw mapStorageUploadError(lastError, candidateBuckets)
    }

    private fun mapStorageUploadError(
        error: Exception?,
        candidateBuckets: List<String>
    ): IllegalStateException {
        val storageEx = error as? StorageException
        val reason = when (storageEx?.errorCode) {
            StorageException.ERROR_BUCKET_NOT_FOUND ->
                "Firebase storage bucket was not found."
            StorageException.ERROR_NOT_AUTHENTICATED ->
                "Firebase storage access denied (not authenticated)."
            StorageException.ERROR_NOT_AUTHORIZED ->
                "Firebase storage access denied by security rules."
            else ->
                error?.message?.takeIf { it.isNotBlank() }
                    ?: "Unknown Firebase storage upload error."
        }

        return IllegalStateException(
            "$reason Tried bucket(s): ${candidateBuckets.joinToString()}." +
                " Update FIREBASE_STORAGE_BUCKET in gradle.properties and rebuild the APK."
        )
    }

    private fun resolveStorageBuckets(rawBucket: String, projectId: String): List<String> {
        val cleanBucket = normalizeBucketName(rawBucket)
        val cleanProjectId = projectId.trim().lowercase()

        val buckets = mutableListOf<String>()
        if (cleanBucket.isNotBlank()) {
            buckets.add(cleanBucket)
            if (!cleanBucket.contains('.')) {
                buckets.add("$cleanBucket.firebasestorage.app")
                buckets.add("$cleanBucket.appspot.com")
            }
        }

        if (cleanProjectId.isNotBlank()) {
            buckets.add("$cleanProjectId.firebasestorage.app")
            buckets.add("$cleanProjectId.appspot.com")
        }

        return buckets
            .map { normalizeBucketName(it) }
            .filter { it.isNotBlank() }
            .distinct()
    }

    private fun normalizeBucketName(bucketValue: String): String {
        var value = bucketValue.trim()
        if (value.startsWith("gs://", ignoreCase = true)) {
            value = value.substring(5)
        }

        val slashIndex = value.indexOf('/')
        if (slashIndex > 0) {
            value = value.substring(0, slashIndex)
        }

        return value.trim().lowercase()
    }
}
