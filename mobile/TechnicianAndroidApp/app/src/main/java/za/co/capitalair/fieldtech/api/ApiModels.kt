package za.co.capitalair.fieldtech.api

import com.google.gson.annotations.SerializedName

data class JobCardDto(
    @SerializedName(value = "id", alternate = ["Id"]) val id: Int,
    @SerializedName(value = "quoteId", alternate = ["QuoteId"]) val quoteId: Int?,
    @SerializedName(value = "jobCardReference", alternate = ["JobCardReference"]) val jobCardReference: String?,
    @SerializedName(value = "quoteReference", alternate = ["QuoteReference"]) val quoteReference: String?,
    @SerializedName(value = "type", alternate = ["Type"]) val type: String?,
    @SerializedName(value = "status", alternate = ["Status"]) val status: String?,
    @SerializedName(value = "company", alternate = ["Company"]) val company: String?,
    @SerializedName(value = "registration", alternate = ["Registration"]) val registration: String?,
    @SerializedName(value = "fleetNumber", alternate = ["FleetNumber"]) val fleetNumber: String?,
    @SerializedName(value = "make", alternate = ["Make"]) val make: String?,
    @SerializedName(value = "model", alternate = ["Model"]) val model: String?,
    @SerializedName(value = "colour", alternate = ["Colour"]) val colour: String?,
    @SerializedName(value = "vinNumber", alternate = ["VinNumber"]) val vinNumber: String?,
    @SerializedName(value = "gridLocation", alternate = ["GridLocation"]) val gridLocation: String?,
    @SerializedName(value = "trackingUnitMake", alternate = ["TrackingUnitMake"]) val trackingUnitMake: String?,
    @SerializedName(value = "imei", alternate = ["Imei"]) val imei: String?,
    @SerializedName(value = "serialNumber", alternate = ["SerialNumber"]) val serialNumber: String?,
    @SerializedName(value = "iccid", alternate = ["Iccid"]) val iccid: String?,
    @SerializedName(value = "simNumber", alternate = ["SimNumber"]) val simNumber: String?,
    @SerializedName(value = "createdAt", alternate = ["CreatedAt"]) val createdAt: String?,
    @SerializedName(value = "completedAt", alternate = ["CompletedAt"]) val completedAt: String?
)

data class UploadPhotoResponse(
    @SerializedName("ok") val ok: Boolean,
    @SerializedName(value = "attachmentId", alternate = ["AttachmentId"]) val attachmentId: Int?,
    @SerializedName(value = "fileName", alternate = ["FileName"]) val fileName: String?,
    @SerializedName(value = "addedAt", alternate = ["AddedAt"]) val addedAt: String?,
    @SerializedName("message") val message: String?
)

data class VerificationStateResponse(
    @SerializedName(value = "jobCardId", alternate = ["JobCardId"]) val jobCardId: Int,
    @SerializedName(value = "uploadedVerificationTags", alternate = ["UploadedVerificationTags"])
    val uploadedVerificationTags: List<String> = emptyList()
)

data class LoginRequest(
    @SerializedName("technicianName") val technicianName: String,
    @SerializedName("pin") val pin: String
)

data class LoginResponse(
    @SerializedName("ok") val ok: Boolean,
    @SerializedName(value = "token", alternate = ["Token"]) val token: String?,
    @SerializedName(value = "technicianName", alternate = ["TechnicianName"]) val technicianName: String?,
    @SerializedName(value = "expiresUtc", alternate = ["ExpiresUtc"]) val expiresUtc: String?
)
