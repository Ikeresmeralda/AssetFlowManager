# Autorización por endpoint

Esta tabla es la referencia de qué puede hacer cada rol. **Todas las
comprobaciones ocurren en el servidor.** El cliente de escritorio oculta las
acciones que la cuenta no puede realizar, pero eso es comodidad de interfaz: la
API rechaza igualmente la operación aunque la petición no venga del cliente.

La política por defecto es **cerrada**: sin `[AllowAnonymous]` explícito, un
endpoint exige usuario autenticado. Un endpoint nuevo nace protegido.

## Leyenda

| Símbolo | Significado |
|---|---|
| — | No requiere autenticación |
| **A** | Cualquier usuario autenticado |
| **P** | Autenticado, y solo sobre sus propios datos |
| **ADM** | Solo administrador |

## Autenticación

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| POST | `/api/auth/login` | — | Limitado por origen |
| POST | `/api/auth/refresh` | — | Limitado por origen. Rota el token |
| POST | `/api/auth/forgot-password` | — | Limitado por origen. **202 siempre**, exista o no la cuenta |
| POST | `/api/auth/change-password` | A | **Sólo con cambio pendiente**; 403 en cualquier otro caso. Devuelve una sesión nueva |
| POST | `/api/auth/logout` | A | Revoca el token de refresco indicado |
| POST | `/api/auth/logout-all` | A | Revoca todas las sesiones propias |
| GET | `/api/auth/me` | A | Identidad del usuario del token |

## Material

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/api/materials` | A | Búsqueda y listado |
| GET | `/api/materials/{id}` | A | |
| POST | `/api/materials` | ADM | |
| PUT | `/api/materials/{id}` | ADM | Rechaza bajar el total por debajo de lo prestado |
| DELETE | `/api/materials/{id}` | ADM | 409 si tiene préstamos registrados |

## Préstamos

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/api/loans` | P / ADM | El administrador ve todos; el resto, solo los suyos. El parámetro `userId` se ignora para un usuario normal |
| GET | `/api/loans/pending` | ADM | Solicitudes a la espera de decisión |
| GET | `/api/loans/{id}` | P / ADM | 403 si el préstamo es de otra persona |
| GET | `/api/loans/{id}/history` | P / ADM | Acciones registradas sobre ese préstamo. 403 si es de otra persona |
| POST | `/api/loans` | A / ADM | Un usuario crea la solicitud **pendiente**; un administrador la crea ya activa. Solo un administrador puede indicar `userId` distinto del propio. Máximo 5 pendientes por usuario |
| POST | `/api/loans/{id}/approve` | ADM | 409 si ya no está pendiente |
| POST | `/api/loans/{id}/reject` | ADM | Libera las unidades reservadas. Admite nota |
| POST | `/api/loans/{id}/request-return` | P / ADM | El propietario pide devolver. 403 sobre el préstamo de otro |
| POST | `/api/loans/{id}/approve-return` | ADM | Confirma que el material ha vuelto |
| POST | `/api/loans/{id}/reject-return` | ADM | El material no ha vuelto: el préstamo regresa a activo |
| POST | `/api/loans/{id}/return` | P / ADM | Para un administrador da el préstamo por devuelto; para el resto equivale a `request-return` |
| DELETE | `/api/loans/{id}` | ADM | |

### Estados y transiciones

Las transiciones válidas están declaradas en un único sitio (`LoanTransitions`)
y todos los endpoints consultan la misma tabla. Cualquier otra combinación
responde `409 Conflict`.

```
                  ┌──────────────► Rejected  (final)
                  │
PendingApproval ──┤
                  │
                  └──────────────► Active ◄──────────┐
                                     │               │
                                     ├──► Returned   │ (reject-return)
                                     │      (final)  │
                                     └──► ReturnRequested
                                              │      │
                                              └──────┘
                                              └──► Returned
```

## Auditoría

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/api/audit` | ADM | Sólo lectura, paginada (máximo 100 por página). Filtros por acción y entidad |

No existe ningún endpoint para escribir ni borrar entradas. Las anota el
servidor dentro de la misma transacción que la operación auditada.

## Cuentas

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/api/users` | ADM | Ficha completa |
| GET | `/api/users/summary` | A | Solo identificador y nombre. Necesario para elegir destinatario de un préstamo, sin exponer correos, teléfonos ni roles |
| GET | `/api/users/{id}` | P / ADM | 403 sobre la ficha de otro |
| POST | `/api/users` | ADM | |
| PUT | `/api/users/{id}` | P / ADM | Datos de contacto. No incluye rol ni estado |
| PUT | `/api/users/{id}/access` | ADM | Rol y activación. Un administrador no puede retirarse el acceso a sí mismo |
| POST | `/api/users/{id}/password` | ADM | Reinicio. No recibe contraseña: asigna la provisional, obliga a cambiarla y cierra todas las sesiones de esa cuenta |
| DELETE | `/api/users/{id}` | ADM | No permite borrarse a uno mismo ni cuentas con préstamos vivos |

**No existe ningún endpoint de «cambiar mi contraseña».** Es deliberado: las
contraseñas las gestiona la administración, y el único punto en el que una
persona fija la suya es `/api/auth/change-password`, que sólo funciona con un
cambio pendiente.

## Recuperación de contraseña

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/api/password-reset-requests` | ADM | Bandeja. `?soloPendientes=true` filtra |
| GET | `/api/password-reset-requests/pending-count` | ADM | Sólo el número, para el aviso del menú |
| POST | `/api/password-reset-requests/{id}/approve` | ADM | Asigna la provisional y revoca sesiones. 409 si ya estaba resuelta |
| POST | `/api/password-reset-requests/{id}/reject` | ADM | No toca la contraseña. 409 si ya estaba resuelta |

## Cuentas con la contraseña sin cambiar

Una sesión abierta con la contraseña provisional **sólo puede llegar a cuatro
rutas**: `change-password`, `me`, `logout` y `refresh`. Cualquier otra devuelve
403 con el tipo `urn:assetflow:cambio-de-contrasena-pendiente`, incluidas las
que ese rol tendría permitidas en condiciones normales. La lista es blanca, así
que un endpoint nuevo nace cerrado para esas sesiones.

## Sistema

| Método | Ruta | Acceso | Notas |
|---|---|---|---|
| GET | `/health` | — | Sonda de vida. Devuelve `{"status":"ok"}` y nada más |

## Decisiones que conviene explicar

**Por qué `/api/users/summary` es accesible para cualquier usuario.** Registrar
un préstamo requiere elegir a una persona, y para eso hace falta una lista de
nombres. Devolver la ficha completa para esa pantalla expondría correos,
teléfonos y roles a quien solo necesita un nombre, así que existe un DTO
reducido aparte.

**Por qué el rol se cambia en un endpoint propio.** Cambiar el rol de alguien o
desactivar su cuenta tiene consecuencias distintas de corregirle el teléfono.
Separarlos evita que ocurra por inercia al pulsar «Guardar» en un formulario de
datos de contacto.

**Por qué un administrador no puede desactivarse a sí mismo.** Es la forma más
fácil de dejar el sistema sin nadie capaz de administrarlo. La comprobación
está en el servidor; la interfaz solo evita que se llegue a intentar.

**Por qué el `userId` del listado de préstamos se ignora para un usuario
normal.** El parámetro es una comodidad para el administrador. Si se respetara
para todos, sería un IDOR de manual: bastaría cambiar un número en la URL para
ver los préstamos de cualquiera.

**Por qué un administrador registra el préstamo ya aprobado.** Sería él quien
tendría que aprobarlo después, así que obligarle a confirmar su propia acción
añade un paso sin aportar ningún control.

**Por qué `/return` se comporta distinto según el rol.** Un administrador da el
material por devuelto porque lo tiene delante; un usuario sólo puede *pedir* que
se lo den por devuelto. Se mantiene la misma ruta porque es la que ya consume el
cliente Android, que queda fuera del alcance de esta versión.

**Qué se le muestra al administrador de quien solicita.** El nombre y el
contenido de la solicitud, nada más. `LoanDto` envía `DecidedByName`, no la
ficha de quien decidió: el solicitante sabe quién resolvió sin recibir de paso
su correo ni su teléfono.

**Por qué la auditoría no guarda direcciones IP.** Es un dato personal y esta
aplicación no lo necesita: la auditoría responde a «quién hizo qué», no a «desde
dónde». El abuso por origen ya lo corta el limitador de peticiones sin
almacenar nada.
