namespace PropertyApi.Observability;

public readonly record struct RequestTelemetryRoute(
    string RouteGroup,
    string Operation,
    bool IsAuth,
    bool IsProperties);

public static class RequestTelemetryClassifier
{
    public static RequestTelemetryRoute Classify(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        var method = request.Method;

        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            return new RequestTelemetryRoute(
                RouteGroup: "auth",
                Operation: ClassifyAuthOperation(path),
                IsAuth: true,
                IsProperties: false);
        }

        if (path.StartsWith("/api/properties", StringComparison.OrdinalIgnoreCase))
        {
            return new RequestTelemetryRoute(
                RouteGroup: "properties",
                Operation: ClassifyPropertyOperation(path, method),
                IsAuth: false,
                IsProperties: true);
        }

        return new RequestTelemetryRoute(
            RouteGroup: "other",
            Operation: "other",
            IsAuth: false,
            IsProperties: false);
    }

    private static string ClassifyAuthOperation(string path)
    {
        if (path.Contains("/register", StringComparison.OrdinalIgnoreCase))
            return "register";

        if (path.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/social/", StringComparison.OrdinalIgnoreCase))
            return "login";

        if (path.Contains("/refresh", StringComparison.OrdinalIgnoreCase))
            return "refresh";

        if (path.Contains("/logout", StringComparison.OrdinalIgnoreCase))
            return "logout";

        if (path.Contains("/phone/send-otp", StringComparison.OrdinalIgnoreCase))
            return "send_otp";

        if (path.Contains("/phone/verify", StringComparison.OrdinalIgnoreCase))
            return "verify_otp";

        if (path.Contains("/email/", StringComparison.OrdinalIgnoreCase))
            return "email_verification";

        if (path.Contains("password", StringComparison.OrdinalIgnoreCase))
            return "password_recovery";

        return "auth_other";
    }

    private static string ClassifyPropertyOperation(string path, string method)
    {
        if (path.Contains("/geo-search", StringComparison.OrdinalIgnoreCase))
            return "geo_search";

        if (HttpMethods.IsGet(method) && IsPropertyByIdPath(path))
            return "get_by_id";

        if (HttpMethods.IsGet(method))
            return "list";

        if (HttpMethods.IsPost(method))
            return "create";

        if (HttpMethods.IsPut(method) || HttpMethods.IsPatch(method))
            return "update";

        if (HttpMethods.IsDelete(method))
            return "delete";

        return "properties_other";
    }

    private static bool IsPropertyByIdPath(string path)
    {
        var trimmed = path.Trim('/');
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 3 &&
            segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("properties", StringComparison.OrdinalIgnoreCase);
    }
}
