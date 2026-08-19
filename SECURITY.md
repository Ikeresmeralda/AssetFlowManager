# Política de seguridad

## Versiones con soporte

| Versión | Soporte |
|---|---|
| 1.0.x | Sí |
| < 1.0 | No |

## Cómo informar de un fallo de seguridad

**No abras una incidencia pública.** Una incidencia describe el fallo a todo el
mundo antes de que exista una corrección.

Usa el aviso privado de GitHub: pestaña **Security → Report a vulnerability**
de este repositorio.

Incluye, en la medida de lo posible:

- Qué componente se ve afectado (API, cliente de escritorio, base de datos).
- Los pasos para reproducirlo.
- Qué consigue un atacante que lo explote.
- La versión sobre la que lo has probado.

Respuesta orientativa: confirmación de recepción en unos días y una valoración
inicial en un par de semanas. Es un proyecto mantenido por una persona, así que
no hay compromiso de servicio ni programa de recompensas.

## Alcance

Entra dentro del alcance todo lo que esté en `src/`:

- `AssetFlow.Api` — autenticación, autorización, validación, acceso a datos.
- `AssetFlow.Core` — cliente HTTP, manejo de sesión, almacenamiento del token.
- `AssetFlow.Desktop` — interfaz.

Queda **fuera del alcance**:

- `AndroidApp/` — no forma parte de esta versión y no ha sido revisado.
- Despliegues configurados de forma insegura por quien los opera (por ejemplo,
  exponer la API por HTTP en Internet, o publicar la base de datos).

## Historial del repositorio

Las versiones anteriores a la 1.0 incluían dos guiones SQL heredados
(`db/schema-sqlserver-legacy.sql` y `db/sqlQuery-hash-passwords.sql`) con
contraseñas de ejemplo en texto plano y sus hashes. Se han eliminado en la 1.0
porque describían un modelo de datos que ya no existe y contradecían el propio
manejo de credenciales del sistema.

Eran datos de demostración inventados (`juan@example.com` y similares) y no
corresponden a ninguna cuenta real de este sistema: la 1.0 solo siembra la
cuenta de administrador inicial, con la contraseña que se indique en la
configuración. Aun así, **siguen siendo accesibles en el historial de git**. Si
alguna de esas contraseñas se hubiera reutilizado en algún sitio real, debe
considerarse comprometida y cambiarse, y el historial debe reescribirse antes
de publicar el repositorio.

## Manejo de credenciales

- **Contraseñas**: BCrypt con factor de trabajo 12, sal por contraseña generada
  por el propio algoritmo. Nunca se almacenan en claro y no salen por ningún
  DTO. La verificación se ejecuta también cuando el usuario no existe, contra
  un hash señuelo, para que el tiempo de respuesta no revele qué cuentas están
  dadas de alta.
- **Tokens de refresco**: sólo se guarda su SHA-256. Aquí un hash rápido es la
  elección correcta y no una omisión: el token tiene entropía criptográfica y no
  admite diccionario, al contrario que una contraseña elegida por una persona.
- **Recuperación de contraseña**: no hay códigos ni enlaces. Quien olvida su
  contraseña deja una solicitud y **un administrador la autoriza desde dentro
  de la aplicación**. La respuesta del endpoint es idéntica exista o no la
  cuenta, y también cuando ya hay una solicitud pendiente o se ha superado el
  límite de dos cada media hora, **en contenido y en tiempo**: hay un suelo de
  duración constante para que el camino de la cuenta existente no se pueda
  distinguir cronometrando.
- **Contraseña provisional**: al aprobar una solicitud, la cuenta recibe
  `usuario + "123@"`. **Es deliberadamente predecible y eso sólo es aceptable
  porque caduca en el primer uso**: la cuenta queda marcada y no puede hacer
  absolutamente nada —ni leer el inventario— hasta cambiarla. Tampoco puede
  «cambiarla» por ella misma. Si esa marca se pudiera saltar, la contraseña
  sería permanente y derivable del nombre de usuario, es decir, una vía de
  acceso pública a cualquier cuenta que hubiera pasado por una recuperación.
  El bloqueo lo aplica un middleware sobre un claim del token, con lista
  blanca: un endpoint nuevo nace cerrado para esas sesiones.
- **Nadie fija la contraseña de otro.** El administrador autoriza el reinicio
  pero no elige la contraseña definitiva; la escribe su titular al entrar. Una
  contraseña que conocen dos personas no identifica a ninguna de las dos.
- **Aviso de cambio**: reiniciar o cambiar una contraseña genera un correo al
  titular, si hay SMTP configurado. Es el mecanismo por el que se entera de un
  robo de cuenta. No contiene contraseñas ni enlaces.
- **Credenciales SMTP**: son un secreto como cualquier otro. Van en variables de
  entorno o gestor de secretos, nunca en `appsettings.json`.

Reiniciar la contraseña, cambiarla o desactivar la cuenta **revocan todas las
sesiones abiertas** de esa cuenta.

## Trazabilidad

Quedan registrados con autor y momento: inicio y cierre de sesión, revocación de
sesiones (incluida la provocada por reutilizar un token de refresco), cambio y
reinicio de contraseña, solicitud y finalización de recuperación, creación,
aprobación y rechazo de préstamos y devoluciones, alta y baja de cuentas,
cambios de rol y modificaciones del inventario.

El registro es **sólo de lectura** y accesible únicamente para administradores.
No guarda direcciones IP, ni contraseñas, ni tokens, ni hashes.

## Lo que el sistema no promete

Conviene ser explícito, porque una lista de garantías sin sus límites es
engañosa:

- **El binario del cliente no está protegido contra ingeniería inversa.** No
  contiene secretos ni claves, precisamente porque cualquier protección de ese
  tipo sería superable. Toda decisión de seguridad se toma en el servidor.
- **El token de acceso no es revocable** durante su vida (minutos). Lo que se
  revoca es el token de refresco, que es el que permite prolongar la sesión.
- **El token guardado en el equipo está cifrado con DPAPI**, ligado a la cuenta
  de Windows del usuario. Eso protege frente a otro usuario del mismo equipo y
  frente a la copia del archivo a otra máquina; **no** protege frente a código
  malicioso ejecutándose con esa misma cuenta.
- **El bloqueo por intentos fallidos vive en memoria.** Reiniciar la API lo
  reinicia. El limitador por origen sigue activo en ese caso.
- **La recuperación depende del criterio de quien administra.** Al no haber
  código por correo, el sistema ya no comprueba que quien pide recuperar una
  cuenta tenga acceso a su buzón: esa comprobación la hace una persona. Un
  administrador que apruebe una solicitud sin confirmar por otra vía quién la
  ha pedido está entregando la cuenta. La aplicación lo advierte en la ventana
  de confirmación, pero no puede impedirlo.
- **No hay segundo factor** en ningún punto.
- **La aplicación no cifra la base de datos.** Protegerla es responsabilidad de
  quien opera el servidor (permisos del sistema de archivos, cifrado de disco o
  TDE en SQL Server).

## Configuración segura

El despliegue es responsabilidad de quien lo opera. Lo mínimo:

- Servir la API **solo por HTTPS**. El cliente rechaza `http://` contra un
  servidor remoto.
- Definir `Jwt:Key` y `Seed:AdminPassword` mediante variables de entorno o
  gestor de secretos, **nunca en un archivo del repositorio**.
- Dar a la cuenta de base de datos los permisos mínimos necesarios.
- No exponer la base de datos directamente a Internet.

Los detalles están en [docs/configuration.md](docs/configuration.md).
