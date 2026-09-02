# Configuración

Ningún valor real de este documento existe en el repositorio. Todos los
ejemplos usan marcadores de posición: sustitúyelos por los tuyos.

## Regla de partida

**No hay secretos en el código ni en `appsettings.json`.** El repositorio es
público; cualquier cosa escrita ahí está publicada. La configuración sensible
se pasa por variables de entorno o por el gestor de secretos de .NET.

Todas las variables de entorno usan el prefijo `ASSETFLOW_` y `__` (doble
guion bajo) como separador de sección.

## Valores necesarios

| Clave | Variable de entorno | Obligatorio | Para qué |
|---|---|---|---|
| `Jwt:Key` | `ASSETFLOW_Jwt__Key` | Sí en producción | Firma de los tokens. Mínimo 32 caracteres |
| `Jwt:Issuer` | `ASSETFLOW_Jwt__Issuer` | No | Emisor declarado y validado |
| `Jwt:Audience` | `ASSETFLOW_Jwt__Audience` | No | Destinatario declarado y validado |
| `Jwt:AccessTokenMinutes` | `ASSETFLOW_Jwt__AccessTokenMinutes` | No | Vida del token de acceso (por defecto 15) |
| `Jwt:RefreshTokenDays` | `ASSETFLOW_Jwt__RefreshTokenDays` | No | Vida del token de refresco (por defecto 7) |
| `ConnectionStrings:Default` | `ASSETFLOW_ConnectionStrings__Default` | Sí con SQL Server | Cadena de conexión |
| `Database:Provider` | `ASSETFLOW_Database__Provider` | No | `Sqlite` (por defecto) o `SqlServer` |
| `Seed:AdminPassword` | `ASSETFLOW_Seed__AdminPassword` | Sí en producción | Contraseña del administrador inicial |
| `Seed:AdminUsername` | `ASSETFLOW_Seed__AdminUsername` | No | Por defecto `admin` |
| `Seed:AdminEmail` | `ASSETFLOW_Seed__AdminEmail` | No | Correo de esa cuenta |
| `PasswordHashing:WorkFactor` | `ASSETFLOW_PasswordHashing__WorkFactor` | No | Coste de BCrypt (por defecto 12) |
| `Email:SmtpHost` | `ASSETFLOW_Email__SmtpHost` | Sí para enviar correo | Servidor SMTP saliente |
| `Email:SmtpPort` | `ASSETFLOW_Email__SmtpPort` | No | Por defecto 587 |
| `Email:UseStartTls` | `ASSETFLOW_Email__UseStartTls` | No | Por defecto `true` |
| `Email:Username` | `ASSETFLOW_Email__Username` | No | Usuario SMTP, si el servidor lo pide |
| `Email:Password` | `ASSETFLOW_Email__Password` | No | Contraseña SMTP |
| `Email:FromAddress` | `ASSETFLOW_Email__FromAddress` | No | Remitente de los correos |
| `Email:FromName` | `ASSETFLOW_Email__FromName` | No | Nombre visible del remitente |

Hay una plantilla lista para copiar en [`.env.example`](../.env.example).

## Desarrollo

Al ejecutar `dotnet run` en entorno de desarrollo, la API se apaña sola:

- Si falta `Jwt:Key`, **genera una clave aleatoria por arranque** y lo avisa
  por consola. Consecuencia buscada: al reiniciar la API caducan las sesiones,
  lo que recuerda que esto no sirve para producción.
- Si falta `Seed:AdminPassword`, genera una contraseña aleatoria para el
  administrador inicial y **la imprime una única vez** por consola. No pasa por
  el sistema de registro, para que no acabe en un archivo de log.
- Siembra un inventario de ejemplo.

Para fijar valores estables en tu equipo, usa los secretos de usuario, que se
guardan fuera del repositorio:

El proyecto ya declara su `UserSecretsId`, así que no hace falta `init`:

```bash
cd src/AssetFlow.Api
dotnet user-secrets set "Jwt:Key" "PON-AQUI-UNA-CLAVE-LARGA-Y-ALEATORIA"
dotnet user-secrets set "Seed:AdminPassword" "PON-AQUI-UNA-CONTRASENA"
dotnet user-secrets set "Email:Password" "PON-AQUI-LA-CLAVE-SMTP"
```

Se guardan en `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` (en Linux y
macOS, `~/.microsoft/usersecrets/<id>/`), **fuera del árbol del repositorio**,
de modo que no hay forma de que `git add` los arrastre por descuido. El `<id>`
del `.csproj` no es un secreto: es sólo el nombre de esa carpeta.

Para ver qué hay guardado, `dotnet user-secrets list`; para borrarlo todo,
`dotnet user-secrets clear`.

## Producción

En producción **no hay red de seguridad**: si falta `Jwt:Key`, la validación de
opciones impide que la aplicación arranque; si falta `Seed:AdminPassword` y no
existe ninguna cuenta, el sembrado lanza una excepción. Es deliberado: es
preferible que no llegue a levantarse a que lo haga con una configuración
insegura.

### Generar una clave de firma

```bash
# Linux / macOS
openssl rand -base64 48

# Windows (PowerShell)
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

### Variables de entorno

```bash
export ASSETFLOW_Jwt__Key="LA-CLAVE-GENERADA"
export ASSETFLOW_Seed__AdminPassword="LA-CONTRASENA-INICIAL"
export ASSETFLOW_Database__Provider="SqlServer"
export ASSETFLOW_ConnectionStrings__Default="Server=SERVIDOR;Database=AssetFlow;User Id=USUARIO;Password=CONTRASENA;Encrypt=True;TrustServerCertificate=False"
export ASPNETCORE_URLS="https://+:5001"
export ASPNETCORE_ENVIRONMENT="Production"
```

### Base de datos

- La cuenta de la aplicación necesita **solo** `SELECT`, `INSERT`, `UPDATE` y
  `DELETE` sobre sus tablas. No necesita ser propietaria ni administradora.
- Mantén `Encrypt=True` y **no** pongas `TrustServerCertificate=True` salvo que
  sepas exactamente por qué: desactiva la comprobación del certificado del
  servidor.
- El esquema se crea y actualiza con migraciones de EF Core, que se aplican al
  arrancar. Para aplicarlas manualmente:

  ```bash
  dotnet ef database update --project src/AssetFlow.Api --context SqlServerAssetFlowDbContext
  ```

- La base de datos no debe ser accesible desde Internet. Solo la API habla con
  ella.

### Detrás de un proxy inverso

Si la API va detrás de nginx, Apache, IIS o similar, **hay que declarar la
dirección del proxy**:

```bash
export ASSETFLOW_ForwardedHeaders__KnownProxies__0="10.0.0.5"
# Varios proxies: __1, __2, ...
```

No es opcional aunque todo parezca funcionar sin ello. ASP.NET Core sólo hace
caso a las cabeceras `X-Forwarded-For` y `X-Forwarded-Proto` si vienen de una
dirección en la que confía, y por defecto sólo confía en el bucle local. Con un
proxy en otra máquina, esas cabeceras se descartan en silencio y la API ve
**todas** las peticiones como si vinieran de la IP del proxy.

La consecuencia es que el limitador del endpoint de acceso —que reparte por
dirección de origen— deja de repartir: todos los clientes caen en la misma
partición, y un solo atacante puede agotar el cupo de intentos de todo el
mundo. Es un fallo silencioso, porque la aplicación sigue respondiendo
correctamente hasta que alguien lo aprovecha.

Si no hay ningún proxy delante, no configures nada: el valor por defecto (sólo
bucle local) es el correcto en ese caso.

### Desplegar en Render

Render no tiene runtime nativo de .NET: el `Dockerfile` de la raíz del
repositorio publica solo `AssetFlow.Api`. Al crear el **Web Service**:

- **Runtime**: Docker (Render lo detecta solo al ver el `Dockerfile`).
- Variables de entorno mínimas para que arranque:

  ```bash
  ASSETFLOW_Jwt__Key=<genera una clave con openssl rand -base64 48>
  ASSETFLOW_Seed__AdminPassword=<contraseña del admin inicial>
  ```

- El plan gratuito no incluye disco persistente: con el proveedor por defecto
  (`Sqlite`), la base de datos se reinicia en cada redeploy o reinicio del
  servicio. Válido para una demo; para persistir datos entre sesiones hay que
  añadir un disco de pago o cambiar a `SqlServer` con una base externa.
- Render inyecta `RENDER=true` en el entorno, que el código usa para omitir
  la redirección HTTPS de la aplicación: el borde de Render ya la hace antes
  de reenviar la petición al contenedor, así que repetirla dentro provocaría
  un bucle de redirección. No hay que configurar nada para esto.
- `ForwardedHeaders:KnownProxies` se queda sin configurar: Render no publica
  una IP fija de proxy, así que el limitador de peticiones por IP no podrá
  repartir por origen real detrás de su borde. No es un problema para una
  demo; si esto pasa a producción real, hay que revisar
  [decisiones-tecnicas.md](decisiones-tecnicas.md).

## Cambiar de proveedor

Hay dos juegos de migraciones, uno por proveedor, porque los tipos de columna
que genera EF Core no son intercambiables:

```
src/AssetFlow.Api/Data/Migrations/Sqlite/
src/AssetFlow.Api/Data/Migrations/SqlServer/
```

Cambiar `Database:Provider` selecciona el contexto y el juego de migraciones
correspondiente. **No migra los datos existentes de un motor a otro**: eso es
una tarea de exportación e importación aparte.

## Correo saliente

**El correo es opcional. La aplicación funciona sin él.**

Se usa para una sola cosa: avisar al titular de que su contraseña ha cambiado.
La recuperación **no pasa por aquí** — la autoriza un administrador dentro de la
aplicación, así que no hay ningún código que enviar.

Cuando `Email:SmtpHost` está vacío, el envío se sustituye por
`LoggingEmailSender`, que escribe el mensaje en el registro. Como ese mensaje ya
no contiene ningún secreto, eso es aceptable en cualquier entorno: la aplicación
arranca igual en producción sin SMTP configurado.

> Ese aviso es, aun así, **el único mecanismo por el que alguien se entera de
> que le han reiniciado la cuenta sin su conocimiento**. Configurarlo es la
> diferencia entre que un robo de cuenta se note y que no.

### Que el correo llegue de verdad

Esta es la parte que falla en la práctica, y no es criptografía:

- **No envíes desde tu propia máquina.** Un SMTP casero o una IP residencial
  acaba en spam o se descarta directamente. Usa un proveedor transaccional
  (Resend, Postmark, Amazon SES, Brevo); todos tienen plan gratuito de sobra
  para esto.
- **Configura SPF, DKIM y DMARC** en el DNS del dominio remitente. Sin los tres,
  el correo llega a spam. Es la causa número uno.
- **Vigila el registro.** El fallo de envío se traga a propósito —si no,
  comparar una respuesta correcta con una de error volvería a delatar qué
  cuentas existen—, así que una configuración rota falla **en silencio**. Pon
  una alerta sobre `No se ha podido enviar el correo`.

### Notas prácticas

- **Gmail, Outlook y similares no aceptan la contraseña de la cuenta.**
  Necesitas una *contraseña de aplicación*, que se genera en los ajustes de
  seguridad del proveedor y requiere la verificación en dos pasos activada.
- El puerto **587 usa STARTTLS** y el **465, TLS implícito**. Ambos funcionan:
  el emisor elige el modo según el puerto.
- La contraseña SMTP es un secreto como cualquier otro. Va en variables de
  entorno o en el gestor de secretos, **nunca en `appsettings.json`**.
- Los correos salen por una cola en segundo plano, no dentro de la petición. Es
  una medida de seguridad, no de rendimiento: esperar al servidor SMTP dentro de
  la petición hacía que el camino de la cuenta existente tardara más y permitía
  averiguar qué correos están dados de alta cronometrando. Ver
  [decisiones-tecnicas.md](decisiones-tecnicas.md).

### Ejemplo con Resend

Resend habla SMTP, así que el emisor de MailKit que ya existe sirve tal cual:
**no hay que añadir ningún paquete ni escribir código específico del
proveedor.** Cambiar a Postmark, SES o Brevo más adelante es cambiar estas
mismas cuatro variables.

| Valor | Qué poner |
|---|---|
| `SmtpHost` | `smtp.resend.com` |
| `SmtpPort` | `465` (TLS implícito) o `587` (STARTTLS) |
| `UseStartTls` | `false` con el 465, `true` con el 587 |
| `Username` | literalmente `resend`, igual para todo el mundo |
| `Password` | la clave de API, que empieza por `re_` |

Dos cosas concretas de este proveedor:

- **Crea la clave con permiso de envío únicamente.** Resend permite elegir entre
  acceso total y sólo envío. Con la segunda, una clave filtrada puede mandar
  correo en tu nombre, que ya es malo, pero **no** puede leer el histórico de
  envíos, crear más claves ni tocar los dominios. Se comprueba fácil: una clave
  restringida devuelve `restricted_api_key` al consultar `/domains`.
- **Sin un dominio verificado sólo se puede enviar desde `onboarding@resend.dev`
  y sólo a la dirección de tu propia cuenta.** Es el modo de pruebas. Para
  escribir a cualquier usuario hay que verificar un dominio en el panel y añadir
  al DNS los registros SPF y DKIM que indica; después `FromAddress` pasa a ser
  una dirección de ese dominio. Sin ese paso, la recuperación de contraseña
  funcionará para ti y para nadie más.

### Ejemplo con un proveedor genérico

```bash
export ASSETFLOW_Email__SmtpHost="smtp.ejemplo.com"
export ASSETFLOW_Email__SmtpPort="587"
export ASSETFLOW_Email__UseStartTls="true"
export ASSETFLOW_Email__Username="USUARIO-SMTP"
export ASSETFLOW_Email__Password="CONTRASENA-SMTP"
export ASSETFLOW_Email__FromAddress="no-reply@tu-dominio.com"
export ASSETFLOW_Email__FromName="AssetFlow Manager"
```

Del envío se registra el destinatario y nada más.

## Cliente de escritorio

El cliente no necesita configuración previa. Al arrancar sin servidor
configurado ofrece **Configurar servidor**, y guarda la dirección en:

```
%APPDATA%\AssetFlow\settings.json
```

Ese archivo contiene únicamente la dirección del servidor, si se recuerda la
sesión y el último nombre de usuario. **No contiene contraseñas ni tokens.**

La sesión, si se elige recordarla, se guarda cifrada con DPAPI en:

```
%APPDATA%\AssetFlow\sesion.bin
```

Solo puede descifrarla la misma cuenta de Windows en el mismo equipo. Copiar el
archivo a otra máquina no sirve de nada.

Contra un servidor remoto, el cliente **exige `https://`** y rechaza `http://`.
Solo se admite HTTP contra `localhost`, para desarrollo.
