# Purpose
Define las especificaciones de negocio, datos y transacciones referenciales 1 a N entre Categorías y Productos en la tienda online, asegurando consistencia bidireccional, operaciones CRUD y borrado lógico.

# Requirements

### Requirement: Atributos y Estructura de Categoría y Producto
The system SHALL maintain at least six business attributes for both Categoria and Producto collections, excluding the MongoDB document identifier `_id`.

#### Scenario: Validación de atributos en catálogo
- **WHEN** category and product entities are created or retrieved
- **THEN** Categoria includes `nombre`, `descripcion`, `codigo`, `fechaCreacion`, `responsable`, `porcentajeImpuesto`, `productosIds`, `activo`, and `version`
- **AND** Producto includes `nombre`, `descripcion`, `precio`, `stock`, `marca`, `categoriaId`, `proveedoresIds`, `pedidosIds`, `activo`, and `version`.

### Requirement: Sincronización Bidireccional en Creación 1 a N
The system SHALL maintain bidirectional referential consistency when a product is assigned to a category, updating both `producto.categoriaId` and `categoria.productosIds`.

#### Scenario: Registro de nuevo producto en categoría activa
- **WHEN** a product is created referencing an active category
- **THEN** the product persists `categoriaId` with the corresponding category identifier
- **AND** the category's `productosIds` array is atomically appended with the new product ID
- **AND** if category synchronization fails, the newly inserted product MUST be rolled back.

### Requirement: Actualización y Reasignación de Categoría
The system SHALL allow updating category and product properties and transfer product references across categories when reassigned.

#### Scenario: Reasignación de categoría entre dos categorías activas
- **WHEN** a product's assigned category is modified
- **THEN** the product identifier is removed from the previous category's `productosIds` list
- **AND** the product identifier is appended to the target category's `productosIds` list
- **AND** document versions MUST increment to ensure optimistic concurrency control.

### Requirement: Eliminación Lógica y Protección contra Huérfanos
The system MUST execute soft deletion via `activo = false` and reject deletion of categories that have active associated products.

#### Scenario: Rechazo de borrado de categoría con productos activos
- **WHEN** a user attempts to delete a category that still contains active products
- **THEN** the system MUST block the deletion and return an error message
- **AND** the category remains active.

#### Scenario: Borrado lógico de producto activo
- **WHEN** a user deletes an active product
- **THEN** the product's `activo` flag is updated to false without physical deletion
- **AND** the category retains historical traceability.

### Requirement: Consulta de Catálogo Activo
The system SHALL return only active category and product records in catalog queries.

#### Scenario: Carga de lista de productos y categorías
- **WHEN** the products or categories page requests data
- **THEN** only documents with `activo = true` are returned
- **AND** deleted products are excluded from active category listings.

# Data Model

### Collection: Categoria
Source: `Models/Categoria.cs`

| BSON Field          | C# Property       | Type              | Default         | Description                                  |
|---------------------|--------------------|-------------------|-----------------|----------------------------------------------|
| `_id`               | Id                 | ObjectId (string) | `string.Empty`  | MongoDB document identifier                  |
| `nombre`            | Nombre             | string            | `string.Empty`  | Category display name                        |
| `descripcion`       | Descripcion        | string            | `string.Empty`  | Category description                         |
| `codigo`            | Codigo             | string            | `string.Empty`  | Unique category code                         |
| `fechaCreacion`     | FechaCreacion      | DateTime          | —               | Creation timestamp                           |
| `responsable`       | Responsable        | string            | `string.Empty`  | Person responsible for the category          |
| `porcentajeImpuesto`| PorcentajeImpuesto | decimal           | `0`             | Tax percentage applied to products           |
| `productosIds`      | ProductosIds       | List\<ObjectId\>  | `[]`            | **→ Producto (1:N)** referenced product IDs  |
| `activo`            | Activo             | bool              | `true`          | Soft-delete flag                             |
| `version`           | Version            | int               | `1`             | Optimistic concurrency version counter       |

### Collection: Producto
Source: `Models/Producto.cs`

| BSON Field        | C# Property    | Type              | Default         | Description                                      |
|-------------------|----------------|-------------------|-----------------|--------------------------------------------------|
| `_id`             | Id             | ObjectId (string) | `string.Empty`  | MongoDB document identifier                      |
| `nombre`          | Nombre         | string            | `string.Empty`  | Product display name                             |
| `descripcion`     | Descripcion    | string            | `string.Empty`  | Product description                              |
| `precio`          | Precio         | decimal           | `0`             | Unit price                                       |
| `stock`           | Stock          | int               | `0`             | Available inventory quantity                     |
| `marca`           | Marca          | string            | `string.Empty`  | Brand name                                       |
| `categoriaId`     | CategoriaId    | ObjectId (string) | `string.Empty`  | **→ Categoria (N:1)** owning category reference  |
| `proveedoresIds`  | ProveedoresIds | List\<ObjectId\>  | `[]`            | **↔ Proveedor (N:M)** associated supplier IDs    |
| `pedidosIds`      | PedidosIds     | List\<ObjectId\>  | `[]`            | **↔ Pedido (N:M)** associated order IDs          |
| `activo`          | Activo         | bool              | `true`          | Soft-delete flag                                 |
| `version`         | Version        | int               | `1`             | Optimistic concurrency version counter           |

### Relationship: Categoria → Producto (1:N Bidirectional)
- **Categoria side**: `productosIds` stores an array of Producto ObjectIds
- **Producto side**: `categoriaId` stores the single owning Categoria ObjectId
- **Synchronization**: Managed at the application layer in `CategoriaService` and `ProductoService` with rollback compensation
