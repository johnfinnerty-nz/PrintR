package com.printr.android.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

class DiscoveryClient(private val context: Context) {
    suspend fun discover(timeoutMs: Int = 2500): List<DiscoveryComputer> = withContext(Dispatchers.IO) {
        val found = mutableListOf<DiscoveryComputer>()
        val multicastLock = (context.applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager)
            ?.createMulticastLock("printr-discovery")
            ?.apply {
                setReferenceCounted(false)
                acquire()
            }
        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.soTimeout = timeoutMs
                val probe = "PRINTR_DISCOVER_V1".toByteArray()
                broadcastTargets().forEach { target ->
                    socket.send(DatagramPacket(probe, probe.size, target, 8788))
                }
                val deadline = System.currentTimeMillis() + timeoutMs
                while (System.currentTimeMillis() < deadline) {
                    try {
                        val buffer = ByteArray(2048)
                        val packet = DatagramPacket(buffer, buffer.size)
                        socket.receive(packet)
                        parseResponse(String(packet.data, 0, packet.length), packet.address.hostAddress.orEmpty())?.let(found::add)
                    } catch (_: SocketTimeoutException) {
                        break
                    }
                }
            }
        } finally {
            if (multicastLock?.isHeld == true) multicastLock.release()
        }
        if (found.isEmpty()) {
            found += discoverBySubnetProbe()
        }
        found.distinctBy { it.instanceId.ifBlank { it.host } }
    }

    private suspend fun discoverBySubnetProbe(): List<DiscoveryComputer> = coroutineScope {
        val hosts = localSubnetHosts()
        if (hosts.isEmpty()) return@coroutineScope emptyList()
        val client = discoveryHttpClient()
        val limit = Semaphore(24)
        hosts.map { host ->
            async {
                limit.withPermit {
                    fetchDiscoveryInfo(client, host, "https")
                        ?: fetchDiscoveryInfo(client, host, "http")
                }
            }
        }.awaitAll().filterNotNull()
    }

    private fun fetchDiscoveryInfo(client: OkHttpClient, host: String, scheme: String): DiscoveryComputer? = runCatching {
        val request = Request.Builder()
            .url("$scheme://$host:8787/discovery-info")
            .get()
            .build()
        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) return@use null
            parseResponse(response.body?.string().orEmpty(), host)
        }
    }.getOrNull()

    private fun localSubnetHosts(): List<String> {
        val connectivity = context.getSystemService(ConnectivityManager::class.java) ?: return emptyList()
        val activeNetwork = connectivity.activeNetwork ?: return emptyList()
        val address = connectivity.getLinkProperties(activeNetwork)
            ?.linkAddresses
            ?.map { it.address }
            ?.filterIsInstance<Inet4Address>()
            ?.firstOrNull { !it.isLoopbackAddress }
            ?: return emptyList()
        val bytes = address.address
        if (bytes.size != 4) return emptyList()
        val prefix = "${bytes[0].toInt() and 0xFF}.${bytes[1].toInt() and 0xFF}.${bytes[2].toInt() and 0xFF}"
        val currentHost = bytes[3].toInt() and 0xFF
        return (1..254).filterNot { it == currentHost }.map { "$prefix.$it" }
    }

    private fun discoveryHttpClient(): OkHttpClient {
        val trustManager = object : X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) = Unit
        }
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf<X509TrustManager>(trustManager), null)
        // Discovery only reads non-sensitive metadata. Pairing still pins the advertised certificate before any authenticated request.
        return OkHttpClient.Builder()
            .connectTimeout(300, TimeUnit.MILLISECONDS)
            .readTimeout(500, TimeUnit.MILLISECONDS)
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier { _, _ -> true }
            .build()
    }

    private fun broadcastTargets(): Set<InetAddress> {
        val targets = linkedSetOf(InetAddress.getByName("255.255.255.255"))
        val interfaces = NetworkInterface.getNetworkInterfaces() ?: return targets
        while (interfaces.hasMoreElements()) {
            val network = interfaces.nextElement()
            if (!network.isUp || network.isLoopback) continue
            network.interfaceAddresses.mapNotNullTo(targets) { it.broadcast }
        }
        return targets
    }

    companion object {
        internal fun parseResponse(raw: String, fallbackHost: String): DiscoveryComputer? = runCatching {
            val json = JSONObject(raw)
            if (json.optString("app") != "PrintR Agent") return null
            val scheme = json.optString("scheme", "http").lowercase()
            val fingerprint = json.optString("tlsFingerprint").ifBlank { null }
            if (scheme == "https" && normalizeTlsFingerprint(fingerprint.orEmpty()).length != 64) return null
            DiscoveryComputer(
                name = json.optString("computerName", fallbackHost.ifBlank { "PrintR Agent" }),
                host = json.optString("host", fallbackHost),
                port = json.optInt("port", 8787).toString(),
                instanceId = json.optString("instanceId"),
                status = if (scheme == "https") "secure" else "legacy HTTP",
                scheme = scheme,
                tlsFingerprint = fingerprint
            ).takeIf { it.host.isNotBlank() && it.scheme in setOf("http", "https") }
        }.getOrNull()
    }
}
