# AssetFlow Manager · cliente Android

Cliente móvil de [AssetFlow Manager](../README.md). Habla con la misma API que
el cliente de escritorio y sigue su mismo principio: **no decide nada**,
pregunta y pinta.

## Qué hace

- **Inventario**: buscar material y ver cuántas unidades quedan libres.
- **Préstamos**: solicitar material, pedir la devolución y —si la cuenta es de
  administración— aprobar y rechazar solicitudes.
- **Recuperación de contraseña**: dejar la solicitud para que un administrador
  la autorice.
- **Cambio obligatorio** de la contraseña provisional al entrar.

Deliberadamente **no** incluye la gestión de usuarios, la auditoría ni la
bandeja de recuperaciones: son tareas de administración que se hacen desde el
cliente de escritorio, y no declararlas mantiene esta aplicación pequeña.

## Estado: escrito, no compilado

> **Este código no se ha compilado ni ejecutado.** Se escribió en un entorno sin
> JDK, sin SDK de Android y sin Gradle, así que no hay ninguna evidencia de que
> compile: es razonable esperar errores de compilación la primera vez que se
> abra en Android Studio.
>
> Lo que sí está verificado es que **las rutas y los DTO se corresponden con la
> API real**, cotejados uno a uno contra los controladores de `AssetFlow.Api`.

Falta además `gradle/wrapper/gradle-wrapper.jar`, que es un binario. Android
Studio lo regenera al abrir el proyecto, o se crea con `gradle wrapper`.

## Cómo abrirlo

1. Android Studio → *Open* → seleccionar la carpeta `android/`.
2. Dejar que sincronice Gradle (descargará el wrapper y las dependencias).
3. Ejecutar en un emulador o dispositivo con **Android 8.0 (API 26)** o
   superior.

Al arrancar por primera vez pide la dirección del servidor. Contra la API en el
propio equipo, desde el emulador, la dirección es `http://10.0.2.2:5171`.

## Decisiones que no son obvias

### La dirección del servidor no está en el código

La versión anterior de este cliente llevaba la dirección IP de un servidor
concreto incrustada en cinco ficheros, incluido un `network_security_config.xml`
que permitía tráfico en claro contra ella. Eso publicaba en un repositorio la
dirección de una máquina real y mandaba las credenciales sin cifrar.

Ahora la escribe quien usa la aplicación, se guarda en el dispositivo, y el
cliente **rechaza cualquier URL que no sea `https://`** salvo el bucle local.

### El token va cifrado, el resto no

Hay dos almacenes: uno normal para la dirección del servidor y el último
usuario, y uno con `EncryptedSharedPreferences` para el token de refresco. La
clave maestra vive en el almacén de claves del sistema y nunca está en el APK.
Es el equivalente de DPAPI en el cliente de escritorio.

`allowBackup="false"` completa la medida: sin eso, la copia de seguridad
automática de Android sacaría ese almacén del dispositivo.

### No se registran los cuerpos de las peticiones

La versión anterior tenía `HttpLoggingInterceptor.Level.BODY` activado siempre,
lo que escribía contraseñas y tokens en el registro del sistema. Aquí no hay
interceptor de registro.

### La paleta está escrita a mano

Los colores de `ui/theme/Theme.kt` son copia literal de `Theme/Tokens.xaml` del
cliente de escritorio. Se renuncia al color dinámico de Material You a
propósito: las dos aplicaciones tienen que verse como la misma, y el color
dinámico las pintaría distintas en cada teléfono.

### Ocultar un botón no es seguridad

`accionesDisponibles()` decide qué botones se dibujan según el rol, pero eso
sólo evita ofrecer acciones que acabarían en un 403. Quien manipule la
aplicación para mostrarlos se encontrará con que **la API las rechaza igual**.

## Estructura

```
app/src/main/java/com/assetflow/manager/
├── MainActivity.kt          punto de entrada y navegación entre pantallas
├── data/
│   ├── Dtos.kt              contratos con la API (copia de AssetFlow.Core)
│   ├── ApiService.kt        rutas, con Retrofit
│   ├── ApiClient.kt         cliente HTTP, renovación de token, veto a HTTP
│   ├── Ajustes.kt           preferencias y almacén cifrado
│   └── Sesion.kt            estado de sesión y traducción de errores
└── ui/
    ├── AppViewModel.kt      estado de la aplicación
    ├── theme/Theme.kt       paleta compartida con el escritorio
    └── screens/             pantallas
```
