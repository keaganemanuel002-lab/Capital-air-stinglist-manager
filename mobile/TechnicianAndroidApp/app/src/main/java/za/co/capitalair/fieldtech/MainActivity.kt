package za.co.capitalair.fieldtech

import android.content.Context
import android.net.Uri
import android.os.Bundle
import android.webkit.URLUtil
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import retrofit2.HttpException
import za.co.capitalair.fieldtech.api.ApiClientFactory
import za.co.capitalair.fieldtech.api.JobCardDto
import java.io.File

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    TechnicianApp()
                }
            }
        }
    }
}

@Composable
private fun TechnicianApp() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val prefs = remember { context.getSharedPreferences("field_tech_prefs", Context.MODE_PRIVATE) }

    var baseUrl by rememberSaveable { mutableStateOf(prefs.getString("base_url", "") ?: "") }
    var apiKey by rememberSaveable { mutableStateOf(prefs.getString("api_key", "") ?: "") }
    var technicianName by rememberSaveable { mutableStateOf(prefs.getString("tech_name", "") ?: "") }

    var loading by remember { mutableStateOf(false) }
    var statusMessage by remember { mutableStateOf("Enter API details and refresh open job cards.") }
    var statusError by remember { mutableStateOf(false) }

    var jobs by remember { mutableStateOf<List<JobCardDto>>(emptyList()) }
    val selectedPhotoUris = remember { mutableStateMapOf<Int, Uri>() }
    val photoNotes = remember { mutableStateMapOf<Int, String>() }
    val uploadStatuses = remember { mutableStateMapOf<Int, String>() }
    val uploadErrors = remember { mutableStateMapOf<Int, Boolean>() }
    var pickPhotoForJobId by remember { mutableStateOf<Int?>(null) }

    fun savePreferences() {
        prefs.edit()
            .putString("base_url", baseUrl.trim())
            .putString("api_key", apiKey.trim())
            .putString("tech_name", technicianName.trim())
            .apply()
    }

    val photoPicker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        val jobId = pickPhotoForJobId
        if (uri != null && jobId != null) {
            selectedPhotoUris[jobId] = uri
            uploadStatuses[jobId] = "Photo selected. Tap Upload."
            uploadErrors[jobId] = false
        }
        pickPhotoForJobId = null
    }

    suspend fun loadOpenJobCards() {
        savePreferences()

        if (baseUrl.trim().isEmpty() || apiKey.trim().isEmpty()) {
            statusError = true
            statusMessage = "API Base URL and API Key are required."
            return
        }

        loading = true
        statusError = false
        statusMessage = "Loading open job cards..."

        try {
            val service = ApiClientFactory.create(baseUrl)
            val rows = service.getOpenJobCards(apiKey.trim())
            jobs = rows
            statusMessage = "Loaded ${rows.size} open job card(s)."
            statusError = false
        } catch (ex: HttpException) {
            statusMessage = "Load failed: HTTP ${ex.code()} ${ex.message()}"
            statusError = true
        } catch (ex: Exception) {
            statusMessage = "Load failed: ${ex.message}"
            statusError = true
        } finally {
            loading = false
        }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(12.dp)
    ) {
        Text("Capital Air Field Technician", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(8.dp))

        OutlinedTextField(
            value = baseUrl,
            onValueChange = { baseUrl = it },
            label = { Text("API Base URL (http://192.168.x.x:5075)") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )
        Spacer(Modifier.height(8.dp))
        OutlinedTextField(
            value = apiKey,
            onValueChange = { apiKey = it },
            label = { Text("Technician API Key") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )
        Spacer(Modifier.height(8.dp))
        OutlinedTextField(
            value = technicianName,
            onValueChange = { technicianName = it },
            label = { Text("Technician Name") },
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )

        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                enabled = !loading,
                onClick = { scope.launch { loadOpenJobCards() } }
            ) {
                Text("Refresh Open Job Cards")
            }

            if (loading) {
                CircularProgressIndicator(modifier = Modifier.height(24.dp), strokeWidth = 2.dp)
            }
        }

        Spacer(Modifier.height(8.dp))
        Text(
            text = statusMessage,
            color = if (statusError) Color(0xFFB91C1C) else Color(0xFF334155),
            style = MaterialTheme.typography.bodyMedium
        )

        Spacer(Modifier.height(12.dp))

        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            items(jobs, key = { it.id }) { job ->
                val selectedUri = selectedPhotoUris[job.id]
                val notes = photoNotes[job.id] ?: ""
                val uploadText = uploadStatuses[job.id] ?: ""
                val isUploadError = uploadErrors[job.id] == true

                Card(
                    modifier = Modifier.fillMaxWidth(),
                    colors = CardDefaults.cardColors(containerColor = Color(0xFFF8FAFC))
                ) {
                    Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(
                            text = "${job.jobCardReference} - ${job.company}",
                            style = MaterialTheme.typography.titleMedium,
                            fontWeight = FontWeight.SemiBold
                        )
                        Text("Type: ${job.type}  |  Quote: ${job.quoteReference ?: "-"}")
                        Text("Reg: ${job.registration.ifBlank { "-" }}  |  Fleet: ${job.fleetNumber ?: "-"}")
                        Text("Vehicle: ${(job.make ?: "-")} ${(job.model ?: "-")} ${(job.colour ?: "")}")
                        Text("IMEI: ${job.imei ?: "-"}  |  ICCID: ${job.iccid ?: "-"}")

                        OutlinedTextField(
                            value = notes,
                            onValueChange = { photoNotes[job.id] = it },
                            label = { Text("Photo Notes (optional)") },
                            modifier = Modifier.fillMaxWidth()
                        )

                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = {
                                pickPhotoForJobId = job.id
                                photoPicker.launch("image/*")
                            }) {
                                Text("Pick Photo")
                            }

                            Button(
                                enabled = selectedUri != null && !loading,
                                onClick = {
                                    val uri = selectedPhotoUris[job.id]
                                    if (uri == null) {
                                        uploadStatuses[job.id] = "Select a photo first."
                                        uploadErrors[job.id] = true
                                        return@Button
                                    }

                                    scope.launch {
                                        uploadStatuses[job.id] = "Uploading photo..."
                                        uploadErrors[job.id] = false

                                        try {
                                            val service = ApiClientFactory.create(baseUrl)
                                            val photoPart = createPhotoPart(context, uri, job.id)
                                            val notesBody = notes.toRequestBody("text/plain".toMediaTypeOrNull())
                                            val techBody = technicianName.ifBlank { "FieldTech" }
                                                .toRequestBody("text/plain".toMediaTypeOrNull())

                                            val response = service.uploadPhoto(
                                                apiKey = apiKey.trim(),
                                                jobCardId = job.id,
                                                photo = photoPart,
                                                notes = notesBody,
                                                technicianName = techBody
                                            )

                                            if (!response.isSuccessful) {
                                                val errorBody = response.errorBody()?.string().orEmpty()
                                                throw IllegalStateException("HTTP ${response.code()} $errorBody")
                                            }

                                            val payload = response.body()
                                            uploadStatuses[job.id] = payload?.message ?: "Photo uploaded."
                                            uploadErrors[job.id] = false
                                            selectedPhotoUris.remove(job.id)
                                        } catch (ex: Exception) {
                                            uploadStatuses[job.id] = "Upload failed: ${ex.message}"
                                            uploadErrors[job.id] = true
                                        }
                                    }
                                }
                            ) {
                                Text("Upload")
                            }
                        }

                        if (selectedUri != null) {
                            Text(
                                text = "Selected: ${extractFileName(selectedUri)}",
                                color = Color(0xFF334155),
                                style = MaterialTheme.typography.bodySmall
                            )
                        }

                        if (uploadText.isNotBlank()) {
                            Text(
                                text = uploadText,
                                color = if (isUploadError) Color(0xFFB91C1C) else Color(0xFF166534),
                                style = MaterialTheme.typography.bodySmall
                            )
                        }
                    }
                }
            }
        }
    }

    LaunchedEffect(Unit) {
        if (baseUrl.isNotBlank() && apiKey.isNotBlank()) {
            loadOpenJobCards()
        }
    }
}

private fun createPhotoPart(context: Context, uri: Uri, jobCardId: Int): MultipartBody.Part {
    val contentResolver = context.contentResolver
    val inputStream = contentResolver.openInputStream(uri)
        ?: throw IllegalStateException("Could not read selected image.")

    val mimeType = contentResolver.getType(uri) ?: "image/jpeg"
    val extension = when {
        mimeType.contains("png") -> "png"
        mimeType.contains("webp") -> "webp"
        else -> "jpg"
    }

    val targetFile = File(context.cacheDir, "job_${jobCardId}_${System.currentTimeMillis()}.$extension")
    inputStream.use { input ->
        targetFile.outputStream().use { output ->
            input.copyTo(output)
        }
    }

    val requestFile = targetFile.asRequestBody(mimeType.toMediaTypeOrNull())
    return MultipartBody.Part.createFormData("photo", targetFile.name, requestFile)
}

private fun extractFileName(uri: Uri): String {
    val text = uri.toString()
    return if (URLUtil.isNetworkUrl(text)) text else text.substringAfterLast('/')
}
