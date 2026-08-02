package com.printr.android.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

class DiscoveryClient {
    suspend fun discover(timeoutMs: Int = 2500): List<DiscoveryComputer> = withContext(Dispatchers.IO) {
        val found = mutableListOf<DiscoveryComputer>()
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = timeoutMs
            val probe = "PRINTR_DISCOVER_V1".toByteArray()
            socket.send(DatagramPacket(probe, probe.size, InetAddress.getByName("255.255.255.255"), 8788))
            val deadline = System.currentTimeMillis() + timeoutMs
            while (System.currentTimeMillis() < deadline) {
                try {
                    val buffer = ByteArray(2048)
                    val packet = DatagramPacket(buffer, buffer.size)
                    socket.receive(packet)
                    val json = JSONObject(String(packet.data, 0, packet.length))
                    found += DiscoveryComputer(
                        name = json.optString("computerName", packet.address.hostAddress ?: "PrintR Agent"),
                        host = json.optString("host", packet.address.hostAddress ?: ""),
                        port = json.optInt("port", 8787).toString(),
                        instanceId = json.optString("instanceId"),
                        status = "found"
                    )
                } catch (_: Exception) {
                    break
                }
            }
        }
        found.distinctBy { it.instanceId.ifBlank { it.host } }
    }
}
