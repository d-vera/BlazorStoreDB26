# Purpose
Define las especificaciones funcionales y de datos para la gestión referencial 1 a 1 entre Clientes y Perfiles en la tienda online, garantizando sincronización bidireccional, operaciones CRUD y borrado lógico.

# Requirements

### Requirement: Atributos y Estructura de Cliente y Perfil
The system SHALL maintain at least six business attributes for both Cliente and Perfil collections, excluding the MongoDB document identifier `_id`.

#### Scenario: Validación de atributos requeridos
- **WHEN** a client or profile record is created or retrieved
- **THEN** Cliente includes `nombres`, `apellidos`, `documento`, `email`, `telefono`, `fechaRegistro`, `perfilId`, `pedidosIds`, `activo`, and `version`
- **AND** Perfil includes `clienteId`, `direccion`, `ciudad`, `pais`, `fechaNacimiento`, `preferencias`, `puntosFidelidad`, `activo`, and `version`.

### Requirement: Sincronización Bidireccional en Creación 1 a 1
The system SHALL establish a synchronized bidirectional reference between Cliente and Perfil upon creation, storing `perfilId` on Cliente and `clienteId` on Perfil.

#### Scenario: Creación exitosa de cliente con perfil
- **WHEN** a new client is submitted with corresponding profile details
- **THEN** the profile is persisted or linked with `clienteId` matching the client's identifier
- **AND** the client record stores the profile's identifier in `perfilId`
- **AND** if synchronization fails at any stage, compensation cleanup MUST roll back created records.

### Requirement: Actualización de Cliente y Perfil con Concurrencia Optimista
The system SHALL support updating Cliente and Perfil records while preventing concurrent conflict overwrites via a version counter.

#### Scenario: Modificación de cliente y verificación de versionamiento
- **WHEN** a user submits updates to a client or profile
- **THEN** the update succeeds only if the target document's `version` matches the persisted version
- **AND** the system increments the `version` attribute upon successful modification
- **AND** reassigning a profile already linked to another client MUST be rejected.

### Requirement: Eliminación Lógica de Clientes y Perfiles
The system MUST implement soft deletion exclusively, marking documents as `activo = false` and prohibiting physical deletion from the database.

#### Scenario: Borrado lógico de cliente sin pedidos activos
- **WHEN** a user requests deletion of an existing client without active orders
- **THEN** the client's `activo` flag is set to false
- **AND** the linked profile's `activo` flag is set to false
- **AND** all existing references are preserved for auditing and historical traceability.

#### Scenario: Rechazo de borrado de cliente con pedidos activos
- **WHEN** a user requests deletion of a client that has active orders
- **THEN** the system MUST reject the deletion request with a validation error
- **AND** both the client and profile remain active.

### Requirement: Consulta de Clientes y Perfiles Activos
The system SHALL filter queries by default so that only active records are accessible in the standard application workflows.

#### Scenario: Listado de clientes en la interfaz
- **WHEN** the client list view is loaded
- **THEN** the query returns only documents where `activo` is true
- **AND** logically deleted clients and profiles are excluded from the view.

# Data Model

### Collection: Cliente
Source: `Models/Cliente.cs`

| BSON Field      | C# Property   | Type              | Default         | Description                                       |
|-----------------|----------------|-------------------|-----------------|---------------------------------------------------|
| `_id`           | Id             | ObjectId (string) | `string.Empty`  | MongoDB document identifier                       |
| `nombres`       | Nombres        | string            | `string.Empty`  | Client first name(s)                              |
| `apellidos`     | Apellidos      | string            | `string.Empty`  | Client last name(s)                               |
| `documento`     | Documento      | string            | `string.Empty`  | National identification document number           |
| `email`         | Email          | string            | `string.Empty`  | Contact email address                             |
| `telefono`      | Telefono       | string            | `string.Empty`  | Contact phone number                              |
| `fechaRegistro` | FechaRegistro  | DateTime          | —               | Registration timestamp                            |
| `perfilId`      | PerfilId       | ObjectId? (string?)| `null`         | **→ Perfil (1:1)** linked profile ID (nullable)   |
| `pedidosIds`    | PedidosIds     | List\<ObjectId\>  | `[]`            | **→ Pedido (1:N)** associated order IDs           |
| `activo`        | Activo         | bool              | `true`          | Soft-delete flag                                  |
| `version`       | Version        | int               | `1`             | Optimistic concurrency version counter            |

### Collection: Perfil
Source: `Models/Perfil.cs`

| BSON Field        | C# Property     | Type              | Default         | Description                                    |
|-------------------|------------------|-------------------|-----------------|------------------------------------------------|
| `_id`             | Id               | ObjectId (string) | `string.Empty`  | MongoDB document identifier                    |
| `clienteId`       | ClienteId        | ObjectId? (string?)| `null`         | **→ Cliente (1:1)** owning client ID (nullable)|
| `direccion`       | Direccion        | string            | `string.Empty`  | Shipping/billing address                       |
| `ciudad`          | Ciudad           | string            | `string.Empty`  | City of residence                              |
| `pais`            | Pais             | string            | `string.Empty`  | Country of residence                           |
| `fechaNacimiento` | FechaNacimiento  | DateTime          | —               | Date of birth                                  |
| `preferencias`    | Preferencias     | List\<string\>    | `[]`            | User preference tags                           |
| `puntosFidelidad` | PuntosFidelidad  | int               | `0`             | Loyalty/fidelity points balance                |
| `activo`          | Activo           | bool              | `true`          | Soft-delete flag                               |
| `version`         | Version          | int               | `1`             | Optimistic concurrency version counter         |

### Relationship: Cliente ↔ Perfil (1:1 Bidirectional)
- **Cliente side**: `perfilId` stores the single Perfil ObjectId (nullable, `BsonIgnoreIfNull`)
- **Perfil side**: `clienteId` stores the single Cliente ObjectId (nullable)
- **Synchronization**: Managed at the application layer in `ClienteService` and `PerfilService` with compensation rollback
