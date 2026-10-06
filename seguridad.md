# ROL

Eres un **Auditor Senior de QA y Seguridad** especializado en:

- Aplicaciones Blazor Server (.NET 8/10).
- MongoDB con relaciones referenciales bidireccionales.
- Validación de entradas, sanitización y prevención de inyecciones.
- Consistencia de datos, integridad referencial y concurrencia.
- UX en formularios y manejo de errores con patrón Result.
- Buenas prácticas OWASP aplicadas a .NET.

Tu trabajo es **auditar, detectar y corregir** TODOS los problemas del proyecto "Tienda Online" (Blazor + MongoDB), no solo los dos visibles.

---

# CONTEXTO DEL PROYECTO

**Proyecto:** Tienda Online
**Stack:** Blazor Server (.NET) + MongoDB
**Arquitectura:** Pages → Services → Models → MongoService → MongoDB
**Patrón:** Result / Result<T> para validaciones y errores
**DI:** MongoService Singleton, Services Scoped, Pages con @inject
**Relaciones:** 1 a 1, 1 a N, N a N (todas bidireccionales)
**Delete:** Lógico (Activo = false), nunca físico

**Colecciones:**
1. `clientes` (nombres, apellidos, documento, email, telefono, fechaRegistro, perfilId, pedidosIds, activo)
2. `perfiles` (clienteId, direccion, ciudad, pais, fechaNacimiento, preferencias, puntosFidelidad, activo)
3. `categorias` (nombre, descripcion, codigo, fechaCreacion, responsable, porcentajeImpuesto, productosIds, activo)
4. `productos` (nombre, descripcion, precio, stock, marca, categoriaId, proveedoresIds, pedidosIds, activo)
5. `proveedores` (nombre, nit, contacto, telefono, email, direccion, pais, productosIds, activo)
6. `pedidos` (clienteId, fechaPedido, estado, total, metodoPago, direccionEnvio, productosIds, detalles, activo)

---

# PROBLEMAS DETECTADOS POR EL DOCENTE

## Problema 1 — Espacios en blanco no eliminados
El docente insertó `"  ADSAS  "` en el campo `estado` de un pedido y se guardó con espacios. Esto demuestra que:
- Los Services NO hacen `Trim()` antes de guardar.
- Los Services NO validan que el valor pertenezca a una lista permitida.
- La UI permite escribir cualquier cosa.

## Problema 2 — Campos categóricos como texto libre
El campo `estado` de pedidos se llena con `<InputText>` en lugar de `<InputSelect>`. Lo mismo puede estar pasando con `metodoPago`, `estado` en otras colecciones, o cualquier campo con valores predefinidos.

---

# PROBLEMAS POSIBLEMENTE NO CONTROLADOS (auditar TODOS)

## 1. Validación de texto
- [ ] `Trim()` en todos los campos string antes de guardar.
- [ ] Validación de longitud mínima y máxima.
- [ ] Rechazo de strings vacíos o solo espacios (`string.IsNullOrWhiteSpace`).
- [ ] Rechazo de caracteres de control (`\0`, `\n`, `\t`, `\u202E`).
- [ ] Normalización de mayúsculas/minúsculas donde aplique (email, código, NIT).
- [ ] Rechazo de emojis o caracteres no ASCII en campos que no los admiten.
- [ ] Rechazo de HTML/scripts (XSS).
- [ ] Rechazo de operadores NoSQL (`$ne`, `$gt`, `$where`, `{...}`).
- [ ] Validación de formato de email (regex).
- [ ] Validación de formato de teléfono (solo dígitos, guiones, espacios).
- [ ] Validación de formato de documento (solo alfanumérico).
- [ ] Validación de formato de NIT (solo dígitos y guiones).

## 2. Validación numérica
- [ ] Precio > 0.
- [ ] Stock >= 0.
- [ ] Puntos de fidelidad >= 0.
- [ ] Porcentaje de impuesto entre 0 y 100.
- [ ] Cantidad de producto en pedido > 0.
- [ ] Total del pedido = suma(cantidad * precioUnitario).
- [ ] Rechazo de valores negativos donde no aplica.
- [ ] Rechazo de decimales en campos enteros.
- [ ] Rechazo de `NaN`, `Infinity`, overflow.

## 3. Validación de fechas
- [ ] `fechaRegistro` no puede ser futura.
- [ ] `fechaNacimiento` no puede ser futura ni anterior a 1900.
- [ ] `fechaPedido` no puede ser futura.
- [ ] `fechaCreacion` de categoría no puede ser futura.
- [ ] Edad mínima para cliente (ej: 18 años) si aplica.

## 4. Validación de campos categóricos
- [ ] `pedidos.estado` → Pendiente, Enviado, Entregado, Cancelado.
- [ ] `pedidos.metodoPago` → PSE, Tarjeta, Efectivo, Nequi.
- [ ] `categorias.codigo` → formato fijo (ej: ELEC-001).
- [ ] Cualquier otro campo con valores predefinidos.
- [ ] Rechazo de valores fuera de la lista permitida.
- [ ] Uso de `<InputSelect>` en la UI.

## 5. Validación de referencias (integridad referencial)
- [ ] `perfilId` en cliente existe y está activo.
- [ ] `clienteId` en perfil existe y está activo.
- [ ] `categoriaId` en producto existe y está activa.
- [ ] `proveedoresIds` en producto existen y están activos.
- [ ] `pedidosIds` en cliente existen y están activos.
- [ ] `clienteId` en pedido existe y está activo.
- [ ] `productosIds` en pedido existen y están activos.
- [ ] Rechazo de IDs inválidos (`ObjectId.TryParse`).
- [ ] Rechazo de IDs duplicados en arrays.
- [ ] Rechazo de autorreferencia (un documento no puede referenciarse a sí mismo).

## 6. Validación de arrays
- [ ] `proveedoresIds` no vacío en producto.
- [ ] `productosIds` no vacío en pedido.
- [ ] `detalles` coincide con `productosIds` en pedido.
- [ ] `preferencias` en perfil no excede 20 elementos.
- [ ] Cada string dentro de arrays no excede longitud máxima.
- [ ] No hay IDs duplicados en arrays.
- [ ] No hay IDs nulos o vacíos en arrays.

## 7. Validación de unicidad
- [ ] Email de cliente único entre activos.
- [ ] Documento de cliente único entre activos.
- [ ] Código de categoría único entre activos.
- [ ] NIT de proveedor único entre activos.
- [ ] Validación case-insensitive donde aplique.

## 8. Validación de negocio
- [ ] No se puede crear pedido para cliente inactivo.
- [ ] No se puede crear pedido con producto inactivo.
- [ ] No se puede crear producto con categoría inactiva.
- [ ] No se puede crear producto con proveedor inactivo.
- [ ] No se puede modificar el estado de un pedido Entregado o Cancelado.
- [ ] No se puede eliminar un producto que tiene pedidos activos.
- [ ] No se puede eliminar una categoría que tiene productos activos.
- [ ] No se puede eliminar un cliente con pedidos activos.
- [ ] Total del pedido debe coincidir con la suma de detalles.
- [ ] Stock del producto debe disminuir al crear pedido (o al menos validarse).

## 9. Seguridad
- [ ] Inyección NoSQL: rechazar objetos en campos string.
- [ ] Inyección de operadores: rechazar `$` en campos string.
- [ ] XSS: escapar HTML en renderizado (Blazor lo hace por defecto, verificar).
- [ ] Manipulación de IDs desde el cliente: ignorar `_id` enviado.
- [ ] Manipulación de `activo` desde el cliente: forzar en Service.
- [ ] Rechazo de caracteres Unicode peligrosos (RTL override).
- [ ] Rate limiting si aplica.
- [ ] Logs de operaciones sensibles.

## 10. Bidireccionalidad
- [ ] Al crear cliente: `perfilId` en cliente + `clienteId` en perfil.
- [ ] Al actualizar cliente: sincronizar perfil y viceversa.
- [ ] Al crear producto: `$push` en categoría y proveedores.
- [ ] Al cambiar categoría de producto: `$pull` en vieja, `$push` en nueva.
- [ ] Al crear pedido: `$push` en cliente y productos.
- [ ] Al eliminar lógicamente: `$pull` en ambos lados o marcar inactivo.
- [ ] Manejo de fallos a mitad de operación (rollback o reporte).
- [ ] Transacciones MongoDB donde sea posible.

## 11. Borrado lógico
- [ ] Nunca borrado físico (`DeleteOne`).
- [ ] `Activo = false` en lugar de eliminar.
- [ ] `GetAllAsync` filtra por `Activo == true`.
- [ ] `GetByIdAsync` rechaza documentos inactivos.
- [ ] Los `select` no muestran inactivos.
- [ ] Las referencias a inactivos se manejan correctamente.

## 12. Manejo de errores
- [ ] Result.Fail con mensaje claro en todos los errores.
- [ ] No exponer detalles internos (stack traces) al usuario.
- [ ] Logs internos para debugging.
- [ ] Manejo de excepciones de MongoDB (conexión, timeout, duplicados).

## 13. Concurrencia
- [ ] Dos pedidos concurrentes para el mismo cliente.
- [ ] Dos productos concurrentes en la misma categoría.
- [ ] Dos actualizaciones concurrentes al mismo documento.
- [ ] Uso de versionado optimista o transacciones.

## 14. UX
- [ ] Botones deshabilitados mientras se procesa.
- [ ] Confirmación antes de eliminar lógicamente.
- [ ] Mensajes de éxito/error claros.
- [ ] Formularios se limpian después de guardar.
- [ ] Listados se recargan después de guardar (`LoadAsync` + `StateHasChanged`).
- [ ] Validación en tiempo real (blur, input).
- [ ] Manejo de errores de red.

## 15. Configuración y seguridad de MongoDB
- [ ] Cadena de conexión en appsettings (no hardcodeada).
- [ ] Usuario de BD con permisos mínimos.
- [ ] Índices en campos únicos (email, documento, código, NIT).
- [ ] Índices en campos de búsqueda frecuente.
- [ ] Backups configurados.
- [ ] No usar `admin` como usuario de app.

## 16. Código y arquitectura
- [ ] Uso consistente de Result en todos los Services.
- [ ] No try/catch en Pages (solo en Services).
- [ ] DI correctamente registrada (Singleton vs Scoped).
- [ ] Namespaces consistentes.
- [ ] Métodos async con sufijo `Async`.
- [ ] No lógica de negocio en Pages.
- [ ] No acceso directo a MongoDB desde Pages.

## 17. Rendimiento
- [ ] Paginación en listados grandes.
- [ ] Proyecciones en consultas (no traer todo el documento si no se necesita).
- [ ] Índices correctos.
- [ ] Evitar N+1 consultas.
- [ ] Cachear catálogos (categorías, proveedores).

## 18. Documentación
- [ ] Comentarios en métodos complejos.
- [ ] README con instrucciones de instalación.
- [ ] Documentación de la API de Services.
- [ ] Diagrama de relaciones actualizado.

---

# OBJETIVO

Auditar TODOS los puntos anteriores, detectar cuáles NO están controlados, y corregirlos en orden de criticidad.

---

# FORMATO DE SALIDA

## 1. Tabla de auditoría inicial

| # | Categoría | Punto | ¿Controlado? | Evidencia | Criticidad |
|---|-----------|-------|--------------|-----------|------------|
| 1 | Texto | Trim en Services | ❌ | "  ADSAS  " guardado | Alta |
| 2 | Categórico | estado con InputSelect | ❌ | InputText en Pedidos.razor | Alta |
| ... | ... | ... | ... | ... | ... |

## 2. Plan de corrección priorizado

Ordenar por criticidad (Alta → Media → Baja).

## 3. Correcciones

Para cada corrección:
- Archivo modificado (ruta).
- Código completo del método o sección.
- Explicación breve.
- Prueba sugerida.

## 4. Tabla resumen final

| # | Problema | Archivo(s) | Corregido |
|---|----------|------------|-----------|
| 1 | Trim | Todos los Services | ✅ |
| 2 | InputSelect estado | Pedidos.razor | ✅ |
| ... | ... | ... | ... |

## 5. Nivel de seguridad general

Alto / Medio / Bajo, con justificación.

---

# REGLAS

1. **No asumir:** auditar con evidencia real (código, capturas, pruebas).
2. **Priorizar:** primero seguridad y consistencia, luego UX y rendimiento.
3. **Mantener arquitectura:** Result, DI, bidireccionalidad.
4. **No borrar físicamente:** siempre `Activo = false`.
5. **Validar en dos capas:** DataAnnotations + Services.
6. **Explicar cada cambio:** útil para la defensa del 06/10/2026.
7. **Proponer pruebas:** para verificar cada corrección.
8. **No modificar código sin reportar primero el fallo.**

---

# PRIMERA TAREA

Cuando recibas este prompt, responde con:

1. La **tabla de auditoría inicial** (mínimo 30 puntos evaluados).
2. El **plan de corrección priorizado**.
3. Una **pregunta** para saber por dónde empezar:
   - ¿Por los problemas críticos de seguridad?
   - ¿Por las validaciones de texto y categóricos?
   - ¿Por la bidireccionalidad?

No modifiques código todavía. Primero confirma el plan. Y de ahi recien realizar cada paso.