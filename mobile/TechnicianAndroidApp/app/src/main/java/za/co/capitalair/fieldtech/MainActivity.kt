package za.co.capitalair.fieldtech

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.net.Uri
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AssistChip
import androidx.compose.material3.AssistChipDefaults
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.google.firebase.firestore.Source
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import retrofit2.HttpException
import za.co.capitalair.fieldtech.api.ApiClientFactory
import za.co.capitalair.fieldtech.api.JobCardDto
import za.co.capitalair.fieldtech.api.LoginRequest
import za.co.capitalair.fieldtech.firebase.FirebaseBridge
import java.io.File

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = AppBackground
                ) {
                    TechnicianApp()
                }
            }
        }
    }
}

private enum class AppScreen {
    Login,
    JobList,
    JobDetail
}

private enum class JobListTab {
    Open,
    Completed
}

private enum class VerificationPhotoType(val title: String, val noteTag: String) {
    Vehicle("Vehicle photo", "Vehicle"),
    Registration("Registration plate photo", "Registration"),
    Vin("VIN photo", "VIN"),
    TrackingUnit("Tracking unit photo", "TrackingUnit"),
    SerialIccid("Serial / ICCID photo", "SerialIccid")
}

private data class PhotoTarget(val jobId: Int, val type: VerificationPhotoType)

private const val AUTO_REFRESH_INTERVAL_MS = 5_000L

private val AppBackground = Color(0xFFF4F6FB)
private val PanelBackground = Color(0xFFFFFFFF)
private val SoftPanelBackground = Color(0xFFF8FAFF)
private val BrandPrimary = Color(0xFF4B5FD6)
private val BrandSecondary = Color(0xFFEDF1FF)
private val MutedText = Color(0xFF64748B)
private val SuccessText = Color(0xFF166534)
private val WarningText = Color(0xFFB45309)
private val ErrorText = Color(0xFFB91C1C)

private sealed class SelectedPhoto {
    data class LocalFile(val file: File) : SelectedPhoto()
    data class ContentUri(val uri: Uri) : SelectedPhoto()
}

@Composable
private fun TechnicianApp() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val prefs = remember { context.getSharedPreferences("field_tech_clean_prefs", Context.MODE_PRIVATE) }

    val baseUrl = remember { BuildConfig.TECH_API_BASE_URL.trim() }
    val usingFirebaseMode = remember { FirebaseBridge.isEnabled() }

    var technicianName by rememberSaveable { mutableStateOf(prefs.getString("tech_name", "") ?: "") }
    var technicianPin by rememberSaveable { mutableStateOf("") }
    var sessionToken by rememberSaveable { mutableStateOf(prefs.getString("session_token", "") ?: "") }
    var loggedInName by rememberSaveable { mutableStateOf(prefs.getString("session_name", "") ?: "") }

    var activeScreen by rememberSaveable {
        mutableStateOf(if (sessionToken.isNotBlank()) AppScreen.JobList.name else AppScreen.Login.name)
    }
    var selectedJobId by rememberSaveable { mutableStateOf(-1) }

    var loading by remember { mutableStateOf(false) }
    var statusMessage by remember { mutableStateOf("Log in to load job cards.") }
    var statusError by remember { mutableStateOf(false) }

    var openJobs by remember { mutableStateOf<List<JobCardDto>>(emptyList()) }
    var completedJobs by remember { mutableStateOf<List<JobCardDto>>(emptyList()) }
    var completedLoaded by remember { mutableStateOf(false) }
    var firebaseRealtimeHandle by remember { mutableStateOf<FirebaseBridge.JobCardsRealtimeHandle?>(null) }
    var firebaseRealtimeIncludesCompleted by remember { mutableStateOf(false) }
    var selectedTab by rememberSaveable { mutableStateOf(JobListTab.Open.name) }

    val jobNotes = remember { mutableStateMapOf<Int, String>() }
    val jobGridLocations = remember { mutableStateMapOf<Int, String>() }
    val uploadedVerificationTypesByJob = remember { mutableStateMapOf<Int, Set<VerificationPhotoType>>() }
    val selectedPhotosBySlot = remember { mutableStateMapOf<String, SelectedPhoto>() }

    var captureTarget by remember { mutableStateOf<PhotoTarget?>(null) }

    fun setStatus(message: String, isError: Boolean) {
        statusMessage = message
        statusError = isError
    }

    fun stopFirebaseRealtimeListener() {
        firebaseRealtimeHandle?.close()
        firebaseRealtimeHandle = null
        firebaseRealtimeIncludesCompleted = false
    }

    fun applyJobCardRows(
        openRows: List<JobCardDto>,
        completedRows: List<JobCardDto>? = null
    ) {
        openJobs = openRows
        if (completedRows != null) {
            completedJobs = completedRows
            completedLoaded = true
        }

        val effectiveCompletedRows = completedRows ?: completedJobs
        val allRows = openRows + effectiveCompletedRows

        allRows.forEach { job ->
            val existing = jobGridLocations[job.id]
            if (existing.isNullOrBlank() && !job.gridLocation.isNullOrBlank()) {
                jobGridLocations[job.id] = job.gridLocation
            }
        }

        val activeJobIds = allRows.map { it.id }.toSet()
        val staleUploadedKeys = uploadedVerificationTypesByJob.keys.filter { !activeJobIds.contains(it) }
        staleUploadedKeys.forEach { uploadedVerificationTypesByJob.remove(it) }
    }

    suspend fun startFirebaseRealtimeListener(
        includeCompleted: Boolean,
        forceReconnect: Boolean = false
    ) {
        if (!FirebaseBridge.isEnabled()) {
            return
        }

        val shouldIncludeCompleted = includeCompleted || completedLoaded
        if (!forceReconnect
            && firebaseRealtimeHandle != null
            && firebaseRealtimeIncludesCompleted == shouldIncludeCompleted
        ) {
            return
        }

        stopFirebaseRealtimeListener()

        try {
            val handle = FirebaseBridge.subscribeJobCards(
                context = context,
                includeCompleted = shouldIncludeCompleted,
                onOpenChanged = { rows ->
                    scope.launch {
                        applyJobCardRows(openRows = rows)
                    }
                },
                onCompletedChanged = { rows ->
                    scope.launch {
                        applyJobCardRows(openRows = openJobs, completedRows = rows)
                    }
                },
                onError = { error ->
                    scope.launch {
                        setStatus(
                            "Realtime sync warning: ${error.message}. Falling back to timed refresh.",
                            true
                        )
                        stopFirebaseRealtimeListener()
                    }
                }
            )

            firebaseRealtimeHandle = handle
            firebaseRealtimeIncludesCompleted = shouldIncludeCompleted
        } catch (ex: Exception) {
            setStatus(
                "Realtime sync unavailable: ${ex.message}. Falling back to timed refresh.",
                true
            )
            stopFirebaseRealtimeListener()
        }
    }

    fun saveSession() {
        prefs.edit()
            .putString("tech_name", technicianName.trim())
            .putString("session_token", sessionToken.trim())
            .putString("session_name", loggedInName.trim())
            .apply()
    }

    val takePhotoLauncher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicturePreview()) { bitmap ->
        val target = captureTarget
        if (bitmap == null || target == null) {
            captureTarget = null
            return@rememberLauncherForActivityResult
        }

        try {
            val file = saveBitmapToCache(context, bitmap, target.jobId)
            selectedPhotosBySlot[slotKey(target.jobId, target.type)] = SelectedPhoto.LocalFile(file)
            setStatus("${target.type.title} captured for ${jobReferenceFor(target.jobId, openJobs + completedJobs)}.", false)
        } catch (ex: Exception) {
            setStatus("Photo capture failed: ${ex.message}", true)
        }

        captureTarget = null
    }

    val notificationPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) {
        // If denied, app remains functional; push notifications will be suppressed by Android.
    }

    suspend fun loadJobCards(
        silent: Boolean = false,
        includeCompleted: Boolean = (selectedTab == JobListTab.Completed.name),
        forceRealtimeReconnect: Boolean = false
    ) {
        val token = sessionToken.trim()
        if (token.isBlank()) {
            setStatus("Please log in first.", true)
            activeScreen = AppScreen.Login.name
            return
        }

        if (!silent) {
            loading = true
            setStatus("Loading job cards...", false)
        }

        try {
            val openRows: List<JobCardDto>
            var completedRows = completedJobs
            if (FirebaseBridge.isEnabled()) {
                var cacheOpenRows = openJobs
                var cacheCompletedRows = completedJobs

                try {
                    cacheOpenRows = FirebaseBridge.getOpenJobCards(context, Source.CACHE)
                    if (includeCompleted) {
                        cacheCompletedRows = FirebaseBridge.getCompletedJobCards(context, Source.CACHE)
                        completedLoaded = true
                    }
                } catch (_: Exception) {
                    // Cache miss is expected on first launch; listener will stream server data next.
                }

                applyJobCardRows(
                    openRows = cacheOpenRows,
                    completedRows = if (includeCompleted) cacheCompletedRows else null
                )

                startFirebaseRealtimeListener(
                    includeCompleted = includeCompleted,
                    forceReconnect = forceRealtimeReconnect
                )

                if (!silent) {
                    val completedCount = if (includeCompleted) cacheCompletedRows.size else completedJobs.size
                    setStatus(
                        "Realtime sync active. Showing ${cacheOpenRows.size} open and $completedCount completed (cached/live).",
                        false
                    )
                }
                return
            } else {
                val service = ApiClientFactory.create(baseUrl)
                openRows = service.getOpenJobCards("Bearer $token")
                if (includeCompleted) {
                    completedRows = service.getCompletedJobCards("Bearer $token")
                    completedLoaded = true
                }
            }

            applyJobCardRows(
                openRows = openRows,
                completedRows = if (includeCompleted) completedRows else null
            )

            if (!silent) {
                val completedCount = if (includeCompleted) completedRows.size else completedJobs.size
                setStatus(
                    "Loaded ${openRows.size} open and $completedCount completed job card(s).",
                    false
                )
            }
        } catch (ex: HttpException) {
            if (ex.code() == 401) {
                stopFirebaseRealtimeListener()
                sessionToken = ""
                loggedInName = ""
                openJobs = emptyList()
                completedJobs = emptyList()
                saveSession()
                activeScreen = AppScreen.Login.name
                setStatus("Session expired. Please log in again.", true)
            } else {
                if (!silent) {
                    setStatus("Load failed: HTTP ${ex.code()} ${ex.message()}", true)
                }
            }
        } catch (ex: Exception) {
            if (!silent) {
                setStatus("Load failed: ${ex.message}", true)
            }
        } finally {
            if (!silent) {
                loading = false
            }
        }
    }

    suspend fun login() {
        if (technicianName.trim().isEmpty() || technicianPin.trim().isEmpty()) {
            setStatus("Username and password are required.", true)
            return
        }

        loading = true
        setStatus("Signing in...", false)

        try {
            if (FirebaseBridge.isEnabled()) {
                loggedInName = FirebaseBridge.login(
                    context = context,
                    technicianName = technicianName.trim(),
                    pin = technicianPin.trim()
                )
                // Local session marker for app navigation in Firebase mode.
                sessionToken = "firebase-session"
            } else {
                val service = ApiClientFactory.create(baseUrl)
                val response = service.login(
                    LoginRequest(
                        technicianName = technicianName.trim(),
                        pin = technicianPin.trim()
                    )
                )

                if (!response.isSuccessful) {
                    if (response.code() == 401) {
                        setStatus("Invalid username or password.", true)
                    } else if (response.code() == 403) {
                        setStatus("Access denied. Only Admin or Tech users can sign in.", true)
                    } else {
                        setStatus("Login failed: HTTP ${response.code()} ${response.message()}", true)
                    }
                    return
                }

                val payload = response.body()
                val token = payload?.token?.trim().orEmpty()
                if (token.isBlank()) {
                    setStatus("Login failed: token not returned.", true)
                    return
                }

                sessionToken = token
                loggedInName = payload?.technicianName?.trim().takeUnless { it.isNullOrBlank() }
                    ?: technicianName.trim()
            }

            technicianName = loggedInName
            technicianPin = ""
            saveSession()

            activeScreen = AppScreen.JobList.name
            setStatus("Logged in as $loggedInName.", false)
        } catch (ex: Exception) {
            setStatus("Login failed: ${ex.message}", true)
        } finally {
            loading = false
        }

        if (activeScreen == AppScreen.JobList.name) {
            loadJobCards(includeCompleted = false, forceRealtimeReconnect = true)
        }
    }

    fun logout() {
        stopFirebaseRealtimeListener()
        sessionToken = ""
        loggedInName = ""
        openJobs = emptyList()
        completedJobs = emptyList()
        completedLoaded = false
        selectedJobId = -1
        selectedTab = JobListTab.Open.name
        activeScreen = AppScreen.Login.name
        saveSession()
        setStatus("Signed out.", false)
    }

    suspend fun loadVerificationTypesForJob(jobId: Int) {
        if (uploadedVerificationTypesByJob.containsKey(jobId)) {
            return
        }

        val token = sessionToken.trim()
        if (token.isBlank()) {
            return
        }

        try {
            if (FirebaseBridge.isEnabled()) {
                val tags = FirebaseBridge.getUploadedVerificationTypes(context, jobId)
                uploadedVerificationTypesByJob[jobId] = tags.mapNotNull { parseVerificationType(it) }.toSet()
            } else {
                val service = ApiClientFactory.create(baseUrl)
                val state = service.getVerificationState("Bearer $token", jobId)
                uploadedVerificationTypesByJob[jobId] = state.uploadedVerificationTags
                    .mapNotNull { parseVerificationType(it) }
                    .toSet()
            }
        } catch (_: Exception) {
            // keep current in-memory value if lookup fails
        }
    }

    suspend fun uploadSinglePhoto(
        job: JobCardDto,
        type: VerificationPhotoType,
        selectedPhoto: SelectedPhoto,
        notes: String,
        gridLocation: String,
        isFinalInBatch: Boolean
    ) {
        val token = sessionToken.trim()
        if (token.isBlank()) {
            throw IllegalStateException("Session expired. Please log in again.")
        }

        val uploadNotes = buildUploadNotes(type, job, notes)
        val uploadTechnicianName = loggedInName.ifBlank { technicianName.ifBlank { "FieldTech" } }

        if (FirebaseBridge.isEnabled()) {
            val photoUri = when (selectedPhoto) {
                is SelectedPhoto.LocalFile -> Uri.fromFile(selectedPhoto.file)
                is SelectedPhoto.ContentUri -> selectedPhoto.uri
            }

            FirebaseBridge.submitPhoto(
                context = context,
                jobCard = job,
                photoUri = photoUri,
                notes = uploadNotes,
                technicianName = uploadTechnicianName,
                gridLocation = gridLocation,
                isFinalInBatch = isFinalInBatch
            )
            return
        }

        val uploadFile = when (selectedPhoto) {
            is SelectedPhoto.LocalFile -> selectedPhoto.file
            is SelectedPhoto.ContentUri -> copyUriToCache(context, selectedPhoto.uri, job.id)
        }

        val service = ApiClientFactory.create(baseUrl)
        val photoPart = MultipartBody.Part.createFormData(
            "photo",
            uploadFile.name,
            uploadFile.asRequestBody("image/jpeg".toMediaTypeOrNull())
        )

        val noteBody = uploadNotes
            .toRequestBody("text/plain".toMediaTypeOrNull())

        val techBody = uploadTechnicianName
            .toRequestBody("text/plain".toMediaTypeOrNull())
        val gridLocationBody = gridLocation
            .toRequestBody("text/plain".toMediaTypeOrNull())
        val finalInBatchBody = isFinalInBatch.toString()
            .toRequestBody("text/plain".toMediaTypeOrNull())

        val response = service.uploadPhoto(
            authorization = "Bearer $token",
            jobCardId = job.id,
            photo = photoPart,
            notes = noteBody,
            technicianName = techBody,
            gridLocation = gridLocationBody,
            isFinalInBatch = finalInBatchBody
        )

        if (response.code() == 401) {
            sessionToken = ""
            loggedInName = ""
            saveSession()
            throw IllegalStateException("Session expired. Please log in again.")
        }

        if (!response.isSuccessful) {
            val errorBody = response.errorBody()?.string().orEmpty()
            throw IllegalStateException("HTTP ${response.code()} $errorBody")
        }
    }

    suspend fun saveAllVerificationPhotos(job: JobCardDto) {
        val alreadyUploaded = uploadedVerificationTypesByJob[job.id] ?: emptySet()
        val missing = missingPhotoTypes(job.id, selectedPhotosBySlot, uploadedVerificationTypesByJob)
        if (missing.isNotEmpty()) {
            val missingText = missing.joinToString { it.title }
            setStatus("Capture all required photos before saving: $missingText.", true)
            return
        }

        val gridLocation = jobGridLocations[job.id].orEmpty().trim()
        val typesToUpload = VerificationPhotoType.entries.filter { type ->
            selectedPhotosBySlot.containsKey(slotKey(job.id, type))
        }

        if (typesToUpload.isEmpty()) {
            setStatus(
                "All required photos are already uploaded for ${displayValue(job.jobCardReference)}. Refresh to confirm completion.",
                false
            )
            loadJobCards(silent = true)
            return
        }

        loading = true
        setStatus("Saving verification photos for ${displayValue(job.jobCardReference)}...", false)

        try {
            val notes = jobNotes[job.id].orEmpty().trim()
            val lastUploadIndex = typesToUpload.lastIndex
            for ((index, type) in typesToUpload.withIndex()) {
                val selected = selectedPhotosBySlot[slotKey(job.id, type)]
                    ?: throw IllegalStateException("Missing ${type.title}.")
                uploadSinglePhoto(
                    job = job,
                    type = type,
                    selectedPhoto = selected,
                    notes = notes,
                    gridLocation = gridLocation,
                    isFinalInBatch = index == lastUploadIndex
                )
            }

            for (type in VerificationPhotoType.entries) {
                selectedPhotosBySlot.remove(slotKey(job.id, type))
            }

            uploadedVerificationTypesByJob[job.id] = (alreadyUploaded + VerificationPhotoType.entries).toSet()

            openJobs = openJobs.filterNot { it.id == job.id }
            val completedJob = job.copy(
                status = "Completed",
                gridLocation = if (gridLocation.isBlank()) job.gridLocation else gridLocation,
                completedAt = java.time.Instant.now().toString()
            )
            completedJobs = listOf(completedJob) + completedJobs.filterNot { it.id == job.id }
            completedLoaded = true

            val successMessage = if (FirebaseBridge.isEnabled()) {
                "Verification photos submitted for ${displayValue(job.jobCardReference)}. Job moved to Completed."
            } else {
                "Verification photos uploaded for ${displayValue(job.jobCardReference)}. Job moved to Completed."
            }
            setStatus(successMessage, false)
            selectedTab = JobListTab.Completed.name
            activeScreen = AppScreen.JobList.name
        } catch (ex: Exception) {
            setStatus("Save failed: ${ex.message}", true)
        } finally {
            loading = false
        }
    }

    val activeTab = if (selectedTab == JobListTab.Completed.name) JobListTab.Completed else JobListTab.Open
    val jobsForTab = if (activeTab == JobListTab.Open) openJobs else completedJobs
    val allJobs = openJobs + completedJobs
    val selectedJob = jobsForTab.firstOrNull { it.id == selectedJobId }
        ?: allJobs.firstOrNull { it.id == selectedJobId }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        when (activeScreen) {
            AppScreen.Login.name -> {
                Text(
                    text = "Capital Air Field Technician",
                    style = MaterialTheme.typography.headlineSmall,
                    fontWeight = FontWeight.SemiBold
                )
                Text(
                    text = if (usingFirebaseMode) "Cloud mode enabled" else "Server: $baseUrl",
                    color = MutedText,
                    style = MaterialTheme.typography.bodySmall
                )

                Card(
                    shape = RoundedCornerShape(16.dp),
                    colors = CardDefaults.cardColors(containerColor = PanelBackground),
                    elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(14.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        OutlinedTextField(
                            value = technicianName,
                            onValueChange = { technicianName = it },
                            label = { Text("Username") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true
                        )

                        OutlinedTextField(
                            value = technicianPin,
                            onValueChange = { technicianPin = it },
                            label = { Text("Password") },
                            modifier = Modifier.fillMaxWidth(),
                            singleLine = true,
                            visualTransformation = PasswordVisualTransformation()
                        )

                        Button(
                            enabled = !loading,
                            onClick = { scope.launch { login() } },
                            modifier = Modifier.fillMaxWidth(),
                            colors = ButtonDefaults.buttonColors(containerColor = BrandPrimary)
                        ) {
                            Text("Log In")
                        }

                        if (loading) {
                            Row(
                                horizontalArrangement = Arrangement.Center,
                                modifier = Modifier.fillMaxWidth()
                            ) {
                                CircularProgressIndicator(modifier = Modifier.height(22.dp), strokeWidth = 2.dp)
                            }
                        }
                    }
                }
            }

            AppScreen.JobList.name -> {
                Card(
                    shape = RoundedCornerShape(16.dp),
                    colors = CardDefaults.cardColors(containerColor = PanelBackground)
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(12.dp),
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column {
                                Text(
                                    text = "Job Cards",
                                    style = MaterialTheme.typography.headlineSmall,
                                    fontWeight = FontWeight.SemiBold
                                )
                                Text(
                                    text = "Signed in: $loggedInName",
                                    color = SuccessText,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }

                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                OutlinedButton(
                                    enabled = !loading,
                                    onClick = {
                                        scope.launch {
                                            loadJobCards(forceRealtimeReconnect = usingFirebaseMode)
                                        }
                                    }
                                ) {
                                    Text("Refresh")
                                }
                                OutlinedButton(enabled = !loading, onClick = { logout() }) {
                                    Text("Log Out")
                                }
                            }
                        }

                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            FilterChip(
                                selected = activeTab == JobListTab.Open,
                                onClick = { selectedTab = JobListTab.Open.name },
                                label = { Text("Open (${openJobs.size})") },
                                enabled = !loading,
                                colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = BrandSecondary,
                                    selectedLabelColor = BrandPrimary
                                )
                            )
                            FilterChip(
                                selected = activeTab == JobListTab.Completed,
                                onClick = {
                                    selectedTab = JobListTab.Completed.name
                                    if (!completedLoaded) {
                                        scope.launch {
                                            loadJobCards(
                                                silent = true,
                                                includeCompleted = true,
                                                forceRealtimeReconnect = usingFirebaseMode
                                            )
                                        }
                                    }
                                },
                                label = { Text("Completed (${completedJobs.size})") },
                                enabled = !loading,
                                colors = FilterChipDefaults.filterChipColors(
                                    selectedContainerColor = BrandSecondary,
                                    selectedLabelColor = BrandPrimary
                                )
                            )
                        }

                        Text(
                            text = if (activeTab == JobListTab.Open)
                                "Tap a job card to capture verification photos."
                            else
                                "Completed history is read-only.",
                            color = MutedText,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                }

                if (jobsForTab.isEmpty()) {
                    Card(
                        shape = RoundedCornerShape(16.dp),
                        colors = CardDefaults.cardColors(containerColor = PanelBackground),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(modifier = Modifier.padding(16.dp)) {
                            Text(
                                text = if (activeTab == JobListTab.Open) "No open job cards." else "No completed job cards.",
                                fontWeight = FontWeight.SemiBold
                            )
                            Text(
                                text = "Use Refresh to pull latest updates.",
                                color = MutedText,
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                }

                LazyColumn(
                    modifier = Modifier.weight(1f, fill = true),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    itemsIndexed(
                        items = jobsForTab,
                        key = { index, job ->
                            buildString {
                                append(job.id)
                                append('|')
                                append(job.jobCardReference ?: "")
                                append('|')
                                append(job.completedAt ?: job.createdAt ?: "")
                                append('|')
                                append(index)
                            }
                        }
                    ) { _, job ->
                        Card(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable {
                                    selectedJobId = job.id
                                    activeScreen = AppScreen.JobDetail.name
                                },
                            shape = RoundedCornerShape(14.dp),
                            colors = CardDefaults.cardColors(containerColor = PanelBackground),
                            elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
                        ) {
                            Column(
                                modifier = Modifier.padding(12.dp),
                                verticalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text(
                                        text = displayValue(job.jobCardReference),
                                        style = MaterialTheme.typography.titleMedium,
                                        fontWeight = FontWeight.SemiBold,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis
                                    )
                                    StatusPill(
                                        text = displayValue(job.status),
                                        isPositive = stringEquals(job.status, "Completed")
                                    )
                                }
                                Text("Client: ${displayValue(job.company)}")
                                Text("Reg: ${displayValue(job.registration)}")
                                Text(
                                    text = "Type: ${displayValue(job.type)} | Grid: ${displayValue(job.gridLocation)}",
                                    color = MutedText,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                        }
                    }
                }
            }

            AppScreen.JobDetail.name -> {
                if (selectedJob == null) {
                    Card(
                        shape = RoundedCornerShape(14.dp),
                        colors = CardDefaults.cardColors(containerColor = PanelBackground)
                    ) {
                        Column(modifier = Modifier.padding(14.dp)) {
                            Text("Selected job card not found.")
                            Spacer(Modifier.height(8.dp))
                            OutlinedButton(onClick = { activeScreen = AppScreen.JobList.name }) {
                                Text("Back to list")
                            }
                        }
                    }
                } else {
                    val job = selectedJob
                    val missingTypes = missingPhotoTypes(job.id, selectedPhotosBySlot, uploadedVerificationTypesByJob)
                    val allRequiredCaptured = missingTypes.isEmpty()
                    val isCompletedJob = activeTab == JobListTab.Completed
                        || stringEquals(job.status, "Completed")

                    LaunchedEffect(job.id) {
                        if (!jobGridLocations.containsKey(job.id)) {
                            jobGridLocations[job.id] = job.gridLocation.orEmpty()
                        }
                        loadVerificationTypesForJob(job.id)
                    }

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        OutlinedButton(onClick = { activeScreen = AppScreen.JobList.name }, enabled = !loading) {
                            Text("Back")
                        }
                        Text(
                            text = displayValue(job.jobCardReference),
                            style = MaterialTheme.typography.titleLarge,
                            fontWeight = FontWeight.SemiBold
                        )
                        StatusPill(text = displayValue(job.status), isPositive = isCompletedJob)
                    }

                    LazyColumn(
                        modifier = Modifier.weight(1f, fill = true),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        item {
                            Card(
                                shape = RoundedCornerShape(14.dp),
                                colors = CardDefaults.cardColors(containerColor = PanelBackground)
                            ) {
                                Column(
                                    modifier = Modifier.padding(12.dp),
                                    verticalArrangement = Arrangement.spacedBy(4.dp)
                                ) {
                                    Text("Job Card Details", fontWeight = FontWeight.SemiBold)
                                    HorizontalDivider()
                                    LabelValue("Client", displayValue(job.company))
                                    LabelValue("Registration", displayValue(job.registration))
                                    LabelValue("VIN", displayValue(job.vinNumber))
                                    LabelValue("Vehicle Colour", displayValue(job.colour))
                                    LabelValue("Make / Model", displayVehicle(job.make, job.model, null))
                                    LabelValue("Fleet", displayValue(job.fleetNumber))
                                    LabelValue("IMEI", displayValue(job.imei))
                                    LabelValue("ICCID", displayValue(job.iccid))
                                    LabelValue("Grid Location", displayValue(jobGridLocations[job.id] ?: job.gridLocation))
                                }
                            }
                        }

                        item {
                            OutlinedTextField(
                                value = jobGridLocations[job.id] ?: "",
                                onValueChange = { jobGridLocations[job.id] = it },
                                label = { Text("Grid Location (install point)") },
                                modifier = Modifier.fillMaxWidth(),
                                enabled = !isCompletedJob && !loading,
                                singleLine = true
                            )
                        }

                        if (!isCompletedJob) {
                            item {
                                OutlinedTextField(
                                    value = jobNotes[job.id] ?: "",
                                    onValueChange = { jobNotes[job.id] = it },
                                    label = { Text("Notes for desktop (optional)") },
                                    modifier = Modifier.fillMaxWidth()
                                )
                            }

                            item {
                                Card(
                                    shape = RoundedCornerShape(14.dp),
                                    colors = CardDefaults.cardColors(containerColor = SoftPanelBackground)
                                ) {
                                    Column(
                                        modifier = Modifier.padding(12.dp),
                                        verticalArrangement = Arrangement.spacedBy(4.dp)
                                    ) {
                                        Text(
                                            text = "Verification Photos (required)",
                                            style = MaterialTheme.typography.titleMedium,
                                            fontWeight = FontWeight.SemiBold
                                        )
                                        Text(
                                            text = "Capture each required photo, then tap Save once.",
                                            color = MutedText,
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                    }
                                }
                            }

                            item {
                                VerificationChecklist(
                                    jobId = job.id,
                                    selectedPhotosBySlot = selectedPhotosBySlot,
                                    uploadedTypes = uploadedVerificationTypesByJob[job.id] ?: emptySet()
                                )
                            }

                            items(VerificationPhotoType.entries, key = { it.name }) { type ->
                                val slotPhoto = selectedPhotosBySlot[slotKey(job.id, type)]
                                Card(
                                    shape = RoundedCornerShape(14.dp),
                                    colors = CardDefaults.cardColors(containerColor = PanelBackground)
                                ) {
                                    Column(
                                        modifier = Modifier.padding(12.dp),
                                        verticalArrangement = Arrangement.spacedBy(6.dp)
                                    ) {
                                        Text(type.title, fontWeight = FontWeight.SemiBold)
                                        Text(
                                            text = if (slotPhoto == null) "Not captured yet" else "Captured: ${selectedPhotoLabel(slotPhoto)}",
                                            color = if (slotPhoto == null) WarningText else SuccessText,
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                        Button(
                                            enabled = !loading,
                                            onClick = {
                                                captureTarget = PhotoTarget(job.id, type)
                                                takePhotoLauncher.launch(null)
                                            },
                                            colors = ButtonDefaults.buttonColors(containerColor = BrandPrimary)
                                        ) {
                                            Text(if (slotPhoto == null) "Take Photo" else "Retake Photo")
                                        }
                                    }
                                }
                            }
                        } else {
                            item {
                                Card(
                                    shape = RoundedCornerShape(14.dp),
                                    colors = CardDefaults.cardColors(containerColor = SoftPanelBackground)
                                ) {
                                    Column(
                                        modifier = Modifier.padding(12.dp),
                                        verticalArrangement = Arrangement.spacedBy(6.dp)
                                    ) {
                                        Text(
                                            text = "This job card is completed.",
                                            color = SuccessText,
                                            fontWeight = FontWeight.SemiBold
                                        )
                                        Text(
                                            text = "Photo capture is disabled for completed job cards.",
                                            color = MutedText,
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                    }
                                }
                            }
                        }
                    }

                    Spacer(Modifier.height(8.dp))
                    if (!isCompletedJob) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(
                                text = if (allRequiredCaptured) "All required photos captured." else "Missing: ${missingTypes.joinToString { it.title }}",
                                color = if (allRequiredCaptured) SuccessText else WarningText,
                                style = MaterialTheme.typography.bodySmall,
                                modifier = Modifier.weight(1f)
                            )
                            Spacer(Modifier.width(8.dp))
                            Button(
                                enabled = !loading,
                                onClick = { scope.launch { saveAllVerificationPhotos(job) } },
                                colors = ButtonDefaults.buttonColors(containerColor = BrandPrimary)
                            ) {
                                Text("Save")
                            }
                        }
                    } else {
                        Text(
                            text = "Completed job card history entry.",
                            color = MutedText,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                }
            }
        }

        StatusBanner(statusMessage = statusMessage, statusError = statusError)
    }

    LaunchedEffect(activeScreen, sessionToken) {
        if (activeScreen == AppScreen.JobList.name
            && sessionToken.isNotBlank()
            && openJobs.isEmpty()
            && completedJobs.isEmpty()
        ) {
            loadJobCards(silent = true)
        }
    }

    LaunchedEffect(usingFirebaseMode, sessionToken, activeScreen, selectedTab, completedLoaded) {
        if (!usingFirebaseMode) {
            stopFirebaseRealtimeListener()
            return@LaunchedEffect
        }

        if (sessionToken.isBlank() || activeScreen == AppScreen.Login.name) {
            stopFirebaseRealtimeListener()
            return@LaunchedEffect
        }

        startFirebaseRealtimeListener(
            includeCompleted = selectedTab == JobListTab.Completed.name || completedLoaded
        )
    }

    LaunchedEffect(sessionToken, selectedTab, activeScreen, usingFirebaseMode, firebaseRealtimeHandle) {
        while (isActive) {
            delay(AUTO_REFRESH_INTERVAL_MS)
            if (sessionToken.isBlank()) continue
            if (activeScreen == AppScreen.Login.name) continue
            if (loading) continue
            if (usingFirebaseMode && firebaseRealtimeHandle != null) continue

            loadJobCards(
                silent = true,
                includeCompleted = selectedTab == JobListTab.Completed.name
            )
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            stopFirebaseRealtimeListener()
        }
    }

    LaunchedEffect(Unit) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            val permissionGranted = ContextCompat.checkSelfPermission(
                context,
                Manifest.permission.POST_NOTIFICATIONS
            ) == PackageManager.PERMISSION_GRANTED

            if (!permissionGranted) {
                notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
            }
        }
    }
}

@Composable
private fun LabelValue(label: String, value: String) {
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            text = "$label:",
            fontWeight = FontWeight.SemiBold,
            color = MutedText,
            modifier = Modifier.width(120.dp)
        )
        Text(text = value)
    }
}

@Composable
private fun VerificationChecklist(
    jobId: Int,
    selectedPhotosBySlot: Map<String, SelectedPhoto>,
    uploadedTypes: Set<VerificationPhotoType>
) {
    Card(
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = SoftPanelBackground)
    ) {
        Column(
            modifier = Modifier.padding(12.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            Text(
                text = "Checklist",
                style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.SemiBold
            )

            VerificationPhotoType.entries.forEach { type ->
                val captured = selectedPhotosBySlot.containsKey(slotKey(jobId, type)) || uploadedTypes.contains(type)
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            text = if (captured) "[x]" else "[ ]",
                            color = if (captured) SuccessText else MutedText,
                            fontWeight = FontWeight.Bold
                        )
                        Spacer(Modifier.width(8.dp))
                        Text(
                            text = type.title,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }

                    Text(
                        text = if (captured && uploadedTypes.contains(type) && !selectedPhotosBySlot.containsKey(slotKey(jobId, type)))
                            "Uploaded"
                        else if (captured)
                            "Done"
                        else
                            "Missing",
                        color = if (captured) SuccessText else WarningText,
                        style = MaterialTheme.typography.bodySmall,
                        fontWeight = FontWeight.SemiBold
                    )
                }
            }
        }
    }
}

@Composable
private fun StatusPill(
    text: String,
    isPositive: Boolean
) {
    AssistChip(
        onClick = { },
        enabled = false,
        label = {
            Text(
                text = text,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        },
        colors = AssistChipDefaults.assistChipColors(
            disabledContainerColor = if (isPositive) Color(0xFFEAF7EE) else Color(0xFFF5F3FF),
            disabledLabelColor = if (isPositive) SuccessText else BrandPrimary
        )
    )
}

@Composable
private fun StatusBanner(
    statusMessage: String,
    statusError: Boolean
) {
    Card(
        shape = RoundedCornerShape(12.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (statusError) Color(0xFFFEEDEE) else SoftPanelBackground
        ),
        modifier = Modifier.fillMaxWidth()
    ) {
        Text(
            text = statusMessage,
            color = if (statusError) ErrorText else Color(0xFF334155),
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp)
        )
    }
}

private fun slotKey(jobId: Int, type: VerificationPhotoType): String = "$jobId:${type.name}"

private fun missingPhotoTypes(
    jobId: Int,
    selectedPhotosBySlot: Map<String, SelectedPhoto>,
    uploadedVerificationTypesByJob: Map<Int, Set<VerificationPhotoType>>
): List<VerificationPhotoType> {
    val uploadedTypes = uploadedVerificationTypesByJob[jobId] ?: emptySet()
    return VerificationPhotoType.entries.filter { type ->
        !uploadedTypes.contains(type) && !selectedPhotosBySlot.containsKey(slotKey(jobId, type))
    }
}

private fun jobReferenceFor(jobId: Int, jobs: List<JobCardDto>): String {
    val reference = jobs.firstOrNull { it.id == jobId }?.jobCardReference
    return displayValue(reference)
}

private fun saveBitmapToCache(context: Context, bitmap: Bitmap, jobId: Int): File {
    val file = File(context.cacheDir, "job_${jobId}_${System.currentTimeMillis()}.jpg")
    file.outputStream().use { output ->
        if (!bitmap.compress(Bitmap.CompressFormat.JPEG, 92, output)) {
            throw IllegalStateException("Failed to save captured photo.")
        }
    }
    return file
}

private fun copyUriToCache(context: Context, uri: Uri, jobId: Int): File {
    val input = context.contentResolver.openInputStream(uri)
        ?: throw IllegalStateException("Could not read selected image.")
    val file = File(context.cacheDir, "job_${jobId}_${System.currentTimeMillis()}_picked.jpg")
    input.use { source ->
        file.outputStream().use { target ->
            source.copyTo(target)
        }
    }
    return file
}

private fun displayValue(value: String?): String {
    val clean = value?.trim().orEmpty()
    return if (clean.isBlank()) "-" else clean
}

private fun displayVehicle(make: String?, model: String?, colour: String?): String {
    val parts = listOf(make, model, colour)
        .mapNotNull { it?.trim()?.takeIf { part -> part.isNotEmpty() } }
    return if (parts.isEmpty()) "-" else parts.joinToString(" ")
}

private fun selectedPhotoLabel(selectedPhoto: SelectedPhoto): String {
    return when (selectedPhoto) {
        is SelectedPhoto.LocalFile -> selectedPhoto.file.name
        is SelectedPhoto.ContentUri -> selectedPhoto.uri.toString().substringAfterLast('/')
    }
}

private fun stringEquals(left: String?, right: String): Boolean {
    return left?.trim()?.equals(right, ignoreCase = true) == true
}

private fun parseVerificationType(token: String): VerificationPhotoType? {
    val normalized = token.trim().lowercase()
    return when (normalized) {
        "vehicle" -> VerificationPhotoType.Vehicle
        "registration" -> VerificationPhotoType.Registration
        "vin" -> VerificationPhotoType.Vin
        "trackingunit", "tracking_unit", "tracking unit" -> VerificationPhotoType.TrackingUnit
        "serialiccid", "serial_iccid", "serial / iccid", "serial/iccid" -> VerificationPhotoType.SerialIccid
        else -> null
    }
}

private fun buildUploadNotes(type: VerificationPhotoType, job: JobCardDto, extraNotes: String): String {
    val base = when (type) {
        VerificationPhotoType.Vehicle -> "[Verification:Vehicle]"
        VerificationPhotoType.Registration -> "[Verification:Registration] Reg=${displayValue(job.registration)}"
        VerificationPhotoType.Vin -> "[Verification:VIN] VIN=${displayValue(job.vinNumber)}"
        VerificationPhotoType.TrackingUnit -> "[Verification:TrackingUnit] IMEI=${displayValue(job.imei)} ICCID=${displayValue(job.iccid)}"
        VerificationPhotoType.SerialIccid -> "[Verification:SerialIccid] Serial=${displayValue(job.serialNumber)} ICCID=${displayValue(job.iccid)}"
    }

    if (extraNotes.isBlank()) {
        return base
    }

    return "$base | Notes: $extraNotes"
}

