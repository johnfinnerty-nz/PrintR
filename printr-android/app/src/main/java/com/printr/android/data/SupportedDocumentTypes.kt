package com.printr.android.data

object SupportedDocumentTypes {
    const val Pdf = "application/pdf"
    const val Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    const val Text = "text/plain"

    val MimeByExtension = linkedMapOf(
        "pdf" to Pdf, "docx" to Docx, "txt" to Text, "csv" to "text/csv",
        "png" to "image/png", "jpg" to "image/jpeg", "jpeg" to "image/jpeg", "bmp" to "image/bmp",
        "xlsx" to "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pptx" to "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "odt" to "application/vnd.oasis.opendocument.text",
        "ods" to "application/vnd.oasis.opendocument.spreadsheet",
        "odp" to "application/vnd.oasis.opendocument.presentation",
        "rtf" to "application/rtf"
    )
    val PickerMimeTypes = MimeByExtension.values.distinct().toTypedArray()
    fun isSupported(name: String) = MimeByExtension.containsKey(name.substringAfterLast('.', "").lowercase())

    fun displayType(file: SelectedFile): String = displayType(file.displayName, file.mimeType)

    fun displayType(displayName: String, mimeType: String?): String = when {
        mimeType == Docx || displayName.endsWith(".docx", ignoreCase = true) -> "DOCX document"
        mimeType == Pdf || displayName.endsWith(".pdf", ignoreCase = true) -> "PDF"
        mimeType?.startsWith("image/") == true -> "Image"
        mimeType == Text || displayName.endsWith(".txt", ignoreCase = true) -> "Text"
        isSupported(displayName) -> displayName.substringAfterLast('.').uppercase() + " document"
        else -> mimeType ?: "Unknown"
    }

    fun uploadMimeType(file: SelectedFile): String = uploadMimeType(file.displayName, file.mimeType)

    fun uploadMimeType(displayName: String, mimeType: String?): String = when {
        isSupported(displayName) -> MimeByExtension.getValue(displayName.substringAfterLast('.').lowercase())
        mimeType == Docx -> Docx
        mimeType.isNullOrBlank() -> "application/octet-stream"
        else -> mimeType
    }
}
