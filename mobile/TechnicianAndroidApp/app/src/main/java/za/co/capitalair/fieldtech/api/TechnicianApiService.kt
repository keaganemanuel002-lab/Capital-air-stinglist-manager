package za.co.capitalair.fieldtech.api

import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Path

interface TechnicianApiService {
    @GET("api/tech/job-cards/open")
    suspend fun getOpenJobCards(
        @Header("X-Tech-Key") apiKey: String
    ): List<JobCardDto>

    @Multipart
    @POST("api/tech/job-cards/{jobCardId}/photos")
    suspend fun uploadPhoto(
        @Header("X-Tech-Key") apiKey: String,
        @Path("jobCardId") jobCardId: Int,
        @Part photo: MultipartBody.Part,
        @Part("notes") notes: RequestBody,
        @Part("technicianName") technicianName: RequestBody
    ): Response<UploadPhotoResponse>
}
