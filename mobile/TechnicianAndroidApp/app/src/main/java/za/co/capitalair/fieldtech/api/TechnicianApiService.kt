package za.co.capitalair.fieldtech.api

import okhttp3.MultipartBody
import okhttp3.RequestBody
import retrofit2.Response
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.Multipart
import retrofit2.http.Body
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Path

interface TechnicianApiService {
    @POST("api/tech/auth/login")
    suspend fun login(
        @Body request: LoginRequest
    ): Response<LoginResponse>

    @GET("api/tech/job-cards/open")
    suspend fun getOpenJobCards(
        @Header("Authorization") authorization: String
    ): List<JobCardDto>

    @GET("api/tech/job-cards/completed")
    suspend fun getCompletedJobCards(
        @Header("Authorization") authorization: String
    ): List<JobCardDto>

    @GET("api/tech/job-cards/{jobCardId}/verification-state")
    suspend fun getVerificationState(
        @Header("Authorization") authorization: String,
        @Path("jobCardId") jobCardId: Int
    ): VerificationStateResponse

    @Multipart
    @POST("api/tech/job-cards/{jobCardId}/photos")
    suspend fun uploadPhoto(
        @Header("Authorization") authorization: String,
        @Path("jobCardId") jobCardId: Int,
        @Part photo: MultipartBody.Part,
        @Part("notes") notes: RequestBody,
        @Part("technicianName") technicianName: RequestBody,
        @Part("gridLocation") gridLocation: RequestBody,
        @Part("isFinalInBatch") isFinalInBatch: RequestBody
    ): Response<UploadPhotoResponse>
}
