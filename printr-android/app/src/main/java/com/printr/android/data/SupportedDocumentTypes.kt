package com.printr.android.data

object SupportedDocumentTypes {
    const val Pdf = "application/pdf"
    const val Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    const val Text = "text/plain"

    val PickerMimeTypes = arrayOf(
        Pdf,
        Docx,
        Text,
        "image/*"
    )

    fun displayType(file: SelectedFile): String = displayType(file.displayName, file.mimeType)

    fun displayType(displayName: String, mimeType: String?): String = when {
        mimeType == Docx || displayName.endsWith(".docx", ignoreCase = true) -> "DOCX document"
        mimeType == Pdf || displayName.endsWith(".pdf", ignoreCase = true) -> "PDF"
        mimeType?.startsWith("image/") == true -> "Image"
        mimeType == Text || displayName.endsWith(".txt", ignoreCase = true) -> "Text"
        else -> mimeType ?: "Unknown"
    }

    fun uploadMimeType(file: SelectedFile): String = uploadMimeType(file.displayName, file.mimeType)

    fun uploadMimeType(displayName: String, mimeType: String?): String = when {
        mimeType == Docx || displayName.endsWith(".docx", ignoreCase = true) -> Docx
        mimeType.isNullOrBlank() -> "application/octet-stream"
        else -> mimeType
    }
}
