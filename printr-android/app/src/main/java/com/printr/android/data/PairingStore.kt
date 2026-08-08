package com.printr.android.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

class PairingStore(context: Context) {
    private val prefs: SharedPreferences = runCatching {
        val key = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        EncryptedSharedPreferences.create(
            context,
            "printr_pairing_secure",
            key,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }.getOrElse {
        context.getSharedPreferences("printr_pairing_private", Context.MODE_PRIVATE)
    }

    fun load(): PairingDetails = loadAll().firstOrNull { it.id == prefs.getString("default_id", "") } ?: loadLegacy()

    fun loadAll(): List<PairingDetails> {
        val raw = prefs.getString("paired_computers", null) ?: return listOfNotNull(loadLegacy().takeIf { it.isComplete })
        val array = JSONArray(raw)
        return (0 until array.length()).map { index ->
            val item = array.getJSONObject(index)
            PairingDetails(
                id = item.optString("id"),
                name = item.optString("name"),
                host = item.optString("host"),
                port = item.optString("port", "8787"),
                token = item.optString("token"),
                instanceId = item.optString("instanceId"),
                tlsFingerprint = item.optString("tlsFingerprint").ifBlank { null },
                scheme = item.optString("scheme").ifBlank { "http" }
            )
        }
    }

    fun save(details: PairingDetails) {
        val id = details.id.ifBlank { UUID.randomUUID().toString() }
        val saved = details.copy(id = id)
        val merged = (loadAll().filterNot { it.id == id || (it.instanceId.isNotBlank() && it.instanceId == saved.instanceId) } + saved)
        saveAll(merged, id)
    }

    fun remove(id: String) {
        val remaining = loadAll().filterNot { it.id == id }
        saveAll(remaining, remaining.firstOrNull()?.id.orEmpty())
    }

    fun clear() {
        prefs.edit().remove("paired_computers").remove("default_id").apply()
    }

    fun loadOptions(): PrintOptions = PrintOptions(
        printerName = prefs.getString("default_printer", "") ?: "",
        copies = prefs.getString("default_copies", "1") ?: "1",
        colorMode = prefs.getString("default_color_mode", "color") ?: "color",
        duplex = prefs.getBoolean("default_duplex", false),
        pageRange = prefs.getString("default_page_range", "") ?: "",
        duplexMode = prefs.getString("default_duplex_mode", null)
            ?: if (prefs.getBoolean("default_duplex", false)) DuplexMode.LongEdge.wireValue else DuplexMode.None.wireValue,
        orientation = prefs.getString("default_orientation", PageOrientation.Portrait.wireValue)
            ?: PageOrientation.Portrait.wireValue,
        paperSize = prefs.getString("default_paper_size", PaperSize.A4.wireValue)
            ?: PaperSize.A4.wireValue
    )

    fun saveOptions(options: PrintOptions) {
        prefs.edit()
            .putString("default_printer", options.printerName)
            .putString("default_copies", options.copies)
            .putString("default_color_mode", options.colorMode)
            .putBoolean("default_duplex", options.duplex || options.duplexMode != DuplexMode.None.wireValue)
            .putString("default_page_range", options.pageRange)
            .putString("default_duplex_mode", options.duplexMode)
            .putString("default_orientation", options.orientation)
            .putString("default_paper_size", options.paperSize)
            .apply()
    }

    private fun loadLegacy(): PairingDetails = PairingDetails(
        host = prefs.getString("host", "") ?: "",
        port = prefs.getString("port", "8787") ?: "8787",
        token = prefs.getString("token", "") ?: "",
        scheme = "http"
    )

    private fun saveAll(items: List<PairingDetails>, defaultId: String) {
        val array = JSONArray()
        items.forEach {
            array.put(JSONObject()
                .put("id", it.id)
                .put("name", it.name)
                .put("host", it.host)
                .put("port", it.port)
                .put("token", it.token)
                .put("instanceId", it.instanceId)
                .put("tlsFingerprint", it.tlsFingerprint)
                .put("scheme", it.scheme))
        }
        prefs.edit()
            .putString("paired_computers", array.toString())
            .putString("default_id", defaultId)
            .apply()
    }
}
