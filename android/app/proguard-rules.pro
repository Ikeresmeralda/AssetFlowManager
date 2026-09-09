# ===========================================================================
# Reglas de R8 para la compilacion de release.
#
# El binario no contiene secretos y toda decision de seguridad se toma en el
# servidor, asi que aqui no se busca ofuscar: se busca que el recortado de
# codigo muerto no elimine nada que haga falta en tiempo de ejecucion.
#
# Es el tipo de fallo mas caro de encontrar, porque solo aparece en release y
# se manifiesta como una respuesta que no deserializa, no como un error de
# compilacion.
# ===========================================================================

# ---------------------------------------------------------------------------
# Atributos que la reflexion necesita
# ---------------------------------------------------------------------------
# Signature: sin ella se pierden los tipos genericos y Retrofit no sabe que
# List<MaterialDto> es una lista de MaterialDto.
-keepattributes Signature, InnerClasses, EnclosingMethod
-keepattributes RuntimeVisibleAnnotations, RuntimeVisibleParameterAnnotations
-keepattributes AnnotationDefault

# ---------------------------------------------------------------------------
# Retrofit
# ---------------------------------------------------------------------------
# Las interfaces de servicio se resuelven por reflexion: si R8 les cambia el
# nombre a los metodos, las anotaciones @GET/@POST dejan de encontrarse.
-keep,allowobfuscation,allowshrinking interface retrofit2.Call
-keep,allowobfuscation,allowshrinking class retrofit2.Response

# Los metodos suspend devuelven Continuation con el tipo real en el generico.
-keep,allowobfuscation,allowshrinking class kotlin.coroutines.Continuation

-keepclassmembers,allowshrinking,allowobfuscation interface * {
    @retrofit2.http.* <methods>;
}

# La interfaz de la API de esta aplicacion, explicitamente.
-keep interface com.assetflow.manager.data.ApiService { *; }

# ---------------------------------------------------------------------------
# kotlinx.serialization
#
# Reglas oficiales del proyecto. Las anteriores cubrian el caso de las clases
# con Companion pero no el de los `object` serializables ni el de los
# serializadores de clases anidadas.
# ---------------------------------------------------------------------------
-if @kotlinx.serialization.Serializable class **
-keepclassmembers class <1> {
    static <1>$Companion Companion;
}

-if @kotlinx.serialization.Serializable class ** {
    static **$* *;
}
-keepclassmembers class <2>$<3> {
    kotlinx.serialization.KSerializer serializer(...);
}

-if @kotlinx.serialization.Serializable class **
-keepclassmembers class <1> {
    public static <1> INSTANCE;
    kotlinx.serialization.KSerializer serializer(...);
}

-keepclassmembers class **$serializer {
    *** serializer(...);
}

# Los DTO viven todos aqui y su forma es el contrato con la API: se conservan
# enteros. Son unas pocas decenas de clases pequenas, el coste en tamano es
# irrelevante frente al riesgo de que una respuesta deje de deserializarse.
-keep @kotlinx.serialization.Serializable class com.assetflow.manager.data.** { *; }

-keepclassmembers class kotlinx.serialization.json.** {
    *** Companion;
}

# ---------------------------------------------------------------------------
# Avisos de implementaciones opcionales de TLS que no se empaquetan
# ---------------------------------------------------------------------------
-dontwarn okhttp3.internal.platform.**
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**

# Tink, que es lo que hay debajo de EncryptedSharedPreferences.
-dontwarn com.google.crypto.tink.**
-dontwarn com.google.errorprone.annotations.**
-dontwarn javax.annotation.**
