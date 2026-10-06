using System.Text.RegularExpressions;

namespace tienda.Common;

public static class InputValidation
{
    public const int ShortTextMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int EmailMaxLength = 254;

    public static readonly string[] AllowedPedidoEstados = ["Pendiente", "Procesando", "Enviado", "Entregado", "Cancelado"];
    public static readonly string[] AllowedMetodosPago = ["PSE", "Tarjeta de Crédito", "Tarjeta de Débito", "Efectivo"];

    public static bool Exceeds(string? value, int maxLength)
    {
        return value is not null && value.Length > maxLength;
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // 1. Trim espacios al inicio y final
        var trimmed = value.Trim();

        // 2. Eliminar caracteres de control invisibles (ej: \0, RTL override \u202E, etc.)
        var cleanControl = Regex.Replace(trimmed, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F\u202E\u202D]", string.Empty);

        // 3. Sanitizar operadores peligrosos NoSQL ($ y llaves de inyección {})
        var cleanNoSql = cleanControl.Replace("$", string.Empty).Replace("{", string.Empty).Replace("}", string.Empty);

        // 4. Remover etiquetas de scripts / HTML básicas (XSS)
        var cleanXss = Regex.Replace(cleanNoSql, @"<[^>]*>", string.Empty);

        return cleanXss;
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    }

    public static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        return Regex.IsMatch(phone, @"^\+?[0-9\s\-]{7,20}$");
    }

    public static bool IsValidDocument(string doc)
    {
        if (string.IsNullOrWhiteSpace(doc)) return false;
        return Regex.IsMatch(doc, @"^[a-zA-Z0-9\.\-]{5,20}$");
    }

    public static bool IsValidNit(string nit)
    {
        if (string.IsNullOrWhiteSpace(nit)) return false;
        return Regex.IsMatch(nit, @"^[0-9\.\-]{8,20}$");
    }
}
