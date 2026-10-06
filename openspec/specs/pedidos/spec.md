# Purpose
Define las especificaciones para el módulo de Pedidos y Detalles de Pedido, integrando clientes y productos en transacciones de compra, cálculo de totales, estados y borrado lógico.

# Requirements

### Requirement: Estructura y Atributos de Pedido y Detalle
The system SHALL maintain at least six business attributes for the Pedido collection, excluding the MongoDB document identifier `_id`, along with structured line items in `detalles`.

#### Scenario: Atributos obligatorios de un pedido
- **WHEN** an order is created or loaded
- **THEN** Pedido includes `clienteId`, `fechaPedido`, `estado`, `total`, `metodoPago`, `direccionEnvio`, `productosIds`, `detalles`, `activo`, and `version`
- **AND** each item in `detalles` includes `productoId`, `cantidad`, and `precioUnitario`.

### Requirement: Sincronización Bidireccional de Pedido con Cliente y Productos
The system SHALL synchronize the generated order identifier across the associated client and each referenced product upon creation.

#### Scenario: Creación exitosa de pedido con sincronización
- **WHEN** a new order is confirmed for an active client
- **THEN** the order ID is added to the client's `pedidosIds` array
- **AND** the order ID is added to each referenced product's `pedidosIds` array
- **AND** if synchronization with any entity fails, compensation logic MUST remove inserted records.

### Requirement: Validación de Totales y Estados Permitidos
The system SHALL calculate the order total based on line items and enforce allowed values for order status and payment method.

#### Scenario: Validación de estado y total del pedido
- **WHEN** an order is created or updated
- **THEN** the status MUST be one of: "Pendiente", "Procesando", "Enviado", "Entregado", "Cancelado"
- **AND** the payment method MUST be one of: "PSE", "Tarjeta de Crédito", "Tarjeta de Débito", "Efectivo"
- **AND** the total MUST match the calculated sum of `cantidad * precioUnitario` for all line items.

### Requirement: Eliminación Lógica de Pedidos
The system MUST implement soft deletion by updating `activo = false`, preserving order documents for auditing and legal accounting.

#### Scenario: Borrado lógico de pedido
- **WHEN** an order is marked as deleted or canceled
- **THEN** `activo` is updated to false
- **AND** the document is never physically removed from the MongoDB database
- **AND** the customer history preserves the past order for historical audits.

# Data Model

### Collection: Pedido
Source: `Models/Pedido.cs`

| BSON Field       | C# Property   | Type                  | Default         | Description                                         |
|------------------|----------------|-----------------------|-----------------|-----------------------------------------------------|
| `_id`            | Id             | ObjectId (string)     | `string.Empty`  | MongoDB document identifier                         |
| `clienteId`      | ClienteId      | ObjectId (string)     | `string.Empty`  | **→ Cliente (N:1)** owning client reference          |
| `fechaPedido`    | FechaPedido    | DateTime              | —               | Order creation timestamp                            |
| `estado`         | Estado         | string                | `string.Empty`  | Order status (Pendiente, Procesando, Enviado, Entregado, Cancelado) |
| `total`          | Total          | decimal               | `0`             | Calculated order total                              |
| `metodoPago`     | MetodoPago     | string                | `string.Empty`  | Payment method (PSE, Tarjeta de Crédito, Tarjeta de Débito, Efectivo) |
| `direccionEnvio` | DireccionEnvio | string                | `string.Empty`  | Shipping address                                    |
| `productosIds`   | ProductosIds   | List\<ObjectId\>      | `[]`            | **↔ Producto (N:M)** referenced product IDs         |
| `detalles`       | Detalles       | List\<DetallePedido\> | `[]`            | **Embedded** order line items                       |
| `activo`         | Activo         | bool                  | `true`          | Soft-delete flag                                    |
| `version`        | Version        | int                   | `1`             | Optimistic concurrency version counter              |

### Embedded Document: DetallePedido
Source: `Models/DetallePedido.cs`

| BSON Field       | C# Property    | Type              | Default         | Description                                |
|------------------|----------------|-------------------|-----------------|--------------------------------------------|
| `productoId`     | ProductoId     | ObjectId (string) | `string.Empty`  | **→ Producto** referenced product ID       |
| `cantidad`       | Cantidad       | int               | `0`             | Quantity ordered                           |
| `precioUnitario` | PrecioUnitario | decimal           | `0`             | Unit price at time of order                |

### Relationships
- **Pedido → Cliente (N:1)**: `clienteId` references the owning Cliente. Bidirectional via `Cliente.pedidosIds`.
- **Pedido ↔ Producto (N:M)**: `productosIds` stores Producto ObjectIds. Bidirectional via `Producto.pedidosIds`.
- **Pedido → DetallePedido (1:N embedded)**: `detalles` is an embedded array within the Pedido document (not a separate collection). Each detail references a Producto via `productoId`.
- **Synchronization**: Managed at the application layer in `PedidoService` with compensation rollback.
