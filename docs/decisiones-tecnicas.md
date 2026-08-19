# Decisiones técnicas

Por qué el código es como es. Cada apartado explica una decisión que no era
obvia, la alternativa que se descartó y el motivo.

---

## 1. La API es el único punto donde se decide quién puede hacer qué

La versión original de este proyecto tenía la lógica de acceso en el cliente de
escritorio: descargaba la lista de usuarios, comparaba la contraseña y ocultaba
botones según el rol. Eso no es control de acceso. Cualquiera con `curl` podía
saltarse la aplicación entera.

El modelo actual parte de una premisa: **un atacante no va a usar la interfaz;
va a llamar directamente a la API.** De ahí se derivan tres reglas que se
aplican sin excepción:

- La identidad sale **siempre** del token firmado, nunca de un parámetro.
  `ControllerBaseExtensions.UsuarioId()` es el único origen. Un atacante puede
  escribir el `userId` que quiera en el cuerpo o en la URL, pero no puede
  falsificar un *claim* sin la clave de firma.
- La autorización es **cerrada por defecto**. `AuthorizationOptions.FallbackPolicy`
  exige autenticación en todo endpoint que no diga lo contrario, así que un
  endpoint nuevo nace protegido y hay que abrirlo a propósito.
- Las comprobaciones de propiedad viven en el servidor, no en la vista.
  `FilaPrestamo` decide qué botones dibuja el cliente; lo que impide aprobar una
  solicitud propia es que `POST /api/loans/{id}/approve` exige rol de
  administrador.

Los tests de `AutorizacionTests` y `FlujoPrestamosTests` atacan la API sin pasar
por el cliente, que es exactamente lo que haría alguien intentando saltarse la
interfaz.

---

## 2. Asignación masiva: los campos del cliente no tocan la entidad

El controlador anterior adjuntaba la entidad recibida con
`EntityState.Modified`. Eso permite sobrescribir **cualquier** columna de la
tabla, incluido el rol.

Ahora todo endpoint de escritura recibe un DTO propio y copia campo a campo. Dos
consecuencias concretas:

- `CreateLoanRequest.UserId` sólo se lee si quien llama es administrador. Para
  el resto se sustituye por el identificador del token antes de tocar nada.
- El rol y el estado de la cuenta se cambian por un endpoint distinto
  (`PUT /api/users/{id}/access`) del que cambia los datos de contacto. Que sean
  dos operaciones separadas evita que un formulario de perfil acabe siendo una
  vía de escalada.

---

## 3. Estado del préstamo: una máquina de estados en un solo sitio

El flujo de aprobación tiene cinco estados. Repartir las transiciones válidas
por los controladores garantizaba que antes o después dos endpoints
discreparían.

`LoanTransitions` declara el grafo completo en un diccionario y todos los
endpoints preguntan a la misma tabla. Aprobar dos veces, devolver algo
rechazado o resucitar un préstamo cerrado responden `409 Conflict`: la petición
es correcta, el estado es el que no admite esa transición.

Los valores numéricos de `Active` y `Returned` se conservaron al ampliar el
`enum` para no tener que reescribir las filas existentes.

---

## 4. Disponibilidad calculada, nunca un contador

No existe una columna «unidades disponibles». Se calcula como
`total − (fuera + reservado)` en cada consulta.

Un contador que se decrementa se desincroniza con el historial en cuanto una
operación falla a medias, y entonces no hay forma de saber cuál de los dos
números miente. Calculándolo, el historial de préstamos es la única fuente de
verdad.

Se distinguen dos conceptos porque no significan lo mismo:

| Concepto | Significado | ¿Está en el almacén? |
|---|---|---|
| **Fuera** | Entregado, sin devolución confirmada | No |
| **Reservado** | Comprometido por una solicitud sin aprobar | Sí, pero prometido |

**Las solicitudes pendientes reservan stock.** Si no lo hicieran, dos
solicitudes sobre la última unidad podrían aprobarse ambas y el inventario
quedaría en negativo. Como contrapartida, una sola cuenta podría reservar el
inventario entero con solicitudes que nadie va a resolver: de ahí el tope de
cinco solicitudes pendientes por usuario.

---

## 5. Sesiones: JWT corto más refresco rotativo con detección de reutilización

- **Token de acceso**: 15 minutos. Corto porque un JWT no se puede revocar.
- **Token de refresco**: 7 días, rotativo. Se guarda **sólo su SHA-256**; el
  valor en claro nunca toca la base de datos.
- **Detección de reutilización**: al rotar, el token consumido queda marcado y
  apunta a su sustituto. Si alguien presenta un token ya rotado, o bien el
  cliente reintentó con uno viejo o bien la cadena está robada. En ambos casos
  se revocan todas las sesiones del usuario y se obliga a entrar de nuevo.

Aquí SHA-256 es la elección correcta, y es el caso contrario al de una
contraseña: el token tiene entropía criptográfica y no admite diccionario, así
que un hash rápido basta. Las contraseñas usan **BCrypt con factor de trabajo
12**, que es lento a propósito.

Cambiar la contraseña, restablecerla o desactivar la cuenta revocan las
sesiones abiertas. Es el motivo principal por el que alguien restablece una
contraseña: recuperar una cuenta de la que ha perdido el control. Dejar vivos
los tokens de quien la tuviera haría inútil el cambio.

---

## 6. Recuperación de contraseña: autorización humana, no código por correo

Quien olvida su contraseña deja una **solicitud** desde la pantalla de acceso, y
un administrador la aprueba desde dentro de la aplicación. No hay código, ni
enlace, ni correo en el camino crítico.

**Qué se gana y qué se pierde**, porque no es una mejora gratuita:

- *Se gana* independencia del correo, que es un canal que esta aplicación no
  controla y que en la práctica falla: dominio sin verificar, SPF/DKIM mal
  configurados, filtros de no deseado. La versión anterior funcionaba en los
  tests y no llegaba a la bandeja de nadie.
- *Se pierde* la prueba de posesión del buzón. Antes, quien recuperaba una
  cuenta demostraba tener acceso al correo registrado; ahora esa comprobación
  la hace una persona. **La seguridad del flujo pasa a depender de que el
  administrador confirme por otra vía quién ha pedido el cambio**, y la
  aplicación no puede obligarle. Por eso la ventana de confirmación lo dice
  explícitamente, y por eso queda registrado en la auditoría quién autorizó
  cada recuperación.

### La contraseña provisional es predecible a propósito

Al aprobar, la cuenta recibe `usuario + "123@"`. Es deducible por cualquiera que
vea un nombre de usuario, y los nombres de usuario están a la vista en la lista
de usuarios. Dicho así suena a defecto, y lo sería si fuera el final de la
historia.

Lo que lo hace aceptable es que **caduca en el primer uso**. La cuenta queda
marcada con `MustChangePassword` y un middleware bloquea todo lo demás:

```
POST /api/auth/login   { ana.lopez, ana.lopez123@ }  →  200  (mustChangePassword: true)
GET  /api/materials                                  →  403
GET  /api/loans                                      →  403
POST /api/loans                                      →  403
GET  /api/auth/me                                    →  200   ← una de las 4 permitidas
```

Esa sesión sólo llega a cuatro rutas: cambiar la contraseña, consultar su propia
identidad, cerrar sesión y renovar el token. La lista es **blanca**, no negra,
así que un endpoint nuevo nace cerrado para estas cuentas — el lado correcto en
el que equivocarse.

Tres detalles que sostienen el resto:

- **No se puede «cambiar» por sí misma.** Sin esa comprobación, el formulario
  obligatorio se pasa dejando la misma contraseña y la cuenta se queda con una
  clave pública. Es la vuelta exacta al agujero que este diseño evita.
- **El cambio devuelve una sesión nueva.** El bloqueo viaja en un claim del
  token para no costar una consulta por petición; la contrapartida es que el
  token viejo sigue diciendo que el cambio está pendiente, así que hay que
  emitir otro.
- **La comprobación de si hay cambio pendiente se hace contra la base de
  datos**, no contra el claim. El token es una copia que puede haber quedado
  atrás.

### El administrador no elige la contraseña de nadie

Ni al aprobar una solicitud ni al reiniciar una cuenta a mano. El servidor
asigna la provisional y la persona escribe la definitiva al entrar.

El motivo no es comodidad: una contraseña que conocen dos personas no
identifica a ninguna de las dos. Si el administrador eligiera la contraseña
definitiva, la auditoría diría «Ana hizo esto» cuando podría haberlo hecho
quien se la puso.

### Nadie cambia su contraseña porque sí

`/api/auth/change-password` es el único punto en el que una persona fija su
propia contraseña, y **sólo funciona con un cambio pendiente**; en cualquier
otro caso devuelve 403. El endpoint de «cambiar mi contraseña» de toda la vida
se ha retirado a propósito: en este sistema las contraseñas las gestiona la
administración.

### La respuesta es idéntica exista o no la cuenta

Mismo código de estado y mismo cuerpo, exista la cuenta, esté desactivada, ya
haya una solicitud pendiente o se haya superado el límite de dos cada media
hora. Si distinguiera los casos, el formulario sería un comprobador de qué
correos están dados de alta, que es el primer paso de un ataque dirigido.

### El cuerpo idéntico no bastaba: había que igualar también el tiempo

La primera versión de este endpoint respondía exactamente lo mismo en los dos
casos y aun así **se podía distinguir con un cronómetro**. Si la cuenta no
existía se hacía `return` tras una consulta; si existía, se hacían dos consultas
más, dos inserciones y **se esperaba al envío SMTP dentro de la petición**.

Medido sobre esta misma aplicación, sin servidor de correo real:

```
Correo que existe:      33 ms · 11 ms · 4,7 ms · 4,6 ms
Correo que no existe:  2,4 ms · 2,2 ms · 2,1 ms · 2,0 ms
```

Más del doble, separable con cuatro muestras. Con un SMTP real, el envío añade
entre 100 ms y varios segundos **sólo en ese camino**: el oráculo pasa de
requerir estadística a verse a simple vista. La protección estaba cerrada por la
puerta y abierta por la ventana.

Se corrigió con dos medidas, en este orden de importancia:

1. **Sacar el envío de la petición.** `IEmailQueue` encola y devuelve el control;
   un `BackgroundService` vacía la cola. El trabajo que distinguía los dos casos
   deja de ocurrir dentro de la petición. Esta es la corrección de fondo.
2. **Un suelo de duración constante** de 250 ms en ambos caminos, que cubre las
   diferencias residuales de las consultas. Es complemento, no sustituto: un
   retardo aleatorio promedia el ruido pero no elimina la señal.

Tras el cambio, ambos caminos tardan lo mismo dentro del ruido de medida:

```
Correo que existe:     0,261 s · 0,260 s · 0,256 s · 0,259 s
Correo que no existe:  0,261 s · 0,263 s · 0,260 s · 0,260 s
```

El envío de correo ya no forma parte de este camino —la recuperación se
resuelve dentro de la aplicación—, pero **el suelo de duración sigue haciendo
falta**: el camino de la cuenta que existe sigue haciendo consultas e insertando
una fila que el otro no hace, y eso también se mide.
`RecuperacionTests.Las_dos_respuestas_tardan_lo_mismo` compara medianas y falla
si se separan más de 80 ms, para que la regresión no vuelva a colarse.

### Aviso de que la contraseña ha cambiado

Tras un reinicio —lo apruebe un administrador desde la bandeja o lo haga a mano
sobre una ficha— sale un correo al titular. **Es el mecanismo por el que se
entera de que alguien ha tocado su cuenta**: sin él, un reinicio no autorizado
es completamente silencioso.

Ese aviso no lleva la contraseña, ni la anterior, ni ningún enlace. Un enlace
para «deshacer el cambio» es exactamente lo que usaría quien acaba de robar la
cuenta para revertir la reacción del titular.

Es la **única** función que queda del correo, y por eso ahora es opcional: la
aplicación arranca sin SMTP en cualquier entorno. Cuando el correo transportaba
códigos de recuperación no podía serlo, porque el sustituto los escribía en
claro en el registro y la función pensada para proteger cuentas era la que las
exponía. Al desaparecer los códigos, desaparece el motivo del bloqueo.

---

## 7. Auditoría: quién hizo qué, y deliberadamente nada más

`AuditEntry` registra actor, acción, entidad y momento. La anotación se añade al
*change tracker* **sin guardar**, para que se persista con el mismo
`SaveChanges` que la operación auditada: o entran las dos cosas o no entra
ninguna. No puede quedar registrada una aprobación que después falló.

Lo que **no** se guarda, y es una decisión, no un olvido:

- **La dirección IP.** Es un dato personal y esta aplicación no la necesita: la
  auditoría responde a «quién hizo qué», no a «desde dónde», y el abuso por
  origen ya lo corta el limitador de peticiones sin almacenar nada.
- **Ningún secreto.** Ni contraseñas, ni tokens, ni sus hashes.
  `AuditoriaTests.La_auditoria_no_contiene_contrasenas_ni_tokens` lo comprueba
  ejercitando precisamente las acciones que manejan secretos.

El nombre del actor se guarda **como copia** además de la clave ajena, y la
tabla no tiene clave ajena a `Users`: si la cuenta se elimina, el registro debe
seguir diciendo quién fue.

No existe ningún endpoint para escribir ni borrar entradas. Un registro que se
pueda editar desde fuera no sirve como registro, ni siquiera para un
administrador.

---

## 8. Concurrencia optimista portable

El control de concurrencia usa una columna `Guid Version` marcada como
`IsConcurrencyToken`, y no el `rowversion` de SQL Server. El motivo es que el
proyecto soporta **dos proveedores** (SQLite por defecto, SQL Server opcional) y
`rowversion` no existe en SQLite. Un `Guid` que se regenera en cada guardado
funciona igual en ambos.

La comprobación se hace a mano en lugar de confiar en la excepción de EF Core:
permite devolver un `409` con el estado actual para que el cliente pueda mostrar
el conflicto en vez de un error genérico.

---

## 9. Dos juegos de migraciones, uno por proveedor

`SqliteAssetFlowDbContext` y `SqlServerAssetFlowDbContext` derivan del mismo
contexto y cada uno tiene su carpeta de migraciones. Los tipos de columna
difieren lo suficiente entre motores como para que un solo juego produzca
esquemas incorrectos en uno de los dos.

---

## 10. Defectos reales encontrados durante el desarrollo

Se documentan porque el diagnóstico explica decisiones del código actual.

**Texto recortado a 8 px en todos los campos.** `TextBoxBase` propaga por su
cuenta el `Padding` del control al `PART_ContentHost`; la plantilla lo ponía
**además** en el `Border`, así que se aplicaba dos veces y al `TextBoxView` le
quedaban 8 de los 38 px. Se encontró midiendo el árbol visual en ejecución,
después de dos hipótesis fallidas. De ahí el comentario de aviso en
`Theme/Controls.xaml`: quitarlo invita a «arreglarlo» volviendo a añadir el
`Padding`.

**Firma y validación de JWT leían configuraciones distintas.** `Program.cs`
resolvía `JwtOptions` con un `Get<JwtOptions>()` inmediato para el validador,
mientras `TokenService` usaba `IOptions<JwtOptions>`, que se resuelve más tarde.
Dos fuentes de verdad para el mismo ajuste: cualquier origen de configuración
añadido después dejaba al emisor firmando con una clave y al validador
comprobando con otra, y **todo respondía 401 sin explicación**. Lo destapó la
batería de integración, no una revisión de código.

**Cuerpo desmedido devolvía 500 en lugar de 413.** Kestrel lanza
`BadHttpRequestException` con el código correcto, pero el middleware de errores
la capturaba como fallo genérico. Un 500 barato de provocar es justo lo que
busca quien tantea una API.

**Fallo de credenciales silencioso.** `TxtClave.Clear()` dispara
`PasswordChanged`, cuyo manejador oculta el aviso; como el vaciado iba después
de mostrar el error, el mensaje se pintaba y se borraba en el mismo ciclo.

**Registro ilegible en español.** `File.AppendAllText` no escribe nunca el
preámbulo UTF-8, ni siquiera al crear el archivo, así que el registro salía sin
BOM y las herramientas que no detectan la codificación mostraban «conexiÃ³n».

**Un test que fallaba el 3 % de las veces por culpa del relleno de Base64.**
`Un_token_con_la_firma_manipulada_no_da_acceso` cambiaba el **último** carácter
del JWT y esperaba un 401. La firma HMAC-SHA256 son 256 bits y sus 43 caracteres
Base64URL codifican 258: el último carácter sólo aporta 4 bits útiles y los 2 de
menor peso son relleno que se descarta al decodificar. Como `'A'` es `000000` y
`'B'` es `000001`, se diferencian **únicamente en un bit de relleno**, así que
cuando la firma terminaba en `A` o en `B` la supuesta manipulación no cambiaba ni
un byte de la firma real: el token seguía siendo válido y la API respondía 200
con toda la razón. Dos casos de 64, de ahí el 3 %. Se corrigió alterando el
primer carácter de la firma, que sí aporta sus 6 bits.
