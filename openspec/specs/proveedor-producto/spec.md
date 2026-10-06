# Purpose
Define las especificaciones para la transacción referencial N a N entre Proveedores y Productos en la tienda online, controlando relaciones múltiples bidireccionales, operaciones Create, Read, Delete lógico y validación referencial.

# Requirements

### Requirement: Atributos y Estructura de Proveedor y Producto N a N
The system SHALL maintain at least six business attributes for Proveedor and Producto collections, excluding the MongoDB document identifier `_id`.

#### Scenario: Atributos de proveedor
- **WHEN** provider records are handled in the application
- **THEN** Proveedor includes `nombre`, `nit`, `contacto`, `telefono`, `email`, `direccion`, `pais`, `productosIds`, `activo`, and `version`
- **AND** Producto includes `proveedoresIds` as a list of referenced provider ObjectIds.

### Requirement: Sincronización Bidireccional N a N en Creación
The system SHALL synchronize multiple references bidirectionally between Producto and Proveedor using array fields `producto.proveedoresIds` and `proveedor.productosIds`.

#### Scenario: Asociación de producto con múltiples proveedores
- **WHEN** a product is saved with a selection of active provider IDs
- **THEN** each selected provider's `productosIds` array adds the product ID
- **AND** the product's `proveedoresIds` stores all selected provider IDs
- **AND** if any provider update fails, compensation logic MUST remove previously inserted references.

### Requirement: Operaciones CRUD N a N y Reglas de Actualización
The system SHALL provide Create, Read, and Soft Delete operations for the N-to-N relationship, preserving relational integrity across associated entities.

#### Scenario: Consulta de proveedores asociados
- **WHEN** a user inspects a product or provider record
- **THEN** the system resolves and displays all active associated entities from the respective collection
- **AND** inactive suppliers or products are excluded from active relationship views.

### Requirement: Eliminación Lógica en Relaciones N a N
The system MUST implement soft deletion (`activo = false`) for Proveedores and Productos, prohibiting physical deletion from MongoDB.

#### Scenario: Borrado lógico de proveedor
- **WHEN** a user deletes an active provider
- **THEN** the provider's `activo` flag is set to false
- **AND** the provider's identifier is removed from active product associations or preserved as inactive for auditing
- **AND** the provider record remains in the database.

#### Scenario: Rechazo de borrado de proveedor con órdenes pendientes
- **WHEN** a user attempts to delete a provider whose products are part of active orders
- **THEN** the system MUST validate and alert the user or safely handle the reference without data loss.

# Data Model

### Collection: Proveedor
Source: `Models/Proveedor.cs`

| BSON Field     | C# Property  | Type              | Default         | Description                                      |
|----------------|--------------|-------------------|-----------------|--------------------------------------------------|
| `_id`          | Id           | ObjectId (string) | `string.Empty`  | MongoDB document identifier                      |
| `nombre`       | Nombre       | string            | `string.Empty`  | Supplier company name                            |
| `nit`          | Nit          | string            | `string.Empty`  | Tax identification number (NIT)                  |
| `contacto`     | Contacto     | string            | `string.Empty`  | Contact person name                              |
| `telefono`     | Telefono     | string            | `string.Empty`  | Contact phone number                             |
| `email`        | Email        | string            | `string.Empty`  | Contact email address                            |
| `direccion`    | Direccion    | string            | `string.Empty`  | Physical address                                 |
| `pais`         | Pais         | string            | `string.Empty`  | Country of origin                                |
| `productosIds` | ProductosIds | List\<ObjectId\>  | `[]`            | **↔ Producto (N:M)** supplied product IDs        |
| `activo`       | Activo       | bool              | `true`          | Soft-delete flag                                 |
| `version`      | Version      | int               | `1`             | Optimistic concurrency version counter           |

### Relationship: Proveedor ↔ Producto (N:M Bidirectional)
- **Proveedor side**: `productosIds` stores an array of Producto ObjectIds
- **Producto side**: `proveedoresIds` stores an array of Proveedor ObjectIds
- **Synchronization**: Managed at the application layer in `ProveedorService` and `ProductoService` with compensation rollback
