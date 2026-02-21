package za.co.capitalair.fieldtech.api

import com.google.gson.annotations.SerializedName

data class JobCardDto(
    @SerializedName("Id") val id: Int,
    @SerializedName("QuoteId") val quoteId: Int?,
    @SerializedName("JobCardReference") val jobCardReference: String,
    @SerializedName("QuoteReference") val quoteReference: String?,
    @SerializedName("Type") val type: String,
    @SerializedName("Status") val status: String,
    @SerializedName("Company") val company: String,
    @SerializedName("Registration") val registration: String,
    @SerializedName("FleetNumber") val fleetNumber: String?,
    @SerializedName("Make") val make: String?,
    @SerializedName("Model") val model: String?,
    @SerializedName("Colour") val colour: String?,
    @SerializedName("VinNumber") val vinNumber: String?,
    @SerializedName("TrackingUnitMake") val trackingUnitMake: String?,
    @SerializedName("Imei") val imei: String?,
    @SerializedName("SerialNumber") val serialNumber: String?,
    @SerializedName("Iccid") val iccid: String?,
    @SerializedName("SimNumber") val simNumber: String?,
    @SerializedName("CreatedAt") val createdAt: String?
)

data class UploadPhotoResponse(
    @SerializedName("ok") val ok: Boolean,
    @SerializedName("attachmentId") val attachmentId: Int?,
    @SerializedName("fileName") val fileName: String?,
    @SerializedName("addedAt") val addedAt: String?,
    @SerializedName("message") val message: String?
)
