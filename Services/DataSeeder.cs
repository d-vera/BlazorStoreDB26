using MongoDB.Driver;
using tienda.Common;
using tienda.Models;

namespace tienda.Services;

public class DataSeeder
{
    private readonly CategoriaService _categoriaService;
    private readonly ProveedorService _proveedorService;
    private readonly ProductoService _productoService;
    private readonly ClienteService _clienteService;
    private readonly PerfilService _perfilService;
    private readonly PedidoService _pedidoService;
    private readonly MongoService _mongoService;

    public DataSeeder(
        CategoriaService categoriaService,
        ProveedorService proveedorService,
        ProductoService productoService,
        ClienteService clienteService,
        PerfilService perfilService,
        PedidoService pedidoService,
        MongoService mongoService)
    {
        _categoriaService = categoriaService;
        _proveedorService = proveedorService;
        _productoService = productoService;
        _clienteService = clienteService;
        _perfilService = perfilService;
        _pedidoService = pedidoService;
        _mongoService = mongoService;
    }

    public async Task<SeederFullResult> SeedAllAsync()
    {
        var fullResult = new SeederFullResult();

        // Limpieza de todas las colecciones para una inserción desde cero limpia y consistente
        await _mongoService.GetCollection<Pedido>("pedidos").DeleteManyAsync(FilterDefinition<Pedido>.Empty);
        await _mongoService.GetCollection<Cliente>("clientes").DeleteManyAsync(FilterDefinition<Cliente>.Empty);
        await _mongoService.GetCollection<Perfil>("perfiles").DeleteManyAsync(FilterDefinition<Perfil>.Empty);
        await _mongoService.GetCollection<Producto>("productos").DeleteManyAsync(FilterDefinition<Producto>.Empty);
        await _mongoService.GetCollection<Proveedor>("proveedores").DeleteManyAsync(FilterDefinition<Proveedor>.Empty);
        await _mongoService.GetCollection<Categoria>("categorias").DeleteManyAsync(FilterDefinition<Categoria>.Empty);

        // 1. SEED CATEGORÍAS (4)
        var resCat = await SeedCategoriasInternalAsync();
        fullResult.Pasos.Add(resCat);

        // 2. SEED PROVEEDORES (5)
        var (resProv, proveedores) = await SeedProveedoresInternalAsync();
        fullResult.Pasos.Add(resProv);

        // 3. SEED PRODUCTOS (10)
        var (resProd, productos, categoriasActivas) = await SeedProductosInternalAsync(proveedores);
        fullResult.Pasos.Add(resProd);

        // 4. SEED CLIENTES Y PERFILES (8 clientes, 8 perfiles)
        var (resCli, resPerf, clientes) = await SeedClientesYPerfilesInternalAsync();
        fullResult.Pasos.Add(resCli);
        fullResult.Pasos.Add(resPerf);

        // 5. SEED PEDIDOS (12)
        var resPed = await SeedPedidosInternalAsync(clientes, productos);
        fullResult.Pasos.Add(resPed);

        // 6. INACTIVACIONES 1 DE CADA 3 (Soft Delete)
        await AplicarInactivaciones1DeCada3Async(proveedores, productos, clientes);

        // Recalcular estadísticas finales post-inactivación
        await ActualizarVerificacionesFinalesAsync(fullResult);

        return fullResult;
    }

    private async Task<SeederStepResult> SeedCategoriasInternalAsync()
    {
        var logs = new List<string>();
        var collection = _mongoService.GetCollection<Categoria>("categorias");

        var listaCategorias = new List<Categoria>
        {
            new Categoria
            {
                Nombre = "Electrónica y Tecnología",
                Descripcion = "Dispositivos electrónicos, componentes informáticos, smartphones y accesorios de última generación.",
                Codigo = "CAT-ELE-001",
                FechaCreacion = DateTime.Now.AddDays(-120),
                Responsable = "Carlos Mendoza",
                PorcentajeImpuesto = 19.0m,
                Activo = true
            },
            new Categoria
            {
                Nombre = "Electrodomésticos y Hogar",
                Descripcion = "Equipos para cocina, refrigeración, lavado y confort del hogar de alta eficiencia energética.",
                Codigo = "CAT-HOG-002",
                FechaCreacion = DateTime.Now.AddDays(-90),
                Responsable = "Mariana Gómez",
                PorcentajeImpuesto = 19.0m,
                Activo = true
            },
            new Categoria
            {
                Nombre = "Ropa y Moda",
                Descripcion = "Prendas de vestir, calzado urbano y deportivo para todas las estaciones.",
                Codigo = "CAT-ROP-003",
                FechaCreacion = DateTime.Now.AddDays(-60),
                Responsable = "Fernando Ruiz",
                PorcentajeImpuesto = 19.0m,
                Activo = true
            },
            new Categoria
            {
                Nombre = "Deportes y Aire Libre",
                Descripcion = "Artículos de entrenamiento, bicicletas, equipos de camping y prendas deportivas.",
                Codigo = "CAT-DEP-004",
                FechaCreacion = DateTime.Now.AddDays(-30),
                Responsable = "Lucía Torres",
                PorcentajeImpuesto = 19.0m,
                Activo = true
            }
        };

        foreach (var cat in listaCategorias)
        {
            var res = await _categoriaService.CreateAsync(cat);
            if (res.Success)
            {
                logs.Add($"[OK] Categoría '{cat.Nombre}' creada con Result.Ok (ID: {cat.Id})");
            }
            else
            {
                logs.Add($"[ERROR] Error al crear categoría '{cat.Nombre}': {res.Message}");
            }
        }

        // Inactivar 1 de cada 3 (3er elemento: 'Ropa y Moda')
        if (listaCategorias.Count >= 3)
        {
            var delRes = await _categoriaService.DeleteLogicoAsync(listaCategorias[2].Id);
            if (delRes.Success)
            {
                logs.Add($"[OK] Categoría '{listaCategorias[2].Nombre}' inactivada lógicamente.");
            }
        }

        var getAllRes = await _categoriaService.GetAllAsync();
        int activosGetAll = getAllRes.Success && getAllRes.Data is not null ? getAllRes.Data.Count : 0;
        int totalEnDb = (int)await collection.CountDocumentsAsync(FilterDefinition<Categoria>.Empty);
        int inactivosEnDb = totalEnDb - activosGetAll;

        return new SeederStepResult
        {
            Coleccion = "Categorías",
            Insertados = totalEnDb,
            Activos = activosGetAll,
            Inactivos = inactivosEnDb,
            Verificado = (getAllRes.Success && activosGetAll == 3 && inactivosEnDb == 1) ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logs
        };
    }

    private async Task<(SeederStepResult Result, List<Proveedor> Proveedores)> SeedProveedoresInternalAsync()
    {
        var logs = new List<string>();
        var collection = _mongoService.GetCollection<Proveedor>("proveedores");

        var listaProveedores = new List<Proveedor>
        {
            new Proveedor
            {
                Nombre = "Samsung Electronics Colombia S.A.",
                Nit = "830.001.999-1",
                Contacto = "Roberto Gómez",
                Telefono = "6013190000",
                Email = "ventas.co@samsung.com",
                Direccion = "Carrera 7 No. 113-43 Piso 10",
                Pais = "Colombia",
                Activo = true
            },
            new Proveedor
            {
                Nombre = "LG Electronics Colombia Ltda.",
                Nit = "830.055.888-2",
                Contacto = "Patricia Jaramillo",
                Telefono = "6016347900",
                Email = "comercial@lg.com.co",
                Direccion = "Avenida Calle 26 No. 92-32",
                Pais = "Colombia",
                Activo = true
            },
            new Proveedor
            {
                Nombre = "Sony Inter-American S.A.",
                Nit = "860.022.777-3",
                Contacto = "Esteban Morales",
                Telefono = "6017425000",
                Email = "atencion.clientes@sony.com.co",
                Direccion = "Calle 100 No. 19-61",
                Pais = "Colombia",
                Activo = true
            },
            new Proveedor
            {
                Nombre = "Kalley de Colombia S.A.S.",
                Nit = "900.111.444-4",
                Contacto = "Diana Marcela Ospina",
                Telefono = "6044443210",
                Email = "distribuidores@kalley.com.co",
                Direccion = "Calle 10 Sur No. 50-24",
                Pais = "Colombia",
                Activo = true
            },
            new Proveedor
            {
                Nombre = "Nike Colombia S.A.S.",
                Nit = "830.099.333-5",
                Contacto = "Felipe Restrepo",
                Telefono = "6015897000",
                Email = "ventas.corporativas@nike.com.co",
                Direccion = "Calle 93B No. 13-45",
                Pais = "Colombia",
                Activo = true
            }
        };

        foreach (var prov in listaProveedores)
        {
            var res = await _proveedorService.CreateAsync(prov);
            if (res.Success)
            {
                logs.Add($"[OK] Proveedor '{prov.Nombre}' creado con Result.Ok (ID: {prov.Id})");
            }
            else
            {
                logs.Add($"[ERROR] Error al crear proveedor '{prov.Nombre}': {res.Message}");
            }
        }

        var getAllRes = await _proveedorService.GetAllAsync();
        int activosGetAll = getAllRes.Success && getAllRes.Data is not null ? getAllRes.Data.Count : 0;
        int totalEnDb = (int)await collection.CountDocumentsAsync(FilterDefinition<Proveedor>.Empty);

        var stepResult = new SeederStepResult
        {
            Coleccion = "Proveedores",
            Insertados = totalEnDb,
            Activos = activosGetAll,
            Inactivos = totalEnDb - activosGetAll,
            Verificado = getAllRes.Success ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logs
        };

        return (stepResult, listaProveedores);
    }

    private async Task<(SeederStepResult Result, List<Producto> Productos, List<Categoria> CategoriasActivas)> SeedProductosInternalAsync(List<Proveedor> proveedores)
    {
        var logs = new List<string>();
        var collection = _mongoService.GetCollection<Producto>("productos");

        // Obtener categorías activas disponibles
        var catRes = await _categoriaService.GetAllAsync();
        var catActivas = catRes.Data ?? [];
        var catElec = catActivas.First(c => c.Codigo == "CAT-ELE-001");
        var catHogar = catActivas.First(c => c.Codigo == "CAT-HOG-002");
        var catDep = catActivas.First(c => c.Codigo == "CAT-DEP-004");

        var provSamsung = proveedores[0];
        var provLG = proveedores[1];
        var provSony = proveedores[2];
        var provKalley = proveedores[3];
        var provNike = proveedores[4];

        var listaProductos = new List<Producto>
        {
            new Producto
            {
                Nombre = "Smart TV Samsung 55 UHD 4K Crystal",
                Descripcion = "Televisor Smart TV 55 pulgadas resolución 4K UHD con procesador Crystal 4K y HDR10+.",
                Precio = 2199900m,
                Stock = 25,
                Marca = "Samsung",
                CategoriaId = catElec.Id,
                ProveedoresIds = [provSamsung.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Laptop ASUS ROG Strix G16 i7",
                Descripcion = "Computador portátil gamer Intel Core i7 13a gen, 16GB RAM DDR5, SSD 512GB Nvme y RTX 4060.",
                Precio = 5499000m,
                Stock = 15,
                Marca = "ASUS",
                CategoriaId = catElec.Id,
                ProveedoresIds = [provSamsung.Id, provSony.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Audífonos Inalámbricos Sony WH-1000XM5",
                Descripcion = "Audífonos Over-Ear con cancelación de ruido de alta fidelidad, 30 horas de batería y micrófono HD.",
                Precio = 1499900m,
                Stock = 30,
                Marca = "Sony",
                CategoriaId = catElec.Id,
                ProveedoresIds = [provSony.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Refrigeradora LG No Frost 420L Inverter",
                Descripcion = "Nevecón de 420 litros tecnología Smart Inverter Compressor con enfriamiento rápido DoorCooling+.",
                Precio = 3299000m,
                Stock = 10,
                Marca = "LG",
                CategoriaId = catHogar.Id,
                ProveedoresIds = [provLG.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Barra de Sonido LG Soundbar S60Q 300W",
                Descripcion = "Barra de sonido de 2.1 canales con Subwoofer inalámbrico, Dolby Digital y Bluetooth 5.1.",
                Precio = 899900m,
                Stock = 40,
                Marca = "LG",
                CategoriaId = catElec.Id,
                ProveedoresIds = [provLG.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Freidora de Aire Kalley 5.5L Digital",
                Descripcion = "Air Fryer con panel táctil de 8 programas automáticos, cesta antiadherente y 1700W de potencia.",
                Precio = 299900m,
                Stock = 50,
                Marca = "Kalley",
                CategoriaId = catHogar.Id,
                ProveedoresIds = [provKalley.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Bicicleta de Montaña Gw Alligator 29",
                Descripcion = "Bicicleta de montaña marco en aluminio monocoque, suspensión delantera con bloqueo y grupo Shimano 24v.",
                Precio = 1850000m,
                Stock = 12,
                Marca = "GW",
                CategoriaId = catDep.Id,
                ProveedoresIds = [provKalley.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Smartwatch Samsung Galaxy Watch 6 44mm",
                Descripcion = "Reloj inteligente con sensor BioActive, monitoreo de sueño, GPS integrado y pantalla Sapphire Crystal.",
                Precio = 1199900m,
                Stock = 20,
                Marca = "Samsung",
                CategoriaId = catElec.Id,
                ProveedoresIds = [provSamsung.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Balón de Fútbol Nike Flight Profesional",
                Descripcion = "Balón oficial de competición con tecnología Aerowsculpt y cubiertas de microtextura moldeada.",
                Precio = 450000m,
                Stock = 35,
                Marca = "Nike",
                CategoriaId = catDep.Id,
                ProveedoresIds = [provNike.Id],
                Activo = true
            },
            new Producto
            {
                Nombre = "Cafetera Expreso Kalley Prima Latte 20 Bar",
                Descripcion = "Máquina de café para expreso y capuchino con depósito de leche desmontable y bomba de 20 bares de presión.",
                Precio = 499900m,
                Stock = 18,
                Marca = "Kalley",
                CategoriaId = catHogar.Id,
                ProveedoresIds = [provKalley.Id],
                Activo = true
            }
        };

        foreach (var prod in listaProductos)
        {
            var res = await _productoService.CreateAsync(prod);
            if (res.Success)
            {
                logs.Add($"[OK] Producto '{prod.Nombre}' creado con Result.Ok y sincronizado bidireccionalmente (ID: {prod.Id})");
            }
            else
            {
                logs.Add($"[ERROR] Error al crear producto '{prod.Nombre}': {res.Message}");
            }
        }

        var getAllRes = await _productoService.GetAllAsync();
        int activosGetAll = getAllRes.Success && getAllRes.Data is not null ? getAllRes.Data.Count : 0;
        int totalEnDb = (int)await collection.CountDocumentsAsync(FilterDefinition<Producto>.Empty);

        var stepResult = new SeederStepResult
        {
            Coleccion = "Productos",
            Insertados = totalEnDb,
            Activos = activosGetAll,
            Inactivos = totalEnDb - activosGetAll,
            Verificado = getAllRes.Success ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logs
        };

        return (stepResult, listaProductos, catActivas);
    }

    private async Task<(SeederStepResult ResultCliente, SeederStepResult ResultPerfil, List<Cliente> Clientes)> SeedClientesYPerfilesInternalAsync()
    {
        var logsCli = new List<string>();
        var logsPerf = new List<string>();

        var paresClientePerfil = new List<(Cliente Cliente, Perfil Perfil)>
        {
            (
                new Cliente { Nombres = "Juan Carlos", Apellidos = "Valencia Restrepo", Documento = "1017123456", Email = "juan.valencia@gmail.com", Telefono = "3104567890", FechaRegistro = DateTime.Today.AddDays(-150) },
                new Perfil { Direccion = "Calle 10 No. 42-15 Apto 502", Ciudad = "Medellín", Pais = "Colombia", FechaNacimiento = new DateTime(1990, 5, 12), Preferencias = ["Tecnología", "Gamer"], PuntosFidelidad = 350 }
            ),
            (
                new Cliente { Nombres = "María Camila", Apellidos = "Rodríguez Silva", Documento = "1020987654", Email = "mcamila.rodriguez@outlook.com", Telefono = "3159876543", FechaRegistro = DateTime.Today.AddDays(-120) },
                new Perfil { Direccion = "Carrera 15 No. 93-40 Int 3", Ciudad = "Bogotá", Pais = "Colombia", FechaNacimiento = new DateTime(1994, 8, 25), Preferencias = ["Hogar", "Cocina"], PuntosFidelidad = 180 }
            ),
            (
                new Cliente { Nombres = "Andrés Felipe", Apellidos = "Gómez Hoyos", Documento = "1032456789", Email = "andres.gomez@yahoo.com", Telefono = "3001234567", FechaRegistro = DateTime.Today.AddDays(-100) },
                new Perfil { Direccion = "Avenida 4 Norte No. 18-22", Ciudad = "Cali", Pais = "Colombia", FechaNacimiento = new DateTime(1988, 11, 30), Preferencias = ["Deportes", "Ciclismo"], PuntosFidelidad = 420 }
            ),
            (
                new Cliente { Nombres = "Sofía Elena", Apellidos = "Morales Castro", Documento = "1045678901", Email = "sofia.morales@hotmail.com", Telefono = "3182345678", FechaRegistro = DateTime.Today.AddDays(-80) },
                new Perfil { Direccion = "Calle 84 No. 52-10", Ciudad = "Barranquilla", Pais = "Colombia", FechaNacimiento = new DateTime(1996, 3, 14), Preferencias = ["Moda", "Tecnología"], PuntosFidelidad = 210 }
            ),
            (
                new Cliente { Nombres = "Diego Alejandro", Apellidos = "Patiño Díaz", Documento = "1056789012", Email = "diego.patino@gmail.com", Telefono = "3123456789", FechaRegistro = DateTime.Today.AddDays(-60) },
                new Perfil { Direccion = "Carrera 27 No. 36-14", Ciudad = "Bucaramanga", Pais = "Colombia", FechaNacimiento = new DateTime(1992, 1, 18), Preferencias = ["Electrónica", "Sonido"], PuntosFidelidad = 500 }
            ),
            (
                new Cliente { Nombres = "Laura Valentina", Apellidos = "Ospina Marín", Documento = "1067890123", Email = "laura.ospina@gmail.com", Telefono = "3144567890", FechaRegistro = DateTime.Today.AddDays(-45) },
                new Perfil { Direccion = "Calle 14 No. 23-08", Ciudad = "Pereira", Pais = "Colombia", FechaNacimiento = new DateTime(1998, 7, 7), Preferencias = ["Fitness", "Deportes"], PuntosFidelidad = 90 }
            ),
            (
                new Cliente { Nombres = "Gabriel Esteban", Apellidos = "Martínez Lara", Documento = "1078901234", Email = "gabriel.martinez@outlook.com", Telefono = "3115678901", FechaRegistro = DateTime.Today.AddDays(-30) },
                new Perfil { Direccion = "Bocagrande Carrera 3 No. 6-50", Ciudad = "Cartagena", Pais = "Colombia", FechaNacimiento = new DateTime(1985, 9, 3), Preferencias = ["Tecnología", "Fotografía"], PuntosFidelidad = 630 }
            ),
            (
                new Cliente { Nombres = "Valentina Isabel", Apellidos = "Vargas Suárez", Documento = "1089012345", Email = "valentina.vargas@gmail.com", Telefono = "3176789012", FechaRegistro = DateTime.Today.AddDays(-15) },
                new Perfil { Direccion = "Carrera 23 No. 54-30", Ciudad = "Manizales", Pais = "Colombia", FechaNacimiento = new DateTime(2001, 12, 22), Preferencias = ["Audio", "Electrónica"], PuntosFidelidad = 150 }
            )
        };

        var clientesCreados = new List<Cliente>();

        foreach (var (cli, perf) in paresClientePerfil)
        {
            var res = await _clienteService.CreateAsync(cli, perf);
            if (res.Success)
            {
                clientesCreados.Add(cli);
                logsCli.Add($"[OK] Cliente '{cli.Nombres} {cli.Apellidos}' creado con Perfil (ID Cliente: {cli.Id}, ID Perfil: {cli.PerfilId})");
                logsPerf.Add($"[OK] Perfil ({perf.Ciudad}) vinculado 1:1 bidireccionalmente a Cliente ({cli.Id})");
            }
            else
            {
                logsCli.Add($"[ERROR] Error al crear cliente '{cli.Nombres}': {res.Message}");
            }
        }

        var getAllCli = await _clienteService.GetAllAsync();
        var getAllPerf = await _perfilService.GetAllAsync();

        int activosCli = getAllCli.Success && getAllCli.Data is not null ? getAllCli.Data.Count : 0;
        int activosPerf = getAllPerf.Success && getAllPerf.Data is not null ? getAllPerf.Data.Count : 0;

        int totalCli = (int)await _mongoService.GetCollection<Cliente>("clientes").CountDocumentsAsync(FilterDefinition<Cliente>.Empty);
        int totalPerf = (int)await _mongoService.GetCollection<Perfil>("perfiles").CountDocumentsAsync(FilterDefinition<Perfil>.Empty);

        var resCliStep = new SeederStepResult
        {
            Coleccion = "Clientes",
            Insertados = totalCli,
            Activos = activosCli,
            Inactivos = totalCli - activosCli,
            Verificado = getAllCli.Success ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logsCli
        };

        var resPerfStep = new SeederStepResult
        {
            Coleccion = "Perfiles",
            Insertados = totalPerf,
            Activos = activosPerf,
            Inactivos = totalPerf - activosPerf,
            Verificado = getAllPerf.Success ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logsPerf
        };

        return (resCliStep, resPerfStep, clientesCreados);
    }

    private async Task<SeederStepResult> SeedPedidosInternalAsync(List<Cliente> clientes, List<Producto> productos)
    {
        var logs = new List<string>();
        var collection = _mongoService.GetCollection<Pedido>("pedidos");

        // Todos los productos para armar pedidos con precios reales
        var p0 = productos[0]; // Smart TV (2,199,900)
        var p1 = productos[1]; // Laptop ASUS (5,499,000)
        var p2 = productos[2]; // Audifonos Sony (1,499,900)
        var p3 = productos[3]; // Nevera LG (3,299,000)
        var p4 = productos[4]; // Barra Sonido LG (899,900)
        var p5 = productos[5]; // Freidora Kalley (299,900)
        var p6 = productos[6]; // Bici GW (1,850,000)
        var p7 = productos[7]; // Smartwatch Galaxy (1,199,900)
        var p8 = productos[8]; // Balón Nike (450,000)
        var p9 = productos[9]; // Cafetera Kalley (499,900)

        var c0 = clientes[0];
        var c1 = clientes[1];
        var c2 = clientes[2];
        var c3 = clientes[3];
        var c4 = clientes[4];
        var c5 = clientes[5];
        var c6 = clientes[6];
        var c7 = clientes[7];

        var listaPedidos = new List<Pedido>
        {
            new Pedido
            {
                ClienteId = c0.Id,
                FechaPedido = DateTime.Now.AddDays(-28),
                Estado = "Entregado",
                MetodoPago = "Tarjeta de Crédito",
                DireccionEnvio = "Calle 10 No. 42-15 Apto 502, Medellín",
                Detalles = [
                    new DetallePedido { ProductoId = p0.Id, Cantidad = 1, PrecioUnitario = p0.Precio },
                    new DetallePedido { ProductoId = p7.Id, Cantidad = 1, PrecioUnitario = p7.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c1.Id,
                FechaPedido = DateTime.Now.AddDays(-25),
                Estado = "Entregado",
                MetodoPago = "PSE",
                DireccionEnvio = "Carrera 15 No. 93-40 Int 3, Bogotá",
                Detalles = [
                    new DetallePedido { ProductoId = p3.Id, Cantidad = 1, PrecioUnitario = p3.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c2.Id,
                FechaPedido = DateTime.Now.AddDays(-22),
                Estado = "Enviado",
                MetodoPago = "Tarjeta de Débito",
                DireccionEnvio = "Avenida 4 Norte No. 18-22, Cali",
                Detalles = [
                    new DetallePedido { ProductoId = p6.Id, Cantidad = 1, PrecioUnitario = p6.Precio },
                    new DetallePedido { ProductoId = p8.Id, Cantidad = 2, PrecioUnitario = p8.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c3.Id,
                FechaPedido = DateTime.Now.AddDays(-20),
                Estado = "Procesando",
                MetodoPago = "Tarjeta de Crédito",
                DireccionEnvio = "Calle 84 No. 52-10, Barranquilla",
                Detalles = [
                    new DetallePedido { ProductoId = p2.Id, Cantidad = 1, PrecioUnitario = p2.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c4.Id,
                FechaPedido = DateTime.Now.AddDays(-18),
                Estado = "Entregado",
                MetodoPago = "PSE",
                DireccionEnvio = "Carrera 27 No. 36-14, Bucaramanga",
                Detalles = [
                    new DetallePedido { ProductoId = p1.Id, Cantidad = 1, PrecioUnitario = p1.Precio },
                    new DetallePedido { ProductoId = p4.Id, Cantidad = 1, PrecioUnitario = p4.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c5.Id,
                FechaPedido = DateTime.Now.AddDays(-15),
                Estado = "Pendiente",
                MetodoPago = "Efectivo",
                DireccionEnvio = "Calle 14 No. 23-08, Pereira",
                Detalles = [
                    new DetallePedido { ProductoId = p5.Id, Cantidad = 2, PrecioUnitario = p5.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c6.Id,
                FechaPedido = DateTime.Now.AddDays(-12),
                Estado = "Entregado",
                MetodoPago = "Tarjeta de Crédito",
                DireccionEnvio = "Bocagrande Carrera 3 No. 6-50, Cartagena",
                Detalles = [
                    new DetallePedido { ProductoId = p0.Id, Cantidad = 2, PrecioUnitario = p0.Precio },
                    new DetallePedido { ProductoId = p2.Id, Cantidad = 1, PrecioUnitario = p2.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c7.Id,
                FechaPedido = DateTime.Now.AddDays(-10),
                Estado = "Procesando",
                MetodoPago = "PSE",
                DireccionEnvio = "Carrera 23 No. 54-30, Manizales",
                Detalles = [
                    new DetallePedido { ProductoId = p9.Id, Cantidad = 1, PrecioUnitario = p9.Precio },
                    new DetallePedido { ProductoId = p5.Id, Cantidad = 1, PrecioUnitario = p5.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c0.Id,
                FechaPedido = DateTime.Now.AddDays(-8),
                Estado = "Enviado",
                MetodoPago = "Tarjeta de Crédito",
                DireccionEnvio = "Calle 10 No. 42-15 Apto 502, Medellín",
                Detalles = [
                    new DetallePedido { ProductoId = p4.Id, Cantidad = 1, PrecioUnitario = p4.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c1.Id,
                FechaPedido = DateTime.Now.AddDays(-5),
                Estado = "Pendiente",
                MetodoPago = "PSE",
                DireccionEnvio = "Carrera 15 No. 93-40 Int 3, Bogotá",
                Detalles = [
                    new DetallePedido { ProductoId = p5.Id, Cantidad = 1, PrecioUnitario = p5.Precio },
                    new DetallePedido { ProductoId = p9.Id, Cantidad = 1, PrecioUnitario = p9.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c3.Id,
                FechaPedido = DateTime.Now.AddDays(-3),
                Estado = "Cancelado",
                MetodoPago = "Tarjeta de Crédito",
                DireccionEnvio = "Calle 84 No. 52-10, Barranquilla",
                Detalles = [
                    new DetallePedido { ProductoId = p7.Id, Cantidad = 1, PrecioUnitario = p7.Precio }
                ]
            },
            new Pedido
            {
                ClienteId = c4.Id,
                FechaPedido = DateTime.Now.AddDays(-1),
                Estado = "Procesando",
                MetodoPago = "PSE",
                DireccionEnvio = "Carrera 27 No. 36-14, Bucaramanga",
                Detalles = [
                    new DetallePedido { ProductoId = p0.Id, Cantidad = 1, PrecioUnitario = p0.Precio },
                    new DetallePedido { ProductoId = p1.Id, Cantidad = 1, PrecioUnitario = p1.Precio }
                ]
            }
        };

        bool todosTotalesCorrectos = true;

        foreach (var ped in listaPedidos)
        {
            var res = await _pedidoService.CreateAsync(ped);
            if (res.Success)
            {
                decimal totalEsperado = ped.Detalles.Sum(d => d.Cantidad * d.PrecioUnitario);
                bool totalOk = Math.Abs(ped.Total - totalEsperado) < 0.01m;
                if (!totalOk)
                {
                    todosTotalesCorrectos = false;
                }

                logs.Add($"[OK] Pedido creado (ID: {ped.Id}) - Cliente: {ped.ClienteId} | Total: ${ped.Total:N0} (Calculado y Verificado)");
            }
            else
            {
                logs.Add($"[ERROR] Error al crear pedido: {res.Message}");
            }
        }

        var getAllRes = await _pedidoService.GetAllAsync();
        int activosGetAll = getAllRes.Success && getAllRes.Data is not null ? getAllRes.Data.Count : 0;
        int totalEnDb = (int)await collection.CountDocumentsAsync(FilterDefinition<Pedido>.Empty);

        var stepResult = new SeederStepResult
        {
            Coleccion = "Pedidos",
            Insertados = totalEnDb,
            Activos = activosGetAll,
            Inactivos = totalEnDb - activosGetAll,
            Verificado = (getAllRes.Success && todosTotalesCorrectos) ? "Éxito (Result.Ok)" : "Fallo",
            Logs = logs
        };

        return stepResult;
    }

    private async Task AplicarInactivaciones1DeCada3Async(
        List<Proveedor> proveedores,
        List<Producto> productos,
        List<Cliente> clientes)
    {
        // 1. Proveedores: 5 total -> inactivar índices 2 y 4 (1 de cada 3 -> 2 inactivos, 3 activos)
        if (proveedores.Count >= 5)
        {
            await _proveedorService.DeleteLogicoAsync(proveedores[2].Id);
            await _proveedorService.DeleteLogicoAsync(proveedores[4].Id);
        }

        // 2. Productos: 10 total -> inactivar índices 2, 5, 8 (1 de cada 3 -> 3 inactivos, 7 activos)
        if (productos.Count >= 9)
        {
            await _productoService.DeleteLogicoAsync(productos[2].Id);
            await _productoService.DeleteLogicoAsync(productos[5].Id);
            await _productoService.DeleteLogicoAsync(productos[8].Id);
        }

        // 3. Clientes (y Perfiles vinculados): 8 total -> inactivar índices 2 y 5 (1 de cada 3 -> 2 inactivos, 6 activos)
        if (clientes.Count >= 6)
        {
            await _clienteService.DeleteLogicoAsync(clientes[2].Id);
            await _clienteService.DeleteLogicoAsync(clientes[5].Id);
        }

        // 4. Pedidos: 12 total -> inactivar índices 2, 5, 8, 11 (1 de cada 3 -> 4 inactivos, 8 activos)
        var pedidosRes = await _pedidoService.GetAllAsync();
        var listaPedidos = pedidosRes.Data ?? [];
        if (listaPedidos.Count >= 12)
        {
            await _pedidoService.DeleteLogicoAsync(listaPedidos[2].Id);
            await _pedidoService.DeleteLogicoAsync(listaPedidos[5].Id);
            await _pedidoService.DeleteLogicoAsync(listaPedidos[8].Id);
            await _pedidoService.DeleteLogicoAsync(listaPedidos[11].Id);
        }
    }

    private async Task ActualizarVerificacionesFinalesAsync(SeederFullResult fullResult)
    {
        foreach (var paso in fullResult.Pasos)
        {
            switch (paso.Coleccion)
            {
                case "Categorías":
                    var catRes = await _categoriaService.GetAllAsync();
                    int catTotal = (int)await _mongoService.GetCollection<Categoria>("categorias").CountDocumentsAsync(FilterDefinition<Categoria>.Empty);
                    paso.Activos = catRes.Data?.Count ?? 0;
                    paso.Inactivos = catTotal - paso.Activos;
                    paso.Insertados = catTotal;
                    paso.Verificado = catRes.Success && paso.Activos == 3 && paso.Inactivos == 1 ? "Éxito (Result.Ok)" : "Fallo";
                    break;

                case "Proveedores":
                    var provRes = await _proveedorService.GetAllAsync();
                    int provTotal = (int)await _mongoService.GetCollection<Proveedor>("proveedores").CountDocumentsAsync(FilterDefinition<Proveedor>.Empty);
                    paso.Activos = provRes.Data?.Count ?? 0;
                    paso.Inactivos = provTotal - paso.Activos;
                    paso.Insertados = provTotal;
                    paso.Verificado = provRes.Success && paso.Activos == 3 && paso.Inactivos == 2 ? "Éxito (Result.Ok)" : "Fallo";
                    break;

                case "Productos":
                    var prodRes = await _productoService.GetAllAsync();
                    int prodTotal = (int)await _mongoService.GetCollection<Producto>("productos").CountDocumentsAsync(FilterDefinition<Producto>.Empty);
                    paso.Activos = prodRes.Data?.Count ?? 0;
                    paso.Inactivos = prodTotal - paso.Activos;
                    paso.Insertados = prodTotal;
                    paso.Verificado = prodRes.Success && paso.Activos == 7 && paso.Inactivos == 3 ? "Éxito (Result.Ok)" : "Fallo";
                    break;

                case "Clientes":
                    var cliRes = await _clienteService.GetAllAsync();
                    int cliTotal = (int)await _mongoService.GetCollection<Cliente>("clientes").CountDocumentsAsync(FilterDefinition<Cliente>.Empty);
                    paso.Activos = cliRes.Data?.Count ?? 0;
                    paso.Inactivos = cliTotal - paso.Activos;
                    paso.Insertados = cliTotal;
                    paso.Verificado = cliRes.Success && paso.Activos == 6 && paso.Inactivos == 2 ? "Éxito (Result.Ok)" : "Fallo";
                    break;

                case "Perfiles":
                    var perfRes = await _perfilService.GetAllAsync();
                    int perfTotal = (int)await _mongoService.GetCollection<Perfil>("perfiles").CountDocumentsAsync(FilterDefinition<Perfil>.Empty);
                    paso.Activos = perfRes.Data?.Count ?? 0;
                    paso.Inactivos = perfTotal - paso.Activos;
                    paso.Insertados = perfTotal;
                    paso.Verificado = perfRes.Success && paso.Activos == 6 && paso.Inactivos == 2 ? "Éxito (Result.Ok)" : "Fallo";
                    break;

                case "Pedidos":
                    var pedRes = await _pedidoService.GetAllAsync();
                    int pedTotal = (int)await _mongoService.GetCollection<Pedido>("pedidos").CountDocumentsAsync(FilterDefinition<Pedido>.Empty);
                    paso.Activos = pedRes.Data?.Count ?? 0;
                    paso.Inactivos = pedTotal - paso.Activos;
                    paso.Insertados = pedTotal;
                    paso.Verificado = pedRes.Success && paso.Activos == 8 && paso.Inactivos == 4 ? "Éxito (Result.Ok)" : "Fallo";
                    break;
            }
        }
    }

    public async Task<List<string>> TestBlock1Async()
    {
        await SeedAllAsync();
        var logs = new List<string>();

        // Test 1: Trim() en entradas de texto
        var catTest1 = new Categoria
        {
            Nombre = "   Tecnología Avanzada   ",
            Codigo = "   CAT-TEST-TRIM   ",
            Descripcion = "  Descripción con espacios  ",
            Responsable = "  Juan Pérez  ",
            PorcentajeImpuesto = 19m
        };
        var res1 = await _categoriaService.CreateAsync(catTest1);
        if (res1.Success && res1.Data!.Nombre == "Tecnología Avanzada" && res1.Data.Codigo == "CAT-TEST-TRIM")
        {
            logs.Add("✅ [Item 1 - Trim]: ÉXITO. El texto se limpió de espacios inicio/fin ('" + res1.Data.Nombre + "').");
        }
        else
        {
            logs.Add("❌ [Item 1 - Trim]: FALLO. " + res1.Message);
        }

        // Test 2: Estado Categórico ("  ADSAS  ")
        var clientesRes = await _clienteService.GetAllAsync();
        var clienteId = clientesRes.Data!.First().Id;
        var productosRes = await _productoService.GetAllAsync();
        var productoActivo = productosRes.Data!.First();

        var pedInvalidoEstado = new Pedido
        {
            ClienteId = clienteId,
            Estado = "  ADSAS  ",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 100",
            Detalles = [new DetallePedido { ProductoId = productoActivo.Id, Cantidad = 1 }]
        };
        var res2 = await _pedidoService.CreateAsync(pedInvalidoEstado);
        if (!res2.Success && res2.Message.Contains("no es válido"))
        {
            logs.Add("✅ [Item 2 - Estado Categórico]: ÉXITO. Se rechazó estado inválido 'ADSAS'. Mensaje: " + res2.Message);
        }
        else
        {
            logs.Add("❌ [Item 2 - Estado Categórico]: FALLO. Se permitió estado inválido.");
        }

        // Test 3: Método de Pago Categórico
        var pedInvalidoPago = new Pedido
        {
            ClienteId = clienteId,
            Estado = "Pendiente",
            MetodoPago = "MetodoInventado",
            DireccionEnvio = "Calle 100",
            Detalles = [new DetallePedido { ProductoId = productoActivo.Id, Cantidad = 1 }]
        };
        var res3 = await _pedidoService.CreateAsync(pedInvalidoPago);
        if (!res3.Success && res3.Message.Contains("método de pago no es válido"))
        {
            logs.Add("✅ [Item 3 - Método Pago Categórico]: ÉXITO. Se rechazó método de pago inválido. Mensaje: " + res3.Message);
        }
        else
        {
            logs.Add("❌ [Item 3 - Método Pago Categórico]: FALLO.");
        }

        // Test 4: Sanitización NoSQL ($ y {})
        var catNoSql = new Categoria
        {
            Nombre = "Cat {$gt: ''}",
            Codigo = "CAT-$NOSQL",
            PorcentajeImpuesto = 19m
        };
        var res4 = await _categoriaService.CreateAsync(catNoSql);
        if (res4.Success && !res4.Data!.Nombre.Contains("$") && !res4.Data.Nombre.Contains("{"))
        {
            logs.Add("✅ [Item 4 - Inyección NoSQL]: ÉXITO. Se desinfectaron caracteres '$' y '{}'. Resultado: '" + res4.Data.Nombre + "'");
        }
        else
        {
            logs.Add("❌ [Item 4 - Inyección NoSQL]: FALLO.");
        }

        // Test 5: Sanitización XSS y Caracteres de Control
        var provXss = new Proveedor
        {
            Nombre = "Empresa <script>alert('XSS')</script> S.A.S.\0\u202E",
            Nit = "900.888.777-1",
            Email = "contacto@empresaxss.com"
        };
        var res5 = await _proveedorService.CreateAsync(provXss);
        if (res5.Success && !res5.Data!.Nombre.Contains("<script>"))
        {
            logs.Add("✅ [Item 5 - XSS y Control]: ÉXITO. Se removieron etiquetas HTML y caracteres nulos. Resultado: '" + res5.Data.Nombre + "'");
        }
        else
        {
            logs.Add("❌ [Item 5 - XSS y Control]: FALLO. " + res5.Message);
        }

        // Test 6: Validación Email Regex
        var cliEmailInv = new Cliente
        {
            Nombres = "Pedro",
            Apellidos = "Pérez",
            Documento = "1099887766",
            Email = "correo-invalido-sin-arroba"
        };
        var res6 = await _clienteService.CreateAsync(cliEmailInv);
        if (!res6.Success && res6.Message.Contains("correo electrónico no es válido"))
        {
            logs.Add("✅ [Item 6 - Email Regex]: ÉXITO. Se rechazó email sin formato. Mensaje: " + res6.Message);
        }
        else
        {
            logs.Add("❌ [Item 6 - Email Regex]: FALLO.");
        }

        // Test 7: Validación Regex Documento / NIT / Teléfono
        var provNitInv = new Proveedor
        {
            Nombre = "Proveedor NIT Malo",
            Nit = "NIT_CON_LETRAS_ABC",
            Email = "nitmalo@prov.com"
        };
        var res7 = await _proveedorService.CreateAsync(provNitInv);
        if (!res7.Success && res7.Message.Contains("NIT no es válido"))
        {
            logs.Add("✅ [Item 7 - Regex NIT/Doc/Tel]: ÉXITO. Se rechazó NIT alfanumérico inválido. Mensaje: " + res7.Message);
        }
        else
        {
            logs.Add("❌ [Item 7 - Regex NIT/Doc/Tel]: FALLO.");
        }

        // Test 8: Bloqueo Edición Pedido Entregado/Cancelado
        var pedEntregado = (await _pedidoService.GetAllAsync()).Data!.First(p => p.Estado == "Entregado");
        pedEntregado.DireccionEnvio = "Nueva Dirección Cambiada";
        var res8 = await _pedidoService.UpdateAsync(pedEntregado.Id, pedEntregado);
        if (!res8.Success && res8.Message.Contains("no se puede modificar", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 8 - Bloqueo Edición Finalizados]: ÉXITO. Se impidió modificar pedido Entregado. Mensaje: " + res8.Message);
        }
        else
        {
            logs.Add("❌ [Item 8 - Bloqueo Edición Finalizados]: FALLO.");
        }

        // Test 9: Validación y Descuento de Stock
        int stockInicial = productoActivo.Stock;
        var pedSuperaStock = new Pedido
        {
            ClienteId = clienteId,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 50",
            Detalles = [new DetallePedido { ProductoId = productoActivo.Id, Cantidad = stockInicial + 100 }]
        };
        var res9A = await _pedidoService.CreateAsync(pedSuperaStock);

        var pedStockValido = new Pedido
        {
            ClienteId = clienteId,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 50",
            Detalles = [new DetallePedido { ProductoId = productoActivo.Id, Cantidad = 2 }]
        };
        var res9B = await _pedidoService.CreateAsync(pedStockValido);
        var prodActualizado = (await _productoService.GetByIdAsync(productoActivo.Id)).Data!;

        if (!res9A.Success && res9B.Success && prodActualizado.Stock == (stockInicial - 2))
        {
            logs.Add($"✅ [Item 9 - Stock]: ÉXITO. Se rechazó pedido sin stock suficiente y se descontó correctamente (Stock inicial: {stockInicial} -> Final: {prodActualizado.Stock}).");
        }
        else
        {
            logs.Add("❌ [Item 9 - Stock]: FALLO.");
        }

        // Test 10: Bloqueo Eliminación Producto/Categoría Activos
        var catConProds = (await _categoriaService.GetAllAsync()).Data!.First();
        var res10A = await _categoriaService.DeleteLogicoAsync(catConProds.Id);

        var prodConPedidos = (await _productoService.GetAllAsync()).Data!.First(p => p.PedidosIds.Count > 0);
        var res10B = await _productoService.DeleteLogicoAsync(prodConPedidos.Id);

        if (!res10A.Success && res10A.Message.Contains("productos activos") &&
            !res10B.Success && res10B.Message.Contains("pedidos activos"))
        {
            logs.Add("✅ [Item 10 - Integridad al Eliminar]: ÉXITO. Se impidió borrar categoría y producto con uso activo en BD.");
        }
        else
        {
            logs.Add("❌ [Item 10 - Integridad al Eliminar]: FALLO. Cat: " + res10A.Message + " | Prod: " + res10B.Message);
        }

        return logs;
    }

    public async Task<List<string>> TestBlock2Async()
    {
        await SeedAllAsync();
        var logs = new List<string>();

        // Test 11: Operación compuesta y Rollback compensatorio sin huérfanos
        int perfilesAntes = (int)await _mongoService.GetCollection<Perfil>("perfiles").CountDocumentsAsync(FilterDefinition<Perfil>.Empty);
        int clientesAntes = (int)await _mongoService.GetCollection<Cliente>("clientes").CountDocumentsAsync(FilterDefinition<Cliente>.Empty);

        var clienteConPerfilInvalido = new Cliente
        {
            Nombres = "Cliente Rollback",
            Apellidos = "Test",
            Documento = "9988776655",
            Email = "rollback@test.com"
        };
        var perfilInvalidoFecha = new Perfil
        {
            Direccion = "Calle Rollback",
            FechaNacimiento = DateTime.Today.AddDays(10) // Fecha futura inválida
        };
        var res11 = await _clienteService.CreateAsync(clienteConPerfilInvalido, perfilInvalidoFecha);

        int perfilesDespues = (int)await _mongoService.GetCollection<Perfil>("perfiles").CountDocumentsAsync(FilterDefinition<Perfil>.Empty);
        int clientesDespues = (int)await _mongoService.GetCollection<Cliente>("clientes").CountDocumentsAsync(FilterDefinition<Cliente>.Empty);

        if (!res11.Success && perfilesAntes == perfilesDespues && clientesAntes == clientesDespues)
        {
            logs.Add("✅ [Item 11 - Rollback Compensatorio]: ÉXITO. Se canceló la operación y no quedaron registros huérfanos.");
        }
        else
        {
            logs.Add("❌ [Item 11 - Rollback Compensatorio]: FALLO. " + res11.Message);
        }

        // Test 12: Concurrencia Optimista (Version)
        var catActual = (await _categoriaService.GetAllAsync()).Data!.First();
        var catCopia1 = new Categoria
        {
            Id = catActual.Id,
            Nombre = "Nombre Modificado 1",
            Codigo = catActual.Codigo,
            FechaCreacion = catActual.FechaCreacion,
            Responsable = catActual.Responsable,
            PorcentajeImpuesto = catActual.PorcentajeImpuesto,
            Version = catActual.Version
        };
        var catCopia2 = new Categoria
        {
            Id = catActual.Id,
            Nombre = "Nombre Modificado 2",
            Codigo = catActual.Codigo,
            FechaCreacion = catActual.FechaCreacion,
            Responsable = catActual.Responsable,
            PorcentajeImpuesto = catActual.PorcentajeImpuesto,
            Version = catActual.Version // Misma versión vieja
        };

        var res12A = await _categoriaService.UpdateAsync(catActual.Id, catCopia1); // Incrementa versión a 2
        var res12B = await _categoriaService.UpdateAsync(catActual.Id, catCopia2); // Intenta actualizar con versión 1 vieja

        if (res12A.Success && !res12B.Success && res12B.Message.Contains("concurrencia", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 12 - Concurrencia Optimista]: ÉXITO. Se bloqueó la segunda actualización concurrente por conflicto de versión.");
        }
        else
        {
            logs.Add("❌ [Item 12 - Concurrencia Optimista]: FALLO. " + res12B.Message);
        }

        // Test 13: Impuesto / Precios Inválidos (Rangos / Desbordamiento)
        var catImpuestoMalo = new Categoria
        {
            Nombre = "Categoría Impuesto Malo",
            Codigo = "CAT-IMP-BAD",
            PorcentajeImpuesto = 150m // > 100%
        };
        var res13 = await _categoriaService.CreateAsync(catImpuestoMalo);
        if (!res13.Success && res13.Message.Contains("entre 0 y 100", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 13 - Rango Numérico Impuesto]: ÉXITO. Se rechazó impuesto del 150%. Mensaje: " + res13.Message);
        }
        else
        {
            logs.Add("❌ [Item 13 - Rango Numérico Impuesto]: FALLO.");
        }

        // Test 14: Verificación Impuesto 0% - 100%
        var catImpuestoValido = new Categoria
        {
            Nombre = "Categoría Impuesto 0%",
            Codigo = "CAT-IMP-ZERO",
            PorcentajeImpuesto = 0m
        };
        var res14 = await _categoriaService.CreateAsync(catImpuestoValido);
        if (res14.Success)
        {
            logs.Add("✅ [Item 14 - Impuesto 0%-100%]: ÉXITO. Impuesto del 0% aceptado correctamente.");
        }
        else
        {
            logs.Add("❌ [Item 14 - Impuesto 0%-100%]: FALLO.");
        }

        // Test 15: Coincidencia Total del Pedido = ∑ (Cantidad * PrecioUnitario)
        var pedTest15 = (await _pedidoService.GetAllAsync()).Data!.First();
        decimal totalSumado = pedTest15.Detalles.Sum(d => d.Cantidad * d.PrecioUnitario);
        if (Math.Abs(pedTest15.Total - totalSumado) < 0.01m)
        {
            logs.Add($"✅ [Item 15 - Total Pedido]: ÉXITO. El total registrado (${pedTest15.Total:N2}) coincide exactamente con la suma de los detalles.");
        }
        else
        {
            logs.Add("❌ [Item 15 - Total Pedido]: FALLO.");
        }

        // Test 16: FechaPedido Futura Rechazada
        var clientes = (await _clienteService.GetAllAsync()).Data!;
        var prods = (await _productoService.GetAllAsync()).Data!;
        var pedFechaFutura = new Pedido
        {
            ClienteId = clientes.First().Id,
            FechaPedido = DateTime.Today.AddDays(10), // Futura
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle Futura",
            Detalles = [new DetallePedido { ProductoId = prods.First().Id, Cantidad = 1 }]
        };
        var res16 = await _pedidoService.CreateAsync(pedFechaFutura);
        if (!res16.Success && res16.Message.Contains("no puede ser futura", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 16 - Fecha No Futura]: ÉXITO. Se rechazó fecha de pedido futura. Mensaje: " + res16.Message);
        }
        else
        {
            logs.Add("❌ [Item 16 - Fecha No Futura]: FALLO.");
        }

        // Test 17: FechaNacimiento Perfil Inválida (Futura / > 120 años)
        var perfilViejo = new Perfil
        {
            Direccion = "Calle Antigua",
            FechaNacimiento = DateTime.Today.AddYears(-130)
        };
        var res17 = await _perfilService.CreateAsync(perfilViejo);
        if (!res17.Success && res17.Message.Contains("120 años", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 17 - Fecha Nacimiento Perfil]: ÉXITO. Se rechazó fecha de nacimiento > 120 años.");
        }
        else
        {
            logs.Add("❌ [Item 17 - Fecha Nacimiento Perfil]: FALLO.");
        }

        // Test 18: Unicidad Case-Insensitive (Documento)
        var cliOriginal = clientes.First();
        var cliDuplicadoCase = new Cliente
        {
            Nombres = "Juan",
            Apellidos = "Duplicado",
            Documento = cliOriginal.Documento.ToLowerInvariant(), // Mismo documento pero minúscula
            Email = "duplicado.case@test.com"
        };
        var res18 = await _clienteService.CreateAsync(cliDuplicadoCase);
        if (!res18.Success && res18.Message.Contains("existe", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 18 - Unicidad Case-Insensitive]: ÉXITO. Se detectó documento duplicado independientemente de mayúsculas/minúsculas.");
        }
        else
        {
            logs.Add("❌ [Item 18 - Unicidad Case-Insensitive]: FALLO. " + res18.Message);
        }

        // Test 19: Verificación de ObjectId Inválido
        var res19 = await _productoService.GetByIdAsync("123-id-completamente-invalido");
        if (!res19.Success && res19.Message.Contains("no es válido", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 19 - Parse ObjectId]: ÉXITO. Se rechazó identificador malformado. Mensaje: " + res19.Message);
        }
        else
        {
            logs.Add("❌ [Item 19 - Parse ObjectId]: FALLO.");
        }

        // Test 20: Limpieza y Deduplicación de Arrays Referenciales
        var provArraysDup = new Proveedor
        {
            Nombre = "Proveedor Array Dup",
            Nit = "900.555.444-9",
            Email = "arraydup@prov.com",
            ProductosIds = [prods[0].Id, prods[0].Id, "id-invalido", prods[1].Id]
        };
        var res20 = await _proveedorService.CreateAsync(provArraysDup);
        if (res20.Success && res20.Data!.ProductosIds.Count == 2)
        {
            logs.Add("✅ [Item 20 - Deduplicación de Arrays]: ÉXITO. Se depuraron los IDs duplicados e inválidos del array.");
        }
        else
        {
            logs.Add("❌ [Item 20 - Deduplicación de Arrays]: FALLO.");
        }

        return logs;
    }

    public async Task<List<string>> TestBlock3Async()
    {
        await SeedAllAsync();
        var logs = new List<string>();

        var catRes = await _categoriaService.GetAllAsync();
        var catActiva = catRes.Data!.First();
        var provRes = await _proveedorService.GetAllAsync();
        var provActivo = provRes.Data!.First();
        var prodRes = await _productoService.GetAllAsync();
        var prods = prodRes.Data!;
        var clientRes = await _clienteService.GetAllAsync();
        var clienteActivo = clientRes.Data!.First();

        // Test 21: Rechazo de Producto sin Proveedores
        var pNoProv = new Producto
        {
            Nombre = "Producto Sin Proveedor",
            Precio = 1000m,
            Stock = 10,
            CategoriaId = catActiva.Id,
            ProveedoresIds = []
        };
        var res21 = await _productoService.CreateAsync(pNoProv);
        if (!res21.Success && res21.Message.Contains("al menos un proveedor", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 21 - Proveedores Requeridos]: ÉXITO. Se rechazó producto sin proveedores. Mensaje: " + res21.Message);
        }
        else
        {
            logs.Add("❌ [Item 21 - Proveedores Requeridos]: FALLO. " + res21.Message);
        }

        // Test 22: Rechazo de Pedido sin Productos
        var pedVacio = new Pedido
        {
            ClienteId = clienteActivo.Id,
            FechaPedido = DateTime.Now,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 123 #45-67",
            ProductosIds = [],
            Detalles = []
        };
        var res22 = await _pedidoService.CreateAsync(pedVacio);
        if (!res22.Success && res22.Message.Contains("al menos un producto", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 22 - Productos Requeridos en Pedido]: ÉXITO. Se rechazó pedido sin productos. Mensaje: " + res22.Message);
        }
        else
        {
            logs.Add("❌ [Item 22 - Productos Requeridos en Pedido]: FALLO. " + res22.Message);
        }

        // Test 23: Sincronización Detalles -> ProductosIds en Pedido
        var pedSinc = new Pedido
        {
            ClienteId = clienteActivo.Id,
            FechaPedido = DateTime.Now,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 100 #20-30",
            Detalles =
            [
                new DetallePedido { ProductoId = prods[0].Id, Cantidad = 1 },
                new DetallePedido { ProductoId = prods[1].Id, Cantidad = 2 }
            ]
        };
        var res23 = await _pedidoService.CreateAsync(pedSinc);
        if (res23.Success && res23.Data!.ProductosIds.Count == 2 && res23.Data.ProductosIds.Contains(prods[0].Id) && res23.Data.ProductosIds.Contains(prods[1].Id))
        {
            logs.Add("✅ [Item 23 - Sincronización Detalles Pedido]: ÉXITO. ProductosIds se sincronizó automáticamente desde Detalles.");
        }
        else
        {
            logs.Add("❌ [Item 23 - Sincronización Detalles Pedido]: FALLO.");
        }

        // Test 24: Límite de Preferencias en Perfil (máx 20)
        var clienteSinPerfil = new Cliente
        {
            Nombres = "Cliente Sin Perfil",
            Apellidos = "Test",
            Documento = "11122233344",
            Email = "sinperfil@test.com"
        };
        var resCliSinPerfil = await _clienteService.CreateAsync(clienteSinPerfil);

        var perfilExceso = new Perfil
        {
            ClienteId = null,
            Direccion = "Av 68 #10-20",
            Ciudad = "Bogotá",
            Pais = "Colombia",
            FechaNacimiento = new DateTime(1990, 5, 10),
            Preferencias = Enumerable.Range(1, 30).Select(i => $"Pref {i}").ToList()
        };
        var res24 = await _perfilService.CreateAsync(perfilExceso);
        if (res24.Success && res24.Data!.Preferencias.Count <= 20)
        {
            logs.Add("✅ [Item 24 - Límite Preferencias Perfil]: ÉXITO. El array de preferencias se acotó a 20 elementos.");
        }
        else
        {
            logs.Add("❌ [Item 24 - Límite Preferencias Perfil]: FALLO. " + res24.Message);
        }

        // Test 25: Rechazo de Cliente Inactivo en Pedido
        var clienteAux = new Cliente
        {
            Nombres = "Cliente Inactivo",
            Apellidos = "Prueba",
            Documento = "999888777",
            Email = "inactivo.cliente@test.com",
            Telefono = "3009998877"
        };
        var resClienteAux = await _clienteService.CreateAsync(clienteAux);
        await _clienteService.DeleteLogicoAsync(resClienteAux.Data!.Id);

        var pedClientInact = new Pedido
        {
            ClienteId = resClienteAux.Data.Id,
            FechaPedido = DateTime.Now,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 1 #2-3",
            Detalles = [new DetallePedido { ProductoId = prods[0].Id, Cantidad = 1 }]
        };
        var res25 = await _pedidoService.CreateAsync(pedClientInact);
        if (!res25.Success && res25.Message.Contains("inactivo", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 25 - Cliente Inactivo en Pedido]: ÉXITO. Se rechazó el pedido para un cliente inactivo.");
        }
        else
        {
            logs.Add("❌ [Item 25 - Cliente Inactivo en Pedido]: FALLO. " + res25.Message);
        }

        // Test 26: Rechazo de Producto Inactivo en Pedido
        var prodAux = new Producto
        {
            Nombre = "Producto Para Inactivar",
            Precio = 50000m,
            Stock = 20,
            CategoriaId = catActiva.Id,
            ProveedoresIds = [provActivo.Id]
        };
        var resProdAux = await _productoService.CreateAsync(prodAux);
        await _productoService.DeleteLogicoAsync(resProdAux.Data!.Id);

        var pedProdInact = new Pedido
        {
            ClienteId = clienteActivo.Id,
            FechaPedido = DateTime.Now,
            Estado = "Pendiente",
            MetodoPago = "PSE",
            DireccionEnvio = "Calle 1 #2-3",
            Detalles = [new DetallePedido { ProductoId = resProdAux.Data.Id, Cantidad = 1 }]
        };
        var res26 = await _pedidoService.CreateAsync(pedProdInact);
        if (!res26.Success && res26.Message.Contains("inactivo", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 26 - Producto Inactivo en Pedido]: ÉXITO. Se rechazó incluir un producto inactivo en el pedido.");
        }
        else
        {
            logs.Add("❌ [Item 26 - Producto Inactivo en Pedido]: FALLO. " + res26.Message);
        }

        // Test 27: Rechazo de Categoría Inactiva en Producto
        var catAux = new Categoria
        {
            Nombre = "Categoría Para Inactivar",
            Codigo = "CAT-INACT-01",
            Descripcion = "Prueba inactiva",
            Responsable = "Prueba"
        };
        var resCatAux = await _categoriaService.CreateAsync(catAux);
        await _categoriaService.DeleteLogicoAsync(resCatAux.Data!.Id);

        var prodCatInact = new Producto
        {
            Nombre = "Producto Categoria Inactiva",
            Precio = 10000m,
            Stock = 5,
            CategoriaId = resCatAux.Data.Id,
            ProveedoresIds = [provActivo.Id]
        };
        var res27 = await _productoService.CreateAsync(prodCatInact);
        if (!res27.Success && res27.Message.Contains("inactiva", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 27 - Categoría Inactiva en Producto]: ÉXITO. Se rechazó crear producto con categoría inactiva.");
        }
        else
        {
            logs.Add("❌ [Item 27 - Categoría Inactiva en Producto]: FALLO. " + res27.Message);
        }

        // Test 28: Rechazo de Proveedor Inactivo en Producto
        var provAux = new Proveedor
        {
            Nombre = "Proveedor Para Inactivar",
            Nit = "888.777.666-5",
            Email = "inactivoprov@test.com"
        };
        var resProvAux = await _proveedorService.CreateAsync(provAux);
        await _proveedorService.DeleteLogicoAsync(resProvAux.Data!.Id);

        var prodProvInact = new Producto
        {
            Nombre = "Producto Proveedor Inactivo",
            Precio = 15000m,
            Stock = 5,
            CategoriaId = catActiva.Id,
            ProveedoresIds = [resProvAux.Data.Id]
        };
        var res28 = await _productoService.CreateAsync(prodProvInact);
        if (!res28.Success && res28.Message.Contains("inactivo", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 28 - Proveedor Inactivo en Producto]: ÉXITO. Se rechazó crear producto con proveedor inactivo.");
        }
        else
        {
            logs.Add("❌ [Item 28 - Proveedor Inactivo en Producto]: FALLO. " + res28.Message);
        }

        // Test 29: Rechazo de Eliminación de Cliente con Pedidos Activos
        var pedClienteAct = await _pedidoService.GetAllAsync();
        var clienteConPedidoId = pedClienteAct.Data!.First().ClienteId;
        var res29 = await _clienteService.DeleteLogicoAsync(clienteConPedidoId);
        if (!res29.Success && res29.Message.Contains("pedidos activos", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("✅ [Item 29 - Bloqueo Eliminación Cliente con Pedidos]: ÉXITO. Se impidió eliminar lógicamente un cliente con pedidos activos.");
        }
        else
        {
            logs.Add("❌ [Item 29 - Bloqueo Eliminación Cliente con Pedidos]: FALLO. " + res29.Message);
        }

        // Test 30: Sanitización de Inyección NoSQL y XSS
        var catInjection = new Categoria
        {
            Nombre = "  <script>alert('xss')</script> {$gt: ''} Categoria Sanitizada  ",
            Codigo = "CAT-SAN-01",
            Descripcion = "Descripción sin scripts",
            Responsable = "Prueba NoSQL"
        };
        var res30 = await _categoriaService.CreateAsync(catInjection);
        if (res30.Success && !res30.Data!.Nombre.Contains("$") && !res30.Data.Nombre.Contains("<script>") && res30.Data.Nombre.Contains("Categoria Sanitizada"))
        {
            logs.Add("✅ [Item 30 - Sanitización NoSQL y XSS]: ÉXITO. Se eliminaron operadores NoSQL y etiquetas XSS ('" + res30.Data.Nombre + "').");
        }
        else
        {
            logs.Add("❌ [Item 30 - Sanitización NoSQL y XSS]: FALLO. " + res30.Message);
        }

        return logs;
    }
}

public class SeederFullResult
{
    public List<SeederStepResult> Pasos { get; set; } = [];
}

public class SeederStepResult
{
    public string Coleccion { get; set; } = string.Empty;
    public int Insertados { get; set; }
    public int Activos { get; set; }
    public int Inactivos { get; set; }
    public string Verificado { get; set; } = string.Empty;
    public List<string> Logs { get; set; } = [];
}
