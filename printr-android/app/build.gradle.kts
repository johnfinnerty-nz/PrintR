plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.printr.android"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.printr.android"
        minSdk = 26
        targetSdk = 35
        versionCode = 3
        versionName = "1.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildFeatures {
        compose = true
    }

    val signingValues = listOf("PRINTR_KEYSTORE", "PRINTR_STORE_PASSWORD", "PRINTR_KEY_ALIAS", "PRINTR_KEY_PASSWORD").map { System.getenv(it) }
    require(signingValues.all { it.isNullOrBlank() } || signingValues.all { !it.isNullOrBlank() }) { "Configure all four PRINTR signing variables, or leave all unset for an unsigned release." }
    if (signingValues.all { !it.isNullOrBlank() }) {
        signingConfigs.create("deployment") {
            storeFile = file(signingValues[0]!!)
            storePassword = signingValues[1]
            keyAlias = signingValues[2]
            keyPassword = signingValues[3]
        }
        buildTypes.getByName("release").signingConfig = signingConfigs.getByName("deployment")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.security:security-crypto:1.1.0-alpha06")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    testImplementation("junit:junit:4.13.2")
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
    testImplementation("org.json:json:20250517")
}
