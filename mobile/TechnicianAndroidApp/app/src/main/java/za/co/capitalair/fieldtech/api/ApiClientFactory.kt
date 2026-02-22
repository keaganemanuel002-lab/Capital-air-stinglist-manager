package za.co.capitalair.fieldtech.api

import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

object ApiClientFactory {
    fun create(baseUrl: String): TechnicianApiService {
        val normalizedBaseUrl = normalizeBaseUrl(baseUrl)
        val logger = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC }
        val okHttp = OkHttpClient.Builder()
            .addInterceptor(logger)
            .connectTimeout(20, TimeUnit.SECONDS)
            .readTimeout(45, TimeUnit.SECONDS)
            .writeTimeout(45, TimeUnit.SECONDS)
            .build()

        return Retrofit.Builder()
            .baseUrl(normalizedBaseUrl)
            .client(okHttp)
            .addConverterFactory(GsonConverterFactory.create())
            .build()
            .create(TechnicianApiService::class.java)
    }

    private fun normalizeBaseUrl(baseUrl: String): String {
        var value = baseUrl.trim()
        if (value.isEmpty()) {
            throw IllegalArgumentException("API Base URL is required.")
        }

        // Allow pasting the technician portal URL and strip it to root API host.
        val lower = value.lowercase()
        val techPathIndex = lower.indexOf("/technician")
        if (techPathIndex > 0) {
            value = value.substring(0, techPathIndex)
        }

        if (!value.startsWith("http://") && !value.startsWith("https://")) {
            value = "http://$value"
        }

        if (!value.endsWith("/")) {
            value += "/"
        }

        return value
    }
}
