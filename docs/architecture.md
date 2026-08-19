# Arquitectura

## Idea de partida

**Un atacante no va a usar la aplicación de escritorio.** Puede ignorarla por
completo y hablar con la API directamente. Por eso el reparto de
responsabilidades es el siguiente:

- La **API** toma todas las decisiones y no se fía de nada de lo que le llegue.
- El **cliente** pregunta, pinta y facilita el trabajo. No decide nada.

Todo lo demás se deriva de ahí.

## Proyectos

```
src/
├── AssetFlow.Api/        ASP.NET Core Web API. Lógica y seguridad.
├── AssetFlow.Core/       Cliente HTTP, DTOs, sesión, DPAPI.
└── AssetFlow.Desktop/    Interfaz WPF.
tests/
└── AssetFlow.Api.Tests/  Integración sobre la API real.
```

`AssetFlow.Desktop` depende de `AssetFlow.Core`, y `AssetFlow.Core` no
depende de nadie del proyecto. La API no depende de ninguno de los dos: se
comunican por HTTP y por la forma de los DTOs, no por una referencia compartida.

## AssetFlow.Api

```
Controllers/    Entrada HTTP. Autorización y traducción a DTO.
Services/       Hash de contraseñas, emisión de tokens, bloqueo de cuentas.
Data/           DbContext, migraciones por proveedor, sembrado.
Entities/       Modelo de datos.
Dtos/           Lo que entra y lo que sale. Nunca se devuelven entidades.
Middleware/     Manejo global de errores.
Security/       Constantes de rol.
```

### Doble proveedor de base de datos

SQLite por defecto para que clonar el repositorio y ejecutar funcione sin
instalar nada; SQL Server como opción para un despliegue real. Se resuelve con
dos `DbContext` derivados y **dos juegos de migraciones**, porque los tipos de
columna que genera EF Core no son intercambiables entre motores.

Los controladores reciben la clase base `AssetFlowDbContext` y no saben sobre
qué motor corren.

> Todas las marcas de tiempo son `DateTime` en UTC y no `DateTimeOffset`: EF
> Core sobre SQLite no sabe comparar ni ordenar `DateTimeOffset`, y descubrirlo
> a mitad del desarrollo costó rehacer el modelo entero.

### Disponibilidad calculada

`Material.TotalQuantity` es **lo que se posee** y no cambia al prestar. Lo
disponible se deriva:

```
disponible = total − suma de líneas de préstamos activos
```

La alternativa habitual —un contador que se resta al prestar y se suma al
devolver— acumula errores: basta con que una operación falle a medias, o que
una devolución se procese dos veces, para que el inventario deje de cuadrar y
no haya forma de saber cuál es el número bueno. Aquí no puede descuadrarse
porque no hay número que mantener.

La creación de un préstamo va dentro de una transacción, de modo que no puede
quedar un préstamo con la mitad de sus líneas.

### Concurrencia

`Material` lleva un `Guid Version` marcado como testigo de concurrencia. El
cliente lo recibe al leer y lo devuelve al guardar; si otra persona ha
modificado el artículo entretanto, la actualización se rechaza con 409 en lugar
de pisar sus cambios sin avisar.

Se usa un `Guid` y no un `rowversion` de SQL Server porque tiene que funcionar
igual en SQLite.

## AssetFlow.Core

```
Http/           ApiClient, ApiResult, manejador de renovación, sonda.
Security/       TokenStore (DPAPI), SessionState.
Services/       Un servicio por área: Auth, Materials, Users, Loans.
Configuration/  AppSettings.
Diagnostics/    Log con redacción.
Dtos/           Espejo de los DTOs de la API.
```

### `ApiResult` en lugar de devolver `null`

Cada llamada devuelve un `ApiResult<T>` con un `ApiStatus` (`Success`,
`Offline`, `Unauthenticated`, `Forbidden`, `NotFound`, `Invalid`, `Conflict`,
`TooManyRequests`, `ServerError`, `Cancelled`).

El motivo es concreto: cuando un fallo se comunica devolviendo `null`, la
interfaz no puede distinguir «el servidor está caído» de «la contraseña es
incorrecta», y acaba enseñando el mensaje equivocado. Con un estado tipado cada
caso se explica de forma distinta, y solo se ofrece reintentar cuando
reintentar puede servir de algo.

### Renovación del token

`AuthenticationHandler` es un `DelegatingHandler` que añade el token, detecta
que ha caducado o que la respuesta ha sido 401, lo renueva y reintenta la
petición original clonada.

La renovación está serializada con un semáforo: si cinco peticiones descubren a
la vez que el token ha caducado, solo una renueva. Sin esto, las otras cuatro
usarían un token de refresco ya rotado y la detección de reutilización cerraría
la sesión del usuario, que es exactamente lo contrario de lo que se pretende.

### El token en reposo

`TokenStore` cifra el token de refresco con DPAPI
(`DataProtectionScope.CurrentUser`) antes de escribirlo en `%APPDATA%`. Protege
frente a otro usuario del mismo equipo y frente a copiar el archivo a otra
máquina. No protege frente a código malicioso ejecutándose con la misma cuenta,
y en `SECURITY.md` se dice así de claro.

## AssetFlow.Desktop

```
Views/      Login, Shell y una página por sección.
Dialogs/    Formularios de alta y edición.
Controls/   Panel de estado (cargando, vacío, error).
Theme/      Tokens, controles, navegación y tabla.
```

`SessionState` mantiene el rol para decidir qué botones se muestran. **Es una
pista de interfaz, no una autoridad**: sirve para no ofrecer acciones que van a
fallar, y está documentado como tal en el propio código. La API rechaza
igualmente cualquier operación no permitida.

## Decisiones que suelen preguntarse

**Por qué no hay CORS.** CORS es una protección del navegador. El cliente es
una aplicación WPF, no un navegador. Añadir una política CORS aquí no
protegería de nada y solo abriría la API a orígenes web sin necesidad.

**Por qué el mapeo entre entidades y DTOs es a mano.** Son cinco entidades. Una
biblioteca de mapeo automático añadiría una dependencia y una capa de magia
para ahorrar unas líneas, y el mapeo explícito hace evidente qué campos salen
hacia el cliente, que es justo lo que interesa vigilar.

**Por qué el bloqueo por intentos fallidos vive en memoria.** Persistirlo
convertiría cada intento fallido en una escritura en la base de datos, que es
precisamente lo que un atacante querría provocar. La contrapartida —que
reiniciar la API borre los contadores— es asumible porque el limitador por
origen sigue activo.

**Por qué no hay inyección de dependencias por constructor en las ventanas.**
WPF no la admite en ventanas creadas desde XAML. La alternativa serían fábricas
para cada ventana, mucho código para una aplicación de este tamaño. Se expone
un acceso estático al contenedor: los servicios se registran y se resuelven en
un único sitio, en lugar de instanciarse con `new` repartidos por las vistas.
