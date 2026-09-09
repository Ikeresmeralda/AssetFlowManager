import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "com.assetflow.manager"
    compileSdk = 36

    // -----------------------------------------------------------------------
    // FIRMA DE RELEASE
    //
    // Las credenciales se leen de keystore.properties, que no esta en git
    // junto con el .jks. Si el archivo no existe, la compilacion de release
    // sigue funcionando pero sale sin firmar: asi quien clone el repositorio
    // puede compilar sin necesidad de la clave privada, y a la vez es
    // imposible que esa clave acabe publicada.
    // -----------------------------------------------------------------------
    val propiedadesDeFirma = Properties().apply {
        val archivo = rootProject.file("keystore.properties")
        if (archivo.exists()) archivo.inputStream().use { load(it) }
    }

    val hayFirma = propiedadesDeFirma.getProperty("storeFile") != null &&
            rootProject.file(propiedadesDeFirma.getProperty("storeFile")).exists()

    signingConfigs {
        if (hayFirma) {
            create("release") {
                storeFile = rootProject.file(propiedadesDeFirma.getProperty("storeFile"))
                storePassword = propiedadesDeFirma.getProperty("storePassword")
                keyAlias = propiedadesDeFirma.getProperty("keyAlias")
                keyPassword = propiedadesDeFirma.getProperty("keyPassword")

                // Ambos esquemas: v1 para Android 6 y anteriores, v2/v3 para
                // el resto. minSdk es 26, pero dejarlos activos no cuesta nada.
                enableV1Signing = true
                enableV2Signing = true
            }
        }
    }

    defaultConfig {
        applicationId = "com.assetflow.manager"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        release {
            if (hayFirma) {
                signingConfig = signingConfigs.getByName("release")
            }

            // Ningun rastro de depuracion en el paquete distribuido.
            isDebuggable = false
            // Sin ofuscacion: el binario no contiene secretos, asi que
            // protegerlo no aportaria nada. Toda decision de seguridad se toma
            // en el servidor. Se activa el recortado de recursos y codigo
            // muerto, que si tiene efecto sobre el tamano.
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.activity.compose)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.graphics)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.material3)
    implementation(libs.androidx.material.icons.extended)

    // Almacenamiento cifrado del token de refresco. Es el equivalente de DPAPI
    // en el cliente de escritorio: la clave vive en el almacen de claves del
    // sistema, no en el APK.
    //
    // No hay DataStore ni navigation-compose: se declaraban y no se usaban.
    // La persistencia va con SharedPreferences (ver Ajustes.kt) y la
    // navegacion es una maquina de estados en AppViewModel, no una pila.
    implementation(libs.androidx.security.crypto)

    implementation(libs.retrofit)
    implementation(libs.retrofit.serialization)
    implementation(libs.okhttp)
    implementation(libs.kotlinx.serialization.json)

    testImplementation(libs.junit)
    androidTestImplementation(libs.androidx.junit)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(platform(libs.androidx.compose.bom))

    debugImplementation(libs.androidx.ui.tooling)
}
