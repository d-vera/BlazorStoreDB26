using tienda.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<MongoService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<PerfilService>();
builder.Services.AddScoped<CategoriaService>();
builder.Services.AddScoped<ProductoService>();
builder.Services.AddScoped<ProveedorService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<DataSeeder>();

var app = builder.Build();

if (args.Contains("--seed-all") || args.Contains("--seed-categorias"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    var fullResult = await seeder.SeedAllAsync();

    Console.WriteLine("\n==========================================================================");
    Console.WriteLine("          RESULTADO DE INSERCIÓN Y VERIFICACIÓN EN MONGODB");
    Console.WriteLine("==========================================================================\n");
    Console.WriteLine("Colección | Insertados | Activos | Inactivos | Verificado");
    Console.WriteLine("--------------------------------------------------------------------------");
    foreach (var paso in fullResult.Pasos)
    {
        Console.WriteLine($"{paso.Coleccion.PadRight(10)} | {paso.Insertados.ToString().PadRight(10)} | {paso.Activos.ToString().PadRight(7)} | {paso.Inactivos.ToString().PadRight(9)} | {paso.Verificado}");
    }
    Console.WriteLine("--------------------------------------------------------------------------\n");

    Console.WriteLine("DETALLE DE LOGS Y VERIFICACIONES DE INTEGRIDAD:\n");
    foreach (var paso in fullResult.Pasos)
    {
        Console.WriteLine($"--- [{paso.Coleccion}] ---");
        foreach (var log in paso.Logs)
        {
            Console.WriteLine(log);
        }
        Console.WriteLine();
    }

    return;
}

if (args.Contains("--test-block-1"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    var logs = await seeder.TestBlock1Async();

    Console.WriteLine("\n==========================================================================");
    Console.WriteLine("          RESULTADOS DE PRUEBAS DEL BLOQUE 1 (ITEMS 1 A 10)");
    Console.WriteLine("==========================================================================\n");
    foreach (var log in logs)
    {
        Console.WriteLine(log);
    }
    Console.WriteLine("\n==========================================================================\n");
    return;
}

if (args.Contains("--test-block-2"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    var logs = await seeder.TestBlock2Async();

    Console.WriteLine("\n==========================================================================");
    Console.WriteLine("          RESULTADOS DE PRUEBAS DEL BLOQUE 2 (ITEMS 11 A 20)");
    Console.WriteLine("==========================================================================\n");
    foreach (var log in logs)
    {
        Console.WriteLine(log);
    }
    Console.WriteLine("\n==========================================================================\n");
    return;
}

if (args.Contains("--test-block-3"))
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    var logs = await seeder.TestBlock3Async();

    Console.WriteLine("\n==========================================================================");
    Console.WriteLine("          RESULTADOS DE PRUEBAS DEL BLOQUE 3 (ITEMS 21 A 30)");
    Console.WriteLine("==========================================================================\n");
    foreach (var log in logs)
    {
        Console.WriteLine(log);
    }
    Console.WriteLine("\n==========================================================================\n");
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
