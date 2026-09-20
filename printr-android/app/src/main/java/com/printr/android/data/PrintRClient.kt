package com.printr.android.data

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.Locale
import org.json.JSONObject
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

class PrintRClient(
    private val httpClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .build()
) {
    suspend fun upload(
        context: Context,
        pairing: PairingDetails,
        file: SelectedFile,
        options: PrintOptions
    ): PrintJobResponse = withContext(Dispatchers.IO) {
        require(SupportedDocumentTypes.isSupported(file.displayName)) { "Unsupported file. Choose PDF, an image, text, CSV or a supported Office document." }
        val cached = copyUriToCache(context, file)
        try {
            val body = buildMultipartBody(cached, file.displayName, file.mimeType, options)

            val request = Request.Builder()
                .url("${pairing.baseUrl}/print")
                .header("X-PrintR-Token", pairing.token)
                .post(body)
                .build()

            clientFor(pairing).newCall(request).execute().use { response ->
                val text = response.body?.string().orEmpty()
                if (!response.isSuccessful) {
                    throw IllegalStateException(toUsefulError(response.code, text))
                }
                parseJobResponse(text)
            }
        } finally {
            cached.delete()
        }
    }

    internal fun buildMultipartBody(
        cached: File,
        displayName: String,
        mimeType: String?,
        options: PrintOptions
    ): MultipartBody =
        MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .addFormDataPart("printerName", options.printerName)
            .addFormDataPart("copies", options.copies.ifBlank { "1" })
            .addFormDataPart("colorMode", options.colorMode)
            .addFormDataPart("duplex", (options.duplex || options.duplexMode != DuplexMode.None.wireValue).toString())
            .addFormDataPart("duplexMode", options.duplexMode)
            .addFormDataPart("orientation", options.orientation)
            .addFormDataPart("paperSize", options.paperSize)
            .addFormDataPart("pageRange", options.pageRange)
            .addFormDataPart(
                "file",
                sanitizeUploadFileName(displayName),
                cached.asRequestBody(SupportedDocumentTypes.uploadMimeType(displayName, mimeType).toMediaTypeOrNull())
            )
            .build()

    suspend fun health(pairing: PairingDetails): Boolean = withContext(Dispatchers.IO) {
        val request = Request.Builder().url("${pairing.baseUrl}/health").get().build()
        clientFor(pairing).newCall(request).execute().use { it.isSuccessful }
    }

    suspend fun pairTest(pairing: PairingDetails): PairTestResult = withContext(Dispatchers.IO) {
        val body = JSONObject().put("token", pairing.token).toString()
            .toRequestBody("application/json".toMediaTypeOrNull())
        val request = Request.Builder().url("${pairing.baseUrl}/pair/test").post(body).build()
        clientFor(pairing).newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                throw IllegalStateException(if (response.code == 401) "Wrong pairing token." else text.ifBlank { "Pairing failed with HTTP ${response.code}" })
            }
            val json = JSONObject(text)
            PairTestResult(
                success = json.optBoolean("success"),
                instanceId = json.optString("instanceId"),
                computerName = json.optString("computerName"),
                message = json.optString("message")
            )
        }
    }

    suspend fun printers(pairing: PairingDetails): List<PrinterInfo> = withContext(Dispatchers.IO) {
        val request = Request.Builder()
            .url("${pairing.baseUrl}/printers")
            .header("X-PrintR-Token", pairing.token)
            .get()
            .build()

        clientFor(pairing).newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                throw IllegalStateException(toUsefulError(response.code, text))
            }
            parsePrinters(text)
        }
    }

    suspend fun job(pairing: PairingDetails, jobId: String): PrintJobResponse = withContext(Dispatchers.IO) {
        val request = Request.Builder()
            .url("${pairing.baseUrl}/jobs/$jobId")
            .header("X-PrintR-Token", pairing.token)
            .get()
            .build()

        clientFor(pairing).newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                throw IllegalStateException(toUsefulError(response.code, text))
            }
            parseJobResponse(text)
        }
    }

    private fun toUsefulError(code: Int, body: String): String = when (code) {
        401 -> "Wrong token. Re-pair this computer from PrintR Agent."
        403 -> "Connection blocked. Check that both devices are on the same Wi-Fi and Windows Firewall allows PrintR on Private networks."
        404 -> "Print job was not found on the paired computer."
        415 -> "Unsupported file type."
        413 -> "File exceeds the computer's upload limit (at most 100 MB)."
        else -> runCatching { JSONObject(body).optString("error") }.getOrNull()?.takeIf { it.isNotBlank() }
            ?: body.ifBlank { "Request failed with HTTP $code" }
    }

    private fun clientFor(pairing: PairingDetails): OkHttpClient {
        if (!pairing.scheme.equals("https", ignoreCase = true)) return httpClient

        val fingerprint = normalizeTlsFingerprint(pairing.tlsFingerprint.orEmpty())
        require(fingerprint.length == 64) { "This secure pairing is missing the TLS fingerprint. Check the PrintR Agent pairing details." }
        val trustManager = FingerprintTrustManager(fingerprint)
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf(trustManager), null)
        return httpClient.newBuilder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            // The QR code pins the exact certificate, so the local IP address does not need a public DNS name.
            .hostnameVerifier { _, _ -> true }
            .build()
    }

    private fun copyUriToCache(context: Context, selected: SelectedFile): File {
        val safeName = sanitizeUploadFileName(selected.displayName)
        val out = File.createTempFile("upload-", "-$safeName", context.cacheDir)
        try {
        context.contentResolver.openInputStream(selected.uri).use { input ->
            requireNotNull(input) { "Could not open selected file." }
            out.outputStream().use { output ->
                val buffer = ByteArray(8192)
                var total = 0L
                var count: Int
                while (input.read(buffer).also { count = it } != -1) {
                    total += count
                    require(total <= 100L * 1024 * 1024) { "File exceeds the 100 MB upload limit." }
                    output.write(buffer, 0, count)
                }
                require(total > 0) { "Selected file is empty." }
            }
        }
        return out
        } catch (e: Exception) { out.delete(); throw e }
    }

    internal fun sanitizeUploadFileName(name: String): String =
        name.replace(Regex("[^A-Za-z0-9._-]"), "_").ifBlank { "upload.bin" }

    private fun parseJobResponse(json: String): PrintJobResponse {
        val objectJson = JSONObject(json)
        val warningArray = objectJson.optJSONArray("warnings")
        val warnings = if (warningArray == null) {
            emptyList()
        } else {
            (0 until warningArray.length()).mapNotNull { warningArray.optString(it).takeIf(String::isNotBlank) }
        }
        return PrintJobResponse(
            jobId = objectJson.optString("jobId"),
            status = objectJson.optString("status"),
            message = objectJson.optString("message"),
            warnings = warnings
        )
    }

    internal fun parsePrinters(json: String): List<PrinterInfo> {
        val printers = JSONObject(json).optJSONArray("printers") ?: return emptyList()
        return (0 until printers.length()).mapNotNull { index ->
            val item = printers.optJSONObject(index) ?: return@mapNotNull null
            PrinterInfo(
                name = item.optString("name"),
                isDefault = item.optBoolean("isDefault"),
                status = item.optString("status")
            )
        }.filter { it.name.isNotBlank() }.toList()
    }
}

internal fun normalizeTlsFingerprint(value: String): String =
    value.filter(Char::isLetterOrDigit).uppercase(Locale.US)

private class FingerprintTrustManager(private val expectedFingerprint: String) : X509TrustManager {
    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()

    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit

    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val certificate = chain.firstOrNull()
            ?: throw CertificateException("PrintR Agent did not provide a TLS certificate.")
        certificate.checkValidity()
        val actualFingerprint = MessageDigest.getInstance("SHA-256")
            .digest(certificate.encoded)
            .joinToString("") { "%02X".format(Locale.US, it.toInt() and 0xFF) }
        if (!actualFingerprint.equals(expectedFingerprint, ignoreCase = true)) {
            throw CertificateException("The PrintR Agent certificate no longer matches this pairing. Check its pairing details and pair again.")
        }
    }
}

fun Context.selectedFileFromUri(uri: Uri): SelectedFile {
    val providerMime = contentResolver.getType(uri)
    var name = uri.lastPathSegment ?: "selected-file"
    contentResolver.query(uri, null, null, null, null)?.use { cursor ->
        val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
        if (index >= 0 && cursor.moveToFirst()) {
            name = cursor.getString(index)
        }
    }
    val mime = providerMime ?: when {
        name.endsWith(".docx", ignoreCase = true) -> SupportedDocumentTypes.Docx
        name.endsWith(".pdf", ignoreCase = true) -> SupportedDocumentTypes.Pdf
        name.endsWith(".txt", ignoreCase = true) -> SupportedDocumentTypes.Text
        name.endsWith(".png", ignoreCase = true) -> "image/png"
        name.endsWith(".jpg", ignoreCase = true) || name.endsWith(".jpeg", ignoreCase = true) -> "image/jpeg"
        else -> null
    }
    return SelectedFile(uri, name, mime)
}
